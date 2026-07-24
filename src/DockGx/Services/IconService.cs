using System.Net.Http;
using System.Runtime.InteropServices;
using DockGx.Interop;
using DockGx.Models;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace DockGx.Services;

/// <summary>
/// Resolves a dock item's visual icon. Apps / files / folders use the Windows shell thumbnail
/// pipeline (the same crisp, theme-correct icons Explorer shows). Web links auto-fetch the
/// site's favicon over HTTP, cached to disk so it only downloads once and works offline after.
/// </summary>
public static class IconService
{
    private const uint IconSize = 96; // request generously; the dock renders smaller & scales down

    // Shared client: favicons are tiny, follow redirects, and a UA header avoids servers that
    // reject "no user agent" requests.
    private static readonly HttpClient Http = CreateClient();

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockGx", "IconCache");

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) DockGx/1.0");
        return c;
    }

    public static async Task<ImageSource?> LoadIconAsync(DockItem item)
    {
        try
        {
            // 1. Explicit user-supplied icon wins.
            if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
                return await FromFileAsync(item.CustomIconPath!);

            // 2. Kind-specific resolution.
            switch (item.Kind)
            {
                case DockItemKind.Application:
                case DockItemKind.File:
                    if (File.Exists(item.Target))
                        return await AppOrFileIconAsync(item.Target);
                    break;

                case DockItemKind.Folder:
                    if (Directory.Exists(item.Target))
                        return await ThumbnailAsync(await StorageFolder.GetFolderFromPathAsync(item.Target));
                    break;

                case DockItemKind.WebLink:
                    return await FaviconAsync(item.Target);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: load failed for '{item.Target}': {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Apps and files usually get a crisp thumbnail via the WinRT Storage pipeline, but that
    /// pipeline flatly refuses to open shortcuts — <c>StorageFile.GetFileFromPathAsync</c> on a
    /// .lnk throws <c>UnauthorizedAccessException</c> ("UNABLE_TO_MASK_PATH") every time,
    /// regardless of where the .lnk lives — and it can deny arbitrary paths for an unpackaged
    /// app more generally. <see cref="NativeMethods.SHGetFileInfo"/> has neither limitation, so
    /// it's the fallback whenever the WinRT path doesn't pan out.
    /// </summary>
    private static async Task<ImageSource?> AppOrFileIconAsync(string path)
    {
        try
        {
            return await ThumbnailAsync(await StorageFile.GetFileFromPathAsync(path));
        }
        catch (Exception ex)
        {
            Diag.Log($"IconService: Storage thumbnail failed for '{path}' ({ex.GetType().Name}) — falling back to the shell icon");
            return await ShellIconAsync(path);
        }
    }

    private static async Task<ImageSource?> ShellIconAsync(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        nint result = NativeMethods.SHGetFileInfo(
            path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
        if (result == 0 || info.hIcon == nint.Zero)
            return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return await DecodeAsync(ms.ToArray());
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
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

    // ---- Favicons ---------------------------------------------------------

    /// <summary>
    /// Resolves a web link's icon: a disk-cached favicon if present, otherwise fetched from the
    /// site itself (its /favicon.ico), falling back to a favicon service. Returns null (→ globe
    /// glyph) when nothing usable is found or there's no connectivity.
    /// </summary>
    private static async Task<ImageSource?> FaviconAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return null;

        string host = uri.Host;
        string scheme = uri.Scheme is "http" or "https" ? uri.Scheme : "https";
        string cacheFile = Path.Combine(CacheDir, Sanitize(host) + ".ico");

        // 1. Cached copy.
        if (File.Exists(cacheFile))
        {
            var cached = await DecodeAsync(await File.ReadAllBytesAsync(cacheFile));
            if (cached is not null)
                return cached;
        }

        // 2. Fetch, preferring the site's own favicon (no third party), then a service fallback.
        foreach (var candidate in new[]
                 {
                     $"{scheme}://{host}/favicon.ico",
                     $"https://icons.duckduckgo.com/ip3/{host}.ico",
                 })
        {
            var bytes = await TryDownloadAsync(candidate);
            if (bytes is null || !LooksLikeImage(bytes))
                continue;

            var image = await DecodeAsync(bytes);
            if (image is null)
                continue;

            TrySaveCache(cacheFile, bytes);
            return image;
        }

        return null;
    }

    private static async Task<byte[]?> TryDownloadAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode)
                return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and < 2_000_000 ? bytes : null;
        }
        catch
        {
            return null; // no connectivity / DNS / TLS / timeout — glyph fallback
        }
    }

    /// <summary>Decodes raw bytes into a BitmapImage, confirming the decode actually succeeded
    /// (so a 200-OK HTML error page never gets shown as a broken image).</summary>
    private static async Task<ImageSource?> DecodeAsync(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            var tcs = new TaskCompletionSource<bool>();
            void OnOpened(object s, Microsoft.UI.Xaml.RoutedEventArgs e) => tcs.TrySetResult(true);
            void OnFailed(object s, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e) => tcs.TrySetResult(false);
            bmp.ImageOpened += OnOpened;
            bmp.ImageFailed += OnFailed;

            using (var ras = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(ras))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
            }

            bool ok = await tcs.Task;
            bmp.ImageOpened -= OnOpened;
            bmp.ImageFailed -= OnFailed;
            return ok ? bmp : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Magic-byte sniff: cheaply reject HTML/text (e.g. a soft-404) before decoding.</summary>
    private static bool LooksLikeImage(byte[] b)
    {
        if (b.Length < 4)
            return false;
        // ICO / CUR
        if (b[0] == 0x00 && b[1] == 0x00 && (b[2] == 0x01 || b[2] == 0x02) && b[3] == 0x00) return true;
        // PNG
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
        // JPEG
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;
        // GIF
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return true;
        // BMP
        if (b[0] == 0x42 && b[1] == 0x4D) return true;
        // WEBP (RIFF....WEBP)
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 &&
            b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return true;
        return false;
    }

    private static void TrySaveCache(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex)
        {
            Diag.Log("IconService cache write failed: " + ex.Message);
        }
    }

    private static string Sanitize(string host)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            host = host.Replace(c, '_');
        return host;
    }
}
