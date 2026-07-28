using System.Diagnostics;
using Anchor.Models;
using Microsoft.Win32;

namespace Anchor.Services;

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

        // A "File" item with no registered handler doesn't throw and doesn't visibly open
        // anything — ShellExecute silently spins up a helper process that exits on its own.
        // Detect that up front and hand it to the shell's own "Open with" picker instead of
        // clicking the item and nothing happening.
        if (item.Kind == DockItemKind.File && File.Exists(item.Target) &&
            !HasFileAssociation(Path.GetExtension(item.Target)))
        {
            Diag.Log($"Launch: no app associated with '{Path.GetExtension(item.Target)}' — opening the picker");
            OpenWithPicker(item.Target);
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

    /// <summary>
    /// Starts an app with files as its arguments — what dropping a file onto an app's icon does,
    /// the same way dropping it onto that exe in Explorer would.
    /// <para>
    /// One process with every path passed to it, rather than one process per file: that is what
    /// the shell does, and it is what an editor or a viewer expects (opening five files in five
    /// windows of the same app is rarely what was meant). The item's own configured
    /// <see cref="DockItem.Arguments"/> come first, so a pinned "app with switches" keeps them.
    /// </para>
    /// </summary>
    public static void LaunchWith(DockItem item, IReadOnlyList<string> paths)
    {
        if (item.Kind != DockItemKind.Application || string.IsNullOrWhiteSpace(item.Target))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Target,
                Arguments = BuildArguments(item.Arguments, paths),
                UseShellExecute = true,
            };

            var dir = TryGetWorkingDirectory(item);
            if (dir is not null)
                psi.WorkingDirectory = dir;

            Process.Start(psi);
            Diag.Log($"LaunchWith: started '{item.Target}' with {paths.Count} file(s)");
        }
        catch (Exception ex)
        {
            Diag.Log($"LaunchWith FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the command line for <see cref="LaunchWith"/>: the item's own configured arguments
    /// first, then each path quoted.
    /// <para>
    /// One string rather than <see cref="ProcessStartInfo.ArgumentList"/>, because an item may
    /// already carry a raw <see cref="DockItem.Arguments"/> string and <c>ProcessStartInfo</c>
    /// refuses to have both — setting <c>Arguments</c> and <c>ArgumentList</c> together throws.
    /// </para>
    /// <para>
    /// Every path is quoted whether or not it contains a space: quoting unconditionally is what
    /// makes the result predictable, and an app that can't cope with a quoted path it asked for
    /// can't cope with the shell either. Simple quoting is sufficient — a Windows path cannot
    /// contain a double quote, so nothing here can escape its own quotes — but any that somehow
    /// does is dropped rather than allowed to split one argument into several.
    /// </para>
    /// </summary>
    public static string BuildArguments(string? configured, IReadOnlyList<string> paths)
    {
        var arguments = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(configured))
            arguments.Append(configured.Trim());

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (arguments.Length > 0)
                arguments.Append(' ');
            arguments.Append('"').Append(path.Replace("\"", string.Empty)).Append('"');
        }

        return arguments.ToString();
    }

    /// <summary>
    /// True if Windows has an app registered to open <paramref name="extension"/> (e.g. ".md"),
    /// checked the same way the shell resolves it: the per-user "UserChoice" override first
    /// (also covers packaged/Store apps), then the classic HKEY_CLASSES_ROOT ProgId. Never
    /// throws; an inconclusive lookup returns true so it never blocks a launch that might
    /// otherwise work fine.
    /// </summary>
    private static bool HasFileAssociation(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;
        try
        {
            using (var userChoice = Registry.CurrentUser.OpenSubKey(
                       $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice"))
            {
                if (userChoice?.GetValue("ProgId") is string progId && !string.IsNullOrEmpty(progId))
                    return true;
            }
            using (var classesRoot = Registry.ClassesRoot.OpenSubKey(extension))
            {
                if (classesRoot?.GetValue(null) is string progId && !string.IsNullOrEmpty(progId))
                    return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Summons the shell's own "How do you want to open this file?" picker.</summary>
    private static void OpenWithPicker(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                ArgumentList = { "shell32.dll,OpenAs_RunDLL", path },
                UseShellExecute = true,
            });
            Diag.Log("Launch: 'Open with' picker invoked");
        }
        catch (Exception ex)
        {
            Diag.Log($"Launch: 'Open with' picker FAILED: {ex.GetType().Name}: {ex.Message}");
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
