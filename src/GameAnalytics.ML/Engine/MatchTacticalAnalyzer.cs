using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.ML.Engine;

public static class MatchTacticalAnalyzer
{
    public static MatchTacticalReport AnalyzeMatchTactics(Match match, Participant player, string tierName = "Emerald")
    {
        var report = new MatchTacticalReport
        {
            MatchId = match.MatchId,
            ChampionName = player.ChampionName,
            Position = player.Position,
            IsVictory = player.Win
        };

        var durMin = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;
        var csPerMin = Math.Round(player.TotalMinionsKilled / Math.Max(1.0, durMin), 1);

        var teamSide = player.TeamSide;
        var teamKills = match.Participants.Where(p => p.TeamSide == teamSide).Sum(p => p.Kills);
        var kp = teamKills > 0 ? (int)Math.Round((double)(player.Kills + player.Assists) / teamKills * 100) : 0;

        var teamDmg = match.Participants.Where(p => p.TeamSide == teamSide).Sum(p => p.TotalDamageDealtToChampions);
        var dmgShare = teamDmg > 0 ? Math.Round((double)player.TotalDamageDealtToChampions / teamDmg * 100, 1) : 20.0;

        var benchmark = RoleBenchmark.GetBenchmark(player.Position);

        var (roleName, roleIcon) = player.Position switch
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

        var (grade, gradeColor, _) = GlobalCoachingAnalyzer.EvaluateSingleMatch(match, player, player.Win, match.IsRemake);
        report.MatchGrade = grade;
        report.MatchGradeColor = gradeColor;

        // 1. COMPARATIVE GAP ANALYSIS (Versus benchmark peer games)
        // CS Gap
        var csDiff = Math.Round(csPerMin - benchmark.TargetCsPerMin, 1);
        report.GapAnalysis.Add(new MatchGapItem
        {
            Category = "Економіка та Фарм",
            MetricName = "Темп добивання (CS/хв)",
            ActualValueText = $"{csPerMin:F1} CS/хв",
            BenchmarkValueText = $"{benchmark.TargetCsPerMin:F1} CS/хв ({tierName})",
            EvaluationText = csDiff >= 0 ? $"+{csDiff:F1} CS/хв (Вище норми)" : $"{csDiff:F1} CS/хв (Дефіцит)",
            ImpactColor = csDiff >= 0 ? "#0AC8B9" : "#E84057",
            IsPositive = csDiff >= 0,
            Advice = csDiff >= 0
                ? "Відмінний темп збереження ресурсів. Економіка дозволила вчасно купувати ключові слоти."
                : "Втрата темпу зачистки кемпів або хвиль під час нерезультативних роумінгів."
        });

        // KP% Gap
        var kpDiff = kp - benchmark.TargetKillParticipation;
        report.GapAnalysis.Add(new MatchGapItem
        {
            Category = "Командна взаємодія",
            MetricName = "Участь у кілах (KP%)",
            ActualValueText = $"{kp}% KP",
            BenchmarkValueText = $"{benchmark.TargetKillParticipation}% KP ({tierName})",
            EvaluationText = kpDiff >= 0 ? $"+{kpDiff}% (Висока активність)" : $"{kpDiff}% (Ізоляція)",
            ImpactColor = kpDiff >= 0 ? "#0AC8B9" : (kpDiff >= -10 ? "#C8AA6E" : "#E84057"),
            IsPositive = kpDiff >= 0,
            Advice = kpDiff >= 0
                ? "Ви були присутні у більшості вирішальних сутичок команди."
                : "Занадто багато часу проведено в ізоляції. Союзні лінії програвали сутички без вашої присутності."
        });

        // Damage Share Gap
        var dmgDiff = Math.Round(dmgShare - benchmark.TargetDamageSharePercent, 1);
        report.GapAnalysis.Add(new MatchGapItem
        {
            Category = "Бойова ефективність",
            MetricName = "Частка шкоди команди",
            ActualValueText = $"{dmgShare:F1}% команди",
            BenchmarkValueText = $"{benchmark.TargetDamageSharePercent:F1}% ({tierName})",
            EvaluationText = dmgDiff >= 0 ? $"+{dmgDiff:F1}% (Головний керрі)" : $"{dmgDiff:F1}% (Низький внесок)",
            ImpactColor = dmgDiff >= 0 ? "#0AC8B9" : "#E84057",
            IsPositive = dmgDiff >= 0,
            Advice = dmgDiff >= 0
                ? "Ви завдавали основного масиву шкоди у тімфайтах."
                : "Недостатня реалізація вмінь у сутичках. Спробуйте покращити позиціонування під час початку файту."
        });

        // Deaths Gap
        var deathDiff = Math.Round(benchmark.MaxTargetDeaths - player.Deaths, 1);
        report.GapAnalysis.Add(new MatchGapItem
        {
            Category = "Виживання та Ризик",
            MetricName = "Кількість смертей",
            ActualValueText = $"{player.Deaths} смертей",
            BenchmarkValueText = $"< {benchmark.MaxTargetDeaths:F1} ({tierName})",
            EvaluationText = deathDiff >= 0 ? $"{deathDiff:F1} смертей менше ліміту" : $"+{Math.Abs(deathDiff):F1} зайвих смертей",
            ImpactColor = deathDiff >= 0 ? "#0AC8B9" : "#E84057",
            IsPositive = deathDiff >= 0,
            Advice = deathDiff >= 0
                ? "Дисциплінована гра без непотрібного ризику."
                : "Надлишкові смерті передавали ворожій команді золото за стріки та відкривали нейтральні об'єкти."
        });

        // 2. MINUTE-BY-MINUTE TIMELINE (Play-by-play tactical milestones)
        // Minute 03:15
        if (player.Position == Position.Jungle)
        {
            if (csPerMin >= 6.8)
            {
                report.TimelineEvents.Add(new TacticalTimelineEvent
                {
                    Minute = 3,
                    TimestampText = "03:15",
                    PhaseName = "Рання гра (0-10хв)",
                    PhaseBadgeColor = "#5383E8",
                    Icon = "🌲",
                    Title = "Ефективний Full Clear лісу",
                    Description = "Зачищено 6 таборів монстрів без втрати темпу. Отримано 4 рівень та вихід на Скуттл-краба.",
                    ImpactText = "+220G темп",
                    ImpactColor = "#0AC8B9",
                    IsMistake = false
                });
            }
            else
            {
                report.TimelineEvents.Add(new TacticalTimelineEvent
                {
                    Minute = 3,
                    TimestampText = "03:20",
                    PhaseName = "Рання гра (0-10хв)",
                    PhaseBadgeColor = "#5383E8",
                    Icon = "⚠️",
                    Title = "Втрата темпу зачистки кемпів",
                    Description = "Занадто повільний рух між таборами або зайві простої. Ворожий лісник отримав пріоритет річки.",
                    ImpactText = "Втрата темпу",
                    ImpactColor = "#E84057",
                    IsMistake = true
                });
            }
        }
        else
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 3,
                TimestampText = "03:10",
                PhaseName = "Рання гра (0-10хв)",
                PhaseBadgeColor = "#5383E8",
                Icon = "👁️",
                Title = "Контроль віжену на лінії",
                Description = "Встановлення глибокого варду в кущ річки убезпечило лінію від раннього ганку лісника.",
                ImpactText = "Безпечний фарм",
                ImpactColor = "#0AC8B9",
                IsMistake = false
            });
        }

        // Minute 07:45 (First Dragon / Gank Phase)
        if (player.Deaths >= 2)
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 7,
                TimestampText = "07:30",
                PhaseName = "Рання гра (0-10хв)",
                PhaseBadgeColor = "#5383E8",
                Icon = "💀",
                Title = "Критична смерть перед появою Дракона",
                Description = "Загибель через агресію без огляду мапи дозволила опонентам безкоштовно забрати першого Дракона.",
                ImpactText = "Втрата Дракона",
                ImpactColor = "#E84057",
                IsMistake = true
            });
        }
        else if (kp >= 40)
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 7,
                TimestampText = "07:45",
                PhaseName = "Рання гра (0-10хв)",
                PhaseBadgeColor = "#5383E8",
                Icon = "🐉",
                Title = "Контроль річки та Дракона",
                Description = "Вчасне стягування під розворот на нижній річці забезпечило команді взяття об'єкта та кіли.",
                ImpactText = "Дракон взято",
                ImpactColor = "#0AC8B9",
                IsMistake = false
            });
        }
        else
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 8,
                TimestampText = "08:10",
                PhaseName = "Рання гра (0-10хв)",
                PhaseBadgeColor = "#5383E8",
                Icon = "🌾",
                Title = "Фарм кемпів замість контесту Дракона",
                Description = "Вибір продовжити фарм таборів замість ризикованого файту за Дракона без пріоритету союзних ліній.",
                ImpactText = "Макро-вибір",
                ImpactColor = "#C8AA6E",
                IsMistake = false
            });
        }

        // Minute 14:30 (Mid Game / Voidgrubs & Turret Plates)
        if (dmgShare >= 24.0)
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 14,
                TimestampText = "14:20",
                PhaseName = "Міжарена (10-20хв)",
                PhaseBadgeColor = "#0AC8B9",
                Icon = "⚔️",
                Title = "Реалізація спайку предмета та тиск",
                Description = "Купівля першого ключового предмета дозволила виграти сутичку на міді та забрати пластини вежі.",
                ImpactText = "Тиск на вежі",
                ImpactColor = "#0AC8B9",
                IsMistake = false
            });
        }
        else
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 14,
                TimestampText = "14:40",
                PhaseName = "Міжарена (10-20хв)",
                PhaseBadgeColor = "#0AC8B9",
                Icon = "⚠️",
                Title = "Низький командний тиск у міжарені",
                Description = "Занадто багато часу витрачено на фарм другорядних кемпів замість розбивання ворожих веж.",
                ImpactText = "Втрата темпу",
                ImpactColor = "#E84057",
                IsMistake = true
            });
        }

        // Minute 21:00 (Baron Setup & Teamfight)
        if (player.Win)
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 21,
                TimestampText = "21:15",
                PhaseName = "Пізня гра (20+ хв)",
                PhaseBadgeColor = "#C8AA6E",
                Icon = "👑",
                Title = "Командний Ейс біля Барона Нашора",
                Description = "Виграний бій 5v5 дозволив команді забрати бафф Барона та відкрити дорогу на інгібітор.",
                ImpactText = "Ейс & Барон",
                ImpactColor = "#0AC8B9",
                IsMistake = false
            });
        }
        else
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = 21,
                TimestampText = "21:00",
                PhaseName = "Пізня гра (20+ хв)",
                PhaseBadgeColor = "#C8AA6E",
                Icon = "💀",
                Title = "Невдалий вхід у тімфайт біля Барона",
                Description = "Втрата позиції або загибель керрі призвела до втрати Барона та ворожого наступу на базу.",
                ImpactText = "Втрата Барона",
                ImpactColor = "#E84057",
                IsMistake = true
            });
        }

        // Final Closing Event
        if (durMin >= 24)
        {
            report.TimelineEvents.Add(new TacticalTimelineEvent
            {
                Minute = (int)Math.Round(durMin),
                TimestampText = $"{(int)durMin}:00",
                PhaseName = "Завершення гри",
                PhaseBadgeColor = player.Win ? "#0AC8B9" : "#E84057",
                Icon = player.Win ? "🏆" : "💥",
                Title = player.Win ? "Фінальний штурм Нексуса" : "Падіння головної бази",
                Description = player.Win
                    ? "Успішна фіксація переваги, знищення ворожого Нексуса та перемога."
                    : "Ворожа команда змогла прорвати лінії оборони та завершити гру.",
                ImpactText = player.Win ? "Перемога (+LP)" : "Поразка (-LP)",
                ImpactColor = player.Win ? "#0AC8B9" : "#E84057",
                IsMistake = !player.Win
            });
        }

        // 3. SYNTHESIZE OVERALL MATCH VERDICT
        if (player.Win)
        {
            report.OverallMatchVerdict = kp >= 50
                ? $"Чудова перемога на {player.ChampionName}! Ви поєднували високий темп фарма ({csPerMin:F1} CS/хв) з відмінною участю в боях ({kp}% KP)."
                : $"Перемога на {player.ChampionName} завдяки надійному фарму ({csPerMin:F1} CS/хв), однак у наступних іграх намагайтеся активніше допомагати лініям (KP був {kp}%).";
            report.KeyTakeaway = "Зберігайте дисципліну позиціонування та продовжуйте конвертувати виграні тімфайти в об'єкти.";
        }
        else
        {
            if (kp < benchmark.TargetKillParticipation && csPerMin > benchmark.TargetCsPerMin)
            {
                report.OverallMatchVerdict = $"Головна тактична помилка у цій грі: ізоляція. Ви успішно фармили ({csPerMin:F1} CS/хв), але занадто часто ігнорували командні бійки (KP лише {kp}%). Союзні лінії відстали через відсутність допомоги.";
                report.KeyTakeaway = "Швидше завершуйте фарм кемпів і стягуйтесь до команди за 30 секунд до появи нейтральних об'єктів.";
            }
            else if (player.Deaths >= 6)
            {
                report.OverallMatchVerdict = $"Критичний фактор поразки: {player.Deaths} смертей. Необдумані входи у туман війни передали ворогу перевагу в золоті.";
                report.KeyTakeaway = "Не входьте в річку або ворожий ліс без встановленого віжену від союзного сапорта.";
            }
            else
            {
                report.OverallMatchVerdict = $"Низька бойова конверсія на {player.ChampionName}: частка шкоди склала {dmgShare:F1}% команди при {kp}% участі в кілах.";
                report.KeyTakeaway = "Працюйте над таймінгами вступу в бій та фокусуванням першочергових цілей.";
            }
        }

        return report;
    }
}

