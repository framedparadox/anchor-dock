using System.Collections.ObjectModel;
using DockGx.Interop;
using DockGx.Models;
using DockGx.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace DockGx;

public sealed partial class DockWindow : Window
{
    private readonly nint _hwnd;
    private readonly WindowId _windowId;
    private readonly AppWindow _appWindow;
    private readonly AcrylicBackdropManager? _backdrop;
    private readonly DockConfig _config;

    /// <summary>The visible items rendered on the dock (a projection of the master list that
    /// excludes hidden items). Reordering operates on this collection.</summary>
    public ObservableCollection<DockItem> Items { get; } = new();

    /// <summary>The full, ordered item list (including hidden items) — the persisted source
    /// of truth, surfaced to the Settings window.</summary>
    public IReadOnlyList<DockItem> AllItems => _config.Items;

    public DockConfig Config => _config;

    /// <summary>Raised whenever the item set changes (add / remove / hide / reorder) so an open
    /// Settings window can refresh its Apps list.</summary>
    public event Action? ItemsChanged;

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

        // Respect the High Contrast accessibility theme: the acrylic "glass" and the
        // pinned-Dark styling are suppressed so the shell's high-contrast system colors
        // come through and the dock stays legible.
        if (IsHighContrast())
        {
            RootGrid.RequestedTheme = ElementTheme.Default; // follow the system HC theme
            if (Application.Current.Resources.TryGetValue(
                    "SolidBackgroundFillColorBaseBrush", out var bg) && bg is Brush brush)
                RootGrid.Background = brush; // opaque, since there is no backdrop behind it
        }
        else
        {
            // The Windows 11 taskbar "glass".
            _backdrop = new AcrylicBackdropManager(this);
            _backdrop.TryApply();
        }

        ItemsHost.ItemsSource = Items;

        // Load persisted items/settings (seed defaults only on the very first run — never
        // after the user has intentionally emptied the dock).
        _config = DockStore.Load();
        bool firstRun = !_config.Seeded;
        if (firstRun && _config.Items.Count == 0)
            SeedDefaults();
        _config.Seeded = true;
        RebuildVisible();

        // ContextRequested (rather than RightTapped) so the dock menu is reachable by the
        // keyboard too (Menu key / Shift+F10), not only by right-click.
        DockStrip.ContextRequested += DockBackground_ContextRequested;
        // Items are Buttons now and mark PointerPressed handled for their own press visual;
        // subscribe with handledEventsToo so a press that starts on an icon still begins a
        // gesture (item reorder for icons, window drag for the background).
        DockStrip.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(Dock_PointerPressed), handledEventsToo: true);
        RootGrid.Loaded += (_, _) => QueueRelayout();

        if (firstRun)
            SaveConfig(); // materialize the default dock on disk
        Closed += (_, _) =>
        {
            _pollTimer?.Stop();
            _slideTimer?.Stop();
            _dragTimer?.Stop();
            _backdrop?.Dispose();
        };

        // Modest initial size so the first frame isn't full-screen before relayout.
        _appWindow.Resize(new SizeInt32(360, 96));

        _ = LoadIconsAsync();
    }

    // ---- Master / visible list sync ---------------------------------------

    /// <summary>Rebuilds the visible collection from the master list, honoring Hidden flags.</summary>
    private void RebuildVisible()
    {
        Items.Clear();
        foreach (var it in _config.Items)
            if (!it.Hidden)
                Items.Add(it);
    }

    /// <summary>
    /// Pushes a visible reorder back into the master list. Hidden items stay anchored at their
    /// absolute master indices; each visible slot is refilled, in order, from the (reordered)
    /// visible collection. Deterministic and stable.
    /// </summary>
    private void SyncMasterFromVisible()
    {
        var q = new Queue<DockItem>(Items);
        for (int i = 0; i < _config.Items.Count && q.Count > 0; i++)
            if (!_config.Items[i].Hidden)
                _config.Items[i] = q.Dequeue();
    }

    // ---- Public API (used by the Settings / Add windows) ------------------

    public void AddDockItem(DockItem item)
    {
        _config.Items.Add(item);
        if (!item.Hidden)
            Items.Add(item);
        PersistAndRelayout();
        RaiseItemsChanged();
        _ = LoadOneIconAsync(item);
    }

    public void RemoveDockItem(DockItem item)
    {
        _config.Items.Remove(item);
        Items.Remove(item);
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    public void SetItemHidden(DockItem item, bool hidden)
    {
        if (item.Hidden == hidden)
            return;
        item.Hidden = hidden;
        RebuildVisible();
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    // ---- Drag & drop onto the dock ---------------------------------------
    //
    // Drop apps, shortcuts (.lnk), files or folders from Explorer / the desktop straight onto
    // the dock to add them; a dropped URL (from a browser) becomes a web link.

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        var data = e.DataView;
        if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems) ||
            data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink) ||
            data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            if (e.DragUIOverride is { } ui)
            {
                ui.Caption = "Add to dock";
                ui.IsCaptionVisible = true;
                ui.IsGlyphVisible = true;
            }
        }
        else
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var data = e.DataView;
            if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                foreach (var storageItem in await data.GetStorageItemsAsync())
                {
                    var target = storageItem.Path;
                    if (string.IsNullOrWhiteSpace(target))
                        continue;
                    AddDockItem(new DockItem
                    {
                        Kind = DockItemFactory.Classify(target),
                        DisplayName = DockItemFactory.SuggestName(target),
                        Target = target,
                    });
                }
            }
            else if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink))
            {
                AddWebLinkFromDrop((await data.GetWebLinkAsync())?.ToString());
            }
            else if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                AddWebLinkFromDrop(await data.GetTextAsync());
            }
        }
        catch (Exception ex)
        {
            Diag.Log("Drop failed: " + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void AddWebLinkFromDrop(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        text = text.Trim();

        if (!text.Contains("://"))
        {
            // Only promote bare text to a URL when it plausibly is one (a single dotted token).
            if (text.Contains(' ') || !text.Contains('.'))
                return;
            text = "https://" + text;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        AddDockItem(new DockItem
        {
            Kind = DockItemKind.WebLink,
            DisplayName = DockItemFactory.SuggestName(uri.ToString()),
            Target = uri.ToString(),
        });
    }

    public void OpenAddNew()
    {
        if (_addNewWindow is not null)
        {
            _addNewWindow.Activate();
            return;
        }
        _addNewWindow = new AddNewWindow(this);
        _addNewWindow.Closed += (_, _) => _addNewWindow = null;
        _addNewWindow.Activate();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private AddNewWindow? _addNewWindow;
    private SettingsWindow? _settingsWindow;

    private void RaiseItemsChanged() => ItemsChanged?.Invoke();

    private void PersistAndRelayout()
    {
        SaveConfig();
        QueueRelayout();
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

    private void SaveConfig() => DockStore.Save(_config);

    private async Task LoadIconsAsync()
    {
        foreach (var item in _config.Items.ToArray())
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
    internal const double CellSize = 40;      // item/gear Grid Width/Height (taskbar-ish)
    internal const double CellSpacing = 4;    // StackLayout + Strip Spacing
    internal const double DividerWidth = 1;   // Divider Rectangle width
    internal const double StripPadX = 8;      // DockStrip Padding (left/right)
    internal const double StripPadY = 6;      // DockStrip Padding (top/bottom)
    internal const double AddNewWidth = 116;  // "+ Add New" empty-state pill width

    /// <summary>Shows the "+ Add New" pill (and hides the item strip) when the dock is empty.</summary>
    private void UpdateEmptyState()
    {
        bool empty = Items.Count == 0;
        AddNewButton.Width = AddNewWidth;
        AddNewButton.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ItemsHost.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSizeAndPosition()
    {
        UpdateEmptyState();

        // Compute the strip size analytically from the (uniform) cell metrics. This is
        // deterministic and avoids the window-shrinks-then-clips-content feedback loop
        // that plagues "auto-size window to content" via ActualWidth.
        int n = Items.Count; // visible items
        bool empty = n == 0;

        // content = [items OR add-new] [gap] [divider] [gap] [gear cell]
        double coreW = empty ? AddNewWidth : n * CellSize + (n - 1) * CellSpacing;
        double contentW = coreW + CellSpacing + DividerWidth + CellSpacing + CellSize;
        double dipW = contentW + 2 * StripPadX;
        double dipH = CellSize + 2 * StripPadY;

        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)Math.Ceiling(dipW * scale);
        int h = (int)Math.Ceiling(dipH * scale);

        // Which monitor is the dock on? Prefer the display under its stored position (so a dock
        // dropped on a secondary screen stays and hides on THAT screen); otherwise the display
        // nearest the window. This is the fix for snap/hide "jumping" across monitors.
        var display = ResolveDisplay(w, h);
        var work = display.WorkArea;      // excludes the taskbar — where the dock shows
        _outer = display.OuterBounds;     // full monitor — where the dock hides (behind the taskbar)
        int margin = (int)Math.Round(8 * scale);
        int x, y;

        if (_config.Snapped)
        {
            // Flush to the snapped edge; along the edge, keep the position the user placed it at
            // (falling back to centered only if it has never been positioned).
            (x, y) = _config.Edge switch
            {
                DockEdge.Top => (AlongX(work, w), work.Y),
                DockEdge.Left => (work.X, AlongY(work, h)),
                DockEdge.Right => (work.X + work.Width - w, AlongY(work, h)),
                _ => (AlongX(work, w), work.Y + work.Height - h), // Bottom
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

    // Along-edge coordinates derived from the stored placement, clamped to the work area.
    private int AlongX(RectInt32 work, int w) =>
        _config.FreeX is int fx
            ? Math.Clamp(fx, work.X, work.X + Math.Max(0, work.Width - w))
            : work.X + (work.Width - w) / 2;

    private int AlongY(RectInt32 work, int h) =>
        _config.FreeY is int fy
            ? Math.Clamp(fy, work.Y, work.Y + Math.Max(0, work.Height - h))
            : work.Y + (work.Height - h) / 2;

    /// <summary>Resolves the monitor the dock belongs to (multi-monitor safe).</summary>
    private DisplayArea ResolveDisplay(int w, int h)
    {
        DisplayArea? da = null;
        if (_config.FreeX is int fx && _config.FreeY is int fy)
        {
            var center = new PointInt32(fx + w / 2, fy + h / 2);
            da = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
        }
        da ??= DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Nearest);
        return da;
    }

    // Last computed "shown" rect, the current work area, and the full monitor bounds (physical
    // px), shared with the auto-hide/snap controller (see DockWindow.AutoHide.cs). The dock shows
    // within the work area but hides against the outer (screen) edge so a bottom-snapped dock
    // tucks behind the taskbar.
    private RectInt32 _shownRect;
    private RectInt32 _work;
    private RectInt32 _outer;

    partial void OnRelayoutApplied();

    // ---- Interaction ------------------------------------------------------
    //
    // Hover, pressed and keyboard-focus visuals come from Button itself (the Windows 11
    // subtle-fill control states), so there is no hand-rolled hover animation here.

    // NOTE: ItemsRepeater does NOT set FrameworkElement.DataContext on realized items
    // (x:Bind resolves via generated code, not DataContext). We stash the item in Tag via
    // Tag="{x:Bind}" in the template and read it back here.
    private static DockItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as DockItem;

    // Button.Click fires for a pointer click AND a keyboard invoke (Space/Enter), so this one
    // handler covers mouse, touch and keyboard. Suppressed after a drag gesture.
    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (_dragOccurred)
            return; // the click that ends a drag/reorder, not a launch
        if (ItemOf(sender) is DockItem item)
            Launcher.Launch(item);
    }

    // ContextRequested fires for right-click and for the keyboard context-menu gesture
    // (Menu key / Shift+F10), so the per-item menu is reachable without a mouse.
    private void Item_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
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
        menu.Items.Add(Mi("Hide", () => SetItemHidden(item, true)));
        menu.Items.Add(Mi("Remove", () => RemoveDockItem(item)));

        if (e.TryGetPosition(target, out var pos))
            menu.ShowAt(target, pos);
        else
            menu.ShowAt(target); // keyboard-invoked: let the platform place it on the element
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
        Items.Move(i, j);
        SyncMasterFromVisible();
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    private void ShowRenameFlyout(FrameworkElement target, DockItem item)
    {
        var box = new TextBox { Text = item.DisplayName, Width = 240 };
        var ok = new Button { Content = "Rename", HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(FlyoutHeader("Rename"));
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
                RaiseItemsChanged();
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
        panel.Children.Add(FlyoutHeader("Edit target"));
        panel.Children.Add(box);
        panel.Children.Add(ok);

        var flyout = new Flyout { Content = panel };
        ok.Click += (_, _) =>
        {
            var t = box.Text.Trim();
            if (t.Length > 0)
            {
                item.Target = t;
                item.Kind = DockItemFactory.Classify(t);
                item.IconImage = null;
                // Re-realize the item so kind-derived visuals (glyph) refresh, then reload icon.
                int i = Items.IndexOf(item);
                if (i >= 0)
                {
                    Items.RemoveAt(i);
                    Items.Insert(i, item);
                }
                SaveConfig();
                RaiseItemsChanged();
                _ = LoadOneIconAsync(item);
            }
            flyout.Hide();
        };
        flyout.ShowAt(target);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
    }

    private void DockBackground_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        if (e.TryGetPosition(fe, out var pos))
            ShowDockMenu(fe, pos);
        else
            ShowDockMenu(fe, new Windows.Foundation.Point(0, 0));
        e.Handled = true;
    }

    private void ShowDockMenu(FrameworkElement target, Windows.Foundation.Point at)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(MenuItem("Add New…", OpenAddNew));
        menu.Items.Add(MenuItem("Settings…", OpenSettings));

        menu.Items.Add(new MenuFlyoutSeparator());

        var snap = new MenuFlyoutSubItem { Text = "Snap to edge" };
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_dragOccurred)
            return; // the click that ends a drag, not a menu open
        OpenSettings();
    }

    private void AddNew_Click(object sender, RoutedEventArgs e)
    {
        if (_dragOccurred)
            return;
        OpenAddNew();
    }

    private static async Task LoadOneIconAsync(DockItem item)
    {
        var icon = await IconService.LoadIconAsync(item);
        if (icon is not null)
            item.IconImage = icon;
    }

    // ---- Snap / free positioning -----------------------------------------

    public void SetSnap(DockEdge? edge)
    {
        if (edge is DockEdge e)
        {
            _config.Snapped = true;
            _config.Edge = e;
            // Keep the current on-screen position as the placement anchor so it snaps flush
            // without jumping to the screen center.
            _config.FreeX = _shownRect.X;
            _config.FreeY = _shownRect.Y;
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

    public void SetAutoHide(bool on)
    {
        _config.AutoHide = on;
        SaveConfig();
        ApplyAutoHide();
        QueueRelayout();
    }

    public void SetLaunchAtStartup(bool on)
    {
        _config.LaunchAtStartup = on;
        StartupService.SetEnabled(on);
        SaveConfig();
    }

    /// <summary>Clears the dock, re-seeds the default items and returns to a floating position.</summary>
    public void ResetToDefaults()
    {
        _config.Items.Clear();
        SeedDefaults();
        _config.Snapped = false;
        _config.FreeX = null;
        _config.FreeY = null;
        RebuildVisible();
        SaveConfig();
        ApplyAutoHide();
        QueueRelayout();
        RaiseItemsChanged();
        _ = LoadIconsAsync();
    }

    // ---- Dragging ---------------------------------------------------------
    //
    // Dragging a top-level window under the cursor is racy with WinUI pointer capture
    // (the cursor outruns the moving window and slips off it, dropping capture). So we
    // poll the global cursor and left-button state on a timer instead — rock solid
    // regardless of which window the cursor is currently over.
    //
    // A press that starts on an item reorders THAT item (never moves the dock); a press on
    // the background / divider / gear moves the whole dock window.

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dragTimer;
    private bool _dragging;
    private bool _dragOccurred;  // suppress the launch "tap" that may follow a drag/reorder
    private NativeMethods.POINT _dragStartCursor;
    private PointInt32 _dragStartWindow;
    private DockItem? _reorderItem; // non-null while a press started on an item
    private double _reorderHostLeftPx;
    private double _reorderPitchPx;
    private const int DragThreshold = 12;

    private void Dock_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint((UIElement)sender).Properties.IsLeftButtonPressed)
            return;

        _dragOccurred = false; // reset on every press so a prior drag never eats this click
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragging = false;

        // Did the press land on a dock item? If so this gesture is an item reorder, not a
        // window move — "don't allow dragging of the dock when moving the apps / items".
        _reorderItem = FindItemFromSource(e.OriginalSource);
        if (_reorderItem is null)
            _dragStartWindow = _appWindow.Position;

        _dragTimer ??= CreateDragTimer();
        if (!_dragTimer.IsRunning)
            _dragTimer.Start();
    }

    /// <summary>Walks up from the pressed element to find the dock item it belongs to (if any).</summary>
    private static DockItem? FindItemFromSource(object source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.Tag is DockItem item)
                return item;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
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

            if (_reorderItem is not null)
            {
                _reorderItem = null;
                if (wasDragging)
                    EndItemReorder();
                return;
            }

            if (wasDragging)
            {
                EndDragSnap();
                // Clear the drag flag once the trailing click (the pointer-release that ended
                // the drag) has been delivered and suppressed. Low priority runs after input
                // delivery, so a later keyboard invoke (Enter/Space) is not blocked.
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _dragOccurred = false);
            }
            return;
        }

        NativeMethods.GetCursorPos(out var cur);
        int dx = cur.X - _dragStartCursor.X;
        int dy = cur.Y - _dragStartCursor.Y;

        // ---- Item reorder path ----
        if (_reorderItem is not null)
        {
            if (!_dragging)
            {
                if (Math.Abs(dx) <= DragThreshold && Math.Abs(dy) <= DragThreshold)
                    return; // still a potential click/launch
                _dragging = true;
                _dragOccurred = true;
                PauseAutoHideForDrag();
                BeginItemReorder();
            }
            UpdateItemReorder(cur.X);
            return;
        }

        // ---- Window move path ----
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

    // ---- Item reorder mechanics ----

    private void BeginItemReorder()
    {
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        // The window is stationary during a reorder, so the strip's screen geometry is fixed:
        // capture the item host's left edge and per-cell pitch once, in physical pixels.
        var origin = ItemsHost.TransformToVisual(RootGrid)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        _reorderHostLeftPx = _appWindow.Position.X + origin.X * scale;
        _reorderPitchPx = (CellSize + CellSpacing) * scale;
    }

    private void UpdateItemReorder(int cursorScreenX)
    {
        int count = Items.Count;
        if (count < 2 || _reorderItem is null || _reorderPitchPx <= 0)
            return;

        int from = Items.IndexOf(_reorderItem);
        if (from < 0)
            return;

        double rel = cursorScreenX - _reorderHostLeftPx;
        int target = (int)Math.Floor(rel / _reorderPitchPx);
        target = Math.Clamp(target, 0, count - 1);
        if (target != from)
            Items.Move(from, target);
    }

    private void EndItemReorder()
    {
        SyncMasterFromVisible();
        SaveConfig();
        RaiseItemsChanged();
        ResumeAutoHideAfterDrag();
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _dragOccurred = false);
    }

    /// <summary>On drop, snap to the nearest work-area edge if close enough, else float free.</summary>
    private void EndDragSnap()
    {
        var pos = _appWindow.Position;
        var size = _appWindow.Size;
        var work = DisplayArea.GetFromWindowId(_windowId, DisplayAreaFallback.Nearest).WorkArea;

        // Only snap when the dock is dropped essentially AT an edge (a small tolerance),
        // otherwise it floats freely wherever it was dropped.
        const int snapThreshold = 16;
        int dLeft = pos.X - work.X;
        int dTop = pos.Y - work.Y;
        int dRight = work.X + work.Width - (pos.X + size.Width);
        int dBottom = work.Y + work.Height - (pos.Y + size.Height);
        int min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

        // Remember exactly where it was dropped: this anchors the snapped position along the
        // edge (so it hides where you left it) and identifies the monitor it lives on.
        _config.FreeX = pos.X;
        _config.FreeY = pos.Y;

        if (min <= snapThreshold)
        {
            _config.Snapped = true;
            _config.Edge = min == dBottom ? DockEdge.Bottom
                         : min == dTop ? DockEdge.Top
                         : min == dLeft ? DockEdge.Left
                         : DockEdge.Right;
        }
        else
        {
            _config.Snapped = false;
        }

        SaveConfig();
        ApplyAutoHide();
        QueueRelayout();
    }

    // ---- Accessibility / small UI helpers ---------------------------------

    /// <summary>
    /// True when Windows is using a High Contrast theme. Guarded: on any failure (e.g. the
    /// setting is unavailable in this host) we assume false and keep the normal glass styling.
    /// </summary>
    private static bool IsHighContrast()
    {
        try { return new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast; }
        catch { return false; }
    }

    /// <summary>A flyout section header using the Fluent "body strong" type-ramp style.</summary>
    private static TextBlock FlyoutHeader(string text)
    {
        var tb = new TextBlock { Text = text };
        if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var s) &&
            s is Style style)
            tb.Style = style;
        else
            tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return tb;
    }

    partial void ApplyAutoHide();
    partial void PauseAutoHideForDrag();
    partial void ResumeAutoHideAfterDrag();
}
