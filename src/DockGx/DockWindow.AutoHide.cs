using DockGx.Interop;
using DockGx.Models;
using DockGx.Services;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace DockGx;

/// <summary>
/// Snap + auto-hide behavior. When the dock is snapped to an edge it slides off that edge
/// leaving a thin peek, and reveals when the cursor reaches the edge within the dock's span.
/// Works for all four edges. Partial of <see cref="DockWindow"/>.
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

    private const int Peek = 3;      // px of the dock left visible when hidden
    private const int HotZone = 6;   // px band at the edge that triggers a reveal
    private const int EdgePad = 24;  // slack around the dock's span
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    // The dock hides along Y for top/bottom, along X for left/right.
    private bool HideIsVertical => _config.Edge is DockEdge.Bottom or DockEdge.Top;

    private int ShownCoord => HideIsVertical ? _shownRect.Y : _shownRect.X;

    private int HiddenCoord => _config.Edge switch
    {
        DockEdge.Bottom => _work.Y + _work.Height - Peek,
        DockEdge.Top => _work.Y - _shownRect.Height + Peek,
        DockEdge.Left => _work.X - _shownRect.Width + Peek,
        DockEdge.Right => _work.X + _work.Width - Peek,
        _ => _shownRect.Y,
    };

    private void MoveWindowCoord(int coord) => _appWindow.Move(
        HideIsVertical ? new PointInt32(_shownRect.X, coord) : new PointInt32(coord, _shownRect.Y));

    // Called after every reposition (from UpdateSizeAndPosition).
    partial void OnRelayoutApplied()
    {
        _currentCoord = ShownCoord;
        if (_config.Snapped)
        {
            EnsureStarted();
            if (!_revealed)
                MoveWindowCoord(HiddenCoord); // keep it tucked away after a size/edge change
        }
    }

    // Called when snap state changes (drag-drop, menu).
    partial void ApplyAutoHide()
    {
        if (_config.Snapped)
        {
            EnsureStarted();
            SetRevealed(false);
        }
        else
        {
            Stop();
        }
    }

    partial void PauseAutoHideForDrag()
    {
        _pollTimer?.Stop();
        _slideTimer?.Stop();
        _revealed = true; // don't fight the drag
    }

    private void EnsureStarted()
    {
        if (_autoHideStarted)
            return;
        _autoHideStarted = true;

        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(100);
        _pollTimer.Tick += (_, _) => PollCursor();
        _pollTimer.Start();
    }

    private void Stop()
    {
        _autoHideStarted = false;
        _pollTimer?.Stop();
        _pollTimer = null;
        _slideTimer?.Stop();
        _slideTimer = null;
        _revealed = true;
        MoveWindowCoord(ShownCoord); // snap fully back into view
    }

    private void PollCursor()
    {
        if (!_config.Snapped)
            return;
        if (!NativeMethods.GetCursorPos(out var p))
            return;

        int l = _shownRect.X - EdgePad;
        int r = _shownRect.X + _shownRect.Width + EdgePad;
        int t = _shownRect.Y - EdgePad;
        int b = _shownRect.Y + _shownRect.Height + EdgePad;
        bool inX = p.X >= l && p.X <= r;
        bool inY = p.Y >= t && p.Y <= b;

        bool atHotZone = _config.Edge switch
        {
            DockEdge.Bottom => inX && p.Y >= _work.Y + _work.Height - HotZone,
            DockEdge.Top => inX && p.Y <= _work.Y + HotZone,
            DockEdge.Left => inY && p.X <= _work.X + HotZone,
            DockEdge.Right => inY && p.X >= _work.X + _work.Width - HotZone,
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
        if (reveal)
            WindowChrome.EnsureTopmost(_hwnd);

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
