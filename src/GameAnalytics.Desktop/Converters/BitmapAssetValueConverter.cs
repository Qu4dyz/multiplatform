using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using GameAnalytics.Core.Entities;

namespace GameAnalytics.Desktop.Converters;

public class BitmapAssetValueConverter : IValueConverter
{
    private const string EmbeddedMinimapAvares =
        "avares://GameAnalytics.Desktop/Assets/Maps/summoners_rift.png";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (compatible; GameAnalytics/1.0)");
        return client;
    }

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

        // Warm the minimap so the first moment open never paints a black square.
        try { TryLoadEmbeddedMinimap(); }
        catch { }
    }

    public static bool IsCached(string? url)
        => !string.IsNullOrWhiteSpace(url) && Cache.TryGetValue(url, out var bmp) && bmp != null;

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

    private static Bitmap? TryLoadEmbeddedMinimap()
    {
        if (Cache.TryGetValue(GameConstants.SummonersRiftMinimapAsset, out var cached) && cached != null)
            return cached;

        using var stream = AssetLoader.Open(new Uri(EmbeddedMinimapAvares));
        var bmp = new Bitmap(stream);
        Cache[GameConstants.SummonersRiftMinimapAsset] = bmp;
        Cache[EmbeddedMinimapAvares] = bmp;
        return bmp;
    }

    private static Bitmap? LoadAvares(string avaresUrl)
    {
        if (Cache.TryGetValue(avaresUrl, out var cached) && cached != null)
            return cached;

        using var stream = AssetLoader.Open(new Uri(avaresUrl));
        var bmp = new Bitmap(stream);
        Cache[avaresUrl] = bmp;
        return bmp;
    }

    public static Bitmap? GetOrLoadBitmap(string? url, Action<Bitmap>? onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (url == GameConstants.SummonersRiftMinimapAsset || url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var embedded = url == GameConstants.SummonersRiftMinimapAsset
                    ? TryLoadEmbeddedMinimap()
                    : LoadAvares(url);
                if (embedded != null)
                {
                    onLoaded?.Invoke(embedded);
                    return embedded;
                }
            }
            catch { }
            return null;
        }

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

        var download = StartDownload(url);
        if (onLoaded != null)
        {
            // Every caller gets the bitmap, not only the first one: lists that are rebuilt while the
            // download runs (top champions, match cards) used to keep a blank image forever.
            _ = download.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result is { } bmp)
                    Dispatcher.UIThread.Post(() => onLoaded(bmp));
            }, TaskScheduler.Default);
        }

        return null;
    }

    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> InFlight = new();
    private static readonly ConcurrentDictionary<string, DateTime> FailedUntil = new();
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    /// <summary>One shared download per URL; a failed URL may be retried after a short back-off.</summary>
    private static Task<Bitmap?> StartDownload(string url)
    {
        if (FailedUntil.TryGetValue(url, out var until) && until > DateTime.UtcNow)
            return Task.FromResult<Bitmap?>(null);

        return InFlight.GetOrAdd(url, u => Task.Run(async () =>
        {
            try
            {
                var bmp = await DownloadWithFallbackAsync(u);
                if (bmp != null)
                {
                    Cache[u] = bmp;
                    FailedUntil.TryRemove(u, out _);
                }
                else
                {
                    FailedUntil[u] = DateTime.UtcNow + FailureBackoff;
                }
                return bmp;
            }
            finally
            {
                InFlight.TryRemove(u, out _);
            }
        }));
    }

    private static async Task<Bitmap?> DownloadWithFallbackAsync(string url)
    {
        foreach (var candidate in CandidateUrls(url))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var response = await HttpClient.GetAsync(candidate);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        break; // wrong key or patch: try the next candidate instead of retrying

                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    using var memStream = new MemoryStream(bytes);
                    var bmp = new Bitmap(memStream);
                    try { await File.WriteAllBytesAsync(GetLocalPathForUrl(url), bytes); } catch { }
                    return bmp;
                }
                catch
                {
                    // transient network error: one more try, then the next candidate
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Data Dragon first; champion portraits fall back to CommunityDragon, which also accepts
    /// aliases and serves champions a fresh Data Dragon patch may not list yet.
    /// </summary>
    internal static IEnumerable<string> CandidateUrls(string url)
    {
        yield return url;

        const string marker = "/img/champion/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && url.Contains("ddragon.leagueoflegends.com", StringComparison.OrdinalIgnoreCase))
        {
            var key = Path.GetFileNameWithoutExtension(url[(idx + marker.Length)..]);
            if (!string.IsNullOrWhiteSpace(key))
                yield return $"https://cdn.communitydragon.org/latest/champion/{key}/square";
        }
    }

    public static async Task PreloadImagesAsync(IEnumerable<string> urls)
    {
        var distinctUrls = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();

        var tasks = distinctUrls.Select(async url =>
        {
            if (Cache.TryGetValue(url, out var known) && known != null) return;

            if (url == GameConstants.SummonersRiftMinimapAsset || url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (url == GameConstants.SummonersRiftMinimapAsset)
                        TryLoadEmbeddedMinimap();
                    else
                        LoadAvares(url);
                }
                catch { }
                return;
            }

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

                await StartDownload(url);
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

        if (url == GameConstants.SummonersRiftMinimapAsset || url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return url == GameConstants.SummonersRiftMinimapAsset
                    ? TryLoadEmbeddedMinimap()
                    : LoadAvares(url);
            }
            catch
            {
                return null;
            }
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

        // Download in background. A converter binding will not re-run; views use RemoteImage.Url for live refresh.
        _ = StartDownload(url);

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
