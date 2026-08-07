using Anchor.Models;
using Anchor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Anchor;

/// <summary>Modal dialogs for in-place item edits. Partial of <see cref="DockWindow"/>.</summary>
public sealed partial class DockWindow
{
    private async Task<bool> ShowDockDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = RootGrid.XamlRoot;
        PauseAutoHideForDrag();
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            ResumeAutoHideAfterDrag();
        }
    }

    private async void ShowRenameDialog(DockItem item)
    {
        var box = new TextBox { Text = item.DisplayName, Width = 280 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(FlyoutHeader(Loc.Get("Flyout.Rename")));
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Flyout.Rename"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Flyout.Rename"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        bool commit = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                commit = true;
                dialog.Hide();
                e.Handled = true;
            }
        };

        if (await ShowDockDialogAsync(dialog) || commit)
        {
            var name = box.Text.Trim();
            if (name.Length > 0)
            {
                item.DisplayName = name;
                SaveConfig();
                RaiseItemsChanged();
            }
        }
    }

    private async void ShowEditDialog(DockItem item)
    {
        var box = new TextBox { Text = item.Target, Width = 360 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(FlyoutHeader(Loc.Get("Flyout.EditTarget")));
        panel.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Flyout.EditTarget"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Common.Save"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        bool commit = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                commit = true;
                dialog.Hide();
                e.Handled = true;
            }
        };

        if (!await ShowDockDialogAsync(dialog) && !commit)
            return;

        var t = box.Text.Trim();
        if (t.Length == 0)
            return;

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

    private async void ShowIconPickerDialog(Action<IconSelection> onSelected)
    {
        IconSelection? picked = null;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 320 };
        panel.Children.Add(FlyoutHeader(Loc.Get("IconPicker.Title")));

        var dialog = new ContentDialog
        {
            Title = Loc.Get("IconPicker.Title"),
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        void Pick(IconSelection selection)
        {
            picked = selection;
            dialog.Hide();
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 280,
            VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            Content = BuildIconPickerGrid(Pick),
        };
        panel.Children.Add(scroll);

        var browseImage = new Button
        {
            Content = Loc.Get("IconPicker.Browse"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        browseImage.Click += async (_, _) =>
        {
            if (await PickIconFileAsync() is { } path)
                Pick(new IconSelection(null, path));
        };
        panel.Children.Add(browseImage);

        var browseApp = new Button
        {
            Content = Loc.Get("IconPicker.BrowseApp"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        browseApp.Click += async (_, _) =>
        {
            if (await PickExecutableIconAsync() is { } path)
                Pick(new IconSelection(null, path));
        };
        panel.Children.Add(browseApp);

        dialog.Content = panel;
        dialog.XamlRoot = RootGrid.XamlRoot;
        PauseAutoHideForDrag();
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            ResumeAutoHideAfterDrag();
        }

        if (picked is { } selection)
            onSelected(selection);
    }

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
