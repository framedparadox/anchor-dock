using System.Threading;
using DockGx.Services;
using Microsoft.UI.Xaml;

namespace DockGx;

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
            // A dock is an always-on utility, so record what went wrong (to %Temp%\dockgx.log) for
            // diagnosis. Handled is deliberately left false: swallowing every fault could leave the
            // dock running in a corrupt state, so a genuinely unhandled exception still surfaces.
            Diag.Log($"UNHANDLED: {e.Message}\n{e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsFirstInstance())
        {
            Diag.Log("Another DockGx instance is already running — exiting this one.");
            Exit();
            return;
        }

        Dock = new DockWindow();
        Dock.Activate();
    }

    /// <summary>
    /// True if this is the only running DockGx in the current session. Uses a named mutex: the
    /// first process creates it (and keeps it alive via <see cref="_instanceMutex"/>); any later
    /// process finds it already present and returns false.
    /// </summary>
    private static bool IsFirstInstance()
    {
        _instanceMutex = new Mutex(initiallyOwned: false, @"Local\DockGx.SingleInstance.v1", out bool createdNew);
        return createdNew;
    }
}
