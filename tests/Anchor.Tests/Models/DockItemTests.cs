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
}
