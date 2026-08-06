using Anchor.Models;
using Microsoft.UI.Dispatching;

namespace Anchor.Services;

/// <summary>
/// Keeps track of which pinned apps currently have a window open, and focuses those windows.
/// <para>
/// Windows offers no notification for "an app opened its first window", so this polls. The walk
/// itself (<see cref="RunningAppService.Snapshot"/>) runs off the UI thread and the result is
/// published back on it. One monitor serves every dock — the answer is a property of the machine,
/// not of a strip, so polling per dock would multiply the cost for identical results.
/// </para>
/// </summary>
public sealed class RunningAppMonitor : IDisposable
{
    // Two seconds is well under the time it takes to notice a stale dot, and the walk itself is
    // a few milliseconds — it is the frequency, not the cost, that had to stay modest.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _timer;
    private Dictionary<string, nint> _windows = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshInFlight;

    /// <summary>Raised on the UI thread after each successful poll.</summary>
    public event Action? Updated;

    public RunningAppMonitor(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public bool IsEnabled { get; private set; }

    /// <summary>Starts or stops polling. Turning it off clears the last snapshot.</summary>
    public void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return;
        IsEnabled = enabled;

        if (!enabled)
        {
            _timer?.Stop();
            _windows = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
            Updated?.Invoke();
            return;
        }

        _timer ??= CreateTimer();
        _timer.Start();
        Refresh(); // don't make the first frame wait a full interval for its dots
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = PollInterval;
        timer.Tick += (_, _) => Refresh();
        return timer;
    }

    private void Refresh()
    {
        if (_refreshInFlight || !IsEnabled)
            return;
        _refreshInFlight = true;

        // The enumeration touches every top-level window and opens a process handle per owning
        // process. That is fast, but it is not something to do on the thread that draws the dock.
        _ = Task.Run(() =>
        {
            var snapshot = RunningAppService.Snapshot();
            _dispatcher.TryEnqueue(() =>
            {
                _refreshInFlight = false;
                if (!IsEnabled)
                    return;
                _windows = snapshot;
                try
                {
                    Updated?.Invoke();
                }
                catch (Exception ex)
                {
                    Diag.Log("RunningAppMonitor: update handler failed: " + ex.Message);
                }
            });
        });
    }

    /// <summary>True when this item's app has a window open right now.</summary>
    public bool IsRunning(DockItem item) =>
        IsEnabled &&
        RunningAppService.ExecutablePathOf(item) is { } exe &&
        _windows.ContainsKey(exe);

    /// <summary>
    /// If this item's app is already open, brings its window forward and reports true so the
    /// caller skips the launch. False for everything else — a not-running app, a folder or link,
    /// a window that has closed since the last poll, or an activation Windows refused.
    /// </summary>
    public bool TryFocus(DockItem item)
    {
        if (!IsEnabled || RunningAppService.ExecutablePathOf(item) is not { } exe)
            return false;
        if (!_windows.TryGetValue(exe, out nint hwnd))
            return false;
        if (RunningAppService.Activate(hwnd))
            return true;

        // The window went away between polls; drop it so the next click launches cleanly instead
        // of trying the same dead handle again.
        _windows.Remove(exe);
        return false;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        IsEnabled = false;
    }
}
