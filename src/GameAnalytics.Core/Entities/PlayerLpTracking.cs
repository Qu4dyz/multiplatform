using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class PlayerLpSnapshot
{
    public int Id { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public int QueueId { get; set; } = 420; // 420 = Solo/Duo, 440 = Flex
    public int LeaguePoints { get; set; }
    public GameTier Tier { get; set; } = GameTier.Emerald;
    public string Rank { get; set; } = "IV";
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string LastMatchId { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PlayerMatchLpRecord
{
    public int Id { get; set; }
    public string Puuid { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public int QueueId { get; set; } = 420;
    public int LpDelta { get; set; }
    public int LeaguePointsAfter { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
