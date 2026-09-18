using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class Match
{
    public int Id { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public int QueueId { get; set; } = 420;
    public DateTime GameCreation { get; set; } = DateTime.UtcNow;
    public int GameDurationSeconds { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public TeamSide WinningTeam { get; set; }
    public bool IsRemake { get; set; }

    public List<Participant> Participants { get; set; } = new();
    public List<TeamStats> Teams { get; set; } = new();

    public string FormattedDuration => $"{GameDurationSeconds / 60}m {GameDurationSeconds % 60}s";
    public string QueueName => QueueId switch
    {
        400 => "Normal Draft",
        420 => "Ranked Solo",
        430 => "Normal Blind",
        440 => "Ranked Flex",
        450 => "ARAM",
        700 => "Clash",
        1700 => "Arena",
        1900 => "Quickplay",
        _ => "Normal Game"
    };
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
    public int ParticipantId { get; set; }
    public int ChampLevel { get; set; } = 1;
    public TeamSide TeamSide { get; set; }
    public Position Position { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int TotalDamageDealtToChampions { get; set; }
    public int GoldEarned { get; set; }
    public int TotalMinionsKilled { get; set; }
    public bool Win { get; set; }

    // Items (6 inventory slots + 1 trinket/ward slot)
    public int Item0 { get; set; }
    public int Item1 { get; set; }
    public int Item2 { get; set; }
    public int Item3 { get; set; }
    public int Item4 { get; set; }
    public int Item5 { get; set; }
    public int Item6 { get; set; }

    // Summoner Spells & Runes
    public int Summoner1Id { get; set; } = 4; // Flash default
    public int Summoner2Id { get; set; } = 14; // Ignite default
    public int PrimaryRuneId { get; set; } = 8010; // Conqueror default
    public int SecondaryRuneStyleId { get; set; } = 8100; // Domination default

    // Vision
    public int VisionScore { get; set; }
    public int WardsPlaced { get; set; }
    public int WardsKilled { get; set; }
    public int ControlWardsBought { get; set; }

    // Damage Breakdown
    public int PhysicalDamageDealtToChampions { get; set; }
    public int MagicDamageDealtToChampions { get; set; }
    public int TrueDamageDealtToChampions { get; set; }
    public int TotalDamageTaken { get; set; }
    public int DamageSelfMitigated { get; set; }
    public int DamageDealtToObjectives { get; set; }
    public int DamageDealtToTurrets { get; set; }

    // Multi-kills & Feats
    public int DoubleKills { get; set; }
    public int TripleKills { get; set; }
    public int QuadraKills { get; set; }
    public int PentaKills { get; set; }
    public int SoloKills { get; set; }
    public int TurretPlatesTaken { get; set; }

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
    public int CsAt15 { get; set; }
    public int XpAt15 { get; set; }

    public int VoidgrubKills { get; set; }
    public int RiftHeraldKills { get; set; }
    public bool FirstVoidgrub { get; set; }
    public bool FirstRiftHerald { get; set; }
}

