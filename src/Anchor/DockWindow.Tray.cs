using Anchor.Interop;
using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;

namespace Anchor;

/// <summary>
/// The dock's presence outside its own window: the notification-area icon and the system-wide
/// shortcut that summons it. Both hang off one hidden <see cref="MessageWindow"/>, since Windows
/// delivers tray callbacks and <c>WM_HOTKEY</c> to an HWND and a WinUI 3 window exposes no
/// <c>WndProc</c>. Partial of <see cref="DockWindow"/>.
/// </summary>
public sealed partial class DockWindow
{
    private MessageWindow? _messageWindow;
    private TrayIconService? _tray;
    private HotkeyService? _hotkeys;

    /// <summary>True while the user has hidden the dock from the tray menu. Distinct from
    /// auto-hide: the dock stays gone until it is explicitly summoned back.</summary>
    private bool _hiddenByUser;

    private void SetUpTrayAndHotkey()
    {
        try
        {
            _messageWindow = new MessageWindow();

            _tray = new TrayIconService(_messageWindow)
            {
                IsDockVisible = () => !_hiddenByUser,
            };
            _tray.Activated += BringToFront;
            _tray.ShowHideRequested += ToggleDockVisibility;
            _tray.AddNewRequested += OpenAddNew;
            _tray.SettingsRequested += OpenSettings;
            _tray.QuitRequested += Quit;

            _hotkeys = new HotkeyService(_messageWindow);
            _hotkeys.Pressed += BringToFront;
            ApplyHotkey();
        }
        catch (Exception ex)
        {
            // A dock that runs without its tray icon is degraded, not broken — never let this
            // take the app down at startup.
            Diag.Log("Tray/hotkey setup failed: " + ex);
        }
    }

    /// <summary>
    /// Removes the tray icon and releases the global shortcut. Idempotent, and called both from
    /// the window's <c>Closed</c> handler and explicitly before the process exits — the shell
    /// leaves a dead icon behind (until it is next hovered) if the process goes away without a
    /// <c>NIM_DELETE</c>, so this must not depend on <c>Closed</c> firing.
    /// </summary>
    public void ReleaseShellIntegration()
    {
        _tray?.Dispose();
        _hotkeys?.Dispose();
        _messageWindow?.Dispose();
    }

    /// <summary>Shuts Anchor down cleanly: shell integration first, then the app.</summary>
    public void Quit()
    {
        ReleaseShellIntegration();
        Application.Current.Exit();
    }

    // ---- Summoning the dock ------------------------------------------------

    /// <summary>
    /// Brings the dock into view and to the front: un-hides it if the tray menu hid it, cancels
    /// an auto-hide slide (and holds it out for the usual settle period), re-asserts top-most
    /// Z-order and takes focus. This is what the tray icon's click and the global shortcut do.
    /// </summary>
    public void BringToFront()
    {
        try
        {
            if (_hiddenByUser)
            {
                _hiddenByUser = false;
                _appWindow.Show(activateWindow: false);
                UpdateSizeAndPosition();
                ApplyAutoHide();
            }

            RevealNow();
            WindowChrome.EnsureTopmost(_hwnd);
            NativeMethods.SetForegroundWindow(_hwnd);
            Activate();
        }
        catch (Exception ex)
        {
            Diag.Log("BringToFront failed: " + ex);
        }
    }

    /// <summary>Tray menu "Show dock" / "Hide dock".</summary>
    private void ToggleDockVisibility()
    {
        if (_hiddenByUser)
        {
            BringToFront();
            return;
        }

        _hiddenByUser = true;
        // Stop the auto-hide controller first: it polls the cursor and would otherwise keep
        // moving (and re-showing) a window the user has asked to be rid of.
        PauseAutoHideForDrag();
        _appWindow.Hide();
    }

    /// <summary>Pulls the dock fully back into view immediately. Implemented in the auto-hide
    /// partial, which owns the slide state.</summary>
    partial void RevealNow();

    // ---- Settings-driven updates ------------------------------------------

    /// <summary>The configured shortcut, or null when none is set or it can't be parsed.</summary>
    internal HotkeyGesture? ConfiguredHotkey =>
        HotkeyGesture.TryParse(_config.Hotkey, out var g) ? g : null;

    /// <summary>
    /// Registers (or releases) the global shortcut to match the current config.
    /// </summary>
    /// <returns>False when the shortcut is enabled and valid but Windows refused it — i.e.
    /// another app already owns that combination.</returns>
    internal bool ApplyHotkey()
    {
        if (_hotkeys is null)
            return true;

        if (!_config.HotkeyEnabled || ConfiguredHotkey is not { } gesture)
        {
            _hotkeys.Unregister();
            return true;
        }
        return _hotkeys.Register(gesture);
    }

    /// <summary>Persists a new shortcut (or clears it with null) and re-registers it.</summary>
    /// <returns>False if Windows refused the combination; the choice is still saved so the
    /// Settings UI can show what was attempted alongside the conflict message.</returns>
    public bool SetHotkey(HotkeyGesture? gesture)
    {
        _config.Hotkey = gesture?.ToString() ?? string.Empty;
        SaveConfig();
        return ApplyHotkey();
    }

    /// <summary>Turns the global shortcut on or off without forgetting the combination.</summary>
    public bool SetHotkeyEnabled(bool on)
    {
        _config.HotkeyEnabled = on;
        SaveConfig();
        return ApplyHotkey();
    }

    /// <summary>
    /// Persists the UI language and switches the string table over. Windows opened from now on
    /// come up translated; already-loaded XAML (the dock strip itself) needs the restart that
    /// Settings offers.
    /// </summary>
    public void SetLanguage(string code)
    {
        _config.Language = code ?? string.Empty;
        SaveConfig();
        Loc.Initialize(_config.Language);
        _tray?.RefreshTooltip(); // the tray menu is built per-open, so it needs nothing else
    }
}
