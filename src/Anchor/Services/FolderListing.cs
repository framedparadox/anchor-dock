using Anchor.Models;

namespace Anchor.Services;

/// <summary>
/// Reads a folder's contents as dock items — what a folder fly-out ("stack") shows.
/// <para>
/// The entries are a view of what is on disk right now and are never persisted, which is why they
/// are ordinary <see cref="DockItem"/>s rather than a type of their own: the fly-out bar, the icon
/// resolution and the launcher all already know how to handle one.
/// </para>
/// <para>
/// Separate from <c>DockWindow</c> so the ordering and filtering rules are testable against a real
/// directory, which is the only way to be sure "folders first, then files, each alphabetical" and
/// the hidden-file exclusion actually hold.
/// </para>
/// </summary>
public static class FolderListing
{
    /// <summary>
    /// How many entries a folder bar will show. A stack is a shortcut to the handful of things you
    /// actually reach for, and a bar with a thousand cells in it is a worse file manager than the
    /// one already on the machine — past this, the bar's "Open in File Explorer" cell is the answer.
    /// </summary>
    public const int MaxEntries = 60;

    /// <summary>
    /// The folder's contents as dock items: directories first, then files, each alphabetical —
    /// Explorer's own order, so the bar lists things where the user expects to find them.
    /// <para>
    /// Never throws. A folder that has been deleted, renamed or that this user can't read yields
    /// an empty list, which the bar renders as its "nothing here" caption — the alternative is an
    /// exception on a click, which is a worse answer to "that folder is gone".
    /// </para>
    /// </summary>
    public static List<DockItem> Read(string path, int max = MaxEntries)
    {
        var items = new List<DockItem>();
        if (max <= 0)
            return items;

        try
        {
            if (!Directory.Exists(path))
                return items;

            // Hidden and system entries are skipped for the same reason Explorer hides them by
            // default: a stack full of desktop.ini and $RECYCLE.BIN is noise, not contents.
            foreach (var entry in new DirectoryInfo(path)
                         .EnumerateFileSystemInfos()
                         .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                         .OrderBy(e => e is DirectoryInfo ? 0 : 1)
                         .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                         .Take(max))
            {
                items.Add(new DockItem
                {
                    Kind = entry is DirectoryInfo
                        ? DockItemKind.Folder
                        : DockItemFactory.Classify(entry.FullName),
                    DisplayName = entry.Name,
                    Target = entry.FullName,
                });
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"FolderListing: could not read '{path}': {ex.GetType().Name}: {ex.Message}");
        }
        return items;
    }
}
