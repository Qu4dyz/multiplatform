namespace GameAnalytics.Core.Entities;

public class LiveGameStats
{
    public bool IsActiveGame { get; set; }
    public string GameMode { get; set; } = string.Empty;
    public double GameTimeSeconds { get; set; }
    public string FormattedGameTime => $"{(int)(GameTimeSeconds / 60):D2}:{(int)(GameTimeSeconds % 60):D2}";

    public int BlueKills { get; set; }
    public int RedKills { get; set; }
    public int KillDiff => BlueKills - RedKills;

    public int BlueCs { get; set; }
    public int RedCs { get; set; }
    public int CsDiff => BlueCs - RedCs;

    public int BlueTowers { get; set; }
    public int RedTowers { get; set; }
    public int TowerDiff => BlueTowers - RedTowers;

    public int BlueDragons { get; set; }
    public int RedDragons { get; set; }
    public int DragonDiff => BlueDragons - RedDragons;

    public int BlueVoidgrubs { get; set; }
    public int RedVoidgrubs { get; set; }

    public int BlueHeralds { get; set; }
    public int RedHeralds { get; set; }

    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }

    public int EstimatedGoldDiff { get; set; }
    public int EstimatedXpDiff { get; set; }

    public List<string> BlueChampions { get; set; } = new();
    public List<string> RedChampions { get; set; } = new();
    public string StatusMessage { get; set; } = string.Empty;
}
