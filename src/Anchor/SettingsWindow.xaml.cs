using Anchor.Interop;
using Anchor.Models;
using Anchor.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anchor;

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
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private bool _initializing;

    public SettingsWindow(DockWindow dock)
    {
        _dock = dock;
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        Title = Loc.Get("Settings.Title");
        SystemBackdrop = new MicaBackdrop();

        // Extend the Mica backdrop under the caption so the title bar matches a native Windows 11
        // window instead of showing an opaque strip. Must happen before the chrome theming below:
        // ExtendsContentIntoTitleBar re-extends the DWM frame into the client area, which resets
        // any border color already applied — set it any later and HideWindowBorder's effect
        // gets silently clobbered, leaving the default rim visible along the top edge.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Match the dock's chosen Light/Dark/System theme so the Settings window reads the same,
        // and keep the caption buttons and window frame in step if the OS theme changes while
        // System mode is on.
        ApplyTheme(dock.Config.Theme);
        RootGrid.ActualThemeChanged += (_, _) => ApplyChromeTheme();
        // Re-assert on activation: DWM otherwise restores its default rim on some state changes.
        Activated += (_, _) => ApplyChromeTheme();

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsMaximizable = false;
            // Only outrank other apps when the dock itself currently does — otherwise this
            // window would needlessly float above everything (full-screen apps, video calls)
            // even though a floating, non-topmost dock doesn't need that.
            p.IsAlwaysOnTop = dock.Config.Snapped || dock.Config.AlwaysOnTop;
        }
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 880, 640);
        WindowChrome.CenterOnCursor(_appWindow, windowId);

        BuildLanguageList();
        LoadGeneral();
        RebuildApps();
        VersionText.Text = Loc.Format("About.Version", GetAppVersion());

        // Keep the Apps list in step if the dock changes elsewhere (drag reorder, per-item menu).
        // Deferred so a change we initiate from here doesn't rebuild the tree mid-handler.
        _dock.ItemsChanged += OnDockItemsChanged;
        Closed += (_, _) => _dock.ItemsChanged -= OnDockItemsChanged;
    }

    private void OnDockItemsChanged() => DispatcherQueue.TryEnqueue(RebuildApps);

    /// <summary>Applies the given app theme to this window's root (called on open and whenever
    /// the choice changes elsewhere), and re-themes the window chrome to match.</summary>
    internal void ApplyTheme(DockTheme theme)
    {
        RootGrid.RequestedTheme = DockWindow.ResolveTheme(theme);
        ApplyChromeTheme();
    }

    /// <summary>
    /// Tracks the caption buttons to the effective theme, and hides the DWM window rim by painting
    /// it this theme's surface color. This window extends its content into the title bar, leaving
    /// only the rim's top edge visible — where anything that doesn't match the surface reads as a
    /// stray line above the title bar (see <see cref="WindowChrome.HideWindowBorder"/>).
    /// </summary>
    private void ApplyChromeTheme()
    {
        bool dark = RootGrid.ActualTheme != ElementTheme.Light;
        WindowChrome.SetTitleBarTheme(_appWindow, dark);
        WindowChrome.HideWindowBorder(_hwnd, dark);
    }

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
        ThemeChoice.SelectedIndex = cfg.Theme switch
        {
            DockTheme.Light => 0,
            DockTheme.Dark => 1,
            DockTheme.System => 2,
            _ => 1,
        };
        EdgeChoice.SelectedIndex = !cfg.Snapped ? 0 : cfg.Edge switch
        {
            DockEdge.Bottom => 1,
            DockEdge.Top => 2,
            DockEdge.Left => 3,
            DockEdge.Right => 4,
            _ => 0,
        };
        // Index 0 is "Match Windows"; the rest follow Loc.Available in order.
        int languageIndex = string.IsNullOrEmpty(cfg.Language)
            ? 0
            : Loc.Available.ToList().FindIndex(
                l => string.Equals(l.Code, cfg.Language, StringComparison.OrdinalIgnoreCase)) + 1;
        LanguageChoice.SelectedIndex = languageIndex > 0 ? languageIndex : 0;

        HotkeySwitch.IsOn = cfg.HotkeyEnabled;
        RenderHotkey();

        VerticalSnapSwitch.IsOn = cfg.VerticalWhenSideSnapped;
        AutoHideSwitch.IsOn = cfg.AutoHide;
        AlwaysOnTopSwitch.IsOn = cfg.AlwaysOnTop;
        StartupSwitch.IsOn = StartupService.IsEnabled();

        _initializing = false;
    }

    // ---- Language ----------------------------------------------------------

    private void BuildLanguageList()
    {
        // "Match Windows" first, then every shipped language under its own name — so someone who
        // can't read the language currently in use can still recognize theirs in the list.
        LanguageChoice.Items.Add(new ComboBoxItem { Content = Loc.Get("Language.System") });
        foreach (var language in Loc.Available)
            LanguageChoice.Items.Add(new ComboBoxItem { Content = language.NativeName });
    }

    private void LanguageChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;

        int index = LanguageChoice.SelectedIndex;
        string code = index <= 0 ? string.Empty : Loc.Available[index - 1].Code;
        _dock.SetLanguage(code);

        // Windows opened from here on are translated, but this window's XAML — and the dock
        // strip's — was already loaded, so offer the restart that switches everything over.
        RestartBar.IsOpen = true;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Restart();

    private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
            return;
        var theme = ThemeChoice.SelectedIndex switch
        {
            0 => DockTheme.Light,
            2 => DockTheme.System,
            _ => DockTheme.Dark,
        };
        _dock.SetTheme(theme);
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

    private void VerticalSnapSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        _dock.SetVerticalWhenSideSnapped(VerticalSnapSwitch.IsOn);
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

    // ---- Global keyboard shortcut -----------------------------------------
    //
    // The button doubles as the capture surface: click it (or focus it and press Space) to arm
    // capture, then the next non-modifier key press — together with whatever modifiers are held
    // — becomes the shortcut. Escape cancels; Backspace/Delete clears the shortcut entirely.

    private bool _capturingHotkey;

    private void RenderHotkey()
    {
        HotkeyButton.Content = _capturingHotkey
            ? Loc.Get("Hotkey.Press")
            : _dock.ConfiguredHotkey?.ToString() ?? Loc.Get("Hotkey.None");
        // The shortcut can still be re-assigned while switched off — it just isn't registered —
        // so the button stays enabled and only the registration follows the switch.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            HotkeyButton, Loc.Get("Settings.Hotkey") + ": " + HotkeyButton.Content);
    }

    private void HotkeySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        if (!_dock.SetHotkeyEnabled(HotkeySwitch.IsOn))
            ShowHotkeyMessage("Hotkey.Taken");
        else
            HotkeyBar.IsOpen = false;
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
            return;
        _capturingHotkey = true;
        HotkeyBar.IsOpen = false;
        RenderHotkey();
        HotkeyButton.Focus(FocusState.Programmatic);
    }

    // PreviewKeyDown (not KeyDown): it runs before the platform gets a chance to treat the press
    // as an access key or navigation, which is exactly what capturing a raw combination needs.
    private void HotkeyButton_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!_capturingHotkey)
            return;

        var key = e.Key;
        e.Handled = true;

        // A modifier on its own isn't the shortcut yet — keep waiting for the real key.
        if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu
                or Windows.System.VirtualKey.Shift or Windows.System.VirtualKey.LeftWindows
                or Windows.System.VirtualKey.RightWindows)
            return;

        if (key == Windows.System.VirtualKey.Escape)
        {
            EndCapture();
            return;
        }

        if (key is Windows.System.VirtualKey.Back or Windows.System.VirtualKey.Delete)
        {
            _dock.SetHotkey(null);
            EndCapture();
            return;
        }

        // Not a key Anchor can bind (see HotkeyGesture's fixed table) — ignore it and stay armed
        // rather than committing something the config can't round-trip.
        if (HotkeyGesture.Name((uint)key) is null)
            return;

        var gesture = new HotkeyGesture(HeldModifiers(), (uint)key);
        if (!gesture.IsValid)
        {
            ShowHotkeyMessage("Hotkey.NeedModifier"); // e.g. a bare "A", or Shift+A
            return;
        }

        bool registered = _dock.SetHotkey(gesture);
        EndCapture();
        if (!registered && HotkeySwitch.IsOn)
            ShowHotkeyMessage("Hotkey.Taken");
    }

    /// <summary>Which modifiers are physically held right now (see NativeMethods.VK_*).</summary>
    private static HotkeyModifiers HeldModifiers()
    {
        var mods = HotkeyModifiers.None;
        if (Down(NativeMethods.VK_CONTROL))
            mods |= HotkeyModifiers.Control;
        if (Down(NativeMethods.VK_MENU))
            mods |= HotkeyModifiers.Alt;
        if (Down(NativeMethods.VK_SHIFT))
            mods |= HotkeyModifiers.Shift;
        if (Down(NativeMethods.VK_LWIN) || Down(NativeMethods.VK_RWIN))
            mods |= HotkeyModifiers.Windows;
        return mods;

        static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    // Clicking or tabbing away abandons a capture in progress, so the button never sits in
    // "Press keys…" forever.
    private void HotkeyButton_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
            EndCapture();
    }

    private void EndCapture()
    {
        _capturingHotkey = false;
        RenderHotkey();
    }

    private void ShowHotkeyMessage(string key)
    {
        HotkeyBar.Message = Loc.Get(key);
        HotkeyBar.IsOpen = true;
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
            Title = Loc.Get("Reset.Title"),
            Content = Loc.Get("Reset.Body"),
            PrimaryButtonText = Loc.Get("Reset.Confirm"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _dock.ResetToDefaults();
    }

    // ---- Apps page ---------------------------------------------------------

    private void AddNew_Click(object sender, RoutedEventArgs e) => _dock.OpenAddNew();

    private void RebuildApps()
    {
        AppsList.Children.Clear();

        if (_dock.AllItems.Count == 0)
        {
            AppsList.Children.Add(new TextBlock
            {
                Text = Loc.Get("Apps.Empty"),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(2, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
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
            Text = string.IsNullOrWhiteSpace(item.DisplayName)
                ? Loc.Get("Apps.Unnamed")
                : item.DisplayName,
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
        ToolTipService.SetToolTip(toggle, Loc.Get("Apps.ShowOnDock"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, Loc.Get("Apps.ShowOnDock"));
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
        ToolTipService.SetToolTip(remove, Loc.Get("Apps.RemoveFromDock"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, Loc.Get("Apps.RemoveFromDock"));
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
        DockItemKind.Application => Loc.Get("Kind.App"),
        DockItemKind.File => Loc.Get("Kind.File"),
        DockItemKind.Folder => Loc.Get("Kind.Folder"),
        DockItemKind.WebLink => Loc.Get("Kind.WebLink"),
        DockItemKind.Separator => Loc.Get("Kind.Separator"),
        _ => kind.ToString(),
    };
}
