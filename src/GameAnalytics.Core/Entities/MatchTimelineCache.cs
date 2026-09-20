namespace GameAnalytics.Core.Entities;

/// <summary>SQLite cache of MATCH-V5 timeline JSON so tactical tab / gold curve skip repeat API calls.</summary>
public class MatchTimelineCache
{
    public int Id { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public string TimelineJson { get; set; } = string.Empty;
    public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
}
