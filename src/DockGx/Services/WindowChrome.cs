using DockGx.Interop;
using Microsoft.UI.Windowing;

namespace DockGx.Services;

/// <summary>Native window chrome tweaks that AppWindow/WinUI don't expose directly.</summary>
public static class WindowChrome
{
    /// <summary>
    /// Makes the window a borderless, always-on-top tool window: no title bar or frame,
    /// absent from the taskbar and Alt-Tab, and non-resizable — i.e. dock-like.
    /// </summary>
    public static void MakeBorderlessToolWindow(AppWindow appWindow, nint hwnd)
    {
        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        appWindow.IsShownInSwitchers = false;

        long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
    }

    /// <summary>Applies the Windows 11 rounded-corner treatment to the window.</summary>
    public static void SetRoundedCorners(nint hwnd, bool small = false)
    {
        int pref = small ? NativeMethods.DWMWCP_ROUNDSMALL : NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
    }

    /// <summary>
    /// Replaces the light default DWM window border with a dark one that blends into the
    /// glass. (COLOR_NONE is unreliable on some builds — it falls back to the light default —
    /// so we set an explicit dark COLORREF instead.)
    /// </summary>
    public static void RemoveWindowBorder(nint hwnd)
    {
        int dark = 0x00161616; // COLORREF 0x00BBGGRR — near-black, matches the dark glass
        NativeMethods.DwmSetWindowAttribute(
            hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref dark, sizeof(int));
    }

    /// <summary>Re-asserts top-most Z-order without stealing activation.</summary>
    public static void EnsureTopmost(nint hwnd)
    {
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
