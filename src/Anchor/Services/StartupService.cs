using Microsoft.Win32;

namespace Anchor.Services;

/// <summary>
/// Toggles "launch Anchor at sign-in" via the per-user <c>Run</c> registry key. Per-user (HKCU)
/// so it needs no elevation. All calls are guarded — a locked-down registry simply means the
/// setting silently no-ops rather than crashing the dock.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Anchor";

    /// <summary>True when the Run key currently points at this executable.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Adds or removes the Run entry to match <paramref name="enabled"/>.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
                return;

            if (enabled)
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Anchor.exe");
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Diag.Log("StartupService.SetEnabled failed: " + ex.Message);
        }
    }
}
