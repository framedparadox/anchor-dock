using System.Runtime.InteropServices;

namespace DockGx.Interop;

/// <summary>
/// Thin Win32 / DWM interop surface. Kept small and focused on what the dock needs:
/// rounded window corners, always-on-top enforcement, tool-window styling, and
/// cursor probing for edge-reveal / auto-hide.
/// </summary>
internal static partial class NativeMethods
{
    // ---- DWM window attributes --------------------------------------------
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE; // sentinel: draw no window border

    // DWM_WINDOW_CORNER_PREFERENCE
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;       // full ~8px Win11 window radius
    public const int DWMWCP_ROUNDSMALL = 3;  // subtle ~4px radius (menus/tooltips)

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    // ---- Window Z-order / positioning -------------------------------------
    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint hwndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    // ---- Extended window styles (tool window: off taskbar & alt-tab) ------
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    // ---- Window styles (strip the non-client frame / resize border) -------
    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_BORDER = 0x00800000;
    public const long WS_DLGFRAME = 0x00400000;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint newLong);

    // ---- Cursor / hit testing (auto-hide reveal) --------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    // ---- DPI (DIP -> physical pixel conversion for exact placement) -------
    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    // ---- Mouse button state (timer-polled dragging) -----------------------
    public const int VK_LBUTTON = 0x01;

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    // ---- Shell icon extraction (SHGetFileInfo) -----------------------------
    // A fallback icon source for anything the filesystem says exists. Needed because the
    // WinRT Storage thumbnail pipeline (IconService's primary path, for higher-res icons)
    // flatly refuses to open .lnk shortcuts ("UNABLE_TO_MASK_PATH") and can deny arbitrary
    // paths for an unpackaged app; SHGetFileInfo has neither limitation and, for a .lnk,
    // naturally resolves to the shortcut target's icon with the arrow overlay, matching
    // Explorer. Classic DllImport here (not LibraryImport) since the fixed-size string
    // fields in SHFILEINFO need ByValTStr marshalling.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);
}
