using Anchor.Interop;
using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Anchor;

/// <summary>
/// Snap + auto-hide behavior. When the dock is snapped to an edge (and auto-hide is on) it
/// tucks away leaving a rounded "notch" handle, and reveals when the cursor reaches the edge
/// within the dock's span. Two hide geometries, because a shared monitor edge is not off-screen:
/// <list type="bullet">
/// <item>
/// <b>Outer edge</b> — the window slides past the physical screen bound (behind the taskbar on
/// the bottom). Nothing lives out there, so the bulk of the dock is genuinely gone.
/// </item>
/// <item>
/// <b>Shared / interior edge</b> — another monitor sits immediately beyond the snapped edge. The
/// window must not cross that boundary: the compositor treats the virtual desktop as one surface,
/// so a slide "off" this screen is a slide <em>onto</em> the neighbor, and a system acrylic
/// backdrop cannot be clipped away. Instead the HWND collapses, entirely inside this monitor, to
/// the notch band on the snapped edge. The dock is gone; only the notch stays, and only on the
/// screen it is snapped to.
/// </item>
/// </list>
/// Partial of <see cref="DockWindow"/>.
/// </summary>
public sealed partial class DockWindow
{
    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _slideTimer;
    private bool _autoHideStarted;
    private bool _revealed = true;
    // 0 = fully shown, 1 = fully hidden. The slide interpolates this; ApplyHideFrame maps it
    // onto a window move (outer edge) or a collapse to the notch (shared edge).
    private double _hideProgress;
    private double _targetProgress;
    private DateTime _lastInside = DateTime.MinValue;

    // While the clock is before this instant the dock refuses to hide. Set when it lands on an
    // edge so it can settle in full view first (see ArmSettleDelay).
    private DateTime _suppressHideUntil = DateTime.MinValue;

    private const int Peek = 6;      // px of the dock left visible when hidden off an OUTER edge
    private const int HotZone = 6;   // px band at the edge that triggers a reveal
    private const int EdgePad = 24;  // slack around the dock's span (and past a true outer edge)
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    // Grace period after the dock is placed on an edge before auto-hide may take it. Longer than
    // HideDelay so that dropping the dock at an edge reads as "it settles, then tucks away".
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(1200);

    // ===== Hidden-state "notch" handle size (DIPs) — change these to resize the notch. =====
    // NotchLength runs ALONG the snapped edge (its long side); NotchThickness is the short side.
    private const double NotchLength = 62;         // was 64 — the notch "width" on a top/bottom edge
    private const double NotchLengthVertical = 40; // long side when snapped to the left/right edge
    private const double NotchThickness = 10;      // the tab's short side (into the screen)

    // Whether snapped edges actually hide (vs. staying pinned flush and visible).
    private bool AutoHideEnabled => _profile.Snapped && _profile.AutoHide;

    // True when another monitor sits immediately beyond the snapped edge (an interior / shared
    // edge). Recomputed on each relayout so it costs nothing per poll tick. Hide then collapses
    // to a notch that stays on this monitor — see ApplyHideFrame — rather than sliding onto the
    // neighbor.
    private bool _edgeHasNeighbor;

    // Strip layout is frozen (explicit size, aligned to the snapped edge) while a shared-edge
    // hide shrinks the HWND, so the icons do not reflow into the collapsing window.
    private bool _sharedLayoutFrozen;

    // A separate name from AutoHideEnabled (even though the two agree today) because every hiding
    // decision below reads CanHide, not the setting directly — one place to add a future
    // condition without touching each call site.
    private bool CanHide => AutoHideEnabled;

    /// <summary>
    /// True when the dock is fully out and settled where the user can actually see it: always so
    /// for a dock that never hides (floating, or snapped without auto-hide), and only between
    /// a completed reveal and the next hide for one that does.
    /// <para>
    /// What it gates is anything that grows <em>out of</em> the strip — a group's fly-out bar (see
    /// <see cref="TrackGroupHover"/>). On a hidden edge-snapped dock the only part under the
    /// cursor is the notch, and the cursor arriving there means "bring the dock back", not "open
    /// the icon it happens to be over": the dock has to come out first, and only then does
    /// hovering an icon in it mean anything.
    /// </para>
    /// <para>
    /// Hide progress is the source of truth, so a dock that was left parked at its hidden frame
    /// is never mistaken for a visible one whatever the flags say.
    /// </para>
    /// </summary>
    private bool DockActivelyVisible =>
        !CanHide ||
        (_revealed && !(_slideTimer?.IsRunning ?? false) && _hideProgress <= 0);

    /// <summary>
    /// True once a shared-edge hide has collapsed far enough that DWM's window-corner radius
    /// would eat the notch. ApplyWindowChrome reads this so an activate cannot round it back on.
    /// </summary>
    private bool SharedEdgeNotchFrame => _edgeHasNeighbor && _hideProgress > 0.85;

    private bool ComputeEdgeHasNeighbor()
    {
        if (_outer.Width == 0 || _outer.Height == 0)
            return false;

        // Prefer geometry against every display's outer bounds (tolerant of small virtual-desktop
        // gaps). Fall back to point probes via DisplayArea when enumeration fails.
        try
        {
            var outers = new List<RectInt32>();
            foreach (var d in DockManager.Displays)
                outers.Add(d.OuterBounds);

            if (outers.Count > 0 &&
                DockPlacement.EdgeHasNeighbor(_outer, _profile.Edge, _shownRect, outers))
                return true;
        }
        catch
        {
            // Enumeration failed — fall through to point probes.
        }

        try
        {
            // Fallback.None → null when the probe point is on no display (a true outer edge).
            // Probe mid-span and near both ends so a strip parked on only part of a shared edge
            // still detects the neighbor — and does not detect one that only covers the other
            // end of this monitor.
            foreach (var probe in DockPlacement.NeighborProbes(_outer, _profile.Edge, _shownRect))
            {
                if (DisplayArea.GetFromPoint(probe, DisplayAreaFallback.None) is not null)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // The dock hides along Y for top/bottom, along X for left/right.
    private bool HideIsVertical => _profile.Edge is DockEdge.Bottom or DockEdge.Top;

    private int ShownCoord => HideIsVertical ? _shownRect.Y : _shownRect.X;

    // The dock hides against the OUTER (physical screen) edge, not the work-area edge, so a
    // bottom-snapped dock slides all the way down behind the taskbar and the notch peeks out
    // over it. (For edges with no taskbar the outer and work edges coincide.) Only used on a
    // true outer edge; a shared edge never moves the window to these coordinates.
    private int HiddenCoord => _profile.Edge switch
    {
        DockEdge.Bottom => _outer.Y + _outer.Height - Peek,
        DockEdge.Top => _outer.Y - _shownRect.Height + Peek,
        DockEdge.Left => _outer.X - _shownRect.Width + Peek,
        DockEdge.Right => _outer.X + _outer.Width - Peek,
        _ => _shownRect.Y,
    };

    private int DipToPx(double dip)
    {
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        return Math.Max(1, (int)Math.Round(dip * scale));
    }

    /// <summary>
    /// Notch-only window rect, flush to the snapped edge and entirely inside this monitor.
    /// </summary>
    private RectInt32 SharedHiddenRect()
    {
        int thick = Math.Max(Peek, DipToPx(NotchThickness));
        int len = DipToPx(HideIsVertical ? NotchLength : NotchLengthVertical);
        return DockPlacement.SharedEdgeHiddenRect(_outer, _shownRect, _profile.Edge, len, thick);
    }

    /// <summary>
    /// Puts the window where <see cref="_hideProgress"/> says it should be: a slide off an outer
    /// edge, or a collapse to the notch that never leaves this monitor on a shared one.
    /// </summary>
    private void ApplyHideFrame()
    {
        if (_edgeHasNeighbor)
        {
            if (_hideProgress <= 0)
            {
                UnfreezeSharedLayout();
                if (DockStrip is not null)
                    DockStrip.Visibility = Visibility.Visible;
                _appWindow.MoveAndResize(_shownRect);
            }
            else
            {
                FreezeSharedLayout();
                var frame = DockPlacement.LerpRect(_shownRect, SharedHiddenRect(), _hideProgress);
                _appWindow.MoveAndResize(frame);
                if (DockStrip is not null)
                    DockStrip.Visibility = _hideProgress >= 1 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        else
        {
            UnfreezeSharedLayout();
            if (DockStrip is not null)
                DockStrip.Visibility = Visibility.Visible;
            // Always size to the shown strip: a previous shared-edge collapse may have shrunk
            // the HWND, and an outer-edge hide must not keep that tiny size as it slides off.
            int coord = (int)Math.Round(ShownCoord + (HiddenCoord - ShownCoord) * _hideProgress);
            _appWindow.MoveAndResize(new RectInt32(
                HideIsVertical ? _shownRect.X : coord,
                HideIsVertical ? coord : _shownRect.Y,
                _shownRect.Width,
                _shownRect.Height));
        }

        UpdateNotch();
        bool wantRound = !SharedEdgeNotchFrame;
        if (wantRound != _cornersRounded)
        {
            _cornersRounded = wantRound;
            ApplyWindowChrome();
        }
    }

    // Last value pushed to DWM so we do not FRAMECHANGED on every slide tick.
    private bool _cornersRounded = true;

    /// <summary>
    /// Locks the strip at its shown size and pins it to the snapped edge so shrinking the HWND
    /// clips the dock toward that edge instead of reflowing the icons into a tiny window.
    /// Size comes from <see cref="_shownRect"/> (not ActualWidth) so a relayout that races the
    /// hide still freezes the full strip rather than the notch it was just displaying.
    /// </summary>
    private void FreezeSharedLayout()
    {
        if (_sharedLayoutFrozen || DockStrip is null)
            return;

        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(_hwnd) / 96.0);
        DockStrip.Width = Math.Max(1, _shownRect.Width / scale);
        DockStrip.Height = Math.Max(1, _shownRect.Height / scale);

        switch (_profile.Edge)
        {
            case DockEdge.Bottom:
                DockStrip.HorizontalAlignment = HorizontalAlignment.Center;
                DockStrip.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case DockEdge.Top:
                DockStrip.HorizontalAlignment = HorizontalAlignment.Center;
                DockStrip.VerticalAlignment = VerticalAlignment.Top;
                break;
            case DockEdge.Left:
                DockStrip.HorizontalAlignment = HorizontalAlignment.Left;
                DockStrip.VerticalAlignment = VerticalAlignment.Center;
                break;
            case DockEdge.Right:
                DockStrip.HorizontalAlignment = HorizontalAlignment.Right;
                DockStrip.VerticalAlignment = VerticalAlignment.Center;
                break;
        }

        _sharedLayoutFrozen = true;
    }

    private void UnfreezeSharedLayout()
    {
        if (!_sharedLayoutFrozen || DockStrip is null)
            return;

        DockStrip.Width = double.NaN;
        DockStrip.Height = double.NaN;
        DockStrip.HorizontalAlignment = HorizontalAlignment.Center;
        DockStrip.VerticalAlignment = VerticalAlignment.Center;
        DockStrip.Visibility = Visibility.Visible;
        _sharedLayoutFrozen = false;
    }

    // Called after every reposition (from UpdateSizeAndPosition).
    partial void OnRelayoutApplied()
    {
        _edgeHasNeighbor = _profile.Snapped && ComputeEdgeHasNeighbor();
        if (CanHide)
        {
            EnsureStarted();
            if (!_revealed)
            {
                // Keep it tucked away after a size/edge change — jump to the hidden frame so
                // the next reveal slides out from where it actually is.
                _hideProgress = 1;
                ApplyHideFrame();
            }
            else
            {
                _hideProgress = 0;
                ApplyHideFrame();
            }
        }
        else if (_autoHideStarted)
        {
            // Auto-hide just turned off: pin the dock flush & visible.
            Stop();
        }
        else
        {
            UnfreezeSharedLayout();
            UpdateNotch();
        }
    }

    // Called when snap state changes (drag-drop, menu, settings).
    partial void ApplyAutoHide()
    {
        _edgeHasNeighbor = _profile.Snapped && ComputeEdgeHasNeighbor();
        if (CanHide)
        {
            EnsureStarted();
            // Let the dock settle at its new edge in full view rather than slamming it shut.
            // Hiding immediately here would fight the cursor that is still parked at the edge
            // from the drag that placed it: the very next poll tick sees the hot zone and
            // re-reveals, giving a hide/show/hide flicker. Arming the grace period instead lets
            // the dock land, then tuck away once — smoothly — after the cursor leaves.
            ArmSettleDelay();
        }
        else
        {
            // Not hiding: fully stop the controller and pin the dock flush/visible.
            Stop();
        }
    }

    /// <summary>
    /// Holds the dock revealed and blocks auto-hide for <see cref="SettleDelay"/>. After that the
    /// normal poll rules resume, so it hides once the cursor has also been away for
    /// <see cref="HideDelay"/>.
    /// </summary>
    private void ArmSettleDelay()
    {
        var now = DateTime.UtcNow;
        _lastInside = now;
        _suppressHideUntil = now + SettleDelay;
        if (!_revealed)
            SetRevealed(true); // slides back out if a hide was already under way
        else
        {
            _hideProgress = 0;
            ApplyHideFrame();
        }
    }

    /// <summary>
    /// Pulls the dock fully into view right now — used when it is summoned from the tray or by
    /// the global shortcut. If it can auto-hide it slides back out and gets the usual settle
    /// grace period (so it doesn't tuck away again the instant the cursor is elsewhere);
    /// otherwise it is simply snapped back to its shown position.
    /// </summary>
    partial void RevealNow()
    {
        if (CanHide)
        {
            EnsureStarted();
            ArmSettleDelay();
        }
        else
        {
            _hideProgress = 0;
            ApplyHideFrame();
        }
    }

    partial void PauseAutoHideForDrag()
    {
        _pollTimer?.Stop();
        _slideTimer?.Stop();
        _revealed = true; // don't fight the drag
        _hideProgress = 0;

        // The flag alone is not enough when the dock is currently tucked away — and it can be,
        // since this also runs when a fly-out opens off an icon in the peek band. Saying
        // "revealed" while the window still sits at its hidden frame is a dock that can
        // never come back: PollCursor reveals by flipping that same flag, so with it already true
        // every later hover is a no-op and the dock stays off-screen for good. Put the window
        // where the flag says it is instead. Done before the caller shows anything, so a fly-out
        // anchored on this window measures against its settled position.
        ApplyHideFrame();
    }

    // Re-arm auto-hide after an in-place interaction (e.g. an item reorder) without slamming
    // the dock shut immediately: keep it revealed for the usual grace period, then it hides.
    partial void ResumeAutoHideAfterDrag()
    {
        if (!CanHide)
            return;
        EnsureStarted();
        ArmSettleDelay();
    }

    private void EnsureStarted()
    {
        if (_autoHideStarted && _pollTimer is not null)
        {
            _pollTimer.Start();
            return;
        }
        _autoHideStarted = true;

        _pollTimer ??= DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(100);
        _pollTimer.Tick -= OnPollTick;
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void OnPollTick(DispatcherQueueTimer sender, object args) => PollCursor();

    private void Stop()
    {
        _autoHideStarted = false;
        _pollTimer?.Stop();
        _pollTimer = null;
        _slideTimer?.Stop();
        _slideTimer = null;
        _revealed = true;
        _hideProgress = 0;
        ApplyHideFrame(); // snap fully back into view
    }

    private void PollCursor()
    {
        if (!CanHide)
            return;
        if (!NativeMethods.GetCursorPos(out var p))
            return;

        int l = _shownRect.X - EdgePad;
        int r = _shownRect.X + _shownRect.Width + EdgePad;
        int t = _shownRect.Y - EdgePad;
        int b = _shownRect.Y + _shownRect.Height + EdgePad;
        bool inX = p.X >= l && p.X <= r;
        bool inY = p.Y >= t && p.Y <= b;

        // Reveal from the physical screen edge (so moving the cursor onto the notch over the
        // taskbar reveals the dock), matching where it hides. On a shared edge the far-side
        // pad is zero: the cursor walking onto the neighbor must not count as "still at this
        // edge", or the dock would pop open for a pointer that is already on the other screen.
        int farPad = _edgeHasNeighbor ? 0 : EdgePad;
        int outerRight = _outer.X + _outer.Width;
        int outerBottom = _outer.Y + _outer.Height;
        bool atHotZone = _profile.Edge switch
        {
            DockEdge.Bottom => inX && p.Y >= outerBottom - HotZone && p.Y <= outerBottom + farPad
                                    && (!_edgeHasNeighbor || p.Y < outerBottom),
            DockEdge.Top => inX && p.Y <= _outer.Y + HotZone && p.Y >= _outer.Y - farPad
                                 && (!_edgeHasNeighbor || p.Y >= _outer.Y),
            DockEdge.Left => inY && p.X <= _outer.X + HotZone && p.X >= _outer.X - farPad
                                  && (!_edgeHasNeighbor || p.X >= _outer.X),
            DockEdge.Right => inY && p.X >= outerRight - HotZone && p.X <= outerRight + farPad
                                   && (!_edgeHasNeighbor || p.X < outerRight),
            _ => false,
        };

        bool overDock = inX && inY;

        var now = DateTime.UtcNow;
        if (atHotZone || (overDock && _revealed))
        {
            _lastInside = now;
            if (!_revealed)
                SetRevealed(true);
        }
        else if (_revealed && now - _lastInside > HideDelay && now >= _suppressHideUntil)
        {
            SetRevealed(false);
        }
    }

    private void SetRevealed(bool reveal)
    {
        _revealed = reveal;
        _targetProgress = reveal ? 0 : 1;
        // Re-assert top-most in both directions: when hidden at the bottom the dock sits behind
        // the taskbar, and its notch must stay above the (also top-most) taskbar to be visible.
        WindowChrome.EnsureTopmost(_hwnd);
        UpdateNotch();

        // Honor the system "show animations" accessibility setting: when animations are off
        // (reduced motion), jump straight to the target instead of the slide.
        if (ReducedMotion)
        {
            _hideProgress = _targetProgress;
            ApplyHideFrame();
            OnSlideSettled();
            return;
        }
        StartSlide();
    }

    /// <summary>
    /// Runs when a slide (or its reduced-motion jump) has landed. On a reveal it re-reads the
    /// strip's hover state from where the cursor physically is, because the dock has just come out
    /// <em>from under</em> a cursor that never moved — no pointer message is generated by a window
    /// sliding beneath it, so the strip still believes the cursor is wherever it was when the dock
    /// was tucked away. Re-reading is what turns "the cursor was already resting on a group icon
    /// when the dock appeared" into that group's bar opening, now that the dock is actually
    /// visible and <see cref="DockActivelyVisible"/> allows it.
    /// </summary>
    private void OnSlideSettled()
    {
        if (_revealed)
            RefreshStripPointerFromCursor();
    }

    /// <summary>
    /// Positions and shows the hidden-state "notch" handle. It only appears when the dock is
    /// hiding. On an outer edge the window has slid off-screen and the notch sits on the inner
    /// remaining band; on a shared edge the window collapses toward the snapped edge and the
    /// notch sits on that outer edge — which is the only part of the HWND still on this monitor.
    /// </summary>
    private void UpdateNotch()
    {
        if (Notch is null)
            return;

        bool show = CanHide && (_hideProgress > 0 || !_revealed);
        Notch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        // Shared-edge collapse: the surviving band is the snapped (outer) edge. Outer-edge
        // slide: the surviving band is the inner edge of a window that has moved off-screen.
        bool notchOnOuterEdge = _edgeHasNeighbor;

        if (notchOnOuterEdge && _hideProgress >= 1)
        {
            // The window *is* the notch now — fill it, matching the HWND we collapsed to.
            Notch.HorizontalAlignment = HorizontalAlignment.Stretch;
            Notch.VerticalAlignment = VerticalAlignment.Stretch;
            Notch.Width = double.NaN;
            Notch.Height = double.NaN;
            return;
        }

        switch (_profile.Edge)
        {
            case DockEdge.Bottom:
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = notchOnOuterEdge
                    ? VerticalAlignment.Bottom
                    : VerticalAlignment.Top;
                Notch.Width = NotchLength; Notch.Height = NotchThickness;
                break;
            case DockEdge.Top:
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = notchOnOuterEdge
                    ? VerticalAlignment.Top
                    : VerticalAlignment.Bottom;
                Notch.Width = NotchLength; Notch.Height = NotchThickness;
                break;
            case DockEdge.Left:
                Notch.HorizontalAlignment = notchOnOuterEdge
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Right;
                Notch.VerticalAlignment = VerticalAlignment.Center;
                Notch.Width = NotchThickness; Notch.Height = NotchLengthVertical;
                break;
            case DockEdge.Right:
                Notch.HorizontalAlignment = notchOnOuterEdge
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left;
                Notch.VerticalAlignment = VerticalAlignment.Center;
                Notch.Width = NotchThickness; Notch.Height = NotchLengthVertical;
                break;
        }
    }

    // Cached once: the reduced-motion preference rarely changes within a session. Guarded so
    // an unavailable setting simply leaves animations on.
    private bool? _reducedMotion;
    private bool ReducedMotion => _reducedMotion ??= ComputeReducedMotion();

    private static bool ComputeReducedMotion()
    {
        try { return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return false; }
    }

    // ---- The slide --------------------------------------------------------
    //
    // Driven by a timer rather than by a Composition animation, and that is a constraint rather
    // than a choice: hiding means moving or resizing the WINDOW, and Composition animates
    // content inside a window — it has no way to animate an HWND's position. Keeping the window
    // still and sliding its content instead would leave a full-size, invisible window sitting over
    // the screen edge swallowing every click aimed at what is behind it.
    //
    // What the timer does is time-based rather than per-frame proportional, which is the part that
    // actually shows: the old "move 28% of the remaining distance each tick" is a different
    // duration for every travel distance (a tall dock took visibly longer to hide than a short
    // one) and never quite arrives, so it ended on a snap. This runs a fixed duration through a
    // cubic ease-out and lands exactly on the target.

    private const double SlideDurationMs = 270;
    private DateTime _slideStart;
    private double _slideFromProgress;

    private void StartSlide()
    {
        _slideStart = DateTime.UtcNow;
        _slideFromProgress = _hideProgress;

        if (_slideTimer is null)
        {
            _slideTimer = DispatcherQueue.CreateTimer();
            // ~120Hz: fine enough that a high-refresh display doesn't show steps, and cheap —
            // each tick is one MoveWindow on a window a few hundred pixels across.
            _slideTimer.Interval = TimeSpan.FromMilliseconds(8);
            _slideTimer.Tick += (_, _) => SlideTick();
        }
        if (!_slideTimer.IsRunning)
            _slideTimer.Start();
    }

    private void SlideTick()
    {
        double progress = (DateTime.UtcNow - _slideStart).TotalMilliseconds / SlideDurationMs;
        if (progress >= 1)
        {
            _hideProgress = _targetProgress;
            ApplyHideFrame();
            _slideTimer?.Stop();
            OnSlideSettled();
            return;
        }

        // Quint ease-out: quick off the mark, settling smoothly into the edge.
        double eased = 1 - Math.Pow(1 - progress, 5);
        _hideProgress = _slideFromProgress + (_targetProgress - _slideFromProgress) * eased;
        ApplyHideFrame();
    }
}
