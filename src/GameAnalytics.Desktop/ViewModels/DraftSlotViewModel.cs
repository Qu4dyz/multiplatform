using CommunityToolkit.Mvvm.ComponentModel;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Desktop.ViewModels;

public partial class DraftSlotViewModel : ObservableObject
{
    public Position Position { get; }
    public TeamSide TeamSide { get; }
    public string PositionName { get; }
    public string PositionIcon { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChampionDisplayName))]
    [NotifyPropertyChangedFor(nameof(ChampionIconUrl))]
    [NotifyPropertyChangedFor(nameof(WinRateText))]
    [NotifyPropertyChangedFor(nameof(HasChampion))]
    private ChampionInfo? _champion;

    public string ChampionDisplayName => Champion?.Name ?? $"[ {PositionName} ]";
    public string ChampionIconUrl => Champion?.IconUrl ?? string.Empty;
    public string WinRateText => Champion != null ? $"{Champion.WinRate:F1}% WR" : "50.0% WR";
    public bool HasChampion => Champion != null;

    public DraftSlotViewModel(Position position, TeamSide teamSide)
    {
        Position = position;
        TeamSide = teamSide;

        (PositionName, PositionIcon) = position switch
        {
            Position.Top => ("TOP", "🛡️"),
            Position.Jungle => ("JGL", "🌲"),
            Position.Middle => ("MID", "⚡"),
            Position.Bottom => ("BOT", "🏹"),
            Position.Utility => ("SUP", "✨"),
            _ => (position.ToString(), "⚔️")
        };
    }
}
