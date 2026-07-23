using DockGx.Interop;
using DockGx.Models;
using DockGx.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DockGx;

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

    private const int Peek = 6;      // px of the dock left visible when hidden (the notch band)
    private const int HotZone = 6;   // px band at the edge that triggers a reveal
    private const int EdgePad = 24;  // slack around the dock's span
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    // Whether snapped edges actually hide (vs. staying pinned flush and visible).
    private bool AutoHideEnabled => _config.Snapped && _config.AutoHide;

    // The dock hides along Y for top/bottom, along X for left/right.
    private bool HideIsVertical => _config.Edge is DockEdge.Bottom or DockEdge.Top;

    private int ShownCoord => HideIsVertical ? _shownRect.Y : _shownRect.X;

    // The dock hides against the OUTER (physical screen) edge, not the work-area edge, so a
    // bottom-snapped dock slides all the way down behind the taskbar and the notch peeks out
    // over it. (For edges with no taskbar the outer and work edges coincide.)
    private int HiddenCoord => _config.Edge switch
    {
        DockEdge.Bottom => _outer.Y + _outer.Height - Peek,
        DockEdge.Top => _outer.Y - _shownRect.Height + Peek,
        DockEdge.Left => _outer.X - _shownRect.Width + Peek,
        DockEdge.Right => _outer.X + _outer.Width - Peek,
        _ => _shownRect.Y,
    };

    private void MoveWindowCoord(int coord) => _appWindow.Move(
        HideIsVertical ? new PointInt32(_shownRect.X, coord) : new PointInt32(coord, _shownRect.Y));

    // Called after every reposition (from UpdateSizeAndPosition).
    partial void OnRelayoutApplied()
    {
        _currentCoord = ShownCoord;
        if (AutoHideEnabled)
        {
            EnsureStarted();
            if (!_revealed)
                MoveWindowCoord(HiddenCoord); // keep it tucked away after a size/edge change
        }
        UpdateNotch();
    }

    // Called when snap state changes (drag-drop, menu, settings).
    partial void ApplyAutoHide()
    {
        if (AutoHideEnabled)
        {
            EnsureStarted();
            SetRevealed(false);
        }
        else
        {
            // Not hiding: fully stop the controller and pin the dock flush/visible.
            Stop();
        }
    }

    partial void PauseAutoHideForDrag()
    {
        _pollTimer?.Stop();
        _slideTimer?.Stop();
        _revealed = true; // don't fight the drag
        UpdateNotch();
    }

    // Re-arm auto-hide after an in-place interaction (e.g. an item reorder) without slamming
    // the dock shut immediately: keep it revealed for the usual grace period, then it hides.
    partial void ResumeAutoHideAfterDrag()
    {
        if (!AutoHideEnabled)
            return;
        _revealed = true;
        _lastInside = DateTime.UtcNow;
        EnsureStarted();
        _pollTimer?.Start();
        UpdateNotch();
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
        if (!AutoHideEnabled)
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
        // taskbar reveals the dock), matching where it hides.
        bool atHotZone = _config.Edge switch
        {
            DockEdge.Bottom => inX && p.Y >= _outer.Y + _outer.Height - HotZone,
            DockEdge.Top => inX && p.Y <= _outer.Y + HotZone,
            DockEdge.Left => inY && p.X <= _outer.X + HotZone,
            DockEdge.Right => inY && p.X >= _outer.X + _outer.Width - HotZone,
            _ => false,
        };

        bool overDock = inX && inY;

        if (atHotZone || (overDock && _revealed))
        {
            _lastInside = DateTime.UtcNow;
            if (!_revealed)
                SetRevealed(true);
        }
        else if (_revealed && DateTime.UtcNow - _lastInside > HideDelay)
        {
            SetRevealed(false);
        }
    }

    private void SetRevealed(bool reveal)
    {
        _revealed = reveal;
        _targetCoord = reveal ? ShownCoord : HiddenCoord;
        // Re-assert top-most in both directions: when hidden at the bottom the dock sits behind
        // the taskbar, and its notch must stay above the (also top-most) taskbar to be visible.
        WindowChrome.EnsureTopmost(_hwnd);
        UpdateNotch();

        // Honor the system "show animations" accessibility setting: when animations are off
        // (reduced motion), jump straight to the target instead of the slide.
        if (ReducedMotion)
        {
            _currentCoord = _targetCoord;
            MoveWindowCoord(_targetCoord);
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

        bool show = AutoHideEnabled && !_revealed;
        Notch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;

        switch (_config.Edge)
        {
            case DockEdge.Bottom: // window pushed down; visible band is at the TOP of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = VerticalAlignment.Top;
                Notch.Width = 64; Notch.Height = 10;
                break;
            case DockEdge.Top:    // visible band at the BOTTOM of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Center;
                Notch.VerticalAlignment = VerticalAlignment.Bottom;
                Notch.Width = 64; Notch.Height = 10;
                break;
            case DockEdge.Left:   // visible band at the RIGHT of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Right;
                Notch.VerticalAlignment = VerticalAlignment.Center;
                Notch.Width = 10; Notch.Height = 40;
                break;
            case DockEdge.Right:  // visible band at the LEFT of the window
                Notch.HorizontalAlignment = HorizontalAlignment.Left;
                Notch.VerticalAlignment = VerticalAlignment.Center;
                Notch.Width = 10; Notch.Height = 40;
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

    private void StartSlide()
    {
        if (_slideTimer is null)
        {
            _slideTimer = DispatcherQueue.CreateTimer();
            _slideTimer.Interval = TimeSpan.FromMilliseconds(15);
            _slideTimer.Tick += (_, _) => SlideTick();
        }
        if (!_slideTimer.IsRunning)
            _slideTimer.Start();
    }

    private void SlideTick()
    {
        double diff = _targetCoord - _currentCoord;
        if (Math.Abs(diff) <= 1)
        {
            _currentCoord = _targetCoord;
            MoveWindowCoord(_targetCoord);
            _slideTimer?.Stop();
            return;
        }
        _currentCoord += diff * 0.28; // exponential ease-out
        MoveWindowCoord((int)Math.Round(_currentCoord));
    }
}
