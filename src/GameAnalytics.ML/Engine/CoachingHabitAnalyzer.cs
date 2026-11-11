using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;

namespace GameAnalytics.ML.Engine;

/// <summary>
/// Picks the habits that cost the player the most games. Each candidate gets a priority from
/// (a) how far the player sits below players of the same role and rank, weighted by how strongly
/// that stat moves ranked games (<see cref="RoleBaselines.WinSwing"/>), and (b) the win-rate gap the
/// habit shows in the player's own games. Only habits with evidence are returned.
/// </summary>
public static class CoachingHabitAnalyzer
{
    private const int MinGamesForSplit = 3;
    private const int HighDeaths = 7;
    private const int LowDeaths = 4;

    public sealed record GameSample(Match Match, Participant Player)
    {
        public double Minutes => Math.Max(1.0, Match.GameDurationSeconds / 60.0);
        public double CsPerMin => Player.TotalMinionsKilled / Minutes;

        public double KillParticipation
        {
            get
            {
                if (Player.KillParticipation > 0) return Player.KillParticipation * 100.0;
                var teamKills = Match.Participants.Where(x => x.TeamSide == Player.TeamSide).Sum(x => x.Kills);
                return teamKills > 0 ? 100.0 * (Player.Kills + Player.Assists) / teamKills : 0;
            }
        }

        public double DamageShare
        {
            get
            {
                if (Player.TeamDamagePercentage > 0) return Player.TeamDamagePercentage * 100.0;
                var teamDmg = Match.Participants.Where(x => x.TeamSide == Player.TeamSide).Sum(x => x.TotalDamageDealtToChampions);
                return teamDmg > 0 ? 100.0 * Player.TotalDamageDealtToChampions / teamDmg : 20.0;
            }
        }

        public bool HasVision => Player.VisionScore > 0 || Player.WardsPlaced > 0;
        public double VisionPerMin => Player.VisionScore / Minutes;

        /// <summary>Gold lead of the player's lane over the enemy lane at 15:00, when the timeline snapshot exists.</summary>
        public double? LaneGoldDiff15
        {
            get
            {
                if (!Match.HasMinute15Objectives) return null;
                var blueMinusRed = Player.Position switch
                {
                    Position.Top => Match.TopGoldDiff15,
                    Position.Jungle => Match.JungleGoldDiff15,
                    Position.Middle => Match.MidGoldDiff15,
                    _ => Match.BotDuoGoldDiff15
                };
                return Player.TeamSide == TeamSide.Blue ? blueMinusRed : -blueMinusRed;
            }
        }
    }

    /// <param name="roleGames">Main-role Summoner's Rift games (the ones the pillars are built from).</param>
    /// <param name="riftGames">All Summoner's Rift games of the player, any role.</param>
    public static (List<CoachingHabit> Habits, List<string> Strengths) Analyze(
        IReadOnlyList<GameSample> roleGames,
        IReadOnlyList<GameSample> riftGames,
        Position role,
        RoleBenchmark benchmark)
    {
        var candidates = new List<CoachingHabit>();
        var strengths = new List<string>();
        if (roleGames.Count == 0)
            return (candidates, strengths);

        var baseline = benchmark.Baseline;
        var roleCode = RoleCode(role);
        var peerLabel = baseline?.Tier is { } t ? $"гравців {t} на {roleCode}" : $"гравців на {roleCode}";

        AddDeathsHabit(candidates, strengths, roleGames, riftGames, role, baseline, peerLabel);
        if (role != Position.Utility)
            AddFarmHabit(candidates, strengths, roleGames, role, baseline, peerLabel);
        AddLaneHabit(candidates, strengths, roleGames, role, baseline, peerLabel);
        AddVisionHabit(candidates, strengths, roleGames, role, baseline, peerLabel);
        AddInvolvementHabit(candidates, roleGames, role, baseline, peerLabel);
        AddChampionPoolHabit(candidates, roleGames, roleCode);
        AddRoleSplitHabit(candidates, riftGames);

        var top = candidates
            .Where(h => h.Priority >= 0.04)
            .OrderByDescending(h => h.Priority)
            .Take(3)
            .ToList();
        for (var i = 0; i < top.Count; i++)
            top[i].Rank = i + 1;

        return (top, strengths.Take(2).ToList());
    }

    private static void AddDeathsHabit(List<CoachingHabit> list, List<string> strengths, IReadOnlyList<GameSample> roleGames,
        IReadOnlyList<GameSample> riftGames, Position role, RoleTierBaseline? baseline, string peerLabel)
    {
        var avgDeaths = roleGames.Average(g => g.Player.Deaths);
        var pct = baseline?.Deaths?.EstimatePercentile(avgDeaths, lowerIsBetter: true);
        var basePriority = MetricPriority(pct, role, "Deaths");

        // The player's own split uses every Rift game: dying a lot costs games on any role.
        var high = riftGames.Where(g => g.Player.Deaths >= HighDeaths).ToList();
        var low = riftGames.Where(g => g.Player.Deaths <= LowDeaths).ToList();
        double personalPriority = 0;
        string split = string.Empty;
        if (high.Count >= MinGamesForSplit && low.Count >= MinGamesForSplit)
        {
            var wrHigh = WinRate(high);
            var wrLow = WinRate(low);
            personalPriority = Math.Max(0, wrLow - wrHigh) * high.Count / riftGames.Count;
            split = $" В іграх з {HighDeaths}+ смертями — {Wins(high)}/{high.Count} перемог, з {LowDeaths} і менше — {Wins(low)}/{low.Count}.";
        }

        if (pct >= 70 && personalPriority < 0.1)
        {
            strengths.Add($"Живучість: {avgDeaths:F1} смертей за гру — менше, ніж у {pct}% {peerLabel}.");
            return;
        }

        var target = baseline?.Deaths?.P25 is { } p25 ? Math.Max(2, Math.Round(p25)) : LowDeaths;
        var comparison = pct switch
        {
            null => string.Empty,
            < 50 => $" — більше, ніж у {100 - pct}% {peerLabel}",
            _ => $" (медіана {peerLabel}: {baseline!.Deaths!.P50:F1})"
        };
        list.Add(new CoachingHabit
        {
            Key = "deaths",
            Icon = "SRV",
            Title = "Забагато смертей",
            Evidence = $"У середньому {avgDeaths:F1} смертей за гру{comparison}.{split}",
            Action = $"Ціль — не більше {target:F0} смертей за гру. " + role switch
            {
                Position.Jungle => "Не заходьте у ворожий ліс, поки не бачите ворожого лісника на мапі; після двох смертей грайте від фарму та об'єктів, а не від ганків.",
                Position.Utility => "Ставте варди до того, як іти вперед, і не фейсчекайте кущі без союзника поруч.",
                Position.Bottom => "У бійках стійте за фронтлайном і бийте найближчу ціль; не фармте сайд-лінію без інформації про ворогів.",
                _ => "Не пушьте лінію далі середини, коли ворожий лісник не показаний на мапі; поверніться на базу замість гри на низькому HP."
            },
            Priority = Math.Max(basePriority, personalPriority)
        });
    }

    private static void AddFarmHabit(List<CoachingHabit> list, List<string> strengths, IReadOnlyList<GameSample> roleGames,
        Position role, RoleTierBaseline? baseline, string peerLabel)
    {
        var cs = baseline?.CsPerMin;
        if (cs == null) return;
        var avg = roleGames.Sum(g => (double)g.Player.TotalMinionsKilled) / roleGames.Sum(g => g.Minutes);
        var pct = cs.EstimatePercentile(avg);
        if (pct >= 70)
        {
            strengths.Add($"Фарм: {avg:F1} CS/хв — більше, ніж у {pct}% {peerLabel}.");
            return;
        }

        var wins = roleGames.Where(g => g.Player.Win).ToList();
        var losses = roleGames.Where(g => !g.Player.Win).ToList();
        var split = wins.Count >= MinGamesForSplit && losses.Count >= MinGamesForSplit
            ? $" У перемогах {wins.Average(g => g.CsPerMin):F1}, у поразках {losses.Average(g => g.CsPerMin):F1} CS/хв."
            : string.Empty;
        list.Add(new CoachingHabit
        {
            Key = "farm",
            Icon = "ECO",
            Title = "Фарм нижче рівня рангу",
            Evidence = $"{avg:F1} CS/хв — менше, ніж у {100 - pct}% {peerLabel} (медіана {cs.P50:F1}).{split}",
            Action = role == Position.Jungle
                ? $"Ціль — {cs.P50:F1}+ CS/хв: робіть повний клір між ганками і забирайте свої табори перед кожним драконом чи личинками, а не стійте в кущах."
                : $"Ціль — {cs.P50:F1}+ CS/хв: після роуму чи повернення на базу встигайте на лінію до підходу хвилі, у мід-геймі забирайте сайд-хвилю перед об'єктом.",
            Priority = MetricPriority(pct, role, "CsPerMin")
        });
    }

    private static void AddLaneHabit(List<CoachingHabit> list, List<string> strengths, IReadOnlyList<GameSample> roleGames,
        Position role, RoleTierBaseline? baseline, string peerLabel)
    {
        var withLane = roleGames.Where(g => g.LaneGoldDiff15.HasValue).ToList();
        var dist = baseline?.LaneGoldDiff15;
        if (withLane.Count < MinGamesForSplit || dist == null) return;

        var avg = withLane.Average(g => g.LaneGoldDiff15!.Value);
        var pct = dist.EstimatePercentile(avg);
        if (pct >= 70)
        {
            strengths.Add($"Рання гра: у середньому {avg:+0;-0} золота на 15 хв — краще, ніж у {pct}% {peerLabel}.");
            return;
        }

        var behind = withLane.Where(g => g.LaneGoldDiff15 <= -500).ToList();
        var behindText = behind.Count > 0
            ? $" У {behind.Count} з {withLane.Count} ігор ви відставали на 500+ золота, з них виграно {Wins(behind)}."
            : string.Empty;
        list.Add(new CoachingHabit
        {
            Key = "lane",
            Icon = "LANE",
            Title = "Відставання до 15-ї хвилини",
            Evidence = $"Середня різниця золота з суперником на 15 хв: {avg:+0;-0}.{behindText} Золото на 15 хв — один з найсильніших факторів перемоги в моделі прогнозу.",
            Action = role == Position.Jungle
                ? "Плануйте перший клір під ту лінію, де можна виграти ганк, і віддавайте перевагу кемпам і скатлу перед ризикованими інвейдами."
                : "Не віддавайте хвилі під вежею заради розмінів; беріть платівки, коли суперник іде на базу, і грайте агресивно на своїх сильних рівнях (2, 3, 6).",
            Priority = MetricPriority(pct, role, "LaneGoldDiff15")
        });
    }

    private static void AddVisionHabit(List<CoachingHabit> list, List<string> strengths, IReadOnlyList<GameSample> roleGames,
        Position role, RoleTierBaseline? baseline, string peerLabel)
    {
        var withVision = roleGames.Where(g => g.HasVision).ToList();
        var dist = baseline?.VisionPerMin;
        if (withVision.Count < MinGamesForSplit || dist == null) return;

        var avg = withVision.Sum(g => (double)g.Player.VisionScore) / withVision.Sum(g => g.Minutes);
        var pct = dist.EstimatePercentile(avg);
        if (pct >= 75)
        {
            strengths.Add($"Віжн: {avg:F2} VS/хв — більше, ніж у {pct}% {peerLabel}.");
            return;
        }

        list.Add(new CoachingHabit
        {
            Key = "vision",
            Icon = "VIS",
            Title = "Мало віжну",
            Evidence = $"{avg:F2} vision score за хвилину — менше, ніж у {100 - pct}% {peerLabel} (медіана {dist.P50:F2}).",
            Action = role == Position.Utility
                ? "Купуйте контрольний вард на кожному беці й ставте його в річці за хвилину до дракона чи личинок."
                : "Купуйте контрольний вард на кожному беці і міняйте тринкет на лінзу після першого предмета.",
            Priority = MetricPriority(pct, role, "VisionPerMin")
        });
    }

    private static void AddInvolvementHabit(List<CoachingHabit> list, IReadOnlyList<GameSample> roleGames,
        Position role, RoleTierBaseline? baseline, string peerLabel)
    {
        var dist = baseline?.KillParticipation;
        if (dist == null) return;
        var avg = roleGames.Average(g => g.KillParticipation);
        var pct = dist.EstimatePercentile(avg);
        if (pct >= 35) return;

        list.Add(new CoachingHabit
        {
            Key = "involvement",
            Icon = "CMB",
            Title = "Мала участь у бійках",
            Evidence = $"Участь у вбивствах {avg:F0}% — менше, ніж у {100 - pct}% {peerLabel} (медіана {dist.P50:F0}%).",
            Action = "За 45 секунд до появи дракона, личинок чи Барона закінчуйте хвилю і йдіть до річки разом з командою.",
            Priority = MetricPriority(pct, role, "KillParticipation")
        });
    }

    private static void AddChampionPoolHabit(List<CoachingHabit> list, IReadOnlyList<GameSample> roleGames, string roleCode)
    {
        if (roleGames.Count < 8) return;
        var byChamp = roleGames.GroupBy(g => g.Player.ChampionName).ToList();
        var spread = (double)byChamp.Count / roleGames.Count;
        if (spread < 0.45) return;

        var best = byChamp
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => WinRate(g.ToList()))
            .ThenByDescending(g => g.Count())
            .Take(2)
            .ToList();
        var bestText = best.Count > 0
            ? " Найкращі результати: " + string.Join(", ", best.Select(g => $"{ChampionNameHelper.ToDisplayName(g.Key)} ({Wins(g.ToList())}/{g.Count()})")) + "."
            : string.Empty;
        list.Add(new CoachingHabit
        {
            Key = "pool",
            Icon = "POOL",
            Title = "Занадто широкий пул чемпіонів",
            Evidence = $"{byChamp.Count} різних чемпіонів за {roleGames.Count} ігор на {roleCode}.{bestText}",
            Action = "Залиште для ранкеду 2–3 чемпіони на основній ролі: менше нових матчапів — менше ранніх помилок.",
            Priority = (spread - 0.3) * 0.5
        });
    }

    private static void AddRoleSplitHabit(List<CoachingHabit> list, IReadOnlyList<GameSample> riftGames)
    {
        var roles = riftGames
            .GroupBy(g => g.Player.Position)
            .Where(g => g.Count() >= 5)
            .Select(g => (Role: g.Key, Games: g.ToList(), WinRate: WinRate(g.ToList())))
            .OrderByDescending(r => r.WinRate)
            .ToList();
        if (roles.Count < 2) return;

        var best = roles[0];
        var worst = roles[^1];
        var gap = best.WinRate - worst.WinRate;
        if (gap < 0.15) return;

        list.Add(new CoachingHabit
        {
            Key = "role",
            Icon = "ROLE",
            Title = $"На {RoleCode(best.Role)} ви граєте помітно краще",
            Evidence = $"{RoleCode(best.Role)}: {Wins(best.Games)}/{best.Games.Count} перемог ({best.WinRate:P0}), {RoleCode(worst.Role)}: {Wins(worst.Games)}/{worst.Games.Count} ({worst.WinRate:P0}).",
            Action = $"Ставте {RoleCode(best.Role)} основною роллю в черзі, а {RoleCode(worst.Role)} тренуйте в нормалах, поки результат не вирівняється.",
            Priority = gap * worst.Games.Count / riftGames.Count
        });
    }

    /// <summary>Gap below the peer median (0–1) times the win-rate swing of that stat for the role.</summary>
    private static double MetricPriority(int? percentile, Position role, string metric)
    {
        if (percentile is not { } pct || pct >= 50) return 0;
        var swing = RoleBaselines.WinSwing(role, metric);
        var weight = swing is { } s ? Math.Max(0, s.Better - s.Worse) : 0.2;
        return (50 - pct) / 50.0 * weight;
    }

    private static int Wins(IReadOnlyCollection<GameSample> games) => games.Count(g => g.Player.Win);
    private static double WinRate(IReadOnlyCollection<GameSample> games) => games.Count == 0 ? 0 : (double)Wins(games) / games.Count;

    internal static string RoleCode(Position position) => position switch
    {
        Position.Top => "TOP",
        Position.Jungle => "JGL",
        Position.Middle => "MID",
        Position.Bottom => "BOT",
        Position.Utility => "SUP",
        _ => "MID"
    };
}
