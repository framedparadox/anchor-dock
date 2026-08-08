using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Anchor;

/// <summary>What the icon picker returned: a built-in glyph, or a path to a custom image file
/// picked via "Browse for an image…". Never both.</summary>
internal readonly record struct IconSelection(string? Glyph, string? FilePath);

/// <summary>In-place item edits — rename, edit target, change icon — each opened as a panel
/// floating above the dock. Partial of <see cref="DockWindow"/>.</summary>
public sealed partial class DockWindow
{
    // ---- The edit panel ----------------------------------------------------
    //
    // Every in-place edit shares one surface: a popup that is NOT clipped to the dock window, hung
    // off the dock the same way the fly-out bars are. Both halves of that matter — getting either
    // wrong is what left these edits drawing inside the strip, where there is nothing to see.

    /// <summary>
    /// Wraps <paramref name="content"/> in a popup that is free of the dock window's bounds.
    /// <para>
    /// The dock is a strip one cell tall and only as wide as its own icons. A
    /// <see cref="Flyout"/> is by default clipped to the window it belongs to, so a rename box or
    /// a swatch grid opened against the dock was drawn <em>inside</em> that strip, which has room
    /// for none of it — what showed was a sliver of a panel behind the icons. (A
    /// <see cref="ContentDialog"/> fares worse still: it centres itself in the same tiny window.)
    /// With <see cref="FlyoutBase.ShouldConstrainToRootBounds"/> off, the popup gets its own
    /// top-level window and renders at full size over the desktop, exactly as the fly-out bars do.
    /// </para>
    /// </summary>
    private static Flyout PanelFlyout(FrameworkElement content) => new()
    {
        Content = content,
        ShouldConstrainToRootBounds = false,
    };

    /// <summary>
    /// Opens a <see cref="PanelFlyout"/> clear of the dock: anchored on the dock <em>window</em>
    /// at the same point the fly-out bars use (<see cref="BarAnchorPoint"/>), so it extends away
    /// from whichever screen edge the dock is snapped to — straight up for the common floating and
    /// bottom-snapped cases — lines up with the icon it was opened from, and never overlaps the
    /// strip's own glass.
    /// <para>
    /// Shown in <see cref="FlyoutShowMode.Standard"/> rather than left to <c>Auto</c>: every panel
    /// has something to type into or click, so it has to take focus rather than open transient
    /// under the cursor.
    /// </para>
    /// </summary>
    private void ShowPanelFlyout(Flyout flyout, FrameworkElement anchor)
    {
        var placement = GroupFlyoutPlacement;
        flyout.Placement = placement;

        // The cursor is about to leave the strip for the panel; hold auto-hide out while it is up,
        // the way the fly-out bars do, and re-arm on close.
        PauseAutoHideForDrag();
        flyout.Closed += (_, _) => ResumeAutoHideAfterDrag();

        flyout.ShowAt(RootGrid, new FlyoutShowOptions
        {
            Placement = placement,
            Position = BarAnchorPoint(anchor, placement),
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    /// <summary>The panel body every edit shares: a titled column its own controls go into.</summary>
    private static StackPanel PanelBody(string header)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(FlyoutHeader(header));
        return panel;
    }

    /// <summary>A panel's confirm button, right-aligned under its field.</summary>
    private static Button PanelCommitButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    // ---- Rename ------------------------------------------------------------

    /// <summary>Renames an item in place. Enter or Save commits; dismissing the panel discards.</summary>
    private void ShowRenameFlyout(FrameworkElement target, DockItem item)
    {
        var box = new TextBox { Text = item.DisplayName, Width = 280 };
        var save = PanelCommitButton(Loc.Get("Common.Save"));

        var panel = PanelBody(Loc.Get("Flyout.Rename"));
        panel.Children.Add(box);
        panel.Children.Add(save);

        var flyout = PanelFlyout(panel);

        void Commit()
        {
            var name = box.Text.Trim();
            if (name.Length > 0)
            {
                item.DisplayName = name;
                SaveConfig();
                RaiseItemsChanged();
            }
            flyout.Hide();
        }

        save.Click += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        ShowPanelFlyout(flyout, target);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
    }

    // ---- Edit target -------------------------------------------------------

    /// <summary>
    /// Edits what an item points at. Re-classifying the new target can change the item's kind (a
    /// path swapped for a URL becomes a web link), so the icon is dropped and re-resolved, and the
    /// item is re-inserted at its own index to make the strip rebuild that one cell.
    /// </summary>
    private void ShowEditFlyout(FrameworkElement target, DockItem item)
    {
        var box = new TextBox { Text = item.Target, Width = 320 };
        var save = PanelCommitButton(Loc.Get("Common.Save"));

        var panel = PanelBody(Loc.Get("Flyout.EditTarget"));
        panel.Children.Add(box);
        panel.Children.Add(save);

        var flyout = PanelFlyout(panel);

        void Commit()
        {
            var t = box.Text.Trim();
            if (t.Length > 0)
            {
                item.Target = t;
                item.Kind = DockItemFactory.Classify(t);
                item.IconImage = null;
                int i = Items.IndexOf(item);
                if (i >= 0)
                {
                    Items.RemoveAt(i);
                    Items.Insert(i, item);
                }
                SaveConfig();
                RaiseItemsChanged();
                _ = LoadOneIconAsync(item);
            }
            flyout.Hide();
        }

        save.Click += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        ShowPanelFlyout(flyout, target);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
    }

    // ---- Change icon -------------------------------------------------------

    /// <summary>
    /// The icon picker: the built-in glyphs as a swatch grid, plus the two "browse" routes to an
    /// icon of the user's own. Opens as the same panel as the edits above — the grid is both wider
    /// and taller than the dock window, which makes it the plainest case of a surface that has to
    /// live outside the strip rather than inside it.
    /// </summary>
    private void ShowIconPickerFlyout(FrameworkElement target, Action<IconSelection> onSelected)
    {
        var panel = PanelBody(Loc.Get("IconPicker.Title"));
        panel.MaxWidth = 320;

        var flyout = PanelFlyout(panel);

        void Pick(IconSelection selection)
        {
            flyout.Hide();
            onSelected(selection);
        }

        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = BuildIconPickerGrid(Pick),
        });

        var browseImage = PanelBrowseButton(Loc.Get("IconPicker.Browse"));
        browseImage.Click += async (_, _) =>
        {
            if (await PickIconFileAsync() is { } path)
                Pick(new IconSelection(null, path));
        };
        panel.Children.Add(browseImage);

        var browseApp = PanelBrowseButton(Loc.Get("IconPicker.BrowseApp"));
        browseApp.Click += async (_, _) =>
        {
            if (await PickExecutableIconAsync() is { } path)
                Pick(new IconSelection(null, path));
        };
        panel.Children.Add(browseApp);

        ShowPanelFlyout(flyout, target);
    }

    /// <summary>One of the picker's full-width "browse…" buttons.</summary>
    private static Button PanelBrowseButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    };

    private Grid BuildIconPickerGrid(Action<IconSelection> onSelected)
    {
        var choices = IconChoices.All;
        int columns = Math.Min(IconPickerColumns, choices.Count);
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
                Style = (Style)RootGrid.Resources["DockGlassButtonStyle"],
                Width = 36,
                Height = 36,
                MinWidth = 0,
                MinHeight = 0,
                Content = new FontIcon
                {
                    Glyph = choice.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    FontSize = 16,
                },
            };
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

    /// <summary>Applies a picker result to an existing item: a glyph or a file path, never both,
    /// and each clears whichever the other kind of custom icon was set.</summary>
    private void ApplyIconSelection(DockItem item, IconSelection selection)
    {
        if (selection.Glyph is not null)
            SetCustomGlyph(item, selection.Glyph);
        else if (selection.FilePath is not null)
            SetCustomIcon(item, selection.FilePath);
    }

    /// <summary>Opens the OS file picker for an icon image, returning the chosen path or null if
    /// cancelled or the picker itself failed.</summary>
    private async Task<string?> PickIconFileAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            // Formats the XAML imaging stack decodes. Deliberately no .exe/.dll: pulling an icon
            // out of a binary means choosing an index too, which is a picker of its own.
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

    private async Task<string?> PickExecutableIconAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
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

    public void RefreshItemIcon(DockItem item)
    {
        IconService.ClearCachedIcon(item);
        item.IconImage = null;
        SaveConfig();
        RaiseItemsChanged();
        _ = LoadOneIconAsync(item);
    }
}
