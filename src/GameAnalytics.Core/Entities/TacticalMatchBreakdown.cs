using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class TacticalTimelineEvent
{
    public string TimestampText { get; set; } = "00:00"; // e.g. "03:15"
    public int Minute { get; set; }
    public string PhaseName { get; set; } = "Early Game";
    public string PhaseBadgeColor { get; set; } = "#5383E8";
    public string Icon { get; set; } = "⚔️";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImpactText { get; set; } = string.Empty;
    public string ImpactColor { get; set; } = "#0AC8B9";
    public bool IsMistake { get; set; }
}

public class MatchGapItem
{
    public string Category { get; set; } = "Економіка";
    public string MetricName { get; set; } = "Фарм (CS/хв)";
    public string ActualValueText { get; set; } = "6.5 CS/хв";
    public string BenchmarkValueText { get; set; } = "8.2 CS/хв (Ранг еталон)";
    public string EvaluationText { get; set; } = "-1.7 CS/хв дефіцит";
    public string ImpactColor { get; set; } = "#E84057";
    public string Advice { get; set; } = "Втрата пачок міньйонів через неефективні таймінги повернення на базу.";
    public bool IsPositive { get; set; }
}

public class MatchTacticalReport
{
    public string MatchId { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public Position Position { get; set; }
    public string RoleName { get; set; } = "JGL";
    public string RoleIcon { get; set; } = "🌲";
    public bool IsVictory { get; set; }
    public string MatchGrade { get; set; } = "A";
    public string MatchGradeColor { get; set; } = "#0AC8B9";

    public string OverallMatchVerdict { get; set; } = string.Empty;
    public string KeyTakeaway { get; set; } = string.Empty;

    public List<TacticalTimelineEvent> TimelineEvents { get; set; } = new();
    public List<MatchGapItem> GapAnalysis { get; set; } = new();
}
