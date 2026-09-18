using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Desktop.ViewModels;

public class MiniParticipantViewModel
{
    public string SummonerName { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public bool IsCurrentPlayer { get; set; }
    public string FormattedKda { get; set; } = string.Empty;
    public string IndicatorText => IsCurrentPlayer ? "◆ " : "";
    public string DisplayName => $"{IndicatorText}{SummonerName}";
    public string NameColor => IsCurrentPlayer ? "#C8AA6E" : "#8A93A5";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
}

public class PlayerMatchItemViewModel
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

    public List<MiniParticipantViewModel> BlueTeam { get; set; } = new();
    public List<MiniParticipantViewModel> RedTeam { get; set; } = new();

    public static PlayerMatchItemViewModel FromMatch(Match match, string searchedPuuidOrName, string fallbackName = "")
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
            CsPerMinute = Math.Round((player?.TotalMinionsKilled ?? 0) / Math.Max(1.0, durationMinutes), 1)
        };

        // Populate Blue and Red mini participant lists
        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Blue))
        {
            vm.BlueTeam.Add(new MiniParticipantViewModel
            {
                SummonerName = ExtractShortName(p.SummonerName),
                ChampionName = p.ChampionName,
                IsCurrentPlayer = p == player,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}"
            });
        }

        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Red))
        {
            vm.RedTeam.Add(new MiniParticipantViewModel
            {
                SummonerName = ExtractShortName(p.SummonerName),
                ChampionName = p.ChampionName,
                IsCurrentPlayer = p == player,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}"
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
