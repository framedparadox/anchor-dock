using Anchor.Services;
using Xunit;

namespace Anchor.Tests.Services;

public class IconColorChoicesTests
{
    [Theory]
    [InlineData("#E81123", 0xE8, 0x11, 0x23)]
    [InlineData("e81123", 0xE8, 0x11, 0x23)]
    [InlineData("#FF0078D4", 0x00, 0x78, 0xD4)] // AARRGGBB — alpha kept for parse, hex normalizes to RGB
    public void TryParse_reads_hex_forms(string input, byte r, byte g, byte b)
    {
        Assert.True(IconColorChoices.TryParse(input, out var color));
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("#GG0000")]
    [InlineData("#123")]
    public void TryParse_rejects_empty_and_garbage(string? input)
    {
        Assert.False(IconColorChoices.TryParse(input, out _));
        Assert.Null(IconColorChoices.Normalize(input));
    }

    [Fact]
    public void Normalize_canonicalizes_to_hash_RRGGBB()
    {
        Assert.Equal("#E81123", IconColorChoices.Normalize("e81123"));
        Assert.Equal("#E81123", IconColorChoices.Normalize("#E81123"));
        Assert.Equal("#0078D4", IconColorChoices.Normalize("#FF0078D4"));
    }

    [Fact]
    public void Palette_entries_all_parse_and_round_trip()
    {
        Assert.NotEmpty(IconColorChoices.All);
        foreach (var choice in IconColorChoices.All)
        {
            Assert.Equal(choice.Hex, IconColorChoices.Normalize(choice.Hex));
            Assert.True(IconColorChoices.TryParse(choice.Hex, out var color));
            Assert.Equal(choice.Color, color);
        }
    }
}
