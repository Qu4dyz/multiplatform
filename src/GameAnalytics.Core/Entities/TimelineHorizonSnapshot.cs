namespace GameAnalytics.Core.Entities;

/// <summary>
/// Minute-agnostic timeline snapshot for multi-horizon ML (15 / 25 / 35).
/// All counts are strictly at-or-before <see cref="AnchorMinute"/> — never end-game team totals.
/// </summary>
public class TimelineHorizonSnapshot
{
    public int AnchorMinute { get; set; }
    public int PriorMinute { get; set; }
    public int FrameTimestampMs { get; set; }

    public int GoldBlue { get; set; }
    public int GoldRed { get; set; }
    public int GoldDiff => GoldBlue - GoldRed;

    public int KillsBlue { get; set; }
    public int KillsRed { get; set; }
    public int KillDiff => KillsBlue - KillsRed;

    public int CsBlue { get; set; }
    public int CsRed { get; set; }
    public int CsDiff => CsBlue - CsRed;

    public int XpBlue { get; set; }
    public int XpRed { get; set; }
    public int XpDiff => XpBlue - XpRed;

    public int TowersBlue { get; set; }
    public int TowersRed { get; set; }
    public int DragonsBlue { get; set; }
    public int DragonsRed { get; set; }
    public int VoidgrubsBlue { get; set; }
    public int VoidgrubsRed { get; set; }
    public int HeraldsBlue { get; set; }
    public int HeraldsRed { get; set; }

    public int PlatesBlue { get; set; }
    public int PlatesRed { get; set; }
    public int PlatesDiff => PlatesBlue - PlatesRed;

    public int DeathsBlue { get; set; }
    public int DeathsRed { get; set; }
    public int DeathDiff => DeathsBlue - DeathsRed;

    public int VisionWardsBlue { get; set; }
    public int VisionWardsRed { get; set; }
    public int VisionWardDiff => VisionWardsBlue - VisionWardsRed;

    public int ControlWardsBlue { get; set; }
    public int ControlWardsRed { get; set; }
    public int ControlWardDiff => ControlWardsBlue - ControlWardsRed;

    public int GoldDiffPrior { get; set; }
    public int KillDiffPrior { get; set; }
    public int CsDiffPrior { get; set; }
    public int XpDiffPrior { get; set; }

    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }
    public int FirstBloodTimeMs { get; set; }
    public int FirstTowerTimeMs { get; set; }
    public int FirstDragonTimeMs { get; set; }
    public string FirstDragonSubType { get; set; } = string.Empty;

    public float CarryGoldDiff { get; set; }
    public float TopGoldDiff { get; set; }
    public float JungleGoldDiff { get; set; }
    public float MidGoldDiff { get; set; }
    public float BotDuoGoldDiff { get; set; }
    public float LevelDiff { get; set; }
}

/// <summary>Supported honest prediction anchors for the lab multi-horizon add-on.</summary>
public static class PredictionHorizons
{
    public const int Early = 15;
    public const int Mid = 25;
    public const int Late = 35;

    public static readonly int[] All = { Early, Mid, Late };

    public static bool IsSupported(int minute) =>
        minute is Early or Mid or Late;

    public static string ModelFileName(int minute) => minute switch
    {
        Early => "fasttree_model.zip",
        Mid => "fasttree_m25_model.zip",
        Late => "fasttree_m35_model.zip",
        _ => throw new ArgumentOutOfRangeException(nameof(minute), minute, "Unsupported horizon")
    };
}
