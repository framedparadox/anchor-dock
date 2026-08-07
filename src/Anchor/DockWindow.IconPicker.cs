using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anchor;

/// <summary>
/// The icon picker entry point. Shared by every "Change icon…" menu item and by the new-group
/// dialog. Partial of <see cref="DockWindow"/>.
/// </summary>
public sealed partial class DockWindow
{
    private const int IconPickerColumns = 6;

    /// <summary>Opens the picker as a modal dialog above the dock strip.</summary>
    private void ShowIconPicker(FrameworkElement anchor, Action<IconSelection> onSelected) =>
        ShowIconPickerDialog(onSelected);
}
