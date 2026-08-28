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
/// slides off that edge leaving a thin peek plus a rounded "notch" handle, and reveals when the
/// cursor reaches the edge within the dock's span. Works for all four edges. Partial of
/// <see cref="DockWindow"/>.
/// </summary>
public sealed partial class DockWindow
{
    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _slideTimer;
    private bool _autoHideStarted;
    private bool _revealed = true;
    private double _currentCoord;
    private int _targetCoord;
    private DateTime _lastInside = DateTime.MinValue;

    // While the clock is before this instant the dock refuses to hide. Set when it lands on an
    // edge so it can settle in full view first (see ArmSettleDelay).
    private DateTime _suppressHideUntil = DateTime.MinValue;

    private const int Peek = 6;      // px of the dock left visible when hidden (the notch band)
    private const int HotZone = 6;   // px band at the edge that triggers a reveal
    private const int EdgePad = 24;  // slack around the dock's span
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
    // edge). Recomputed on each relayout so it costs nothing per poll tick.
    private bool _edgeHasNeighbor;

    // Both a true outer screen edge and one shared with a neighboring monitor can hide now — see
    // HiddenRect for how the two differ. Sliding the full-size window off the shared edge would
    // carry it onto the neighbor's screen, so that case shrinks the window down to the notch
    // instead of translating it (kept fully within THIS monitor either way).
    private bool CanHide => AutoHideEnabled;

    /// <summary>
    /// True once a reveal has actually landed — the window is sitting at its shown position, not
    /// just on its way there.
    /// <para>
    /// <see cref="_revealed"/> alone is not enough: it flips true the instant a reveal is
    /// <em>decided</em> (see <see cref="SetRevealed"/>), before the slide that carries the window
    /// there has run a single frame. A hidden, edge-snapped dock still carries its full strip
    /// inside the window (only a sliver is left on screen), so while that slide is under way a
    /// stray pointer hit on the moving strip can land on an icon — a group, say — while the dock
    /// itself has not visually arrived. Gating on <c>_revealed</c> alone let that hover open the
    /// icon's fly-out mid-slide, over a dock that was not there yet; see
    /// <see cref="TrackGroupHover"/>.
    /// </para>
    /// </summary>
    private bool RevealSettled =>
        _revealed && !(_slideTimer?.IsRunning ?? false) && (int)Math.Round(_currentCoord) == ShownCoord;

    private bool ComputeEdgeHasNeighbor()
    {
        if (_outer.Width == 0 || _outer.Height == 0)
            return false;

        int midX = _shownRect.X + _shownRect.Width / 2;
        int midY = _shownRect.Y + _shownRect.Height / 2;
        var probe = _profile.Edge switch
        {
            DockEdge.Bottom => new PointInt32(midX, _outer.Y + _outer.Height + 2),
            DockEdge.Top => new PointInt32(midX, _outer.Y - 2),
            DockEdge.Left => new PointInt32(_outer.X - 2, midY),
            DockEdge.Right => new PointInt32(_outer.X + _outer.Width + 2, midY),
            _ => new PointInt32(midX, midY),
        };

        try
        {
            // Fallback.None → null when the probe point is on no display (a true outer edge).
            return DisplayArea.GetFromPoint(probe, DisplayAreaFallback.None) is not null;
        }
        catch
        {
            return false;
        }
    }

    // The dock hides along Y for top/bottom, along X for left/right.
    private bool HideIsVertical => _profile.Edge is DockEdge.Bottom or DockEdge.Top;

    private int ShownCoord => HideIsVertical ? _shownRect.Y : _shownRect.X;

    // The window's position and size ALONG the edge (the cross axis) while shown — the strip's own
    // span, untouched by hiding on a true outer edge.
    private int ShownCrossLead => HideIsVertical ? _shownRect.X : _shownRect.Y;
    private int ShownCrossExtent => HideIsVertical ? _shownRect.Width : _shownRect.Height;

    // The dock hides against the OUTER (physical screen) edge, not the work-area edge, so a
    // bottom-snapped dock slides all the way down behind the taskbar and the notch peeks out
    // over it. (For edges with no taskbar the outer and work edges coincide.) Only meaningful on a
    // true outer edge — see HiddenRect for the shared-edge case.
    private int HiddenCoord => _profile.Edge switch
    {
        DockEdge.Bottom => _outer.Y + _outer.Height - Peek,
        DockEdge.Top => _outer.Y - _shownRect.Height + Peek,
        DockEdge.Left => _outer.X - _shownRect.Width + Peek,
        DockEdge.Right => _outer.X + _outer.Width - Peek,
        _ => _shownRect.Y,
    };

    /// <summary>
    /// The window's complete rect once hidden.
    /// <para>
    /// On a true outer edge this is the familiar slide: full size, translated so only
    /// <see cref="Peek"/> px remain on screen along the hide axis; the cross axis (the strip's own
    /// span) is untouched.
    /// </para>
    /// <para>
    /// On an edge shared with a neighboring monitor, translating a full-size window off it would
    /// carry most of the window onto that neighbor's screen — so instead the <em>entire</em>
    /// window shrinks to the notch's own footprint, both the thickness into the screen and the
    /// span along the edge, centered over the strip and flush against the true screen edge.
    /// Shrinking only the thickness and leaving the along-edge span at its full "shown" size (an
    /// earlier version of this did exactly that) left a visible remnant: a full-length sliver of
    /// glass surrounding the small notch decoration instead of a clean, compact handle matching
    /// what a true outer edge shows.
    /// </para>
    /// </summary>
    private RectInt32 HiddenRect()
    {
        if (!_edgeHasNeighbor)
        {
            return HideIsVertical
                ? new RectInt32(_shownRect.X, HiddenCoord, _shownRect.Width, _shownRect.Height)
                : new RectInt32(HiddenCoord, _shownRect.Y, _shownRect.Width, _shownRect.Height);
        }

        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        int thickness = Math.Max(1, (int)Math.Round(NotchThickness * scale));
        double lengthDip = HideIsVertical ? NotchLength : NotchLengthVertical;
        int crossExtent = Math.Clamp(
            (int)Math.Round(lengthDip * scale), 1, Math.Max(1, ShownCrossExtent));
        int crossLead = ShownCrossLead + (ShownCrossExtent - crossExtent) / 2;

        return _profile.Edge switch
        {
            DockEdge.Top => new RectInt32(crossLead, _outer.Y, crossExtent, thickness),
            DockEdge.Bottom => new RectInt32(
                crossLead, _outer.Y + _outer.Height - thickness, crossExtent, thickness),
            DockEdge.Left => new RectInt32(_outer.X, crossLead, thickness, crossExtent),
            DockEdge.Right => new RectInt32(
                _outer.X + _outer.Width - thickness, crossLead, thickness, crossExtent),
            _ => _shownRect,
        };
    }

    private void MoveWindowCoord(int coord) => _appWindow.MoveAndResize(
        HideIsVertical
            ? new RectInt32(_shownRect.X, coord, _shownRect.Width, _shownRect.Height)
            : new RectInt32(coord, _shownRect.Y, _shownRect.Width, _shownRect.Height));

    // Re-applies whichever hidden geometry currently applies (slide or shrink) — used to keep the
    // dock tucked away through a relayout without restarting the reveal/hide cycle.
    private void ApplyHiddenRect() => _appWindow.MoveAndResize(HiddenRect());

    // Called after every reposition (from UpdateSizeAndPosition).
    partial void OnRelayoutApplied()
    {
        _edgeHasNeighbor = _profile.Snapped && ComputeEdgeHasNeighbor();
        _currentCoord = ShownCoord;
        if (CanHide)
        {
            EnsureStarted();
            if (!_revealed)
                ApplyHiddenRect(); // keep it tucked away (slid or shrunk) after a size/edge change
        }
        else if (_autoHideStarted)
        {
            // Auto-hide off: pin the dock flush & visible.
            Stop();
        }
        UpdateNotch();
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
            UpdateNotch();
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
            _currentCoord = ShownCoord;
            MoveWindowCoord(ShownCoord);
        }
    }

    partial void PauseAutoHideForDrag()
    {
        _pollTimer?.Stop();
        _slideTimer?.Stop();
        _revealed = true; // don't fight the drag

        // If this interrupts a reveal already in flight — a group's fly-out can open mid-slide;
        // see RevealSettled — the window is currently sitting wherever that slide had gotten to,
        // not necessarily at ShownCoord. Saying "revealed" without correcting the window itself
        // leaves the dock frozen half-transitioned: PollCursor's reveal trigger is a no-op once
        // _revealed already reads true, so nothing ever finishes moving it the rest of the way,
        // and it can sit stuck like that until the dock is dragged and relaid-out by hand (which
        // repositions it unconditionally). Snapping the window to its shown rect here is what
        // actually finishes the reveal instead of merely declaring it finished.
        if (CanHide && (int)Math.Round(_currentCoord) != ShownCoord)
        {
            _currentCoord = ShownCoord;
            _appWindow.MoveAndResize(_shownRect);
        }
        UpdateNotch();
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
        MoveWindowCoord(ShownCoord); // snap fully back into view
        UpdateNotch();
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
        // taskbar reveals the dock), matching where it hides. On an edge shared with a neighbor,
        // bounded on the far side too: past that boundary is a different monitor's desktop, not a
        // taskbar, and without this bound any cursor position anywhere on that whole neighboring
        // screen (same padded span) reads as "at this edge" — popping the dock straight back open
        // the moment it finishes collapsing, over and over, for as long as the cursor happens to
        // be resting on the other monitor.
        bool atHotZone = _profile.Edge switch
        {
            DockEdge.Bottom => inX && p.Y >= _outer.Y + _outer.Height - HotZone
                                    && (!_edgeHasNeighbor || p.Y < _outer.Y + _outer.Height),
            DockEdge.Top => inX && p.Y <= _outer.Y + HotZone
                                 && (!_edgeHasNeighbor || p.Y >= _outer.Y),
            DockEdge.Left => inY && p.X <= _outer.X + HotZone
                                  && (!_edgeHasNeighbor || p.X >= _outer.X),
            DockEdge.Right => inY && p.X >= _outer.X + _outer.Width - HotZone
                                   && (!_edgeHasNeighbor || p.X < _outer.X + _outer.Width),
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
        _targetCoord = reveal ? ShownCoord : HiddenCoord; // consumed by the true-outer-edge slide
        // Re-assert top-most in both directions: when hidden at the bottom the dock sits behind
        // the taskbar, and its notch must stay above the (also top-most) taskbar to be visible.
        WindowChrome.EnsureTopmost(_hwnd);
        UpdateNotch();

        // Honor the system "show animations" accessibility setting: when animations are off
        // (reduced motion), jump straight to the target instead of the slide. A neighbor-edge hide
        // does the same, unconditionally: it resizes the window rather than sliding it, and
        // animating a live resize means re-laying-out the strip's content every tick — heavier and
        // glitchier than the plain window move the slide otherwise is.
        if (ReducedMotion || _edgeHasNeighbor)
        {
            _currentCoord = _targetCoord;
            _appWindow.MoveAndResize(reveal ? _shownRect : HiddenRect());
            return;
        }
        StartSlide();
    }

    /// <summary>
    /// Positions and shows the hidden-state "notch" handle. It only appears when the dock is
    /// hidden, and is anchored to the edge of the window that stays on-screen (the side facing
    /// into the desktop) so it pokes out of the thin peek band.
    /// </summary>
    private void UpdateNotch()
    {
        if (Notch is null)
            return;

        bool show = CanHide && !_revealed;
        Notch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        switch (_profile.Edge)
        {
            case DockEdge.Bottom: // window pushed down; visible band is at the TOP of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = VerticalAlignment.Top;
                Notch.Width = NotchLength; Notch.Height = NotchThickness;
                break;
            case DockEdge.Top:    // visible band at the BOTTOM of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = VerticalAlignment.Bottom;
                Notch.Width = NotchLength; Notch.Height = NotchThickness;
                break;
            case DockEdge.Left:   // visible band at the RIGHT of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Right;
                Notch.VerticalAlignment = VerticalAlignment.Center;
                Notch.Width = NotchThickness; Notch.Height = NotchLengthVertical;
                break;
            case DockEdge.Right:  // visible band at the LEFT of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Left;
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
    // than a choice: hiding means moving the WINDOW off the screen edge, and Composition animates
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
    private double _slideFrom;

    private void StartSlide()
    {
        _slideStart = DateTime.UtcNow;
        _slideFrom = _currentCoord;

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
            _currentCoord = _targetCoord;
            MoveWindowCoord(_targetCoord);
            _slideTimer?.Stop();
            return;
        }

        // Quint ease-out: quick off the mark, settling smoothly into the edge.
        double eased = 1 - Math.Pow(1 - progress, 5);
        _currentCoord = _slideFrom + (_targetCoord - _slideFrom) * eased;
        MoveWindowCoord((int)Math.Round(_currentCoord));
    }
}
