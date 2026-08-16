using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace Anchor;

/// <summary>
/// What happens when something from outside Anchor is dropped on a particular <em>icon</em> rather
/// than on the strip in general. Partial of <see cref="DockWindow"/>.
/// <para>
/// Two targets, both of which read the way the shell has trained people to expect:
/// dropping a file on an <b>app</b> opens the file with that app (as dropping it on an exe in
/// Explorer does), and dropping one on a <b>group</b> files it into the group. Everything else on
/// the strip keeps the existing behavior — the drop adds the item to the dock — which is why these
/// handlers mark the event handled only when they actually take it (see
/// <c>DockWindow.Root_DragOver</c>, which would otherwise overwrite the caption on the way up).
/// </para>
/// </summary>
public sealed partial class DockWindow
{
    /// <summary>The icon a drag is currently hovering, swelled as the drop cue.</summary>
    private DockItem? _dropTarget;

    /// <summary>How much a cell grows to say "drop it here".</summary>
    private const double DropTargetSwell = 1.3;

    private void Item_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DockItem item })
            return;

        // Only file-system drags land on an icon. A dropped URL has nothing to hand an app on a
        // command line and nothing sensible to file into a group, so it falls through to the
        // strip's own handler, which turns it into a web link.
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        string? caption = item.Kind switch
        {
            DockItemKind.Application => Loc.Format("Dock.DropOnApp", item.DisplayName),
            DockItemKind.Group => Loc.Format("Dock.DropInGroup", item.DisplayName),
            _ => null,
        };
        if (caption is null)
        {
            SetDropTarget(null);
            return;
        }

        SetDropTarget(item);
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is { } ui)
        {
            ui.Caption = caption;
            ui.IsCaptionVisible = true;
            ui.IsGlyphVisible = true;
        }
        // Stop here: the strip's handler would otherwise replace the caption with "Add to dock",
        // which is not what this drop is going to do.
        e.Handled = true;
    }

    /// <summary>
    /// Drops the cue when the drag leaves this cell — but only if this cell is still the one
    /// showing it. Moving from one icon straight onto the next raises the new cell's DragOver
    /// before the old cell's DragLeave, so an unconditional clear here would wipe the cue off the
    /// cell the cursor had just arrived at.
    /// </summary>
    private void Item_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DockItem item } && ReferenceEquals(_dropTarget, item))
            SetDropTarget(null);
    }

    private async void Item_Drop(object sender, DragEventArgs e)
    {
        SetDropTarget(null);

        if (sender is not FrameworkElement { Tag: DockItem item } ||
            item.Kind is not (DockItemKind.Application or DockItemKind.Group) ||
            !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        // Handled before the first await: the strip's Drop handler runs as this one bubbles, and
        // by the time an await has resumed it has already added the files to the dock.
        e.Handled = true;

        var deferral = e.GetDeferral();
        try
        {
            var paths = (await e.DataView.GetStorageItemsAsync())
                .Select(s => s.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (paths.Count == 0)
                return;

            if (item.Kind == DockItemKind.Application)
                Launcher.LaunchWith(item, paths);
            else
                FileIntoGroup(item, paths);
        }
        catch (Exception ex)
        {
            Diag.Log("Drop onto an icon failed: " + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>Adds dropped paths straight into a group, skipping the strip entirely.</summary>
    private void FileIntoGroup(DockItem group, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var child = new DockItem
            {
                Kind = DockItemFactory.Classify(path),
                DisplayName = DockItemFactory.SuggestName(path),
                Target = path,
            };
            group.Children.Add(child);
            _ = LoadOneIconAsync(child);
        }
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    /// <summary>
    /// Swells the icon a drag is over, and un-swells whatever it was over before. Reuses the
    /// magnification channel rather than adding a second visual state: it is already the property
    /// the item template watches for "this cell is bigger than the others", and two mechanisms
    /// fighting over one icon's size is a bug waiting to be written.
    /// </summary>
    private void SetDropTarget(DockItem? item)
    {
        if (ReferenceEquals(_dropTarget, item))
            return;
        _dropTarget?.SetMagnification(1);
        _dropTarget = item;
        _dropTarget?.SetMagnification(DropTargetSwell);
        // A target change needs the animation timer running to ever be seen — SetMagnification
        // only records where to lerp to, and nothing else on this path is guaranteed to already
        // have it ticking (the item-reorder path suppresses hover, the only other thing that
        // reliably kicks it, for the whole gesture; see Strip_PointerMoved).
        EnsureVisualAnimationRunning();
    }
}
