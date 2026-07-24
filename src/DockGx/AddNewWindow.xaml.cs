using DockGx.Models;
using DockGx.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DockGx;

/// <summary>
/// A Windows-app-style modal for adding a new dock entry: an app, file, folder, web link or a
/// free-form shortcut/command. Mica-backed, centered, and matches the dock's own topmost state
/// (see <see cref="SettingsWindow"/>). Not truly modal to the OS, but presented like the shell's
/// add-item dialogs.
/// </summary>
public sealed partial class AddNewWindow : Window
{
    private enum AddKind { App, File, Folder, Link, Shortcut }

    private readonly DockWindow _dock;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private AddKind _kind = AddKind.App;
    private bool _nameEdited;

    public AddNewWindow(DockWindow dock)
    {
        _dock = dock;
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        Title = "Add to Dock";
        SystemBackdrop = new MicaBackdrop();

        // Match the dock's chosen Light/Dark/System theme so this window reads the same, and keep
        // the caption buttons in step if the OS theme changes while System mode is on.
        ApplyTheme(dock.Config.Theme);
        RootGrid.ActualThemeChanged += (_, _) => ApplyCaptionButtonTheme();

        // Extend the Mica backdrop under the caption so the title bar matches a native Windows 11
        // window instead of showing an opaque strip.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (_appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            // Only outrank other apps when the dock itself currently does — otherwise this
            // dialog would needlessly float above everything (full-screen apps, video calls)
            // even though a floating, non-topmost dock doesn't need that.
            p.IsAlwaysOnTop = dock.Config.Snapped || dock.Config.AlwaysOnTop;
        }
        _appWindow.IsShownInSwitchers = true;

        WindowChrome.SetClientSizeDip(_appWindow, _hwnd, 600, 560);
        WindowChrome.CenterOnCursor(_appWindow, windowId);

        // Track manual edits to the name so an auto-suggested name doesn't clobber user input.
        NameBox.TextChanged += (_, _) =>
        {
            if (NameBox.FocusState != FocusState.Unfocused)
                _nameEdited = !string.IsNullOrEmpty(NameBox.Text);
        };

        SelectType(AddKind.App);
    }

    /// <summary>Applies the given app theme to this window's root (called on open and whenever
    /// the choice changes while this window is open), and re-themes the caption buttons to match.</summary>
    internal void ApplyTheme(DockTheme theme)
    {
        RootGrid.RequestedTheme = DockWindow.ResolveTheme(theme);
        ApplyCaptionButtonTheme();
    }

    private void ApplyCaptionButtonTheme() =>
        WindowChrome.SetTitleBarTheme(_appWindow, dark: RootGrid.ActualTheme != ElementTheme.Light);

    // ---- Type selection ----------------------------------------------------

    private void TypeSelected(object sender, RoutedEventArgs e)
    {
        AddKind kind = ReferenceEquals(sender, AppType) ? AddKind.App
                     : ReferenceEquals(sender, FileType) ? AddKind.File
                     : ReferenceEquals(sender, FolderType) ? AddKind.Folder
                     : ReferenceEquals(sender, LinkType) ? AddKind.Link
                     : AddKind.Shortcut;
        SelectType(kind);
    }

    private void SelectType(AddKind kind)
    {
        _kind = kind;

        // Manual radio-group behavior: exactly one toggle stays checked.
        AppType.IsChecked = kind == AddKind.App;
        FileType.IsChecked = kind == AddKind.File;
        FolderType.IsChecked = kind == AddKind.Folder;
        LinkType.IsChecked = kind == AddKind.Link;
        ShortcutType.IsChecked = kind == AddKind.Shortcut;

        bool canBrowse = kind is AddKind.App or AddKind.File or AddKind.Folder;
        BrowseButton.Visibility = canBrowse ? Visibility.Visible : Visibility.Collapsed;
        ArgsPanel.Visibility = kind == AddKind.App ? Visibility.Visible : Visibility.Collapsed;

        (TargetLabel.Text, TargetBox.PlaceholderText, TargetHint.Text) = kind switch
        {
            AddKind.App => ("Application", "Path to an .exe or .lnk",
                "Pick an application to launch. Shortcuts (.lnk) are resolved automatically."),
            AddKind.File => ("File", "Path to a file",
                "Any file — it opens with its default app."),
            AddKind.Folder => ("Folder", "Path to a folder",
                "Opens the folder in File Explorer."),
            AddKind.Link => ("Web address", "https://example.com",
                "Opens in your default browser. https:// is added if you omit it."),
            _ => ("Target or command", "e.g. ms-settings: or shell:RecycleBinFolder",
                "Any path, URI or shell command. The type is detected automatically."),
        };

        HideError();
    }

    // ---- Browsing ----------------------------------------------------------

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_kind == AddKind.Folder)
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                    SetTarget(folder.Path);
            }
            else
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                if (_kind == AddKind.App)
                {
                    picker.FileTypeFilter.Add(".exe");
                    picker.FileTypeFilter.Add(".lnk");
                    picker.FileTypeFilter.Add(".bat");
                    picker.FileTypeFilter.Add(".cmd");
                }
                else
                {
                    picker.FileTypeFilter.Add("*");
                }
                var file = await picker.PickSingleFileAsync();
                if (file is not null)
                    SetTarget(file.Path);
            }
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open the picker: " + ex.Message);
        }
    }

    private void SetTarget(string path)
    {
        TargetBox.Text = path;
        if (!_nameEdited)
            NameBox.Text = DockItemFactory.SuggestName(path);
        HideError();
    }

    private void TargetBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep the suggested name in step with the typed target until the user edits the name.
        if (!_nameEdited && !string.IsNullOrWhiteSpace(TargetBox.Text))
            NameBox.Text = DockItemFactory.SuggestName(TargetBox.Text.Trim());
        HideError();
    }

    // ---- Commit ------------------------------------------------------------

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var target = TargetBox.Text.Trim();
        if (target.Length == 0)
        {
            ShowError("Enter or choose a target first.");
            return;
        }

        DockItemKind kind;
        switch (_kind)
        {
            case AddKind.App:
                kind = DockItemKind.Application;
                break;
            case AddKind.File:
                kind = DockItemKind.File;
                break;
            case AddKind.Folder:
                kind = DockItemKind.Folder;
                break;
            case AddKind.Link:
                // A "Web link" is specifically an http/https address (arbitrary URIs/commands go
                // through the "Shortcut" type instead). Add the scheme if omitted, then validate —
                // so a WebLink item never carries a non-web scheme that its globe icon would belie.
                if (!target.Contains("://"))
                    target = "https://" + target;
                if (!Uri.TryCreate(target, UriKind.Absolute, out var linkUri) ||
                    (linkUri.Scheme != Uri.UriSchemeHttp && linkUri.Scheme != Uri.UriSchemeHttps))
                {
                    ShowError("Enter a valid web address, e.g. https://example.com. "
                            + "For other URIs or commands, use the Shortcut type.");
                    return;
                }
                target = linkUri.ToString();
                kind = DockItemKind.WebLink;
                break;
            default: // Shortcut: detect from the raw target
                kind = DockItemFactory.Classify(target);
                break;
        }

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
            name = DockItemFactory.SuggestName(target);

        var item = new DockItem
        {
            Kind = kind,
            DisplayName = name,
            Target = target,
        };
        if (kind == DockItemKind.Application && ArgsBox.Text.Trim() is { Length: > 0 } args)
            item.Arguments = args;

        _dock.AddDockItem(item);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Error surface -----------------------------------------------------

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void HideError() => ErrorBar.IsOpen = false;
}
