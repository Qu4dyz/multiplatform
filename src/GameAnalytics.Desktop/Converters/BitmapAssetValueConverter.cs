using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GameAnalytics.Core.Entities;

namespace GameAnalytics.Desktop.Converters;

public class BitmapAssetValueConverter : IValueConverter
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    static BitmapAssetValueConverter()
    {
        try
        {
            var baseCache = Path.Combine(AppContext.BaseDirectory, "cache", "icons");
            if (Directory.Exists(baseCache))
            {
                // Purge legacy flat files directly under cache/icons from 14.x
                foreach (var file in Directory.GetFiles(baseCache, "*.png"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    public static string GetCacheDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "cache", "icons", GameConstants.DDragonVersion);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string GetLocalPathForUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.Segments;
        var category = segments.Length >= 2 ? segments[^2].Trim('/') : "misc";
        var fileName = $"{category}_{Path.GetFileName(uri.LocalPath)}";
        return Path.Combine(GetCacheDir(), fileName);
    }

    public static Bitmap? GetOrLoadBitmap(string? url, Action<Bitmap>? onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (Cache.TryGetValue(url, out var cached) && cached != null)
        {
            return cached;
        }

        var localPath = GetLocalPathForUrl(url);

        if (File.Exists(localPath))
        {
            try
            {
                using var stream = File.OpenRead(localPath);
                var bmp = new Bitmap(stream);
                Cache[url] = bmp;
                return bmp;
            }
            catch { }
        }

        // Asynchronously load and notify UI thread
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await HttpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);
                using var memStream = new MemoryStream(bytes);
                var bmp = new Bitmap(memStream);
                Cache[url] = bmp;
                if (onLoaded != null)
                {
                    Dispatcher.UIThread.Post(() => onLoaded(bmp));
                }
            }
            catch
            {
                // Silently ignore failed network requests
            }
        });

        return null;
    }

    public static async Task PreloadImagesAsync(IEnumerable<string> urls)
    {
        var distinctUrls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();

        var tasks = distinctUrls.Select(async url =>
        {
            if (Cache.ContainsKey(url) && Cache[url] != null) return;

            try
            {
                var localPath = GetLocalPathForUrl(url);

                if (File.Exists(localPath))
                {
                    try
                    {
                        using var stream = File.OpenRead(localPath);
                        Cache[url] = new Bitmap(stream);
                        return;
                    }
                    catch { }
                }

                var bytes = await HttpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);
                using var memStream = new MemoryStream(bytes);
                Cache[url] = new Bitmap(memStream);
            }
            catch
            {
                // Silently ignore failed network requests
            }
        });

        await Task.WhenAll(tasks);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Cache.TryGetValue(url, out var cachedBitmap) && cachedBitmap != null)
        {
            return cachedBitmap;
        }

        // Try local disk cache
        var localPath = GetLocalPathForUrl(url);

        if (File.Exists(localPath))
        {
            try
            {
                using var stream = File.OpenRead(localPath);
                var bitmap = new Bitmap(stream);
                Cache[url] = bitmap;
                return bitmap;
            }
            catch
            {
                // Fall through to download if corrupted
            }
        }

        // Download in background
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await HttpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                Cache[url] = bitmap;
            }
            catch
            {
                // Ignore download errors (e.g. offline)
            }
        });

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
