using Microsoft.UI.Xaml;

namespace DockGx;

public partial class App : Application
{
    /// <summary>The single dock window instance.</summary>
    public static DockWindow? Dock { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the dock alive on non-fatal XAML exceptions during development.
            System.Diagnostics.Debug.WriteLine($"[DockGx] Unhandled: {e.Message}\n{e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Dock = new DockWindow();
        Dock.Activate();
    }
}
