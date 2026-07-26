using System.Threading;
using Anchor.Services;
using Microsoft.UI.Xaml;

namespace Anchor;

/// <summary>
/// Application entry point. Creates the single <see cref="DockWindow"/>, enforces a
/// one-dock-per-session policy (a second launch exits quietly), and routes otherwise-unhandled
/// exceptions to the diagnostic log so a crash leaves a trace instead of vanishing silently.
/// </summary>
public partial class App : Application
{
    /// <summary>The single dock window instance.</summary>
    public static DockWindow? Dock { get; private set; }

    // Held for the whole process lifetime so the named mutex it represents stays alive; a second
    // launch (e.g. the Start-menu entry while the "start with Windows" copy is already running)
    // sees the mutex already exists and bows out. Kept in a static field so the GC/finalizer
    // never closes the handle out from under a running dock. "Local\" scopes it per user session.
    private static Mutex? _instanceMutex;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // A dock is an always-on utility, so record what went wrong (to %Temp%\anchor.log) for
            // diagnosis. Handled is deliberately left false: swallowing every fault could leave the
            // dock running in a corrupt state, so a genuinely unhandled exception still surfaces.
            Diag.Log($"UNHANDLED: {e.Message}\n{e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsFirstInstance())
        {
            Diag.Log("Another Anchor instance is already running — exiting this one.");
            Exit();
            return;
        }

        // Load the string table before any window is constructed: XAML resolves its
        // {loc:Localize} bindings as it loads, so the language has to be settled first.
        Loc.Initialize(DockStore.Load().Language);

        Dock = new DockWindow();
        Dock.Activate();
    }

    /// <summary>
    /// Relaunches Anchor and exits this instance — the way a language change is applied to
    /// windows (the dock strip above all) whose XAML has already been loaded. The instance mutex
    /// is released first so the replacement process doesn't mistake us for a second launch and
    /// bow out.
    /// </summary>
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            Diag.Log("Restart: no process path — staying put.");
            return;
        }

        try
        {
            // Drop the tray icon and the global shortcut before the replacement starts, so the
            // two processes never briefly show two icons or fight over the same hotkey.
            Dock?.ReleaseShellIntegration();
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Failed to spawn the replacement: better to keep running (with the old language)
            // than to exit and leave the user with no dock at all.
            Diag.Log("Restart failed: " + ex.Message);
            return;
        }

        Current.Exit();
    }

    /// <summary>
    /// True if this is the only running Anchor in the current session. Uses a named mutex: the
    /// first process creates it (and keeps it alive via <see cref="_instanceMutex"/>); any later
    /// process finds it already present and returns false.
    /// </summary>
    private static bool IsFirstInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: false, @"Local\Anchor.SingleInstance.v1", out bool createdNew);
        return createdNew;
    }
}
