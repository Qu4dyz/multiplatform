using Microsoft.ML.Data;

namespace GameAnalytics.ML.Models;

public class MatchInputData
{
    [LoadColumn(0)]
    public float FirstBlood { get; set; }

    [LoadColumn(1)]
    public float FirstTower { get; set; }

    [LoadColumn(2)]
    public float FirstDragon { get; set; }

    [LoadColumn(3)]
    public float GoldDiff15 { get; set; }

    [LoadColumn(4)]
    public float KillDiff15 { get; set; }

    [LoadColumn(5)]
    public float BlueAvgWinRate { get; set; }

    [LoadColumn(6)]
    public float RedAvgWinRate { get; set; }

    [LoadColumn(7)]
    public float TowerDiff { get; set; }

    [LoadColumn(8)]
    public float DragonDiff { get; set; }

    [LoadColumn(9)]
    public float CsDiff15 { get; set; }

    [LoadColumn(10)]
    public float XpDiff15 { get; set; }

    [LoadColumn(11)]
    public float VoidgrubDiff { get; set; }

    [LoadColumn(12)]
    public float HeraldDiff { get; set; }

    /// <summary>Blue early-power − Red early-power (champion scaling profiles).</summary>
    [LoadColumn(13)]
    public float EarlyPowerDiff { get; set; }

    /// <summary>Blue late-power − Red late-power.</summary>
    [LoadColumn(14)]
    public float LatePowerDiff { get; set; }

    /// <summary>Lobby skill 1–10 (Iron…Challenger).</summary>
    [LoadColumn(15)]
    public float AvgRankScore { get; set; }

    /// <summary>Total team gold at 15' / 1000 — game pace proxy.</summary>
    [LoadColumn(16)]
    public float GoldPace15 { get; set; }

    [LoadColumn(17)]
    public float TopGoldDiff15 { get; set; }

    [LoadColumn(18)]
    public float JungleGoldDiff15 { get; set; }

    [LoadColumn(19)]
    public float MidGoldDiff15 { get; set; }

    [LoadColumn(20)]
    public float BotDuoGoldDiff15 { get; set; }

    [LoadColumn(21)]
    public float LevelDiff15 { get; set; }

    /// <summary>Composite objective lead at 15' (towers/dragons/grubs/herald).</summary>
    [LoadColumn(22)]
    public float ObjectiveScoreDiff { get; set; }

    /// <summary>GoldDiff scaled by rank so high-elo leads weigh differently.</summary>
    [LoadColumn(23)]
    public float RankAdjustedGoldDiff { get; set; }

    [LoadColumn(24)]
    public float GoldDiff10 { get; set; }

    [LoadColumn(25)]
    public float KillDiff10 { get; set; }

    /// <summary>GoldDiff15 − GoldDiff10: is the lead growing or fading?</summary>
    [LoadColumn(26)]
    public float GoldMomentum15 { get; set; }

    /// <summary>BlueWr − RedWr from chronological past-only champion stats.</summary>
    [LoadColumn(27)]
    public float WinRateDiff { get; set; }

    [LoadColumn(28)]
    public float EngageDiff { get; set; }

    [LoadColumn(29)]
    public float TankDiff { get; set; }

    [LoadColumn(30)]
    public float AdApBalanceDiff { get; set; }

    /// <summary>Combined early snowball signal (kills + gold), scaled.</summary>
    [LoadColumn(31)]
    public float SnowballScore { get; set; }

    /// <summary>Richest blue − richest red gold at 15'.</summary>
    [LoadColumn(32)]
    public float CarryGoldDiff15 { get; set; }

    /// <summary>Earlier first blood by blue ⇒ positive (minutes left to 15').</summary>
    [LoadColumn(33)]
    public float FirstBloodTempo { get; set; }

    [LoadColumn(34)]
    public float FirstTowerTempo { get; set; }

    [LoadColumn(35)]
    public float FirstDragonTempo { get; set; }

    /// <summary>Signed first-dragon type value by 15'.</summary>
    [LoadColumn(36)]
    public float FirstDragonValue { get; set; }

    /// <summary>Past-only same-role champ-vs-champ WR edge (avg over lanes).</summary>
    [LoadColumn(37)]
    public float LaneMatchupDiff { get; set; }

    [LoadColumn(38), ColumnName("Label")]
    public bool Label { get; set; }
}

public class MatchPrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }
}
