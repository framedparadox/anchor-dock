using Anchor.Models;
using Microsoft.UI.Xaml;
using Xunit;

namespace Anchor.Tests.Models;

public class DockItemTests
{
    [Fact]
    public void DisplayName_change_raises_PropertyChanged()
    {
        var item = new DockItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.DisplayName = "Notepad";

        Assert.Equal("Notepad", item.DisplayName);
        Assert.Contains(nameof(DockItem.DisplayName), raised);
    }

    [Theory]
    [InlineData(DockItemKind.WebLink, "\uE774")]
    [InlineData(DockItemKind.Folder, "\uE8B7")]
    [InlineData(DockItemKind.Separator, "")]
    [InlineData(DockItemKind.Application, "\uE7C3")]
    [InlineData(DockItemKind.File, "\uE7C3")]
    public void Glyph_matches_kind(DockItemKind kind, string expectedGlyph)
    {
        var item = new DockItem { Kind = kind };

        Assert.Equal(expectedGlyph, item.Glyph);
    }

    [Fact]
    public void With_no_icon_the_glyph_is_shown_and_the_image_is_hidden()
    {
        var item = new DockItem();

        Assert.Null(item.IconImage);
        Assert.Equal(Visibility.Collapsed, item.ImageVisibility);
        Assert.Equal(Visibility.Visible, item.GlyphVisibility);
    }

    [Fact]
    public void IsSeparator_is_true_only_for_the_separator_kind()
    {
        Assert.True(new DockItem { Kind = DockItemKind.Separator }.IsSeparator);
        Assert.False(new DockItem { Kind = DockItemKind.Application }.IsSeparator);
        Assert.False(new DockItem { Kind = DockItemKind.File }.IsSeparator);
        Assert.False(new DockItem { Kind = DockItemKind.Folder }.IsSeparator);
        Assert.False(new DockItem { Kind = DockItemKind.WebLink }.IsSeparator);
    }

    [Fact]
    public void A_separator_takes_a_narrower_slot_than_a_launchable_cell()
    {
        // The dock sizes its window by summing these, so a separator that claimed a full cell
        // would leave a visible gap around the hairline.
        Assert.Equal(DockMetrics.SeparatorExtent, new DockItem { Kind = DockItemKind.Separator }.CellExtent);
        Assert.Equal(DockMetrics.Cell, new DockItem { Kind = DockItemKind.Application }.CellExtent);
        Assert.True(DockMetrics.SeparatorExtent < DockMetrics.Cell);
    }

    [Fact]
    public void A_separator_renders_the_divider_instead_of_the_launch_button()
    {
        var separator = new DockItem { Kind = DockItemKind.Separator };
        var app = new DockItem { Kind = DockItemKind.Application };

        Assert.Equal(Visibility.Collapsed, separator.ButtonVisibility);
        Assert.Equal(Visibility.Visible, separator.SeparatorVisibility);
        Assert.Equal(Visibility.Visible, app.ButtonVisibility);
        Assert.Equal(Visibility.Collapsed, app.SeparatorVisibility);
    }

    [Fact]
    public void Re_orienting_the_strip_swaps_a_separators_narrow_side()
    {
        var separator = new DockItem { Kind = DockItemKind.Separator };
        var raised = new List<string?>();
        separator.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Horizontal strip: narrow across, full height.
        Assert.Equal(DockMetrics.SeparatorExtent, separator.CellWidth);
        Assert.Equal(DockMetrics.Cell, separator.CellHeight);

        separator.SetFlowVertical(true);

        Assert.Equal(DockMetrics.Cell, separator.CellWidth);
        Assert.Equal(DockMetrics.SeparatorExtent, separator.CellHeight);
        Assert.Contains(nameof(DockItem.CellWidth), raised);
        Assert.Contains(nameof(DockItem.CellHeight), raised);

        // The hairline lies across the flow, so its long side follows the orientation too.
        Assert.True(separator.SeparatorLineWidth > separator.SeparatorLineHeight);
    }

    [Fact]
    public void Re_orienting_to_the_same_orientation_raises_nothing()
    {
        var item = new DockItem { Kind = DockItemKind.Separator };
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.SetFlowVertical(false); // already horizontal

        Assert.Empty(raised);
    }

    [Fact]
    public void A_launchable_cell_is_square_in_either_orientation()
    {
        var item = new DockItem { Kind = DockItemKind.Application };

        Assert.Equal(DockMetrics.Cell, item.CellWidth);
        Assert.Equal(DockMetrics.Cell, item.CellHeight);

        item.SetFlowVertical(true);

        Assert.Equal(DockMetrics.Cell, item.CellWidth);
        Assert.Equal(DockMetrics.Cell, item.CellHeight);
    }

    [Fact]
    public void IsGroup_is_true_only_for_the_group_kind()
    {
        Assert.True(new DockItem { Kind = DockItemKind.Group }.IsGroup);
        Assert.False(new DockItem { Kind = DockItemKind.Application }.IsGroup);
        Assert.False(new DockItem { Kind = DockItemKind.Separator }.IsGroup);
    }

    [Fact]
    public void Every_item_starts_with_no_children()
    {
        // Children are a group's contents; every other kind carries an empty list rather than
        // null, so the dock can walk them unconditionally.
        Assert.NotNull(new DockItem().Children);
        Assert.Empty(new DockItem().Children);
        Assert.Empty(new DockItem { Kind = DockItemKind.Group }.Children);
    }

    [Fact]
    public void A_group_takes_a_full_cell_and_renders_as_a_button()
    {
        // It is a normal-sized, clickable icon that happens to open a fly-out rather than launch.
        var group = new DockItem { Kind = DockItemKind.Group };

        Assert.Equal(DockMetrics.Cell, group.CellExtent);
        Assert.Equal(Visibility.Visible, group.ButtonVisibility);
        Assert.Equal(Visibility.Collapsed, group.SeparatorVisibility);
    }

    [Fact]
    public void Running_state_drives_both_the_dot_and_the_announced_status()
    {
        var item = new DockItem { Kind = DockItemKind.Application };
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(item.IsRunning);
        Assert.Equal(Visibility.Collapsed, item.RunningIndicatorVisibility);
        Assert.Equal("", item.RunningStatus);

        item.SetRunning(true, "Running");

        Assert.True(item.IsRunning);
        Assert.Equal(Visibility.Visible, item.RunningIndicatorVisibility);
        Assert.Equal("Running", item.RunningStatus);
        Assert.Contains(nameof(DockItem.RunningIndicatorVisibility), raised);
        Assert.Contains(nameof(DockItem.RunningStatus), raised);
    }

    [Fact]
    public void Setting_the_same_running_state_twice_raises_nothing()
    {
        // The poll re-applies the same answer every couple of seconds; re-notifying on each pass
        // would churn the bindings of every icon on the dock for no reason.
        var item = new DockItem();
        item.SetRunning(true, "Running");

        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        item.SetRunning(true, "Running");

        Assert.Empty(raised);
    }

    [Fact]
    public void Separators_and_groups_never_show_a_running_dot()
    {
        // Neither has a process behind it, so even a stale "running" flag must not draw one.
        var separator = new DockItem { Kind = DockItemKind.Separator };
        var group = new DockItem { Kind = DockItemKind.Group };

        separator.SetRunning(true, "Running");
        group.SetRunning(true, "Running");

        Assert.Equal(Visibility.Collapsed, separator.RunningIndicatorVisibility);
        Assert.Equal(Visibility.Collapsed, group.RunningIndicatorVisibility);
    }

    [Fact]
    public void HasCustomIcon_tracks_the_custom_icon_path()
    {
        var item = new DockItem();
        Assert.False(item.HasCustomIcon);

        item.CustomIconPath = @"C:\icons\thing.png";
        Assert.True(item.HasCustomIcon);

        // Cleared the way the "Use the default icon" command clears it.
        item.CustomIconPath = null;
        Assert.False(item.HasCustomIcon);

        // Whitespace is not an icon either — it would otherwise show the menu command with no
        // icon actually pinned.
        item.CustomIconPath = "   ";
        Assert.False(item.HasCustomIcon);
    }

    // A stand-in for whatever the icon picker hands back — any of IconChoices.All would do
    // equally well here; the tests below exercise the mechanism, not a specific glyph.
    private const string TestGlyph = ""; // FavoriteStar

    [Fact]
    public void HasCustomIcon_also_tracks_a_picked_glyph()
    {
        // A built-in icon-picker glyph is just as much "a custom icon" as a picked image file —
        // both put "Use the default icon" in the item's context menu.
        var item = new DockItem();
        Assert.False(item.HasCustomIcon);

        item.CustomGlyph = TestGlyph;
        Assert.True(item.HasCustomIcon);

        item.CustomGlyph = null;
        Assert.False(item.HasCustomIcon);
    }

    [Fact]
    public void CustomGlyph_overrides_the_kind_derived_Glyph()
    {
        var item = new DockItem { Kind = DockItemKind.Folder };
        var kindGlyph = item.Glyph; // Folder's own default, before any override

        item.CustomGlyph = TestGlyph;
        Assert.Equal(TestGlyph, item.Glyph);
        Assert.NotEqual(kindGlyph, item.Glyph);

        // Clearing it falls back to the kind-derived glyph again, not to nothing.
        item.CustomGlyph = null;
        Assert.Equal(kindGlyph, item.Glyph);
    }

    [Fact]
    public void Setting_CustomGlyph_raises_Glyph_and_HasCustomIcon_but_not_for_a_no_op_set()
    {
        var item = new DockItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.CustomGlyph = TestGlyph;
        Assert.Contains(nameof(DockItem.CustomGlyph), raised);
        Assert.Contains(nameof(DockItem.Glyph), raised);
        Assert.Contains(nameof(DockItem.HasCustomIcon), raised);

        raised.Clear();
        item.CustomGlyph = TestGlyph; // same value again
        Assert.Empty(raised);
    }

    // ---- Density ----------------------------------------------------------

    [Fact]
    public void Cell_geometry_follows_the_density_setting()
    {
        // DockMetrics is app-wide state, so this test restores it — a leaked Large would silently
        // change what every other sizing assertion in this class is comparing against.
        var original = DockMetrics.Density;
        try
        {
            DockMetrics.SetDensity(DockDensity.Small);
            double small = new DockItem().CellExtent;

            DockMetrics.SetDensity(DockDensity.Large);
            double large = new DockItem().CellExtent;

            Assert.True(small < large, $"Small ({small}) should be tighter than Large ({large})");
            Assert.Equal(DockMetrics.Cell, large);
        }
        finally
        {
            DockMetrics.SetDensity(original);
        }
    }

    [Fact]
    public void RefreshMetrics_renotifies_every_size_binding()
    {
        // The item template binds these; without the notification a density change would resize
        // the window but leave the icons drawn at the old size.
        var item = new DockItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.RefreshMetrics();

        Assert.Contains(nameof(DockItem.CellWidth), raised);
        Assert.Contains(nameof(DockItem.CellHeight), raised);
        Assert.Contains(nameof(DockItem.RenderIconSize), raised);
        Assert.Contains(nameof(DockItem.RenderGlyphSize), raised);
    }

    // ---- Magnification -----------------------------------------------------

    [Fact]
    public void Magnification_grows_the_icon_but_never_past_its_cell()
    {
        var item = new DockItem();
        double resting = item.RenderIconSize;

        item.SetMagnification(1.5);
        Assert.True(item.RenderIconSize > resting);

        // An unbounded swell would just clip against a cell that never changes size.
        item.SetMagnification(10);
        Assert.True(item.RenderIconSize <= DockMetrics.Cell);
    }

    [Fact]
    public void Re_applying_the_same_magnification_raises_nothing()
    {
        // Pointer-move fires continuously; re-notifying on every event for an icon whose size
        // hasn't actually changed would churn the whole strip's bindings.
        var item = new DockItem();
        item.SetMagnification(1.4);

        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        item.SetMagnification(1.4);

        Assert.Empty(raised);
    }

    // ---- New per-item state ------------------------------------------------

    [Fact]
    public void A_new_item_has_no_shortcut_and_no_folder_flyout()
    {
        // Both are opt-in: a folder keeps opening in Explorer, and no item claims a system-wide
        // combination, until the user says so.
        var item = new DockItem();

        Assert.Null(item.Hotkey);
        Assert.False(item.FolderFlyout);
    }

    [Fact]
    public void Id_defaults_to_a_unique_generated_value()
    {
        var a = new DockItem();
        var b = new DockItem();

        Assert.False(string.IsNullOrWhiteSpace(a.Id));
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void New_item_defaults_to_not_hidden_and_the_Application_kind()
    {
        var item = new DockItem();

        Assert.False(item.Hidden);
        Assert.Equal(DockItemKind.Application, item.Kind);
        Assert.Equal("", item.DisplayName);
        Assert.Equal("", item.Target);
        Assert.Null(item.Arguments);
        Assert.Null(item.CustomIconPath);
    }

    // ---- The hover highlight ------------------------------------------------

    [Fact]
    public void An_item_shows_no_highlight_until_the_cursor_is_on_it()
    {
        var item = new DockItem();

        Assert.Equal(0, item.HoverOpacity);
        item.SetHovered(true);
        Assert.Equal(1, item.HoverOpacity);
        item.SetHovered(false);
        Assert.Equal(0, item.HoverOpacity);
    }

    [Fact]
    public void A_separator_never_highlights()
    {
        // It is a divider, not something to click, so it must stay dark even when the strip's
        // pointer tracking reports the cursor inside its slot.
        var item = new DockItem { Kind = DockItemKind.Separator };

        item.SetHovered(true);

        Assert.Equal(0, item.HoverOpacity);
    }

    [Fact]
    public void Hovering_raises_a_change_notification_for_the_highlight()
    {
        // The cell's Opacity is bound OneWay to HoverOpacity; without the notification the
        // highlight would never actually appear.
        var item = new DockItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.SetHovered(true);
        item.SetHovered(true); // no-op: already hovered

        Assert.Equal(new[] { nameof(DockItem.HoverOpacity) }, changed);
    }
}
