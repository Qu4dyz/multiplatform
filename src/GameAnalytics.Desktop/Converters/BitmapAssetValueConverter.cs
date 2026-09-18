using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace GameAnalytics.Desktop.Converters;

public class BitmapAssetValueConverter : IValueConverter
{
    private static readonly HttpClient HttpClient = new();
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();
    private static readonly string CacheDir = Path.Combine(AppContext.BaseDirectory, "cache", "icons");

    static BitmapAssetValueConverter()
    {
        if (!Directory.Exists(CacheDir))
        {
            Directory.CreateDirectory(CacheDir);
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Cache.TryGetValue(url, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        // Try local disk cache
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var localPath = Path.Combine(CacheDir, fileName);

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

