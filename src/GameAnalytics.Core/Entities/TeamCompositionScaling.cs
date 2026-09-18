using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public enum ScalingPhase
{
    EarlyGame,  // 0 - 15 min
    MidGame,    // 15 - 25 min
    LateGame    // 25+ min
}

public class ChampionScalingProfile
{
    public string ChampionName { get; set; } = string.Empty;
    public int EarlyGameScore { get; set; } = 5; // 1 to 10
    public int MidGameScore { get; set; } = 5;   // 1 to 10
    public int LateGameScore { get; set; } = 5;  // 1 to 10
    public string PrimaryStrength { get; set; } = "Стабільний універсал";
}

public class TeamCompositionAnalysis
{
    public TeamSide TeamSide { get; set; }
    public double EarlyGamePower { get; set; }  // 0 - 100%
    public double MidGamePower { get; set; }    // 0 - 100%
    public double LateGamePower { get; set; }   // 0 - 100%

    public ScalingPhase PeakPhase { get; set; } = ScalingPhase.MidGame;
    public string ArchetypeTitle { get; set; } = "Збалансований драфт";
    public string WinConditionSummary { get; set; } = string.Empty;

    public string FormattedEarlyPower => $"{Math.Round(EarlyGamePower)}%";
    public string FormattedMidPower => $"{Math.Round(MidGamePower)}%";
    public string FormattedLatePower => $"{Math.Round(LateGamePower)}%";
}

public class DraftPowerSpikeComparison
{
    public TeamCompositionAnalysis BlueTeam { get; set; } = new();
    public TeamCompositionAnalysis RedTeam { get; set; } = new();

    public double EarlyAdvantage => BlueTeam.EarlyGamePower - RedTeam.EarlyGamePower;
    public double MidAdvantage => BlueTeam.MidGamePower - RedTeam.MidGamePower;
    public double LateAdvantage => BlueTeam.LateGamePower - RedTeam.LateGamePower;

    public string ComparativeVerdict { get; set; } = string.Empty;
}
