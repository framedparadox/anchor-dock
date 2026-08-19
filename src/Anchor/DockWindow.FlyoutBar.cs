using Anchor.Interop;
using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Anchor;

/// <summary>
/// The <b>fly-out bar</b>: a second dock strip that opens out of a single icon. A group's contents
/// and a folder's contents both render into one, so opening either reads as the dock itself
/// growing a branch at that icon rather than as a menu or a list appearing next to it.
/// Partial of <see cref="DockWindow"/>.
/// <para>
/// The bar runs <em>the same way the dock does</em> — a horizontal bar over a horizontal dock, a
/// vertical one beside a side-snapped vertical dock — and always extends away from whichever
/// screen edge the dock is snapped to, so it can never open into that edge. That parallel run is
/// the whole point: what appears is a second dock floating over the main one, rather than a
/// column of icons sprouting sideways out of it. Its cells are built from the same metrics and the
/// same chrome style as the strip's own (<c>DockGlassButtonStyle</c>), so the two stay identical
/// at every density.
/// </para>
/// <para>
/// It keeps the platform <see cref="FlyoutPresenter"/> as its surface rather than hand-rolling a
/// popup: the presenter already carries the Windows 11 flyout material, shadow and light-dismiss
/// behavior, and re-creating those would be a worse copy of the same thing. Only its chrome is
/// restyled — the strip's corner radius and padding, no border — so what shows is the dock's
/// glass bar and not a boxed menu around it.
/// </para>
/// </summary>
public sealed partial class DockWindow
{
    /// <summary>Cell-to-cell gap inside the bar; matches the strip's own <c>Spacing</c>.</summary>
    private const double BarSpacing = 4;

    /// <summary>
    /// Clear air between the bar and the dock's own border. The bar is anchored on the dock
    /// <em>window</em> rather than on the clicked icon precisely so this can be guaranteed:
    /// anchoring on the icon put the bar's edge at the icon's edge, which is
    /// <see cref="StripPadY"/> inside the window, so it overlapped the dock's glass rim by that
    /// much. Measuring from the window edge instead means the two never touch, whatever the
    /// density or padding.
    /// </summary>
    private const double BarGap = 4;

    /// <summary>
    /// How long the bar may grow before it scrolls. A group holds what the user put in it, but a
    /// folder fly-out lists whatever is on disk, which is unbounded — so the bar caps out well
    /// inside a screen rather than running off one.
    /// </summary>
    private const double BarMaxExtent = 560;

    /// <summary>True when the bar should stack its cells vertically: exactly when the dock itself
    /// does, since the bar is a second dock laid out alongside the first.</summary>
    private bool BarIsVertical => IsVertical;

    /// <summary>
    /// What a bar shows and what its cells do.
    /// </summary>
    /// <param name="Items">The cells, in order.</param>
    /// <param name="EmptyText">Shown in place of the cells when <paramref name="Items"/> is empty.</param>
    /// <param name="OnActivate">Called when a cell is clicked; returning true means the caller
    /// handled it and the default launch is skipped (a folder bar drills into a subfolder that
    /// way instead of opening it in Explorer). Null launches everything.</param>
    /// <param name="OnContext">Called for a right-click or the Menu key on a cell, so each caller
    /// supplies its own per-entry menu — a group child's differs from a folder entry's. Null for
    /// no menu at all.</param>
    /// <param name="OnDragOut">Called when a cell is dragged clear of the bar, which is how an
    /// item is taken back out of a group. Null means the bar has nothing to drag out of it (a
    /// folder bar is a view of the disk, not a container).</param>
    /// <param name="KeepDockInteractive">Lets pointer input over the dock strip keep reaching the
    /// dock while the bar is up, instead of being swallowed by the fly-out's light-dismiss layer
    /// — see <see cref="ShowDockBarFlyout"/>. A group bar needs it (hover has to keep working
    /// underneath it); a folder bar, which is click-only, does not.</param>
    private sealed record DockBarOptions(
        IReadOnlyList<DockItem> Items,
        string EmptyText,
        Func<DockItem, bool>? OnActivate = null,
        Action<FrameworkElement, DockItem, Flyout, ContextRequestedEventArgs>? OnContext = null,
        Action<DockItem>? OnDragOut = null,
        bool KeepDockInteractive = false);

    /// <summary>Opens a bar for the icon <paramref name="anchor"/>, clear of the dock's border.</summary>
    private Flyout ShowDockBarFlyout(FrameworkElement anchor, DockBarOptions options)
    {
        var placement = GroupFlyoutPlacement;
        var flyout = new Flyout
        {
            Placement = placement,
            FlyoutPresenterStyle = BarPresenterStyle(),
            // The dock window is one cell tall and only as wide as its own icons; a bar running
            // parallel to it is wider than that and has to open *outside* it. Unconstrained, the
            // popup gets its own top-level window and floats over whatever is at that screen
            // position instead of being clipped to the strip it came out of.
            ShouldConstrainToRootBounds = false,
        };

        // A light-dismiss fly-out puts an invisible input layer over everything outside itself,
        // and the dock strip is outside itself. Left alone, opening the bar cuts the strip off
        // from the pointer entirely: TrackStripPointer stops being called, the strip reports the
        // cursor as having left, and a hover-opened bar closes a beat later — whereupon the
        // strip gets the pointer back, sees the cursor still parked on the group icon, and opens
        // the bar again. That loop is what made a hovered group flicker open and shut. Naming the
        // dock's root as the pass-through element carves the dock back out of that layer (the
        // same mechanism a MenuBar uses to swap menus under the cursor), so hover tracking and
        // clicks on the strip keep working while the bar is up. Must be set before ShowAt.
        if (options.KeepDockInteractive)
            flyout.OverlayInputPassThroughElement = RootGrid;

        flyout.Content = options.Items.Count == 0
            ? EmptyBarContent(options.EmptyText)
            : BuildBar(flyout, options);

        // The dock auto-hides on a cursor-position poll, and the cursor is about to leave the
        // strip for the bar. Hold it out while the bar is up, then re-arm on close.
        PauseAutoHideForDrag();
        flyout.Closed += (_, _) => ResumeAutoHideAfterDrag();

        flyout.ShowAt(RootGrid, new FlyoutShowOptions
        {
            Placement = placement,
            Position = BarAnchorPoint(anchor, placement),
        });
        return flyout;
    }

    /// <summary>
    /// Where the bar hangs from, in <c>RootGrid</c> (whole-window) coordinates: lined up with the
    /// centre of the icon that opened it along the dock's flow, and <see cref="BarGap"/> clear of
    /// the window's border across it. Splitting the two axes like this is what gives "grows out of
    /// this icon" and "never touches the dock" at the same time — anchoring straight on the icon
    /// can only give the first.
    /// </summary>
    private Windows.Foundation.Point BarAnchorPoint(FrameworkElement anchor, FlyoutPlacementMode placement)
    {
        var bounds = anchor.TransformToVisual(RootGrid).TransformBounds(
            new Windows.Foundation.Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
        double centreX = bounds.X + bounds.Width / 2;
        double centreY = bounds.Y + bounds.Height / 2;

        return placement switch
        {
            FlyoutPlacementMode.Bottom => new(centreX, RootGrid.ActualHeight + BarGap),
            FlyoutPlacementMode.Left => new(-BarGap, centreY),
            FlyoutPlacementMode.Right => new(RootGrid.ActualWidth + BarGap, centreY),
            _ => new(centreX, -BarGap), // Top
        };
    }

    /// <summary>
    /// Strips the flyout presenter back to the dock strip's own chrome: the strip's corner radius
    /// and padding, no border, and none of the presenter's default minimum size (which is sized
    /// for a menu and would leave a wide empty margin around a single column of 40px cells) — and
    /// the dock's own glass in place of the presenter's default flyout material, so the bar is the
    /// same material as the strip rather than a lighter panel sitting over it.
    /// </summary>
    private Style BarPresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(StripPadX, StripPadY, StripPadX, StripPadY)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(10)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, BarBackground()));
        // The bar is a strip of icons, not a scrollable document: its own ScrollViewer (below)
        // handles overflow along the one axis that can overflow.
        style.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        style.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        return style;
    }

    /// <summary>
    /// The bar's surface: the dock's own tint, at the transparency that makes it land on the dock's
    /// own rendered color.
    /// <para>
    /// It cannot literally be the dock's material. The dock's glass is a <em>system backdrop</em>,
    /// which is applied to a <see cref="Window"/>; with <c>ShouldConstrainToRootBounds</c> off the
    /// bar lives in a popup window, which is not one, and a XAML <c>AcrylicBrush</c> is no help
    /// either — in-app acrylic samples the app content behind it, and behind this popup there is
    /// no app content, so it renders its flat fallback (measurably lighter than the dock: RGB ~40
    /// against the dock's ~14 over the same wallpaper). What it can do is take the same tint and
    /// let the desktop through at the same strength, which puts the two within a couple of levels
    /// of each other. The difference that remains is the blur, and at this size it does not read.
    /// </para>
    /// <para>
    /// Falls back to the opaque system surface when there is no backdrop to match — the High
    /// Contrast case, where the dock is painting that same solid color for the same reason.
    /// </para>
    /// </summary>
    private Brush BarBackground()
    {
        if (_backdrop is null)
            return (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        // The dock's acrylic is opaque and simply *is* its tint, so an opaque tint brush matches
        // it exactly.
        return new SolidColorBrush(_backdrop.Current.Tint);
    }

    /// <summary>
    /// The "nothing in here yet" line an empty group shows in place of its cells. The dimming
    /// comes from <see cref="UIElement.Opacity"/> rather than a secondary-text brush on purpose:
    /// the brush would have to be looked up from <c>Application.Current.Resources</c>, which
    /// resolves against the <em>application's</em> theme, and a Light dock under a Dark app would
    /// get near-white text on light glass. Leaving the foreground inherited keeps it whatever the
    /// dock's own theme says it should be.
    /// </summary>
    private static TextBlock EmptyBarContent(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 220,
    };

    /// <summary>
    /// The bar itself: one line of dock cells across the dock's flow, scrolling along that line
    /// once there are more of them than <see cref="BarMaxExtent"/> allows.
    /// </summary>
    private FrameworkElement BuildBar(Flyout flyout, DockBarOptions options)
    {
        bool vertical = BarIsVertical;

        var stack = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = BarSpacing,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var item in options.Items)
            stack.Children.Add(BuildBarCell(item, flyout, options));

        // Only the flow axis can overflow — the other is exactly one cell wide — so the scroll
        // viewer is disabled across it rather than left to add a second, useless scrollbar.
        return new ScrollViewer
        {
            Content = stack,
            MaxHeight = vertical ? BarMaxExtent : double.PositiveInfinity,
            MaxWidth = vertical ? double.PositiveInfinity : BarMaxExtent,
            VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            VerticalScrollMode = vertical ? ScrollMode.Auto : ScrollMode.Disabled,
            HorizontalScrollMode = vertical ? ScrollMode.Auto : ScrollMode.Disabled,
        };
    }

    /// <summary>
    /// One cell in the bar — the same square, the same chrome style and the same icon size the
    /// strip uses at this density.
    /// <para>
    /// Built imperatively rather than from the strip's <c>DataTemplate</c>, because — like the
    /// Settings list — it has to subscribe to the item to catch an icon that resolves after the
    /// bar is already on screen, and drop that subscription when the cell goes away. A folder
    /// fly-out's entries in particular are created and resolved at the moment the bar opens.
    /// </para>
    /// </summary>
    private Button BuildBarCell(DockItem item, Flyout flyout, DockBarOptions options)
    {
        var host = new Grid();

        void RenderIcon()
        {
            host.Children.Clear();
            if (item.IconImage is not null)
            {
                host.Children.Add(new Image
                {
                    Source = item.IconImage,
                    Width = DockMetrics.Icon,
                    Height = DockMetrics.Icon,
                    Stretch = Stretch.Uniform,
                });
            }
            else
            {
                host.Children.Add(new FontIcon
                {
                    Glyph = item.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = DockMetrics.Glyph,
                });
            }
        }
        RenderIcon();

        void OnItemPropertyChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DockItem.IconImage))
                RenderIcon();
        }
        item.PropertyChanged += OnItemPropertyChanged;

        var button = new Button
        {
            Style = (Style)RootGrid.Resources["DockGlassButtonStyle"],
            Width = DockMetrics.Cell,
            Height = DockMetrics.Cell,
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(DockMetrics.CellCorner),
            Content = host,
            // The reorder/drag code and the drop targets both find an item by walking up to the
            // nearest Tag, exactly as they do on the strip.
            Tag = item,
        };
        ToolTipService.SetToolTip(button, item.DisplayName);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, item.DisplayName);

        button.Click += (_, _) =>
        {
            flyout.Hide();
            if (options.OnActivate is null || !options.OnActivate(item))
                LaunchOrFocus(item);
        };

        if (options.OnContext is { } onContext)
        {
            button.ContextRequested += (s, e) =>
            {
                onContext((FrameworkElement)s, item, flyout, e);
                e.Handled = true;
            };
        }

        if (options.OnDragOut is { } onDragOut)
            HookDragOut(button, item, flyout, onDragOut);

        button.Unloaded += (_, _) => item.PropertyChanged -= OnItemPropertyChanged;
        return button;
    }

    // ---- Dragging a cell back out of the bar -------------------------------
    //
    // The counterpart to dropping an icon onto a group: drag one out of the open bar and it goes
    // back on the strip. Measured as movement ACROSS the bar's flow, because along the flow is
    // where the bar's own cells are — a drag up or down a vertical bar is aiming at another cell
    // in it, and only a drag sideways is unambiguously "out".
    //
    // Cursor-polled rather than pointer-captured for the same reason the dock's own drag is (see
    // DockWindow.DragTick): the bar is a light-dismiss popup, and the press that starts the
    // gesture is exactly the kind of thing that dismisses it out from under a captured pointer.

    /// <summary>How far across the bar the cursor must travel to count as "out of it".</summary>
    private const double DragOutThreshold = 52;

    private void HookDragOut(Button cell, DockItem item, Flyout flyout, Action<DockItem> onDragOut)
    {
        cell.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
                return;
            if (!NativeMethods.GetCursorPos(out var start))
                return;
            WatchForDragOut(start, item, flyout, onDragOut);
        }), handledEventsToo: true);
    }

    /// <summary>
    /// The drag-out watch currently running, if any. Held only so closing the dock can stop it:
    /// it otherwise ends itself on the next mouse-release, but a timer still ticking against a
    /// closed window's dispatcher is the kind of thing that outlives its usefulness.
    /// </summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dragOutTimer;

    private void WatchForDragOut(
        NativeMethods.POINT start, DockItem item, Flyout flyout, Action<DockItem> onDragOut)
    {
        bool acrossIsX = BarIsVertical; // a vertical bar is escaped sideways, and vice versa

        // One watch at a time: a second press before the first released would otherwise leave two
        // timers racing to file the same gesture.
        _dragOutTimer?.Stop();

        var timer = DispatcherQueue.CreateTimer();
        _dragOutTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            // Button released without escaping: this was a click, which the cell's own Click
            // handler has already dealt with.
            if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) == 0)
            {
                timer.Stop();
                return;
            }

            if (!NativeMethods.GetCursorPos(out var now))
                return;

            double across = acrossIsX ? Math.Abs(now.X - start.X) : Math.Abs(now.Y - start.Y);
            if (across < DragOutThreshold * (NativeMethods.GetDpiForWindow(_hwnd) / 96.0))
                return;

            timer.Stop();
            flyout.Hide();
            onDragOut(item);
        };
        timer.Start();
    }
}
