namespace GameAnalytics.Infrastructure.Services;

public class RiotRateLimiter
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Queue<DateTime> _shortWindowRequests = new(); // 20 requests per 1 second
    private readonly Queue<DateTime> _longWindowRequests = new();  // 100 requests per 120 seconds

    private const int ShortWindowMax = 20;
    private static readonly TimeSpan ShortWindowDuration = TimeSpan.FromSeconds(1);

    private const int LongWindowMax = 100;
    private static readonly TimeSpan LongWindowDuration = TimeSpan.FromSeconds(120);

    public async Task WaitForSlotAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = DateTime.UtcNow;

                // Clean old entries
                while (_shortWindowRequests.Count > 0 && (now - _shortWindowRequests.Peek()) > ShortWindowDuration)
                {
                    _shortWindowRequests.Dequeue();
                }

                while (_longWindowRequests.Count > 0 && (now - _longWindowRequests.Peek()) > LongWindowDuration)
                {
                    _longWindowRequests.Dequeue();
                }

                // Check limits
                if (_shortWindowRequests.Count < ShortWindowMax && _longWindowRequests.Count < LongWindowMax)
                {
                    _shortWindowRequests.Enqueue(now);
                    _longWindowRequests.Enqueue(now);
                    break;
                }

                // Wait until the earliest slot frees up
                var waitTime = TimeSpan.FromMilliseconds(50);
                if (_shortWindowRequests.Count >= ShortWindowMax)
                {
                    var waitShort = ShortWindowDuration - (now - _shortWindowRequests.Peek());
                    if (waitShort > waitTime) waitTime = waitShort;
                }

                await Task.Delay(waitTime, ct);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}

