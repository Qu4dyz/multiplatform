using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class SummonerProfile
{
    public string Puuid { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public int SummonerLevel { get; set; } = 1;
    public int ProfileIconId { get; set; }

    public GameTier Tier { get; set; } = GameTier.Unranked;
    public string Rank { get; set; } = "IV";
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    public int TotalGames => Wins + Losses;
    public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0.0;
    public string FullName => $"{GameName}#{TagLine}";
}

public class PredictionResult
{
    public double BlueWinProbability { get; set; }
    public double RedWinProbability { get; set; }
    public TeamSide PredictedWinner { get; set; }
    public double ConfidenceScore { get; set; }
    public List<string> KeyFactors { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string FormattedBluePercent => $"{Math.Round(BlueWinProbability * 100, 1)}%";
    public string FormattedRedPercent => $"{Math.Round(RedWinProbability * 100, 1)}%";
}

public class MatchInputFeatures
{
    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }
    public int GoldDiffAt15 { get; set; }
    public int KillDiffAt15 { get; set; }
    public float BlueTeamAvgWinRate { get; set; } = 50.0f;
    public float RedTeamAvgWinRate { get; set; } = 50.0f;
    public int BlueDragonCount { get; set; }
    public int RedDragonCount { get; set; }
    public int BlueTowerCount { get; set; }
    public int RedTowerCount { get; set; }
}

