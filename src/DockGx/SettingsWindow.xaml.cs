using DockGx.Models;
using DockGx.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DockGx;

/// <summary>
/// A Windows-Settings-style window: a left NavigationView with a <b>General</b> page (position,
/// auto-hide, start-with-Windows, maintenance) and an <b>Apps &amp; links</b> page that lists every
/// dock entry with a show/hide switch and a remove button. Mica-backed, and matches the dock's
/// own topmost state so it stays above the dock when the dock is topmost (snapped, or
/// floating with "always on top" on) without needlessly outranking every other app otherwise.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly DockWindow _dock;
    private bool _initializing;

    public SettingsWindow(DockWindow dock)
    {
        _dock = dock;
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        Title = "DockGx Settings";
        SystemBackdrop = new MicaBackdrop();

        // Extend the Mica backdrop under the caption so the title bar matches a native Windows 11
        // window instead of showing an opaque strip.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            // Only outrank other apps when the dock itself currently does — otherwise this
            // window would needlessly float above everything (full-screen apps, video calls)
            // even though a floating, non-topmost dock doesn't need that.
            p.IsAlwaysOnTop = dock.Config.Snapped || dock.Config.AlwaysOnTop;
        }
        appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(appWindow, hwnd, 880, 640);
        WindowChrome.CenterOnCursor(appWindow, windowId);

        LoadGeneral();
        RebuildApps();
        VersionText.Text = "Version " + GetAppVersion();

        // Keep the Apps list in step if the dock changes elsewhere (drag reorder, per-item menu).
        // Deferred so a change we initiate from here doesn't rebuild the tree mid-handler.
        _dock.ItemsChanged += OnDockItemsChanged;
        Closed += (_, _) => _dock.ItemsChanged -= OnDockItemsChanged;
    }

    private void OnDockItemsChanged() => DispatcherQueue.TryEnqueue(RebuildApps);

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // ---- Navigation --------------------------------------------------------

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (GeneralPanel is null || AppsPanel is null || AboutPanel is null)
            return;
        GeneralPanel.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = tag == "apps" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- General page ------------------------------------------------------

    private void LoadGeneral()
    {
        _initializing = true;

        var cfg = _dock.Config;
        EdgeChoice.SelectedIndex = !cfg.Snapped ? 0 : cfg.Edge switch
        {
            DockEdge.Bottom => 1,
            DockEdge.Top => 2,
            DockEdge.Left => 3,
            DockEdge.Right => 4,
            _ => 0,
        };
        AutoHideSwitch.IsOn = cfg.AutoHide;
        AlwaysOnTopSwitch.IsOn = cfg.AlwaysOnTop;
        StartupSwitch.IsOn = StartupService.IsEnabled();

        _initializing = false;
    }

    private void EdgeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        DockEdge? edge = EdgeChoice.SelectedIndex switch
        {
            1 => DockEdge.Bottom,
            2 => DockEdge.Top,
            3 => DockEdge.Left,
            4 => DockEdge.Right,
            _ => null, // Floating
        };
        _dock.SetSnap(edge);
    }

    private void AutoHideSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _dock.SetAutoHide(AutoHideSwitch.IsOn);
    }

    private void AlwaysOnTopSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _dock.SetAlwaysOnTop(AlwaysOnTopSwitch.IsOn);
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _dock.SetLaunchAtStartup(StartupSwitch.IsOn);
    }

    // Resetting wipes every pinned app/file/folder/link with no undo, so — unlike the
    // low-stakes, easily-re-added per-item "Remove" — it gets a confirmation dialog, per the
    // Fluent guidance to confirm destructive, hard-to-recover actions. The safe choice (Cancel)
    // is the default button so an accidental Enter doesn't wipe the dock.
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Nav.XamlRoot,
            Title = "Reset dock to defaults?",
            Content = "This removes every pinned app, file, folder and link you've added, and "
                    + "restores the built-in defaults. This can't be undone.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _dock.ResetToDefaults();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Exit();

    // ---- Apps page ---------------------------------------------------------

    private void AddNew_Click(object sender, RoutedEventArgs e) => _dock.OpenAddNew();

    private void RebuildApps()
    {
        AppsList.Children.Clear();

        if (_dock.AllItems.Count == 0)
        {
            AppsList.Children.Add(new TextBlock
            {
                Text = "No items yet. Use “Add new” to add an app, file, folder or link.",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(2, 8, 0, 0),
            });
            return;
        }

        foreach (var item in _dock.AllItems)
            AppsList.Children.Add(BuildRow(item));
    }

    private Border BuildRow(DockItem item)
    {
        var grid = new Grid { ColumnSpacing = 14, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon (bitmap if resolved, else the kind glyph). Built imperatively rather than via
        // a binding, so it needs its own refresh: icons often resolve asynchronously (shell
        // thumbnail / favicon fetch) after this row is already on screen, and this keeps it
        // in sync with that instead of freezing on whatever was resolved (or not) at build time.
        var iconHost = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        void RenderIcon()
        {
            iconHost.Children.Clear();
            if (item.IconImage is not null)
            {
                iconHost.Children.Add(new Image
                {
                    Source = item.IconImage,
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                });
            }
            else
            {
                iconHost.Children.Add(new FontIcon
                {
                    Glyph = item.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = 18,
                });
            }
        }
        RenderIcon();

        void OnItemPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DockItem.IconImage))
                RenderIcon();
        }
        item.PropertyChanged += OnItemPropertyChanged;

        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);

        // Name + subtitle (kind • target).
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.DisplayName) ? "(unnamed)" : item.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{KindLabel(item.Kind)} • {item.Target}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Actions: the show/hide switch sits directly to the left of the delete button.
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Show/hide switch (On = shown on the dock). No on/off caption — the switch state alone
        // conveys it; the accessible name/tooltip carry the meaning for AT users.
        var toggle = new ToggleSwitch
        {
            IsOn = !item.Hidden,
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(toggle, "Show on the dock");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Show on the dock");
        toggle.Toggled += (s, _) =>
        {
            if (s is ToggleSwitch ts)
                _dock.SetItemHidden(item, !ts.IsOn);
        };
        actions.Children.Add(toggle);

        // Remove.
        var remove = new Button
        {
            Content = new FontIcon
            {
                Glyph = "\uE74D", // Delete
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 14,
            },
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(remove, "Remove from dock");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove from dock");
        remove.Click += (_, _) => _dock.RemoveDockItem(item);
        actions.Children.Add(remove);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var row = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 12, 10),
            Child = grid,
        };
        // The DockItem outlives this row (it's rebuilt wholesale on every RebuildApps), so the
        // subscription above must be torn down explicitly or it leaks a handler per rebuild.
        row.Unloaded += (_, _) => item.PropertyChanged -= OnItemPropertyChanged;
        return row;
    }

    private static string KindLabel(DockItemKind kind) => kind switch
    {
        DockItemKind.Application => "App",
        DockItemKind.File => "File",
        DockItemKind.Folder => "Folder",
        DockItemKind.WebLink => "Web link",
        DockItemKind.Separator => "Separator",
        _ => kind.ToString(),
    };
}
