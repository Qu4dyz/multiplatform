using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class Match
{
    public int Id { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public DateTime GameCreation { get; set; } = DateTime.UtcNow;
    public int GameDurationSeconds { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public TeamSide WinningTeam { get; set; }

    public List<Participant> Participants { get; set; } = new();
    public List<TeamStats> Teams { get; set; } = new();

    public string FormattedDuration => $"{GameDurationSeconds / 60}m {GameDurationSeconds % 60}s";
}

public class Participant
{
    public int Id { get; set; }
    public int MatchEntityId { get; set; }
    public Match? Match { get; set; }

    public string Puuid { get; set; } = string.Empty;
    public string SummonerName { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public TeamSide TeamSide { get; set; }
    public Position Position { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int TotalDamageDealtToChampions { get; set; }
    public int GoldEarned { get; set; }
    public int TotalMinionsKilled { get; set; }
    public bool Win { get; set; }

    public double KdaRatio => Deaths == 0 ? (Kills + Assists) : Math.Round((double)(Kills + Assists) / Deaths, 2);
}

public class TeamStats
{
    public int Id { get; set; }
    public int MatchEntityId { get; set; }
    public Match? Match { get; set; }

    public TeamSide TeamSide { get; set; }
    public bool Win { get; set; }
    public bool FirstBlood { get; set; }
    public bool FirstTower { get; set; }
    public bool FirstDragon { get; set; }
    public bool FirstBaron { get; set; }

    public int TowerKills { get; set; }
    public int DragonKills { get; set; }
    public int BaronKills { get; set; }

    public int GoldAt15 { get; set; }
    public int KillsAt15 { get; set; }
}

