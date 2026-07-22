using System.Collections.ObjectModel;
using DockGx.Interop;
using DockGx.Models;
using DockGx.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace DockGx;

public sealed partial class DockWindow : Window
{
    private readonly nint _hwnd;
    private readonly WindowId _windowId;
    private readonly AppWindow _appWindow;
    private readonly AcrylicBackdropManager _backdrop;
    private readonly DockConfig _config;

    public ObservableCollection<DockItem> Items { get; } = new();

    public DockWindow()
    {
        InitializeComponent();

        // Content fills the whole window (no reserved title bar). This also makes WinUI
        // size the content island's INPUT site to the full client area — without it, a
        // borderless window can end up with a 0x0 input site that silently swallows all
        // pointer input (no clicks / hover / drag reach the content).
        ExtendsContentIntoTitleBar = true;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(_windowId);

        // Dock-like chrome: borderless, topmost, off the taskbar & Alt-Tab, rounded corners.
        WindowChrome.MakeBorderlessToolWindow(_appWindow, _hwnd);
        WindowChrome.StripFrame(_hwnd);
        WindowChrome.SetRoundedCorners(_hwnd, small: false);
        WindowChrome.RemoveWindowBorder(_hwnd);
        Activated += (_, _) => WindowChrome.RemoveWindowBorder(_hwnd);

        // The Windows 11 taskbar "glass".
        _backdrop = new AcrylicBackdropManager(this);
        _backdrop.TryApply();

        ItemsHost.ItemsSource = Items;

        // Load persisted items/settings (seed defaults on first run).
        _config = DockStore.Load();
        bool firstRun = _config.Items.Count == 0;
        if (firstRun)
            SeedDefaults();
        foreach (var it in _config.Items)
            Items.Add(it);

        DockStrip.RightTapped += DockBackground_RightTapped;
        DockStrip.PointerPressed += Dock_PointerPressed;
        RootGrid.Loaded += (_, _) => QueueRelayout();
        Items.CollectionChanged += (_, _) => { SaveConfig(); QueueRelayout(); };

        if (firstRun)
            SaveConfig(); // materialize the default dock on disk
        Closed += (_, _) =>
        {
            _pollTimer?.Stop();
            _slideTimer?.Stop();
            _dragTimer?.Stop();
            _backdrop.Dispose();
        };

        // Modest initial size so the first frame isn't full-screen before relayout.
        _appWindow.Resize(new SizeInt32(360, 96));

        _ = LoadIconsAsync();
    }

    // ---- Seed content -----------------------------------------------------

    private void SeedDefaults()
    {
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Add(DockItemKind.Application, "File Explorer", Path.Combine(win, "explorer.exe"));
        Add(DockItemKind.Application, "Notepad", Path.Combine(sys, "notepad.exe"));
        Add(DockItemKind.Application, "Calculator", Path.Combine(sys, "calc.exe"));
        Add(DockItemKind.Folder, "Home", profile);
        Add(DockItemKind.WebLink, "WinUI Gallery", "https://github.com/microsoft/WinUI-Gallery");

        void Add(DockItemKind kind, string name, string target) =>
            _config.Items.Add(new DockItem { Kind = kind, DisplayName = name, Target = target });
    }

    private void SaveConfig()
    {
        _config.Items = Items.ToList();
        DockStore.Save(_config);
    }

    private async Task LoadIconsAsync()
    {
        foreach (var item in Items.ToArray())
        {
            var icon = await IconService.LoadIconAsync(item);
            if (icon is not null)
                item.IconImage = icon;
        }
    }

    // ---- Size & position (bottom-center, above the taskbar) ---------------

    private bool _relayoutQueued;

    private void QueueRelayout()
    {
        if (_relayoutQueued)
            return;
        _relayoutQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _relayoutQueued = false;
            try
            {
                UpdateSizeAndPosition();
            }
            catch (Exception ex)
            {
                Diag.Log("UpdateSizeAndPosition failed: " + ex);
            }
        });
    }

    // Dock metrics — keep in sync with the item template, Strip and DockStrip in DockWindow.xaml.
    internal const double CellSize = 40;    // item/gear Grid Width/Height (taskbar-ish)
    internal const double CellSpacing = 4;  // StackLayout + Strip Spacing
    internal const double DividerWidth = 1; // Divider Rectangle width
    internal const double StripPadX = 8;    // DockStrip Padding (left/right)
    internal const double StripPadY = 6;    // DockStrip Padding (top/bottom)

    private void UpdateSizeAndPosition()
    {
        // Compute the strip size analytically from the (uniform) cell metrics. This is
        // deterministic and avoids the window-shrinks-then-clips-content feedback loop
        // that plagues "auto-size window to content" via ActualWidth.
        int n = Items.Count;
        if (n < 1)
            return;

        // content = [n item cells] [gap] [divider] [gap] [gear cell]
        double repeaterW = n * CellSize + (n - 1) * CellSpacing;
        double contentW = repeaterW + CellSpacing + DividerWidth + CellSpacing + CellSize;
        double dipW = contentW + 2 * StripPadX;
        double dipH = CellSize + 2 * StripPadY;

        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)Math.Ceiling(dipW * scale);
        int h = (int)Math.Ceiling(dipH * scale);

        var work = DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Nearest).WorkArea;
        int margin = (int)Math.Round(8 * scale);
        int x, y;

        if (_config.Snapped)
        {
            // Flush to the snapped edge, centered along it.
            (x, y) = _config.Edge switch
            {
                DockEdge.Top => (work.X + (work.Width - w) / 2, work.Y),
                DockEdge.Left => (work.X, work.Y + (work.Height - h) / 2),
                DockEdge.Right => (work.X + work.Width - w, work.Y + (work.Height - h) / 2),
                _ => (work.X + (work.Width - w) / 2, work.Y + work.Height - h), // Bottom
            };
        }
        else if (_config.FreeX is int fx && _config.FreeY is int fy)
        {
            x = Math.Clamp(fx, work.X, work.X + work.Width - w);
            y = Math.Clamp(fy, work.Y, work.Y + work.Height - h);
        }
        else
        {
            // Default free position: bottom-center with a small margin, fully visible.
            x = work.X + (work.Width - w) / 2;
            y = work.Y + work.Height - h - margin;
        }

        _shownRect = new RectInt32(x, y, w, h);
        _work = work;
        _appWindow.MoveAndResize(_shownRect);
        WindowChrome.EnsureTopmost(_hwnd);
        OnRelayoutApplied();
    }

    // Last computed "shown" rect and the current work area (physical px),
    // shared with the auto-hide/snap controller (see DockWindow.AutoHide.cs).
    private RectInt32 _shownRect;
    private RectInt32 _work;

    partial void OnRelayoutApplied();

    // ---- Hover (Windows 11 taskbar style: a rounded highlight, no move/scale) --------

    private static readonly Duration HoverDuration = new(TimeSpan.FromMilliseconds(120));

    private void Item_PointerEntered(object sender, PointerRoutedEventArgs e)
        => FadeHover((Grid)sender, 1.0);

    private void Item_PointerExited(object sender, PointerRoutedEventArgs e)
        => FadeHover((Grid)sender, 0.0);

    private static void FadeHover(Grid root, double opacity)
    {
        var hoverPill = (Border)root.Children[0];
        var sb = new Storyboard();
        var a = new DoubleAnimation
        {
            To = opacity,
            Duration = HoverDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(a, hoverPill);
        Storyboard.SetTargetProperty(a, "Opacity");
        sb.Children.Add(a);
        sb.Begin();
    }

    // ---- Interaction ------------------------------------------------------

    // Launch is triggered from BOTH Tapped and PointerReleased (belt & suspenders — whichever
    // the input stack delivers), debounced so an item never launches twice per click.
    private DateTime _lastLaunch = DateTime.MinValue;

    // NOTE: ItemsRepeater does NOT set FrameworkElement.DataContext on realized items
    // (x:Bind resolves via generated code, not DataContext). We stash the item in Tag via
    // Tag="{x:Bind}" in the template and read it back here.
    private static DockItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as DockItem;

    private void Item_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ItemOf(sender) is DockItem item)
            TryLaunch(item, "Tapped");
    }

    private void Item_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint((UIElement)sender).Properties;
        if (props.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
            return;
        if (ItemOf(sender) is DockItem item)
            TryLaunch(item, "PointerReleased");
    }

    private void TryLaunch(DockItem item, string via)
    {
        if (_dragOccurred)
            return; // the release/tap that ends a drag, not a launch
        var now = DateTime.UtcNow;
        if ((now - _lastLaunch).TotalMilliseconds < 350)
            return; // already launched from the sibling event this click
        _lastLaunch = now;
        Launcher.Launch(item);
    }

    private void Item_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var target = (FrameworkElement)sender;
        if (target.Tag is not DockItem item)
            return;

        int index = Items.IndexOf(item);
        var menu = new MenuFlyout();

        menu.Items.Add(Mi("Open", () => Launcher.Launch(item)));
        menu.Items.Add(Mi("Edit…", () => ShowEditFlyout(target, item)));
        menu.Items.Add(Mi("Rename…", () => ShowRenameFlyout(target, item)));
        menu.Items.Add(new MenuFlyoutSeparator());

        var moveLeft = Mi("Move left", () => MoveItem(item, -1));
        moveLeft.IsEnabled = index > 0;
        menu.Items.Add(moveLeft);

        var moveRight = Mi("Move right", () => MoveItem(item, +1));
        moveRight.IsEnabled = index >= 0 && index < Items.Count - 1;
        menu.Items.Add(moveRight);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Mi("Remove", () => Items.Remove(item)));

        menu.ShowAt(target, e.GetPosition(target));
        e.Handled = true;

        static MenuFlyoutItem Mi(string text, Action onClick)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => onClick();
            return mi;
        }
    }

    private void MoveItem(DockItem item, int direction)
    {
        int i = Items.IndexOf(item);
        int j = i + direction;
        if (i < 0 || j < 0 || j >= Items.Count)
            return;
        Items.Move(i, j); // CollectionChanged -> SaveConfig + relayout
    }

    private void ShowRenameFlyout(FrameworkElement target, DockItem item)
    {
        var box = new TextBox { Text = item.DisplayName, Width = 240 };
        var ok = new Button { Content = "Rename", HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Rename", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            if (name.Length > 0)
            {
                item.DisplayName = name; // observable -> tooltip updates
                SaveConfig();
            }
            flyout.Hide();
        };
        flyout.ShowAt(target);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
    }

    private void ShowEditFlyout(FrameworkElement target, DockItem item)
    {
        var box = new TextBox { Text = item.Target, Width = 320 };
        var ok = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Edit target", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            var t = box.Text.Trim();
            if (t.Length > 0)
            {
                item.Target = t;
                item.Kind = ClassifyTarget(t);
                item.IconImage = null;
                // Re-realize the item so kind-derived visuals (glyph) refresh, then reload icon.
                int i = Items.IndexOf(item);
                if (i >= 0)
                {
                    Items.RemoveAt(i);
                    Items.Insert(i, item);
                }
                _ = LoadOneIconAsync(item);
            }
            flyout.Hide();
        };
        flyout.ShowAt(target);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
    }

    private static DockItemKind ClassifyTarget(string target)
    {
        if (Directory.Exists(target))
            return DockItemKind.Folder;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return DockItemKind.WebLink;
        return Path.GetExtension(target).ToLowerInvariant() switch
        {
            ".exe" or ".lnk" or ".bat" or ".cmd" or ".com" => DockItemKind.Application,
            _ => DockItemKind.File,
        };
    }

    private void DockBackground_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        ShowDockMenu((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        e.Handled = true;
    }

    private void ShowDockMenu(FrameworkElement target, Windows.Foundation.Point at)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(MenuItem("Add App…", async () => await AddAppOrFileAsync(DockItemKind.Application)));
        menu.Items.Add(MenuItem("Add File…", async () => await AddAppOrFileAsync(DockItemKind.File)));
        menu.Items.Add(MenuItem("Add Folder…", async () => await AddFolderAsync()));
        menu.Items.Add(MenuItem("Add Web Link…", () => ShowAddWebLinkFlyout(target)));

        menu.Items.Add(new MenuFlyoutSeparator());

        var snap = new MenuFlyoutSubItem { Text = "Snap to edge (hides behind it)" };
        snap.Items.Add(SnapItem("Bottom", DockEdge.Bottom));
        snap.Items.Add(SnapItem("Top", DockEdge.Top));
        snap.Items.Add(SnapItem("Left", DockEdge.Left));
        snap.Items.Add(SnapItem("Right", DockEdge.Right));
        menu.Items.Add(snap);
        menu.Items.Add(MenuItem("Float (unsnap)", () => SetSnap(null)));

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(MenuItem("Quit DockGx", () => Application.Current.Exit()));

        menu.ShowAt(target, at);

        static MenuFlyoutItem MenuItem(string text, Action onClick)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => onClick();
            return mi;
        }

        MenuFlyoutItem SnapItem(string text, DockEdge edge)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => SetSnap(edge);
            return mi;
        }
    }

    private void Settings_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint((UIElement)sender).Properties;
        if (props.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
            return;
        if (_dragOccurred)
            return;
        ShowDockMenu((FrameworkElement)sender, new Windows.Foundation.Point(0, 0));
        e.Handled = true;
    }

    // ---- Adding items -----------------------------------------------------

    private async Task AddAppOrFileAsync(DockItemKind kind)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        // A .lnk / .exe picked under "Add File" is still really an app to launch.
        string ext = Path.GetExtension(file.Path).ToLowerInvariant();
        if (kind == DockItemKind.File && ext is ".exe" or ".lnk" or ".bat" or ".cmd" or ".com")
            kind = DockItemKind.Application;

        AddItem(new DockItem
        {
            Kind = kind,
            DisplayName = Path.GetFileNameWithoutExtension(file.Path),
            Target = file.Path,
        });
    }

    private async Task AddFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        AddItem(new DockItem
        {
            Kind = DockItemKind.Folder,
            DisplayName = folder.Name,
            Target = folder.Path,
        });
    }

    private void ShowAddWebLinkFlyout(FrameworkElement target)
    {
        var nameBox = new TextBox { PlaceholderText = "Name (optional)", Width = 260 };
        var urlBox = new TextBox { PlaceholderText = "https://example.com", Width = 260 };
        var addButton = new Button { Content = "Add", HorizontalAlignment = HorizontalAlignment.Right };

        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Add Web Link", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(nameBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(addButton);

        var flyout = new Flyout { Content = panel };

        addButton.Click += (_, _) =>
        {
            var url = urlBox.Text.Trim();
            if (url.Length == 0)
                return;
            if (!url.Contains("://"))
                url = "https://" + url;
            var name = string.IsNullOrWhiteSpace(nameBox.Text) ? url : nameBox.Text.Trim();

            AddItem(new DockItem { Kind = DockItemKind.WebLink, DisplayName = name, Target = url });
            flyout.Hide();
        };

        flyout.ShowAt(target);
        urlBox.Focus(FocusState.Programmatic);
    }

    private void AddItem(DockItem item)
    {
        Items.Add(item); // triggers SaveConfig + relayout
        _ = LoadOneIconAsync(item);
    }

    private static async Task LoadOneIconAsync(DockItem item)
    {
        var icon = await IconService.LoadIconAsync(item);
        if (icon is not null)
            item.IconImage = icon;
    }

    // ---- Snap / free positioning -----------------------------------------

    private void SetSnap(DockEdge? edge)
    {
        if (edge is DockEdge e)
        {
            _config.Snapped = true;
            _config.Edge = e;
            _config.FreeX = null;
            _config.FreeY = null;
        }
        else
        {
            // Unsnap: leave it visible where it currently shows.
            _config.Snapped = false;
            _config.FreeX = _shownRect.X;
            _config.FreeY = _shownRect.Y;
        }
        SaveConfig();
        ApplyAutoHide();   // start/stop hide-behind for the new state
        QueueRelayout();   // reposition (snap flush or clamp free)
    }

    // ---- Dragging ---------------------------------------------------------
    //
    // Dragging a top-level window under the cursor is racy with WinUI pointer capture
    // (the cursor outruns the moving window and slips off it, dropping capture). So we
    // poll the global cursor and left-button state on a timer instead — rock solid
    // regardless of which window the cursor is currently over.

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dragTimer;
    private bool _dragging;
    private bool _dragOccurred; // suppress the launch "tap" that may follow a drag
    private NativeMethods.POINT _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private const int DragThreshold = 12;

    private void Dock_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;

        _dragOccurred = false; // reset on every press so a prior drag never eats this click

        // The dock is freely movable — a press anywhere (including on an icon) can begin a
        // drag. Movement past DragThreshold becomes a drag; a press-release without that
        // movement stays a click and launches the item.
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragStartWindow = _appWindow.Position;
        _dragging = false;

        _dragTimer ??= CreateDragTimer();
        if (!_dragTimer.IsRunning)
            _dragTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateDragTimer()
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(8);
        t.Tick += (_, _) => DragTick();
        return t;
    }

    private void DragTick()
    {
        // Button released -> end the gesture.
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0)
        {
            _dragTimer?.Stop();
            bool wasDragging = _dragging;
            _dragging = false;
            if (wasDragging)
                EndDragSnap();
            return;
        }

        NativeMethods.GetCursorPos(out var cur);
        int dx = cur.X - _dragStartCursor.X;
        int dy = cur.Y - _dragStartCursor.Y;

        if (!_dragging)
        {
            if (Math.Abs(dx) <= DragThreshold && Math.Abs(dy) <= DragThreshold)
                return; // still a potential click
            _dragging = true;
            _dragOccurred = true;
            PauseAutoHideForDrag();
        }

        _appWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
    }

    /// <summary>On drop, snap to the nearest work-area edge if close enough, else float free.</summary>
    private void EndDragSnap()
    {
        var pos = _appWindow.Position;
        var size = _appWindow.Size;
        var work = DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Nearest).WorkArea;

        // Only snap when the dock is dropped essentially AT an edge (a small tolerance),
        // otherwise it floats freely wherever it was dropped.
        const int snapThreshold = 12;
        int dLeft = pos.X - work.X;
        int dTop = pos.Y - work.Y;
        int dRight = work.X + work.Width - (pos.X + size.Width);
        int dBottom = work.Y + work.Height - (pos.Y + size.Height);
        int min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

        if (min <= snapThreshold)
        {
            _config.Snapped = true;
            _config.Edge = min == dBottom ? DockEdge.Bottom
                         : min == dTop ? DockEdge.Top
                         : min == dLeft ? DockEdge.Left
                         : DockEdge.Right;
            _config.FreeX = null;
            _config.FreeY = null;
        }
        else
        {
            _config.Snapped = false;
            _config.FreeX = pos.X;
            _config.FreeY = pos.Y;
        }

        SaveConfig();
        ApplyAutoHide();
        QueueRelayout();
    }

    partial void ApplyAutoHide();
    partial void PauseAutoHideForDrag();
}
