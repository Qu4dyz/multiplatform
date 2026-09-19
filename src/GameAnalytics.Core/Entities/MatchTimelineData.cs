using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class MatchTimelineData
{
    public string MatchId { get; set; } = string.Empty;

    // Minute 10 snapshot (early snowball / momentum vs 15')
    public int GoldAt10Blue { get; set; }
    public int GoldAt10Red { get; set; }
    public int GoldDiffAt10 => GoldAt10Blue - GoldAt10Red;
    public int KillsAt10Blue { get; set; }
    public int KillsAt10Red { get; set; }
    public int KillDiffAt10 => KillsAt10Blue - KillsAt10Red;

    // Minute 15 snapshot (standard metric mark for LoL competitive analysis)
    public int GoldAt15Blue { get; set; }
    public int GoldAt15Red { get; set; }
    public int GoldDiffAt15 => GoldAt15Blue - GoldAt15Red;

    public int KillsAt15Blue { get; set; }
    public int KillsAt15Red { get; set; }
    public int KillDiffAt15 => KillsAt15Blue - KillsAt15Red;

    public int CsAt15Blue { get; set; }
    public int CsAt15Red { get; set; }
    public int CsDiffAt15 => CsAt15Blue - CsAt15Red;

    public int XpAt15Blue { get; set; }
    public int XpAt15Red { get; set; }
    public int XpDiffAt15 => XpAt15Blue - XpAt15Red;

    public int TowersAt15Blue { get; set; }
    public int TowersAt15Red { get; set; }

    public int DragonsAt15Blue { get; set; }
    public int DragonsAt15Red { get; set; }

    public int VoidgrubsAt15Blue { get; set; }
    public int VoidgrubsAt15Red { get; set; }
    public int VoidgrubDiffAt15 => VoidgrubsAt15Blue - VoidgrubsAt15Red;

    public int HeraldsAt15Blue { get; set; }
    public int HeraldsAt15Red { get; set; }
    public int HeraldDiffAt15 => HeraldsAt15Blue - HeraldsAt15Red;

    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }

    /// <summary>Timestamp (ms) of first blood / tower / dragon; 0 if none before 15'.</summary>
    public int FirstBloodTimeMs { get; set; }
    public int FirstTowerTimeMs { get; set; }
    public int FirstDragonTimeMs { get; set; }
    public string FirstDragonSubType { get; set; } = string.Empty;

    /// <summary>Richest blue player gold − richest red player gold at ~15'.</summary>
    public float CarryGoldDiff15 { get; set; }

    public List<TimelineEventRecord> RealEvents { get; set; } = new();
    public bool HasRealTimelineEvents => RealEvents.Count > 0;

    /// <summary>Per-minute (approx) participant position/gold snapshots from match-v5 timeline frames.</summary>
    public List<TimelineFrameSnapshot> Frames { get; set; } = new();

    public MatchInputFeatures ToInputFeatures(float blueAvgWinRate = 50.0f, float redAvgWinRate = 50.0f)
    {
        return new MatchInputFeatures
        {
            BlueFirstBlood = BlueFirstBlood,
            BlueFirstTower = BlueFirstTower,
            BlueFirstDragon = BlueFirstDragon,
            GoldDiffAt15 = GoldDiffAt15,
            KillDiffAt15 = KillDiffAt15,
            CsDiffAt15 = CsDiffAt15,
            XpDiffAt15 = XpDiffAt15,
            BlueVoidgrubs = VoidgrubsAt15Blue,
            RedVoidgrubs = VoidgrubsAt15Red,
            BlueHeralds = HeraldsAt15Blue,
            RedHeralds = HeraldsAt15Red,
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
    public int? PositionX { get; set; }
    public int? PositionY { get; set; }
    public bool HasPosition => PositionX.HasValue && PositionY.HasValue;
}

public class TimelineFrameSnapshot
{
    public int TimestampMs { get; set; }
    public List<TimelineParticipantPos> Participants { get; set; } = new();
}

public class TimelineParticipantPos
{
    public int ParticipantId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int TotalGold { get; set; }
    public int Level { get; set; }
}

