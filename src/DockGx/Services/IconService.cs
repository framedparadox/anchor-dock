using DockGx.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace DockGx.Services;

/// <summary>
/// Resolves a dock item's visual icon using the Windows shell thumbnail pipeline.
/// This yields the same crisp, theme-correct icons Explorer shows (including resolving
/// .lnk shortcut targets and .exe embedded icons) without any GDI/HICON marshalling.
/// </summary>
public static class IconService
{
    private const uint IconSize = 96; // request generously; the dock renders smaller & scales down

    public static async Task<ImageSource?> LoadIconAsync(DockItem item)
    {
        try
        {
            // 1. Explicit user-supplied icon wins.
            if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
                return await FromFileAsync(item.CustomIconPath!);

            // 2. Kind-specific shell thumbnail.
            switch (item.Kind)
            {
                case DockItemKind.Application:
                case DockItemKind.File:
                    if (File.Exists(item.Target))
                        return await ThumbnailAsync(await StorageFile.GetFileFromPathAsync(item.Target));
                    break;

                case DockItemKind.Folder:
                    if (Directory.Exists(item.Target))
                        return await ThumbnailAsync(await StorageFolder.GetFolderFromPathAsync(item.Target));
                    break;

                case DockItemKind.WebLink:
                    // Glyph (globe) fallback for now; favicon fetching can be layered on later.
                    return null;
            }
        }
        catch
        {
            // Fall through to glyph fallback on any shell/IO failure.
        }
        return null;
    }

    private static async Task<ImageSource?> ThumbnailAsync(IStorageItemProperties item)
    {
        using StorageItemThumbnail? thumb =
            await item.GetThumbnailAsync(ThumbnailMode.SingleItem, IconSize, ThumbnailOptions.ResizeThumbnail);

        if (thumb is null || thumb.Size == 0)
            return null;

        var bmp = new BitmapImage { DecodePixelWidth = (int)IconSize };
        await bmp.SetSourceAsync(thumb);
        return bmp;
    }

    private static async Task<ImageSource?> FromFileAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        return bmp;
    }
}
