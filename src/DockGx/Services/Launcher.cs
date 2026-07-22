using System.Diagnostics;
using DockGx.Models;

namespace DockGx.Services;

/// <summary>Launches a dock item via the shell (handles apps, files, folders, .lnk and URLs).</summary>
public static class Launcher
{
    public static void Launch(DockItem item)
    {
        if (item.IsSeparator || string.IsNullOrWhiteSpace(item.Target))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Target,
                UseShellExecute = true, // resolves associations, shortcuts, and default browser for URLs
            };

            if (item.Kind == DockItemKind.Application && !string.IsNullOrWhiteSpace(item.Arguments))
                psi.Arguments = item.Arguments;

            // Give apps a sensible working directory.
            if (item.Kind is DockItemKind.Application or DockItemKind.File)
            {
                var dir = Path.GetDirectoryName(item.Target);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    psi.WorkingDirectory = dir;
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DockGx] Failed to launch '{item.DisplayName}' ({item.Target}): {ex.Message}");
        }
    }
}
