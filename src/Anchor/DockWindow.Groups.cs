using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anchor;

/// <summary>
/// Groups: one dock icon that holds several items and opens them in a fly-out, so a long dock
/// stays short. Partial of <see cref="DockWindow"/>.
/// <para>
/// A group is a <see cref="DockItemKind.Group"/> item that carries <see cref="DockItem.Children"/>
/// instead of a target. Groups deliberately do not nest, and separators can't go inside one: both
/// would give the fly-out a structure it has no way to render, and neither earns its complexity.
/// </para>
/// </summary>
public sealed partial class DockWindow
{
    /// <summary>Every group currently on the dock, in dock order (drives the "Move to" menu).</summary>
    public IReadOnlyList<DockItem> Groups =>
        _profile.Items.Where(i => i.IsGroup).ToList();

    /// <summary>True for an item that can be filed into a group: not a group, not a separator.</summary>
    private static bool CanBeGrouped(DockItem item) => !item.IsGroup && !item.IsSeparator;

    // ---- Creating and dissolving ------------------------------------------

    /// <summary>Adds an empty group to the end of the dock and returns it.</summary>
    public DockItem CreateGroup(string name)
    {
        var group = new DockItem
        {
            Kind = DockItemKind.Group,
            DisplayName = string.IsNullOrWhiteSpace(name) ? Loc.Get("Kind.Group") : name.Trim(),
        };
        AddDockItem(group);
        return group;
    }

    /// <summary>
    /// Files an item into a group. The item leaves the top-level list entirely — it lives inside
    /// the group from here on, and comes back via <see cref="RemoveFromGroup"/> or
    /// <see cref="Ungroup"/>.
    /// </summary>
    public void MoveItemToGroup(DockItem item, DockItem group)
    {
        if (!CanBeGrouped(item) || !group.IsGroup || ReferenceEquals(item, group))
            return;

        _profile.Items.Remove(item);
        Items.Remove(item);
        group.Children.Add(item);

        PersistAndRelayout();
        RaiseItemsChanged();
    }

    /// <summary>
    /// Lifts a child back out onto the dock, immediately after the group it came from, so it
    /// lands where the user is looking rather than at the far end of the strip.
    /// </summary>
    public void RemoveFromGroup(DockItem child)
    {
        var group = _profile.Items.FirstOrDefault(i => i.IsGroup && i.Children.Contains(child));
        if (group is null)
            return;

        group.Children.Remove(child);
        int at = _profile.Items.IndexOf(group);
        _profile.Items.Insert(at < 0 ? _profile.Items.Count : at + 1, child);

        RebuildVisible();
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    /// <summary>
    /// Dissolves a group, leaving its children on the dock in its place and in their own order.
    /// The non-destructive counterpart to removing a group, which takes the children with it.
    /// </summary>
    public void Ungroup(DockItem group)
    {
        if (!group.IsGroup)
            return;

        int at = _profile.Items.IndexOf(group);
        if (at < 0)
            return;

        _profile.Items.RemoveAt(at);
        _profile.Items.InsertRange(at, group.Children);
        group.Children.Clear();

        RebuildVisible();
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    /// <summary>Removes a single entry from inside a group, discarding it.</summary>
    public void RemoveGroupChild(DockItem child)
    {
        var group = _profile.Items.FirstOrDefault(i => i.IsGroup && i.Children.Contains(child));
        if (group is null)
            return;
        group.Children.Remove(child);
        PersistAndRelayout();
        RaiseItemsChanged();
    }

    // ---- The fly-out -------------------------------------------------------

    /// <summary>
    /// Opens a group's fly-out directly over its dock icon: its (visible) children as a
    /// <see cref="ShowDockBarFlyout">dock bar</see> — the same cells, chrome and spacing as the
    /// strip itself, running the same way the dock does, so what pops out is a second dock
    /// floating above the main one rather than a menu beside it.
    /// <para>
    /// It always opens away from whichever screen edge the dock is snapped to (so it can never
    /// open off-screen into that edge), or upward when the dock is floating — see
    /// <see cref="GroupFlyoutPlacement"/>.
    /// </para>
    /// </summary>
    private void ShowGroupFlyout(FrameworkElement anchor, DockItem group)
    {
        var children = group.Children.Where(c => !c.Hidden).ToList();
        ShowDockBarFlyout(anchor, new DockBarOptions(
            children,
            Loc.Get("Group.Empty"),
            OnContext: ShowGroupChildMenu,
            // Dragging a cell clear of the bar is the reverse of dropping an icon onto the group:
            // it puts the item back on the strip, next to the group it came out of.
            OnDragOut: RemoveFromGroup));
    }

    /// <summary>
    /// Which way the fly-out bar should extend: away from the dock's snapped edge (so it never
    /// tries to open into the screen edge the dock is pinned against), or straight up when the
    /// dock floats — matching the "extends above the dock" look for the common floating and
    /// bottom-snapped cases.
    /// </summary>
    private FlyoutPlacementMode GroupFlyoutPlacement =>
        !_profile.Snapped ? FlyoutPlacementMode.Top : _profile.Edge switch
        {
            DockEdge.Bottom => FlyoutPlacementMode.Top,
            DockEdge.Top => FlyoutPlacementMode.Bottom,
            DockEdge.Left => FlyoutPlacementMode.Right,
            DockEdge.Right => FlyoutPlacementMode.Left,
            _ => FlyoutPlacementMode.Top,
        };

    private void ShowGroupChildMenu(
        FrameworkElement target, DockItem child, Flyout owner, ContextRequestedEventArgs e)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Mi(Loc.Get("Menu.Open"), () => { owner.Hide(); LaunchOrFocus(child); }));
        // The same one editor the strip's own menu opens — name, target and icon together — in a
        // window of its own, so closing the bar first takes nothing with it.
        menu.Items.Add(Mi(Loc.Get("Menu.Edit"), () =>
        {
            owner.Hide();
            _manager.OpenItemEditor(this, child);
        }));
        if (child.HasCustomIcon)
            menu.Items.Add(Mi(Loc.Get("Menu.ResetIcon"), () => SetCustomIcon(child, null)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Mi(Loc.Get("Menu.RemoveFromGroup"), () => { owner.Hide(); RemoveFromGroup(child); }));
        menu.Items.Add(Mi(Loc.Get("Menu.Remove"), () => { owner.Hide(); RemoveGroupChild(child); }));

        if (e.TryGetPosition(target, out var pos))
            menu.ShowAt(target, pos);
        else
            menu.ShowAt(target);

        static MenuFlyoutItem Mi(string text, Action onClick)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => onClick();
            return mi;
        }
    }

    // ---- "Move to group" menu ---------------------------------------------

    /// <summary>
    /// The <c>Move to group ▸</c> submenu for a top-level item: every existing group, plus a
    /// "New group…" entry that creates one and files the item into it in a single step.
    /// </summary>
    private MenuFlyoutSubItem BuildMoveToGroupMenu(FrameworkElement target, DockItem item)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Menu.MoveToGroup") };

        foreach (var group in Groups)
        {
            var entry = new MenuFlyoutItem
            {
                Text = string.IsNullOrWhiteSpace(group.DisplayName)
                    ? Loc.Get("Kind.Group")
                    : group.DisplayName,
            };
            var captured = group;
            entry.Click += (_, _) => MoveItemToGroup(item, captured);
            sub.Items.Add(entry);
        }

        if (sub.Items.Count > 0)
            sub.Items.Add(new MenuFlyoutSeparator());

        var create = new MenuFlyoutItem { Text = Loc.Get("Menu.NewGroup") };
        create.Click += (_, _) => ShowNewGroupDialog(target, item);
        sub.Items.Add(create);

        return sub;
    }

    /// <summary>
    /// The default icon shown in the new-group dialog's icon box before the user picks one — the
    /// same glyph a group gets if it's never customized (see <see cref="DockItem.Glyph"/>).
    /// </summary>
    private const string DefaultGroupGlyph = ""; // FolderOpen

    /// <summary>
    /// The modal for creating a group: an icon box (click it to open the same picker "Change
    /// icon…" uses), a name field, and a confirm/cancel pair. A real <see cref="ContentDialog"/>
    /// rather than the small inline flyout every other rename/edit action uses — unlike those,
    /// this one also decides the group's icon, which needs enough room for a picker to open out
    /// of, and reads better as a deliberate "create" step than a quick in-place edit.
    /// <para>
    /// When <paramref name="item"/> is given (opened via an item's "Move to group ▸ New group…")
    /// it is filed into the group the moment it's created.
    /// </para>
    /// </summary>
    internal async void ShowNewGroupDialog(FrameworkElement anchor, DockItem? item)
    {
        IconSelection? pending = null;

        var iconBox = new Button
        {
            Width = 56,
            Height = 56,
            MinWidth = 0,
            MinHeight = 0,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
        };
        ToolTipService.SetToolTip(iconBox, Loc.Get("IconPicker.Title"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(iconBox, Loc.Get("IconPicker.Title"));

        void RenderIconBox()
        {
            if (pending is { FilePath: { } path })
            {
                iconBox.Content = new Image
                {
                    Source = new BitmapImage(new Uri(path)),
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                };
            }
            else
            {
                iconBox.Content = new FontIcon
                {
                    Glyph = pending?.Glyph ?? DefaultGroupGlyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = 22,
                };
            }
        }
        RenderIconBox();
        iconBox.Click += (_, _) => ShowIconPickerFlyout(iconBox, sel =>
        {
            pending = sel;
            RenderIconBox();
        });

        var nameBox = new TextBox
        {
            Text = Loc.Get("Kind.Group"),
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox, Loc.Get("Docks.Name"));

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(iconBox);
        row.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Loc.Get("Flyout.NewGroup"),
            Content = row,
            PrimaryButtonText = Loc.Get("Flyout.CreateGroup"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        // The dialog itself is topmost-adjacent (same XamlRoot as the dock), but the pointer is
        // about to leave the dock strip for it; hold auto-hide out for the same reason the group
        // fly-out does.
        PauseAutoHideForDrag();
        dialog.Closed += (_, _) => ResumeAutoHideAfterDrag();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var group = CreateGroup(nameBox.Text.Trim());
        if (item is not null)
            MoveItemToGroup(item, group);
        if (pending is { } selection)
            ApplyIconSelection(group, selection);
    }
}
