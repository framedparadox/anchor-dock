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
            Diag.Log("Launch skipped: separator or empty target");
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

            // Give apps a sensible working directory — but never let resolving it block the
            // launch itself (a bad path here must not stop the item from opening).
            var dir = TryGetWorkingDirectory(item);
            if (dir is not null)
                psi.WorkingDirectory = dir;

            Process.Start(psi);
            Diag.Log("Launch: Process.Start succeeded");
        }
        catch (Exception ex)
        {
            Diag.Log($"Launch FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Working directory for a real local file/app, or null. Never throws.</summary>
    private static string? TryGetWorkingDirectory(DockItem item)
    {
        if (item.Kind is not (DockItemKind.Application or DockItemKind.File))
            return null; // folders, web links, shell commands: let the shell decide
        try
        {
            var dir = Path.GetDirectoryName(item.Target);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }
}
