using System.Text;
using Anchor.Interop;
using Anchor.Models;

namespace Anchor.Services;

/// <summary>
/// Answers "is this pinned app already running, and which window is it?" — the data behind the
/// running-app indicators and click-to-focus.
/// <para>
/// Works the way the taskbar does: enumerate the visible top-level windows, map each to the image
/// path of the process that owns it, and match that against a dock item's target (following a
/// <c>.lnk</c> through <see cref="ShortcutResolver"/> first). Matching on the full path rather
/// than the file name matters — two different apps can both ship an <c>Update.exe</c>.
/// </para>
/// </summary>
public static class RunningAppService
{
    /// <summary>
    /// Shell-owned windows that are always present and would otherwise make File Explorer (and
    /// anything else pinned from the shell process) permanently read as "running".
    /// </summary>
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.Ordinal)
    {
        "Progman",       // the desktop
        "WorkerW",       // the desktop's wallpaper/icon layers
        "Shell_TrayWnd", // the taskbar
        "Shell_SecondaryTrayWnd",
    };

    /// <summary>
    /// A snapshot of what is running now: lowercased full executable path to one representative
    /// top-level window. Safe to call from a background thread; never throws.
    /// </summary>
    public static Dictionary<string, nint> Snapshot()
    {
        var result = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        // Reused across the enumeration: several windows normally belong to the same process, and
        // opening a process handle is the expensive part of this walk.
        var pathByProcess = new Dictionary<uint, string?>();

        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsAppWindow(hwnd))
                        return true;

                    if (NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
                        return true;

                    if (!pathByProcess.TryGetValue(pid, out var path))
                    {
                        path = ProcessImagePath(pid);
                        pathByProcess[pid] = path;
                    }

                    // First window wins: enumeration runs front-to-back in Z-order, so the one
                    // kept is the most recently used window of that app — the one a click should
                    // bring forward.
                    if (path is not null && !result.ContainsKey(path))
                        result[path] = hwnd;
                }
                catch
                {
                    // A window can die mid-enumeration; skip it rather than abandoning the walk.
                }
                return true;
            }, nint.Zero);
        }
        catch (Exception ex)
        {
            Diag.Log("RunningAppService: enumeration failed: " + ex.Message);
        }

        return result;
    }

    /// <summary>
    /// True for the windows a user would call "an open app window": visible, un-owned, not a tool
    /// window, with a title, and not one of the shell's own fixtures.
    /// </summary>
    private static bool IsAppWindow(nint hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
            return false;
        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != nint.Zero)
            return false; // a dialog or palette belonging to another window
        if (NativeMethods.GetWindowTextLength(hwnd) == 0)
            return false; // untitled helper surfaces

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            return false; // deliberately off the taskbar — including Anchor's own dock

        var cls = new StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, cls, cls.Capacity) > 0 &&
            ShellWindowClasses.Contains(cls.ToString()))
            return false;

        return true;
    }

    /// <summary>The full image path of a process, or null when it can't be read.</summary>
    private static string? ProcessImagePath(uint pid)
    {
        nint handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == nint.Zero)
            return null; // a protected or higher-integrity process — not something we can match

        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// The executable a dock item would start, for comparison against a snapshot: the target
    /// itself for an <c>.exe</c>, or a shortcut's resolved target. Null for anything that has no
    /// single executable behind it (folders, web links, separators, shell commands).
    /// </summary>
    public static string? ExecutablePathOf(DockItem item)
    {
        if (item.Kind != DockItemKind.Application || string.IsNullOrWhiteSpace(item.Target))
            return null;

        var target = item.Target;
        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            target = ShortcutResolver.Resolve(target) ?? string.Empty;

        if (target.Length == 0 ||
            !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            // Normalize so "C:\Windows\..\Windows\notepad.exe" and the snapshot's path compare
            // equal; the dictionary itself is already case-insensitive.
            return Path.GetFullPath(target);
        }
        catch
        {
            return target;
        }
    }

    /// <summary>
    /// Brings an existing window to the front, restoring it first if it is minimized. Returns
    /// false when the window has gone away or Windows refused the activation, so the caller can
    /// fall back to launching a fresh instance.
    /// </summary>
    public static bool Activate(nint hwnd)
    {
        if (hwnd == nint.Zero || !NativeMethods.IsWindow(hwnd))
            return false;
        try
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            // Allowed without the usual foreground restrictions because Anchor is the foreground
            // process at this point: the click that got us here activated the dock.
            return NativeMethods.SetForegroundWindow(hwnd);
        }
        catch (Exception ex)
        {
            Diag.Log("RunningAppService: activation failed: " + ex.Message);
            return false;
        }
    }
}
