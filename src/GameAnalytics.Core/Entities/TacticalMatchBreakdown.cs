using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;

namespace GameAnalytics.Core.Entities;

public class TacticalTimelineEvent : INotifyPropertyChanged
{
    private bool _isMomentOpen;
    private bool _wasMomentViewed;

    public string TimestampText { get; set; } = "00:00"; // e.g. "03:15"
    public int Minute { get; set; }
    public int TimestampMs { get; set; }
    public string PhaseName { get; set; } = "Early Game";
    public string PhaseBadgeColor { get; set; } = "#5383E8";
    public string Icon { get; set; } = "⚔️";
    public string IconKind { get; set; } = "sword";
    public string IconPath => TacticalIconHelper.GetPathForKind(IconKind);

    // Modern Tactical HUD Telemetry
    public string CategoryTag { get; set; } = "MACRO";
    public string CategoryTagColor { get; set; } = "#818CF8";
    public string SeverityTag { get; set; } = "TACTICAL";
    public string SeverityBg { get; set; } = "#1B1634";
    public string SeverityFg { get; set; } = "#C4B5FD";
    public bool HasSeverityTag => !string.IsNullOrWhiteSpace(SeverityTag) && !string.Equals(SeverityTag, CategoryTag, StringComparison.OrdinalIgnoreCase);
    public string IconBadgeBg => IsMistake ? "#2E121C" : "#1B1634";
    public string IconBadgeBorder => IsMistake ? "#F43F5E" : "#7C3AED";
    public string IconBadgeFg => IsMistake ? "#FB7185" : "#C4B5FD";
    public string CardBorderColor => IsMomentOpen ? "#F43F5E" : (IsMistake ? "#4C1C2A" : "#24283E");
    public string CardBackground => IsMomentOpen ? "#241018" : (IsMistake ? "#1F1219" : "#161928");

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImpactText { get; set; } = string.Empty;
    public string ImpactColor { get; set; } = "#818CF8";
    public bool IsMistake { get; set; }

    /// <summary>Optional interactive moment board (minimap + context) for this event.</summary>
    public TacticalMomentReplay? Moment { get; set; }
    public bool HasMomentReplay => Moment != null && Moment.Markers.Count > 0;

    public bool IsMomentOpen
    {
        get => _isMomentOpen;
        set
        {
            if (_isMomentOpen == value) return;
            _isMomentOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MomentHint));
            OnPropertyChanged(nameof(MomentHintColor));
            OnPropertyChanged(nameof(CardBorderColor));
            OnPropertyChanged(nameof(CardBackground));
        }
    }

    public bool WasMomentViewed
    {
        get => _wasMomentViewed;
        set
        {
            if (_wasMomentViewed == value) return;
            _wasMomentViewed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MomentHint));
            OnPropertyChanged(nameof(MomentHintColor));
        }
    }

    public string MomentHint
    {
        get
        {
            if (!HasMomentReplay) return string.Empty;
            if (IsMomentOpen) return "▼ Закрити момент";
            if (WasMomentViewed) return "✓ Переглянуто · відкрити";
            return "▶ Відкрити момент";
        }
    }

    public string MomentHintColor => IsMomentOpen ? "#FBBF24" : (WasMomentViewed ? "#94A3B8" : "#F43F5E");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TacticalMomentMarker
{
    public int ParticipantId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty; // ВИ / KILLER / ALLY / ENEMY
    public string BorderColor { get; set; } = "#94A3B8";
    public bool IsBlueTeam { get; set; }
    public bool IsFocus { get; set; }
    /// <summary>True for killer/victim/assists — drawn larger and on top.</summary>
    public bool IsInFight { get; set; }
    public double MarkerSize { get; set; } = 28;
    public double MapLeft { get; set; }
    public double MapTop { get; set; }
    public int Level { get; set; }
    public int Gold { get; set; }
    public double Opacity => IsInFight || IsFocus ? 1.0 : 0.72;
}

public class TacticalMomentContextLine
{
    public string TimestampText { get; set; } = "00:00";
    public string Text { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#94A3B8";
    public bool IsCurrent { get; set; }
    public string RowBackground => IsCurrent ? "#2A1520" : "#141726";
    public string RowBorder => IsCurrent ? "#F43F5E" : "#24283E";
}

public class TacticalMomentReplay
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string TimestampText { get; set; } = "00:00";
    public string FocusChampionName { get; set; } = string.Empty;
    public string KillerChampionName { get; set; } = string.Empty;
    public string AssistsText { get; set; } = string.Empty;
    public bool IsDeath { get; set; }
    public int KillerId { get; set; }
    public int VictimId { get; set; }
    public int TimestampMs { get; set; }
    public double MapSize { get; set; } = 260;
    /// <summary>Summoner's Rift minimap asset key or URL.</summary>
    public string MapImageUrl { get; set; } = string.Empty;
    /// <summary>Kill epicenter marker on the canvas (centered ring).</summary>
    public double KillMarkerLeft { get; set; }
    public double KillMarkerTop { get; set; }
    public bool HasKillMarker { get; set; }
    public List<TacticalMomentMarker> Markers { get; set; } = new();
    public List<TacticalMomentContextLine> ContextLines { get; set; } = new();
}

public class MatchGapItem
{
    public string Category { get; set; } = "Економіка";
    public string MetricName { get; set; } = "Фарм (CS/хв)";
    public string ActualValueText { get; set; } = "6.5 CS/хв";
    public string BenchmarkValueText { get; set; } = "8.2 CS/хв (Ранг еталон)";
    public string EvaluationText { get; set; } = "-1.7 CS/хв дефіцит";
    public string ImpactColor { get; set; } = "#F43F5E";
    public string Advice { get; set; } = "Втрата пачок міньйонів через неефективні таймінги повернення на базу.";
    public bool IsPositive { get; set; }

    public string CardBorder => IsPositive ? "#24283E" : "#4C1C2A";
    public string CardBg => IsPositive ? "#161928" : "#1F1219";
    public string BadgeBg => IsPositive ? "#1B1634" : "#2E121C";
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
    public List<GoldCurveBuilder.GoldCurvePoint> GoldCurve { get; set; } = new();
}
