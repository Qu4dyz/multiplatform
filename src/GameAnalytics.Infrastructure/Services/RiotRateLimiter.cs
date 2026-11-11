using System.Collections.Concurrent;
using GameAnalytics.Core.Exceptions;

namespace GameAnalytics.Infrastructure.Services;

/// <summary>
/// Personal Riot keys expose <c>X-App-Rate-Limit: 100:120,20:1</c> (confirmed live).
/// Production keys may be higher — headers always win if we ever parse them.
/// Buckets are per routing/platform region so europe (match-v5) and euw1 (league-v4)
/// do not steal each other's quota.
/// </summary>
public class RiotRateLimiter
{
    private readonly ConcurrentDictionary<string, RegionBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public const int DefaultShortMax = 20;
    public static readonly TimeSpan DefaultShortDuration = TimeSpan.FromSeconds(1);

    public const int DefaultLongMax = 100;
    public static readonly TimeSpan DefaultLongDuration = TimeSpan.FromSeconds(120);

    public Task WaitForSlotAsync(CancellationToken ct = default) => WaitForSlotAsync("global", null, ct);

    public Task WaitForSlotAsync(string region, CancellationToken ct = default)
        => WaitForSlotAsync(region, maxBlockWait: null, ct);

    /// <param name="maxBlockWait">
    /// When set, fail fast with <see cref="RiotApiException"/> instead of sleeping out a long Riot Retry-After (desktop UI).
    /// </param>
    public async Task WaitForSlotAsync(string region, TimeSpan? maxBlockWait, CancellationToken ct = default)
    {
        var key = NormalizeRegion(region);
        var bucket = _buckets.GetOrAdd(key, _ => new RegionBucket(DefaultShortMax, DefaultShortDuration, DefaultLongMax, DefaultLongDuration));
        await bucket.WaitForSlotAsync(maxBlockWait, key, ct);
    }

    public void NotifyRateLimitHit(string region, TimeSpan retryAfter)
    {
        var key = NormalizeRegion(region);
        var bucket = _buckets.GetOrAdd(key, _ => new RegionBucket(DefaultShortMax, DefaultShortDuration, DefaultLongMax, DefaultLongDuration));
        bucket.BlockUntil(DateTime.UtcNow.Add(retryAfter));
    }

    public int ActiveBucketCount => _buckets.Count;

    private static string NormalizeRegion(string? region)
    {
        return string.IsNullOrWhiteSpace(region) ? "global" : region.Trim().ToLowerInvariant();
    }

    private class RegionBucket
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly Queue<DateTime> _shortWindowRequests = new();
        private readonly Queue<DateTime> _longWindowRequests = new();

        private readonly int _shortMax;
        private readonly TimeSpan _shortDuration;
        private readonly int _longMax;
        private readonly TimeSpan _longDuration;

        private DateTime _blockedUntil = DateTime.MinValue;

        public RegionBucket(int shortMax, TimeSpan shortDuration, int longMax, TimeSpan longDuration)
        {
            _shortMax = shortMax;
            _shortDuration = shortDuration;
            _longMax = longMax;
            _longDuration = longDuration;
        }

        public void BlockUntil(DateTime until)
        {
            lock (_shortWindowRequests)
            {
                if (until > _blockedUntil)
                {
                    _blockedUntil = until;
                }
            }
        }

        public async Task WaitForSlotAsync(TimeSpan? maxBlockWait, string region, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var now = DateTime.UtcNow;

                    // If explicit rate limit (429) active, sleep until block expires
                    if (now < _blockedUntil)
                    {
                        var blockDelay = _blockedUntil - now + TimeSpan.FromMilliseconds(50);
                        if (maxBlockWait.HasValue && blockDelay > maxBlockWait.Value)
                        {
                            var seconds = Math.Max(1, (int)Math.Ceiling((_blockedUntil - now).TotalSeconds));
                            throw new RiotApiException(
                                $"Riot API rate limit active for '{region}'. Retry in ~{seconds}s.",
                                statusCode: 429,
                                retryAfter: _blockedUntil - now,
                                region: region);
                        }

                        await Task.Delay(blockDelay, ct);
                        continue;
                    }

                    // Evict entries older than short window (1 second)
                    while (_shortWindowRequests.Count > 0 && (now - _shortWindowRequests.Peek()) > _shortDuration)
                    {
                        _shortWindowRequests.Dequeue();
                    }

                    // Evict entries older than long window (120 seconds)
                    while (_longWindowRequests.Count > 0 && (now - _longWindowRequests.Peek()) > _longDuration)
                    {
                        _longWindowRequests.Dequeue();
                    }

                    // If capacity available in both windows, consume a slot and proceed
                    if (_shortWindowRequests.Count < _shortMax && _longWindowRequests.Count < _longMax)
                    {
                        _shortWindowRequests.Enqueue(now);
                        _longWindowRequests.Enqueue(now);
                        break;
                    }

                    // Calculate the required wait time to free up a slot
                    var waitTime = TimeSpan.FromMilliseconds(25);

                    if (_shortWindowRequests.Count >= _shortMax)
                    {
                        var waitShort = _shortDuration - (now - _shortWindowRequests.Peek());
                        if (waitShort > waitTime) waitTime = waitShort;
                    }

                    if (_longWindowRequests.Count >= _longMax)
                    {
                        var waitLong = _longDuration - (now - _longWindowRequests.Peek());
                        if (waitLong > waitTime) waitTime = waitLong;
                    }

                    // Local window waits (1s / up to 120s). Cap interactive callers so UI cannot freeze
                    // when the long bucket is full from rapid desktop refreshes.
                    if (maxBlockWait.HasValue && waitTime > maxBlockWait.Value)
                    {
                        throw new RiotApiException(
                            $"Riot API local rate limit for '{region}'. Retry in ~{(int)Math.Ceiling(waitTime.TotalSeconds)}s.",
                            statusCode: 429,
                            retryAfter: waitTime,
                            region: region);
                    }

                    // Add a safety buffer to guarantee crossing Riot's edge window
                    waitTime += TimeSpan.FromMilliseconds(25);

                    await Task.Delay(waitTime, ct);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
