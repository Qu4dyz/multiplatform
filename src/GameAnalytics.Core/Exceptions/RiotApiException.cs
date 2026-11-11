namespace GameAnalytics.Core.Exceptions;

/// <summary>
/// Raised when the Riot API rejects a call (auth, rate limit, etc.).
/// Desktop UI uses this to stop infinite "loading" and show a clear error.
/// </summary>
public sealed class RiotApiException : Exception
{
    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
    public string? Region { get; }

    public bool IsRateLimited => StatusCode == 429;
    public bool IsUnauthorized => StatusCode is 401 or 403;

    public RiotApiException(string message, int statusCode, TimeSpan? retryAfter = null, string? region = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
        Region = region;
    }
}
