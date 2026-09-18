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
    public string NameColor => IsCurrentPlayer ? "#C8AA6E" : "#A09B8C";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
}

public class PlayerMatchItemViewModel
{
    public string MatchId { get; set; } = string.Empty;
    public bool IsVictory { get; set; }
    public string ResultText => IsVictory ? "ПЕРЕМОГА" : "ПОРАЗКА";
    public string ResultColor => IsVictory ? "#0397AB" : "#E84057";
    public string ResultBgColor => IsVictory ? "#0F2634" : "#28121A";
    public string ResultBorderColor => IsVictory ? "#0AC8B9" : "#E84057";

    public string GameModeText { get; set; } = "Ranked Solo/Duo";
    public string FormattedDuration { get; set; } = string.Empty;
    public string FormattedTimeAgo { get; set; } = string.Empty;

    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public string PositionName { get; set; } = "MID";
    public string PositionIcon { get; set; } = "⚡";

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double KdaRatio { get; set; }
    public string KdaText => $"{Kills} / {Deaths} / {Assists}";
    public string KdaRatioText => Deaths == 0 ? "Perfect KDA" : $"{KdaRatio:F2}:1 KDA";

    public int DamageDealt { get; set; }
    public string DamageText => $"{DamageDealt / 1000.0:F1}k DMG";

    public int GoldEarned { get; set; }
    public string GoldText => $"{GoldEarned / 1000.0:F1}k Gold";

    public int MinionsKilled { get; set; }
    public double CsPerMinute { get; set; }
    public string CsText => $"{MinionsKilled} CS ({CsPerMinute:F1}/хв)";

    public List<MiniParticipantViewModel> BlueTeam { get; set; } = new();
    public List<MiniParticipantViewModel> RedTeam { get; set; } = new();

    public static PlayerMatchItemViewModel FromMatch(Match match, string searchedPuuidOrName)
    {
        var cleanSearched = searchedPuuidOrName.Trim();
        var player = match.Participants.FirstOrDefault(p =>
            (!string.IsNullOrWhiteSpace(p.Puuid) && p.Puuid.Equals(cleanSearched, StringComparison.OrdinalIgnoreCase)) ||
            p.SummonerName.Contains(cleanSearched, StringComparison.OrdinalIgnoreCase));

        // If not matched, default to 4th player (arbitrary sample)
        player ??= match.Participants.ElementAtOrDefault(3) ?? match.Participants.FirstOrDefault();

        var isVictory = player?.Win ?? (match.WinningTeam == TeamSide.Blue);
        var durationMinutes = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;

        var (posName, posIcon) = (player?.Position ?? Position.Middle) switch
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

        var vm = new PlayerMatchItemViewModel
        {
            MatchId = match.MatchId,
            IsVictory = isVictory,
            FormattedDuration = match.FormattedDuration,
            FormattedTimeAgo = timeAgo,
            ChampionName = player?.ChampionName ?? "Aatrox",
            PositionName = posName,
            PositionIcon = posIcon,
            Kills = player?.Kills ?? 0,
            Deaths = player?.Deaths ?? 0,
            Assists = player?.Assists ?? 0,
            KdaRatio = player?.KdaRatio ?? 0,
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
