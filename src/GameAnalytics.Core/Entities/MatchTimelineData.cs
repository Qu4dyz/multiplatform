using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class MatchTimelineData
{
    public string MatchId { get; set; } = string.Empty;

    // Minute 15 snapshot (standard metric mark for LoL competitive analysis)
    public int GoldAt15Blue { get; set; }
    public int GoldAt15Red { get; set; }
    public int GoldDiffAt15 => GoldAt15Blue - GoldAt15Red;

    public int KillsAt15Blue { get; set; }
    public int KillsAt15Red { get; set; }
    public int KillDiffAt15 => KillsAt15Blue - KillsAt15Red;

    public int TowersAt15Blue { get; set; }
    public int TowersAt15Red { get; set; }

    public int DragonsAt15Blue { get; set; }
    public int DragonsAt15Red { get; set; }

    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }

    public List<TimelineEventRecord> RealEvents { get; set; } = new();
    public bool HasRealTimelineEvents => RealEvents.Count > 0;

    public MatchInputFeatures ToInputFeatures(float blueAvgWinRate = 50.0f, float redAvgWinRate = 50.0f)
    {
        return new MatchInputFeatures
        {
            BlueFirstBlood = BlueFirstBlood,
            BlueFirstTower = BlueFirstTower,
            BlueFirstDragon = BlueFirstDragon,
            GoldDiffAt15 = GoldDiffAt15,
            KillDiffAt15 = KillDiffAt15,
            BlueTeamAvgWinRate = blueAvgWinRate,
            RedTeamAvgWinRate = redAvgWinRate,
            BlueTowerCount = TowersAt15Blue,
            RedTowerCount = TowersAt15Red,
            BlueDragonCount = DragonsAt15Blue,
            RedDragonCount = DragonsAt15Red
        };
    }
}

public class TimelineEventRecord
{
    public int TimestampMs { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int KillerId { get; set; }
    public int VictimId { get; set; }
    public List<int> AssistingParticipantIds { get; set; } = new();
    public string MonsterType { get; set; } = string.Empty;
    public string MonsterSubType { get; set; } = string.Empty;
    public string BuildingType { get; set; } = string.Empty;
    public string LaneType { get; set; } = string.Empty;
    public int KillerTeamId { get; set; }
    public int Bounty { get; set; }
}

