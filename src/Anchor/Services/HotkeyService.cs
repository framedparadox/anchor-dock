using System.Runtime.InteropServices;
using Anchor.Interop;
using Anchor.Models;

namespace Anchor.Services;

/// <summary>
/// Registers Anchor's one system-wide shortcut with Windows and raises <see cref="Pressed"/> when
/// it's used. Windows delivers <c>WM_HOTKEY</c> to a window, so this rides on the same hidden
/// <see cref="MessageWindow"/> the tray icon uses.
/// <para>
/// Only one hotkey is live at a time: <see cref="Register"/> replaces whatever was registered
/// before, and reports false (without throwing) when another app already owns the combination —
/// the caller surfaces that as a "pick a different one" message rather than failing silently.
/// </para>
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // Any process-unique id in [0x0000, 0xBFFF] will do; Anchor only ever registers one.
    private const int HotkeyId = 0x4143; // 'A','C'

    private readonly MessageWindow _window;
    private bool _registered;
    private bool _disposed;

    /// <summary>Raised on the UI thread each time the registered shortcut is pressed.</summary>
    public event Action? Pressed;

    /// <summary>The gesture currently registered with Windows, if any.</summary>
    public HotkeyGesture? Current { get; private set; }

    public HotkeyService(MessageWindow window)
    {
        _window = window;
        _window.MessageReceived += OnMessage;
    }

    private void OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            Pressed?.Invoke();
    }

    /// <summary>
    /// Makes <paramref name="gesture"/> the live shortcut, replacing any previous one. Passing
    /// null (or an invalid gesture) just unregisters.
    /// </summary>
    /// <returns>True if the shortcut is now registered; false if Windows refused it — almost
    /// always because another application already holds that combination.</returns>
    public bool Register(HotkeyGesture? gesture)
    {
        Unregister();

        if (_window.Handle == nint.Zero || gesture is null || !gesture.IsValid)
            return false;

        // MOD_NOREPEAT so holding the keys down brings the dock forward once rather than
        // hammering the reveal on every auto-repeat.
        uint modifiers = (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, modifiers, gesture.Key))
        {
            Diag.Log($"Hotkey: RegisterHotKey({gesture}) failed ({Marshal.GetLastWin32Error()})");
            return false;
        }

        _registered = true;
        Current = gesture;
        return true;
    }

    /// <summary>Releases the shortcut back to the system. Safe to call when nothing is registered.</summary>
    public void Unregister()
    {
        if (_registered && _window.Handle != nint.Zero)
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _window.MessageReceived -= OnMessage;
        Unregister();
    }
}
