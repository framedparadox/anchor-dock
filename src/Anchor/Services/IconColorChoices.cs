using Windows.UI;

namespace Anchor.Services;

/// <summary>
/// A curated palette for tinting a group's (or any glyph-backed item's) icon, offered as
/// colour swatches beside the glyph grid. Deliberately a short, high-contrast set — enough to
/// tell a "Work" group from a "Media" group at a glance — not a full colour picker.
/// <para>
/// <see cref="Choice.Name"/> is a plain English mnemonic on the swatch (same scoping as
/// <see cref="IconChoices"/>); the picker's chrome labels are fully localized.
/// </para>
/// </summary>
public static class IconColorChoices
{
    public readonly record struct Choice(string Hex, string Name, Color Color);

    /// <summary>
    /// Normalizes a stored or typed colour to the canonical <c>#RRGGBB</c> form used in
    /// <c>dock.json</c>, or null when the value is empty or unparseable.
    /// </summary>
    public static string? Normalize(string? value) =>
        TryParse(value, out var color) ? ToHex(color) : null;

    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        try
        {
            if (hex.Length == 6)
            {
                color = Color.FromArgb(
                    255,
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
                return true;
            }

            if (hex.Length == 8)
            {
                color = Color.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    Convert.ToByte(hex[6..8], 16));
                return true;
            }
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }

        return false;
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static readonly IReadOnlyList<Choice> All = new[]
    {
        // Windows Fluent accent-adjacent hues, kept vivid enough to read on both light and dark
        // glass. Names match the everyday colour words users reach for when tagging folders.
        Make("#E81123", "Red"),
        Make("#F7630C", "Orange"),
        Make("#FCE100", "Yellow"),
        Make("#107C10", "Green"),
        Make("#0078D4", "Blue"),
        Make("#8764B8", "Purple"),
        Make("#E3008C", "Pink"),
        Make("#00B7C3", "Teal"),
        Make("#7A7574", "Gray"),
    };

    private static Choice Make(string hex, string name)
    {
        if (!TryParse(hex, out var color))
            throw new InvalidOperationException($"Bad palette entry: {hex}");
        return new Choice(hex, name, color);
    }
}
