using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Desktop.ViewModels;

public class ItemSlotViewModel
{
    public int ItemId { get; set; }
    public bool HasItem => ItemId > 0;
    public string IconUrl => HasItem ? $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/item/{ItemId}.png" : string.Empty;
}

public class DetailedParticipantViewModel
{
    public string SummonerName { get; set; } = string.Empty;
    public string FullRiotId { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public int ChampLevel { get; set; } = 1;
    public string ChampLevelText => $"{ChampLevel}";
    public bool IsCurrentPlayer { get; set; }
    public string PositionName { get; set; } = "MID";
    public string PositionIcon { get; set; } = "⚡";

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public string KdaText => $"{Kills}/{Deaths}/{Assists}";
    public string KdaRatioText => Deaths == 0 ? "Perfect" : $"{(Kills + Assists) / (double)Deaths:F2}:1";

    public int DamageDealt { get; set; }
    public string DamageText => $"{DamageDealt / 1000.0:F1}k";
    public double DamagePercentOfMax { get; set; }
    public double DamageBarWidth => Math.Max(4, Math.Round(DamagePercentOfMax * 0.7)); // Max 70px

    public int GoldEarned { get; set; }
    public string GoldText => $"{GoldEarned / 1000.0:F1}k";

    public int MinionsKilled { get; set; }
    public string CsText => $"{MinionsKilled}";

    public List<ItemSlotViewModel> Items { get; set; } = new();
    public ItemSlotViewModel Trinket { get; set; } = new();

    public string NameColor => IsCurrentPlayer ? "#C8AA6E" : "#F0E6D2";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
    public string IndicatorText => IsCurrentPlayer ? "◆ " : "";
    public string DisplayName => $"{IndicatorText}{SummonerName}";
    public string ToolTipText => IsCurrentPlayer ? $"{FullRiotId} (Ви)" : $"Переглянути профіль {FullRiotId} (клікніть)";

    public System.Windows.Input.ICommand? SelectPlayerCommand { get; set; }
}

public class MiniParticipantViewModel
{
    public string SummonerName { get; set; } = string.Empty;
    public string FullRiotId { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public bool IsCurrentPlayer { get; set; }
    public string FormattedKda { get; set; } = string.Empty;
    public string IndicatorText => IsCurrentPlayer ? "◆ " : "";
    public string DisplayName => $"{IndicatorText}{SummonerName}";
    public string NameColor => IsCurrentPlayer ? "#C8AA6E" : "#8A93A5";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
    public string ToolTipText => IsCurrentPlayer ? $"{FullRiotId} (Ви)" : $"Переглянути профіль {FullRiotId} (клікніть)";

    public System.Windows.Input.ICommand? SelectPlayerCommand { get; set; }
}

public partial class PlayerMatchItemViewModel : ObservableObject
{
    public string MatchId { get; set; } = string.Empty;
    public bool IsRemake { get; set; }
    public bool IsVictory { get; set; }
    public string ResultText => IsRemake ? "РЕМЕЙК" : (IsVictory ? "ПЕРЕМОГА" : "ПОРАЗКА");
    public string ResultColor => IsRemake ? "#A0A8B6" : (IsVictory ? "#5383E8" : "#E84057");
    public string ResultBgColor => IsRemake ? "#1C222B" : (IsVictory ? "#16243A" : "#2C1822");
    public string ResultBorderColor => IsRemake ? "#363E4D" : (IsVictory ? "#283852" : "#4D222E");
    public string AccentBarColor => IsRemake ? "#758092" : (IsVictory ? "#5383E8" : "#E84057");

    public string GameModeText { get; set; } = "Normal Draft";
    public string FormattedDuration { get; set; } = string.Empty;
    public string FormattedTimeAgo { get; set; } = string.Empty;

    public string ChampionName { get; set; } = string.Empty;
    public int ChampLevel { get; set; } = 1;
    public string ChampLevelText => $"{ChampLevel}";
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public string PositionName { get; set; } = "MID";
    public string PositionIcon { get; set; } = "⚡";

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double KdaRatio { get; set; }
    public string KdaText => $"{Kills} / {Deaths} / {Assists}";
    public string KdaRatioText => Deaths == 0 ? "Perfect KDA" : $"{KdaRatio:F2}:1 KDA";
    public string KdaRatioColor => (Deaths == 0 || KdaRatio >= 5.0) ? "#E6B328" : (KdaRatio >= 3.0 ? "#0AC8B9" : "#F0E6D2");

    public int KillParticipationPercent { get; set; }
    public string KillParticipationText => $"P/Kill {KillParticipationPercent}%";
    public string KillParticipationColor => KillParticipationPercent >= 60 ? "#E84057" : (KillParticipationPercent >= 45 ? "#C8AA6E" : "#8A93A5");

    public int DamageDealt { get; set; }
    public string DamageText => $"{DamageDealt / 1000.0:F1}k DMG";

    public int GoldEarned { get; set; }
    public string GoldText => $"{GoldEarned / 1000.0:F1}k Gold";

    public int MinionsKilled { get; set; }
    public double CsPerMinute { get; set; }
    public string CsText => $"{MinionsKilled} CS ({CsPerMinute:F1}/хв)";

    // Items for current player
    public List<ItemSlotViewModel> PlayerItems { get; set; } = new();
    public ItemSlotViewModel PlayerTrinket { get; set; } = new();

    private static readonly ItemSlotViewModel EmptySlot = new() { ItemId = 0 };
    public ItemSlotViewModel ItemSlot0 => PlayerItems.ElementAtOrDefault(0) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot1 => PlayerItems.ElementAtOrDefault(1) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot2 => PlayerItems.ElementAtOrDefault(2) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot3 => PlayerItems.ElementAtOrDefault(3) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot4 => PlayerItems.ElementAtOrDefault(4) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot5 => PlayerItems.ElementAtOrDefault(5) ?? EmptySlot;

    // Compact lists
    public List<MiniParticipantViewModel> BlueTeam { get; set; } = new();
    public List<MiniParticipantViewModel> RedTeam { get; set; } = new();

    // Detailed lists for expanded view
    public List<DetailedParticipantViewModel> BlueTeamDetailed { get; set; } = new();
    public List<DetailedParticipantViewModel> RedTeamDetailed { get; set; } = new();

    // Accordion Expansion state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandButtonText))]
    [NotifyPropertyChangedFor(nameof(ExpandButtonColor))]
    private bool _isExpanded;

    public string ExpandButtonText => IsExpanded ? "▲ Згорнути" : "▼ Деталі";
    public string ExpandButtonColor => IsExpanded ? "#C8AA6E" : "#8A93A5";

    [RelayCommand]
    public void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    public static PlayerMatchItemViewModel FromMatch(Match match, string searchedPuuidOrName, string fallbackName = "", System.Windows.Input.ICommand? selectPlayerCommand = null)
    {
        var cleanSearched = searchedPuuidOrName?.Trim() ?? string.Empty;
        var cleanFallback = fallbackName?.Trim() ?? string.Empty;

        var player = match.Participants.FirstOrDefault(p =>
            (!string.IsNullOrWhiteSpace(p.Puuid) && !string.IsNullOrWhiteSpace(cleanSearched) && p.Puuid.Equals(cleanSearched, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(p.SummonerName) && !string.IsNullOrWhiteSpace(cleanSearched) && p.SummonerName.Contains(cleanSearched, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(p.SummonerName) && !string.IsNullOrWhiteSpace(cleanFallback) && p.SummonerName.Contains(cleanFallback, StringComparison.OrdinalIgnoreCase)));

        // If not matched, default to 4th player (sample)
        player ??= match.Participants.ElementAtOrDefault(3) ?? match.Participants.FirstOrDefault();

        var isRemake = match.IsRemake || (match.GameDurationSeconds > 0 && match.GameDurationSeconds < 300);
        var isVictory = !isRemake && (player?.Win ?? (match.WinningTeam == TeamSide.Blue));
        var durationMinutes = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;

        var isAram = match.QueueId == 450 || match.QueueName.Equals("ARAM", StringComparison.OrdinalIgnoreCase);

        var (posName, posIcon) = isAram
            ? ("ARAM", "🎲")
            : (player?.Position ?? Position.Middle) switch
            {
                Position.Top => ("TOP", "🛡️"),
                Position.Jungle => ("JGL", "🌲"),
                Position.Middle => ("MID", "⚡"),
                Position.Bottom => ("BOT", "🏹"),
                Position.Utility => ("SUP", "✨"),
                _ => ("MID", "⚔️")
            };

        var timeDiff = DateTime.UtcNow - match.GameCreation;
        var timeAgo = timeDiff.TotalDays >= 1
            ? $"{(int)timeDiff.TotalDays} дн. тому"
            : timeDiff.TotalHours >= 1
                ? $"{(int)timeDiff.TotalHours} год. тому"
                : $"{(int)Math.Max(1, timeDiff.TotalMinutes)} хв. тому";

        // Kill participation
        var playerSide = player?.TeamSide ?? TeamSide.Blue;
        var teamTotalKills = match.Participants.Where(p => p.TeamSide == playerSide).Sum(p => p.Kills);
        var playerKillsAndAssists = (player?.Kills ?? 0) + (player?.Assists ?? 0);
        var kpPercent = teamTotalKills > 0 ? (int)Math.Round((double)playerKillsAndAssists / teamTotalKills * 100) : 0;

        var vm = new PlayerMatchItemViewModel
        {
            MatchId = match.MatchId,
            IsRemake = isRemake,
            IsVictory = isVictory,
            GameModeText = match.QueueName,
            FormattedDuration = match.FormattedDuration,
            FormattedTimeAgo = timeAgo,
            ChampionName = player?.ChampionName ?? "Aatrox",
            ChampLevel = player?.ChampLevel > 0 ? player.ChampLevel : 1,
            PositionName = posName,
            PositionIcon = posIcon,
            Kills = player?.Kills ?? 0,
            Deaths = player?.Deaths ?? 0,
            Assists = player?.Assists ?? 0,
            KdaRatio = player?.KdaRatio ?? 0,
            KillParticipationPercent = kpPercent,
            DamageDealt = player?.TotalDamageDealtToChampions ?? 0,
            GoldEarned = player?.GoldEarned ?? 0,
            MinionsKilled = player?.TotalMinionsKilled ?? 0,
            CsPerMinute = Math.Round((player?.TotalMinionsKilled ?? 0) / Math.Max(1.0, durationMinutes), 1),
            PlayerItems = new List<ItemSlotViewModel>
            {
                new() { ItemId = player?.Item0 ?? 0 },
                new() { ItemId = player?.Item1 ?? 0 },
                new() { ItemId = player?.Item2 ?? 0 },
                new() { ItemId = player?.Item3 ?? 0 },
                new() { ItemId = player?.Item4 ?? 0 },
                new() { ItemId = player?.Item5 ?? 0 }
            },
            PlayerTrinket = new ItemSlotViewModel { ItemId = player?.Item6 ?? 0 }
        };

        // Find max damage for relative damage bar width
        var maxDamage = Math.Max(1, match.Participants.Count > 0 ? match.Participants.Max(p => p.TotalDamageDealtToChampions) : 1);

        // Populate Blue and Red participants (both compact and detailed)
        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Blue))
        {
            var isCurrent = p == player;
            var shortName = ExtractShortName(p.SummonerName);
            var fullRiotId = string.IsNullOrWhiteSpace(p.SummonerName) ? shortName : p.SummonerName;

            vm.BlueTeam.Add(new MiniParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = p.ChampionName,
                IsCurrentPlayer = isCurrent,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}",
                SelectPlayerCommand = selectPlayerCommand
            });

            var (pRoleName, pRoleIcon) = isAram
                ? ("ARAM", "🎲")
                : p.Position switch
                {
                    Position.Top => ("TOP", "🛡️"),
                    Position.Jungle => ("JGL", "🌲"),
                    Position.Middle => ("MID", "⚡"),
                    Position.Bottom => ("BOT", "🏹"),
                    Position.Utility => ("SUP", "✨"),
                    _ => ("MID", "⚔️")
                };

            vm.BlueTeamDetailed.Add(new DetailedParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = p.ChampionName,
                ChampLevel = p.ChampLevel > 0 ? p.ChampLevel : 1,
                IsCurrentPlayer = isCurrent,
                PositionName = pRoleName,
                PositionIcon = pRoleIcon,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                DamageDealt = p.TotalDamageDealtToChampions,
                DamagePercentOfMax = Math.Round((double)p.TotalDamageDealtToChampions / maxDamage * 100, 1),
                GoldEarned = p.GoldEarned,
                MinionsKilled = p.TotalMinionsKilled,
                Items = new List<ItemSlotViewModel>
                {
                    new() { ItemId = p.Item0 },
                    new() { ItemId = p.Item1 },
                    new() { ItemId = p.Item2 },
                    new() { ItemId = p.Item3 },
                    new() { ItemId = p.Item4 },
                    new() { ItemId = p.Item5 }
                },
                Trinket = new ItemSlotViewModel { ItemId = p.Item6 },
                SelectPlayerCommand = selectPlayerCommand
            });
        }

        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Red))
        {
            var isCurrent = p == player;
            var shortName = ExtractShortName(p.SummonerName);
            var fullRiotId = string.IsNullOrWhiteSpace(p.SummonerName) ? shortName : p.SummonerName;

            vm.RedTeam.Add(new MiniParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = p.ChampionName,
                IsCurrentPlayer = isCurrent,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}",
                SelectPlayerCommand = selectPlayerCommand
            });

            var (pRoleName, pRoleIcon) = isAram
                ? ("ARAM", "🎲")
                : p.Position switch
                {
                    Position.Top => ("TOP", "🛡️"),
                    Position.Jungle => ("JGL", "🌲"),
                    Position.Middle => ("MID", "⚡"),
                    Position.Bottom => ("BOT", "🏹"),
                    Position.Utility => ("SUP", "✨"),
                    _ => ("MID", "⚔️")
                };

            vm.RedTeamDetailed.Add(new DetailedParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = p.ChampionName,
                ChampLevel = p.ChampLevel > 0 ? p.ChampLevel : 1,
                IsCurrentPlayer = isCurrent,
                PositionName = pRoleName,
                PositionIcon = pRoleIcon,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                DamageDealt = p.TotalDamageDealtToChampions,
                DamagePercentOfMax = Math.Round((double)p.TotalDamageDealtToChampions / maxDamage * 100, 1),
                GoldEarned = p.GoldEarned,
                MinionsKilled = p.TotalMinionsKilled,
                Items = new List<ItemSlotViewModel>
                {
                    new() { ItemId = p.Item0 },
                    new() { ItemId = p.Item1 },
                    new() { ItemId = p.Item2 },
                    new() { ItemId = p.Item3 },
                    new() { ItemId = p.Item4 },
                    new() { ItemId = p.Item5 }
                },
                Trinket = new ItemSlotViewModel { ItemId = p.Item6 },
                SelectPlayerCommand = selectPlayerCommand
            });
        }

        return vm;
    }

    private static string ExtractShortName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Player";
        var idx = fullName.IndexOf('#');
        return idx > 0 ? fullName[..idx] : fullName;
    }
}
