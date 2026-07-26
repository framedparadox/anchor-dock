using Anchor.Services;
using Microsoft.UI.Xaml.Markup;

namespace Anchor.Localization;

/// <summary>
/// XAML markup extension for translated text: <c>Text="{loc:Localize Key=Settings.Theme}"</c>.
/// <para>
/// Resolved once, when the XAML is loaded, from the table <see cref="Loc"/> chose at startup —
/// so switching language re-reads correctly for any window opened afterwards, and the dock strip
/// itself picks it up on the next launch (Settings offers a restart for exactly that reason).
/// </para>
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class LocalizeExtension : MarkupExtension
{
    /// <summary>The string-table key to look up (see <c>Strings\en.json</c>).</summary>
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => Loc.Get(Key);
}
