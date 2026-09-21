using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Anchor.Controls;

/// <summary>
/// The "choose an icon" panel: every built-in glyph as one unscrolled swatch grid, an optional
/// colour row for tinting the glyph (groups use this so two folder icons can still be told
/// apart), plus the two routes to an image of the user's own.
/// <para>
/// Built as a plain panel rather than a control or a flyout of its own, because the two places it
/// appears host it differently — the item editor puts it in a flyout beside its icon field, and
/// the dock's new-group modal opens it as a panel over the strip — and the only thing they need to
/// agree on is what is in it.
/// </para>
/// </summary>
internal static class IconPickerPanel
{
    private const int Columns = 6;
    private const double SwatchSize = 36;
    private const double ColorSwatchSize = 28;

    /// <summary>
    /// Builds the panel.
    /// </summary>
    /// <param name="ownerHwnd">Window the OS file dialogs are parented to; without it they open
    /// behind the app.</param>
    /// <param name="swatchStyle">Chrome for the glyph buttons. The dock passes its own glass
    /// button style so the picker matches the strip it opens over; a normal window passes null and
    /// gets the standard button.</param>
    /// <param name="onSelected">Called with the chosen glyph or file path. Never both.</param>
    /// <param name="beforeBrowse">Run just before an OS file dialog opens — it takes focus, which
    /// light-dismisses whatever popup this panel is sitting in.</param>
    /// <param name="onColorSelected">When set, a colour row is shown under the glyphs. Called with
    /// a <c>#RRGGBB</c> hex, or null to clear the tint back to the theme colour.</param>
    /// <param name="currentColor">The item's current <c>IconColor</c>, used to ring the matching
    /// swatch so the open picker shows which tint is live.</param>
    public static StackPanel Build(
        nint ownerHwnd,
        Style? swatchStyle,
        Action<IconSelection> onSelected,
        Action? beforeBrowse = null,
        Action<string?>? onColorSelected = null,
        string? currentColor = null)
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(4), MaxWidth = 320 };

        var header = new TextBlock { Text = Loc.Get("IconPicker.Title") };
        if (Application.Current.Resources.TryGetValue("BodyStrongTextBlockStyle", out var h) &&
            h is Style headerStyle)
            header.Style = headerStyle;
        else
            header.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        panel.Children.Add(header);

        // Every glyph at once, in no scroller. There are few enough of them (six rows of six) that
        // the whole set fits a flyout without one, and a scrolling panel of icons hides half the
        // choice behind a gesture — the point of a swatch grid is that you can see all of it.
        panel.Children.Add(BuildGrid(swatchStyle, onSelected));

        if (onColorSelected is not null)
            panel.Children.Add(BuildColorRow(onColorSelected, currentColor));

        var browseImage = BrowseButton(Loc.Get("IconPicker.Browse"));
        browseImage.Click += async (_, _) =>
        {
            beforeBrowse?.Invoke();
            if (await PickImageAsync(ownerHwnd) is { } path)
                onSelected(new IconSelection(null, path));
        };
        panel.Children.Add(browseImage);

        var browseApp = BrowseButton(Loc.Get("IconPicker.BrowseApp"));
        browseApp.Click += async (_, _) =>
        {
            beforeBrowse?.Invoke();
            if (await PickExecutableAsync(ownerHwnd) is { } path)
                onSelected(new IconSelection(null, path));
        };
        panel.Children.Add(browseApp);

        return panel;
    }

    private static Grid BuildGrid(Style? swatchStyle, Action<IconSelection> onSelected)
    {
        var choices = IconChoices.All;
        int columns = Math.Min(Columns, choices.Count);
        int rows = (choices.Count + columns - 1) / columns;

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var swatch = new Button
            {
                Width = SwatchSize,
                Height = SwatchSize,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Content = new FontIcon
                {
                    Glyph = choice.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = 16,
                },
            };
            if (swatchStyle is not null)
                swatch.Style = swatchStyle;
            ToolTipService.SetToolTip(swatch, choice.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, choice.Name);
            var captured = choice;
            swatch.Click += (_, _) => onSelected(new IconSelection(captured.Glyph, null));
            Grid.SetColumn(swatch, i % columns);
            Grid.SetRow(swatch, i / columns);
            grid.Children.Add(swatch);
        }
        return grid;
    }

    /// <summary>
    /// One row: a "default / theme" chip, then every curated colour as a filled circle. Shown only
    /// when the host asked for colour picking (today: groups), so an app/file editor's picker
    /// stays the glyph+browse panel it was.
    /// </summary>
    private static StackPanel BuildColorRow(Action<string?> onColorSelected, string? currentColor)
    {
        var row = new StackPanel { Spacing = 6 };
        var label = new TextBlock
        {
            Text = Loc.Get("IconPicker.Color"),
            Style = Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var s) && s is Style caption
                ? caption
                : null,
        };
        row.Children.Add(label);

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        strip.Children.Add(ColorChip(
            fill: null,
            name: Loc.Get("IconPicker.ColorDefault"),
            selected: string.IsNullOrEmpty(currentColor),
            onClick: () => onColorSelected(null)));

        var current = IconColorChoices.Normalize(currentColor);
        foreach (var choice in IconColorChoices.All)
        {
            var captured = choice;
            strip.Children.Add(ColorChip(
                fill: captured.Color,
                name: captured.Name,
                selected: string.Equals(current, captured.Hex, StringComparison.OrdinalIgnoreCase),
                onClick: () => onColorSelected(captured.Hex)));
        }
        row.Children.Add(strip);
        return row;
    }

    private static Button ColorChip(Color? fill, string name, bool selected, Action onClick)
    {
        var disk = new Ellipse
        {
            Width = ColorSwatchSize - 8,
            Height = ColorSwatchSize - 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (fill is { } color)
        {
            disk.Fill = new SolidColorBrush(color);
        }
        else
        {
            // Hollow chip for "theme default": same size as the filled ones, outlined so it reads
            // as a colour choice rather than an empty hole in the row.
            disk.Stroke = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            disk.StrokeThickness = 1.5;
            disk.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        var button = new Button
        {
            Width = ColorSwatchSize,
            Height = ColorSwatchSize,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(ColorSwatchSize / 2),
            Content = disk,
            BorderThickness = new Thickness(selected ? 2 : 0),
            BorderBrush = selected
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : null,
        };
        ToolTipService.SetToolTip(button, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Button BrowseButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    /// <summary>Opens the OS file picker for an icon image, returning the chosen path or null if
    /// cancelled or the picker itself failed.</summary>
    public static async Task<string?> PickImageAsync(nint ownerHwnd)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerHwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            // Formats the XAML imaging stack decodes. Deliberately no .exe/.dll here: pulling an
            // icon out of a binary means choosing an index too, which is the picker below.
            foreach (var ext in new[] { ".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" })
                picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            Diag.Log($"Change icon failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Opens the OS file picker for a binary to lift an icon out of.</summary>
    public static async Task<string?> PickExecutableAsync(nint ownerHwnd)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerHwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            foreach (var ext in new[] { ".exe", ".dll", ".ico" })
                picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            Diag.Log($"Pick executable icon failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
