using System.Diagnostics;
using DockGx.Models;

namespace DockGx.Services;

/// <summary>Launches a dock item via the shell (handles apps, files, folders, .lnk and URLs).</summary>
public static class Launcher
{
    public static void Launch(DockItem item)
    {
        Diag.Log($"Launch requested: kind={item.Kind} name='{item.DisplayName}' target='{item.Target}'");
        if (item.IsSeparator || string.IsNullOrWhiteSpace(item.Target))
        {
            Diag.Log("Launch aborted: separator or empty target");
            return;
        }

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
            Diag.Log("Launch: Process.Start succeeded");
        }
        catch (Exception ex)
        {
            Diag.Log($"Launch FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
