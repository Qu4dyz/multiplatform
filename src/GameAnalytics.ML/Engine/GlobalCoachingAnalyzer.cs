using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.ML.Engine;

public static class GlobalCoachingAnalyzer
{
    public static GlobalCoachingReport Analyze(IReadOnlyList<Match> matches, string searchedPuuidOrName)
    {
        var report = new GlobalCoachingReport();

        if (matches == null || matches.Count == 0)
        {
            report.CoachAdvice = "Зіграйте кілька матчів для формування персонального аналітичного профілю JungleKingdom.";
            return report;
        }

        var cleanTarget = searchedPuuidOrName?.Trim() ?? string.Empty;

        var playerMatches = new List<(Match Match, Participant Player)>();
        foreach (var m in matches)
        {
            var p = m.Participants.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(x.Puuid) && !string.IsNullOrWhiteSpace(cleanTarget) && x.Puuid.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.SummonerName) && !string.IsNullOrWhiteSpace(cleanTarget) && x.SummonerName.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)));

            p ??= m.Participants.FirstOrDefault();
            if (p != null)
            {
                playerMatches.Add((m, p));
            }
        }

        if (playerMatches.Count == 0)
        {
            report.CoachAdvice = "Не знайдено ігор з участю гравця для аналітики.";
            return report;
        }

        var validGames = playerMatches.Where(pm => !pm.Match.IsRemake && pm.Match.GameDurationSeconds >= 300).ToList();
        if (validGames.Count == 0)
        {
            validGames = playerMatches;
        }

        report.TotalMatchesAnalyzed = validGames.Count;

        // Determine Primary Role
        var roleGroup = validGames
            .Where(x => x.Match.QueueId != 450)
            .GroupBy(x => x.Player.Position)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var primaryPos = roleGroup?.Key ?? validGames.FirstOrDefault().Player.Position;
        report.PrimaryRole = primaryPos;
        var benchmark = RoleBenchmark.GetBenchmark(primaryPos);

        var (roleName, roleIcon) = primaryPos switch
        {
            Position.Top => ("TOP", "🛡️"),
            Position.Jungle => ("JGL", "🌲"),
            Position.Middle => ("MID", "⚡"),
            Position.Bottom => ("BOT", "🏹"),
            Position.Utility => ("SUP", "✨"),
            _ => ("MID", "⚔️")
        };
        report.RoleName = roleName;
        report.RoleIcon = roleIcon;

        // Collect aggregate stats
        double totalCs = 0;
        double totalDurationMinutes = 0;
        double totalK = 0, totalD = 0, totalA = 0;
        double totalDpm = 0;
        double totalDamageShare = 0;
        double totalKp = 0;
        int dragonKills = 0;
        int baronKills = 0;
        int towerKills = 0;

        foreach (var (m, p) in validGames)
        {
            var durMin = Math.Max(1.0, m.GameDurationSeconds / 60.0);
            totalDurationMinutes += durMin;

            totalK += p.Kills;
            totalD += p.Deaths;
            totalA += p.Assists;
            totalCs += p.TotalMinionsKilled;

            var dpm = p.TotalDamageDealtToChampions / durMin;
            totalDpm += dpm;

            var teamParticipants = m.Participants.Where(x => x.TeamSide == p.TeamSide).ToList();
            var teamTotalDmg = teamParticipants.Sum(x => x.TotalDamageDealtToChampions);
            var teamTotalKills = teamParticipants.Sum(x => x.Kills);

            var dmgShare = teamTotalDmg > 0 ? (double)p.TotalDamageDealtToChampions / teamTotalDmg * 100 : 20.0;
            totalDamageShare += dmgShare;

            var kp = teamTotalKills > 0 ? (double)(p.Kills + p.Assists) / teamTotalKills * 100 : 0;
            totalKp += kp;

            var teamStats = m.Teams.FirstOrDefault(t => t.TeamSide == p.TeamSide);
            if (teamStats != null)
            {
                dragonKills += teamStats.DragonKills;
                baronKills += teamStats.BaronKills;
                towerKills += teamStats.TowerKills;
            }
        }

        var gameCount = validGames.Count;
        var avgCsPerMin = Math.Round(totalCs / Math.Max(1.0, totalDurationMinutes), 1);
        var avgK = totalK / gameCount;
        var avgD = totalD / gameCount;
        var avgA = totalA / gameCount;
        var avgKdaRatio = avgD > 0 ? (avgK + avgA) / avgD : (avgK + avgA);
        var avgDpm = Math.Round(totalDpm / gameCount);
        var avgDamageShare = Math.Round(totalDamageShare / gameCount, 1);
        var avgKp = (int)Math.Round(totalKp / gameCount);

        // 1. COMBAT PILLAR
        double combatBase = 68.0;
        combatBase += (avgKp - benchmark.TargetKillParticipation) * 0.8;
        combatBase += (avgDamageShare - benchmark.TargetDamageSharePercent) * 2.0;
        combatBase += (avgKdaRatio - 2.5) * 8.0;
        var combatScore = (int)Math.Clamp(Math.Round(combatBase), 20, 99);
        var (combatGrade, combatColor) = ScoreToGrade(combatScore);

        report.Combat = new SkillPillarScore
        {
            PillarName = "Бій (Combat)",
            Icon = "⚔️",
            Score = combatScore,
            Grade = combatGrade,
            GradeColor = combatColor,
            PlayerValueText = $"{avgKp}% KP | {avgDamageShare}% DMG",
            BenchmarkText = $"Еталон: {benchmark.TargetKillParticipation}% KP",
            DiffText = avgKp >= benchmark.TargetKillParticipation
                ? $"+{avgKp - benchmark.TargetKillParticipation}% (Висока участь)"
                : $"{avgKp - benchmark.TargetKillParticipation}% (Нижче норми)",
            DiffColor = avgKp >= benchmark.TargetKillParticipation ? "#0AC8B9" : "#E84057",
            Description = $"Середній DPM: {avgDpm} од./хв. Доля шкоди команди: {avgDamageShare}%."
        };

        // 2. ECONOMY PILLAR
        double economyBase = 68.0;
        if (primaryPos == Position.Utility)
        {
            economyBase += (avgKp - 55) * 1.0;
        }
        else
        {
            var csDiff = avgCsPerMin - benchmark.TargetCsPerMin;
            economyBase += csDiff * 14.0;
        }
        var economyScore = (int)Math.Clamp(Math.Round(economyBase), 20, 99);
        var (economyGrade, economyColor) = ScoreToGrade(economyScore);
        var csDifference = Math.Round(avgCsPerMin - benchmark.TargetCsPerMin, 1);

        report.Economy = new SkillPillarScore
        {
            PillarName = "Фарм (Economy)",
            Icon = "🌾",
            Score = economyScore,
            Grade = economyGrade,
            GradeColor = economyColor,
            PlayerValueText = $"{avgCsPerMin:F1} CS/хв",
            BenchmarkText = $"Еталон: {benchmark.TargetCsPerMin:F1} CS/хв",
            DiffText = csDifference >= 0
                ? $"+{csDifference:F1} CS/хв (Відмінний темп)"
                : $"{csDifference:F1} CS/хв (Втрата пачок)",
            DiffColor = csDifference >= 0 ? "#0AC8B9" : "#E84057",
            Description = $"Сумарно добито {totalCs:F0} міньйонів за {totalDurationMinutes:F0} хв."
        };

        // 3. OBJECTIVES PILLAR
        double objBase = 68.0;
        var avgDragons = (double)dragonKills / gameCount;
        var avgTowers = (double)towerKills / gameCount;
        objBase += (avgDragons - 1.8) * 12.0;
        objBase += (avgTowers - 5.0) * 4.0;
        if (primaryPos == Position.Jungle)
        {
            objBase += 6.0; // Jungle objective focus
        }
        var objScore = (int)Math.Clamp(Math.Round(objBase), 20, 99);
        var (objGrade, objColor) = ScoreToGrade(objScore);

        report.Objectives = new SkillPillarScore
        {
            PillarName = "Об'єкти (Objectives)",
            Icon = "🗺️",
            Score = objScore,
            Grade = objGrade,
            GradeColor = objColor,
            PlayerValueText = $"{avgDragons:F1} Драконів / гру",
            BenchmarkText = "Еталон: 2.0 Дракони",
            DiffText = avgDragons >= 2.0 ? "Контроль епічних монстрів" : "Потребує пріоритету річки",
            DiffColor = avgDragons >= 2.0 ? "#0AC8B9" : "#C8AA6E",
            Description = $"Знищено в середньому {avgTowers:F1} веж та {avgDragons:F1} драконів за гру."
        };

        // 4. SURVIVAL PILLAR
        double survivalBase = 68.0;
        var deathDiff = benchmark.MaxTargetDeaths - avgD; // Positive is good (fewer deaths)
        survivalBase += deathDiff * 12.0;
        var survivalScore = (int)Math.Clamp(Math.Round(survivalBase), 20, 99);
        var (survivalGrade, survivalColor) = ScoreToGrade(survivalScore);

        report.Survival = new SkillPillarScore
        {
            PillarName = "Живучість (Survival)",
            Icon = "🛡️",
            Score = survivalScore,
            Grade = survivalGrade,
            GradeColor = survivalColor,
            PlayerValueText = $"{avgD:F1} Смертей / гру",
            BenchmarkText = $"Еталон: < {benchmark.MaxTargetDeaths:F1}",
            DiffText = deathDiff >= 0
                ? $"{Math.Abs(deathDiff):F1} смертей менше норми"
                : $"+{Math.Abs(deathDiff):F1} зайвих смертей",
            DiffColor = deathDiff >= 0 ? "#0AC8B9" : "#E84057",
            Description = $"Показник KDA: {avgKdaRatio:F2}:1. Безпека позиціонування."
        };

        // Overall Weighted Score
        var overall = (int)Math.Round(combatScore * 0.35 + economyScore * 0.25 + objScore * 0.20 + survivalScore * 0.20);
        report.OverallScore = overall;
        var (ovGrade, ovColor) = ScoreToGrade(overall);
        report.OverallGrade = ovGrade;
        report.OverallGradeColor = ovColor;

        // Coaching Advice based on weakest and strongest pillar
        var pillars = new[] { report.Combat, report.Economy, report.Objectives, report.Survival };
        var weakest = pillars.OrderBy(p => p.Score).First();
        var strongest = pillars.OrderByDescending(p => p.Score).First();

        var advice = weakest.PillarName switch
        {
            var p when p.Contains("Survival") =>
                $"⚠️ Зверніть увагу на живучість ({avgD:F1} смертей): часті смерті у мід-геймі позбавляють вашу команду присутності на ключових об'єктах. Грайте обачніше навколо «туману війни».",
            var p when p.Contains("Economy") =>
                $"🌾 Необхідно підтягнути фарм ({avgCsPerMin:F1} CS/хв проти цільових {benchmark.TargetCsPerMin:F1}): не втрачайте пачки міньйонів при роумінгах і забирайте кемпи між сутичками.",
            var p when p.Contains("Combat") =>
                $"⚔️ Збільште участь у командних бійках (поточний KP: {avgKp}%). Вашому піку не вистачає присутності під час сутичок 5v5.",
            _ =>
                $"🗺️ Акцентуйте увагу на ранньому контролі драконів та личинок Безодні для посилення снігового кому команди."
        };

        report.CoachAdvice = $"Коучинг-висновок: {advice} Найсильніша сторона: {strongest.PillarName} ({strongest.Grade}).";

        return report;
    }

    public static (string Grade, string GradeColor, string Tag) EvaluateSingleMatch(Match match, Participant player, bool isVictory, bool isRemake)
    {
        if (isRemake)
        {
            return ("-", "#8A93A5", "Ремейк");
        }

        var kda = player.KdaRatio;
        var durMin = Math.Max(1.0, match.GameDurationSeconds / 60.0);
        var csPerMin = player.TotalMinionsKilled / durMin;

        var teamDmg = match.Participants.Where(p => p.TeamSide == player.TeamSide).Sum(p => p.TotalDamageDealtToChampions);
        var dmgShare = teamDmg > 0 ? (double)player.TotalDamageDealtToChampions / teamDmg * 100 : 20.0;

        // Scoring algorithm
        double score = 50.0;
        if (isVictory) score += 15.0;

        // KDA impact
        if (kda >= 5.0) score += 20;
        else if (kda >= 3.0) score += 12;
        else if (kda >= 2.0) score += 4;
        else score -= 12;

        // Damage share impact
        if (dmgShare >= 32.0) score += 15;
        else if (dmgShare >= 24.0) score += 8;
        else if (dmgShare < 15.0) score -= 8;

        // CS impact (for non-supports)
        if (player.Position != Position.Utility)
        {
            if (csPerMin >= 8.0) score += 10;
            else if (csPerMin >= 6.8) score += 5;
            else if (csPerMin < 5.0) score -= 8;
        }

        var (grade, color) = ScoreToGrade((int)Math.Clamp(Math.Round(score), 20, 99));

        // Tag
        string tag;
        if (dmgShare >= 30.0 && isVictory) tag = "💎 Hard Carry";
        else if (dmgShare >= 26.0) tag = "⚔️ Топ-дамаг";
        else if (csPerMin >= 8.0 && player.Position != Position.Utility) tag = "🌾 CS Монстр";
        else if (player.Deaths <= 1 && kda >= 6.0) tag = "🛡️ Безсмертний";
        else if (player.Deaths >= 8) tag = "⚠️ Багато смертей";
        else if (isVictory) tag = "⚡ Надійна гра";
        else tag = "📉 Складний матч";

        return (grade, color, tag);
    }

    public static (int Delta, string Text, string Bg, string Fg) CalculateLpDelta(Match match, bool isVictory, bool isRemake, bool isMvp, bool isAce)
    {
        var isRanked = match.QueueId == 420 || match.QueueId == 440 ||
                       match.QueueName.Contains("Ranked", StringComparison.OrdinalIgnoreCase);

        if (!isRanked)
        {
            return (0, "-", "Transparent", "#8A93A5");
        }

        if (isRemake)
        {
            return (0, "0 LP", "#1C222B", "#A0A8B6");
        }

        if (isVictory)
        {
            // +20 to +24 LP
            var gain = isMvp ? 24 : 22;
            return (gain, $"+{gain} LP", "#0F2823", "#0AC8B9");
        }
        else
        {
            // -17 to -20 LP
            var loss = isAce ? -17 : -19;
            return (loss, $"{loss} LP", "#2A141A", "#E84057");
        }
    }

    private static (string Grade, string Color) ScoreToGrade(int score) => score switch
    {
        >= 90 => ("S+", "#C8AA6E"),
        >= 82 => ("S", "#C8AA6E"),
        >= 72 => ("A", "#0AC8B9"),
        >= 60 => ("B", "#5383E8"),
        >= 45 => ("C", "#F0E6D2"),
        _ => ("D", "#E84057")
    };
}
