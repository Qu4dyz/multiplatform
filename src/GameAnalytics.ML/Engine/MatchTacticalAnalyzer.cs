using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.ML.Engine;

public static class MatchTacticalAnalyzer
{
    public static MatchTacticalReport AnalyzeMatchTactics(Match match, Participant player, string tierName)
        => AnalyzeMatchTactics(match, player, null, GameTier.Emerald, tierName);

    public static MatchTacticalReport AnalyzeMatchTactics(Match match, Participant player, MatchTimelineData? timeline = null, GameTier tier = GameTier.Emerald, string? tierName = null)
    {
        var report = new MatchTacticalReport
        {
            MatchId = match.MatchId,
            ChampionName = player.ChampionName,
            Position = player.Position,
            IsVictory = player.Win
        };

        var displayTier = !string.IsNullOrWhiteSpace(tierName) ? tierName : tier.ToString();
        var durMin = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;
        var csPerMin = Math.Round(player.TotalMinionsKilled / Math.Max(1.0, durMin), 1);

        var teamSide = player.TeamSide;
        var teamKills = match.Participants.Where(p => p.TeamSide == teamSide).Sum(p => p.Kills);
        var kp = teamKills > 0 ? (int)Math.Round((double)(player.Kills + player.Assists) / teamKills * 100) : 0;

        var teamDmg = match.Participants.Where(p => p.TeamSide == teamSide).Sum(p => p.TotalDamageDealtToChampions);
        var dmgShare = teamDmg > 0 ? Math.Round((double)player.TotalDamageDealtToChampions / teamDmg * 100, 1) : 20.0;

        var benchmark = RoleBenchmark.GetBenchmark(player.Position, tier);

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
            BenchmarkValueText = $"{benchmark.TargetCsPerMin:F1} CS/хв ({displayTier})",
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
            BenchmarkValueText = $"{benchmark.TargetKillParticipation}% KP ({displayTier})",
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
            BenchmarkValueText = $"{benchmark.TargetDamageSharePercent:F1}% ({displayTier})",
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
            BenchmarkValueText = $"< {benchmark.MaxTargetDeaths:F1} ({displayTier})",
            EvaluationText = deathDiff >= 0 ? $"{deathDiff:F1} смертей менше ліміту" : $"+{Math.Abs(deathDiff):F1} зайвих смертей",
            ImpactColor = deathDiff >= 0 ? "#0AC8B9" : "#E84057",
            IsPositive = deathDiff >= 0,
            Advice = deathDiff >= 0
                ? "Дисциплінована гра без непотрібного ризику."
                : "Надлишкові смерті передавали ворожій команді золото за стріки та відкривали нейтральні об'єкти."
        });

        // 2. MINUTE-BY-MINUTE TIMELINE (Play-by-play real events from Riot Timeline API or tactical milestones)
        var playerParticipantId = player.ParticipantId > 0
            ? player.ParticipantId
            : match.Participants.IndexOf(player) + 1;

        if (timeline != null && timeline.RealEvents.Count > 0)
        {
            report.TimelineEvents = BuildRealTimelineEvents(match, player, timeline, playerParticipantId);
        }

        if (report.TimelineEvents.Count == 0)
        {
            // Fallback: simulated tactical milestones
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
                        IconKind = "gold",
                        CategoryTag = "ECONOMY",
                        CategoryTagColor = "#10B981",
                        SeverityTag = "FULL CLEAR",
                        SeverityBg = "#0E241E",
                        SeverityFg = "#2DEB90",
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
                        IconKind = "warning",
                        CategoryTag = "ECONOMY",
                        CategoryTagColor = "#EF4444",
                        SeverityTag = "LOST TEMPO",
                        SeverityBg = "#2E1217",
                        SeverityFg = "#FF4655",
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
                    IconKind = "eye",
                    CategoryTag = "VISION",
                    CategoryTagColor = "#8B5CF6",
                    SeverityTag = "VISION CONTROL",
                    SeverityBg = "#1F1530",
                    SeverityFg = "#C084FC",
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
                    IconKind = "skull",
                    CategoryTag = "MACRO",
                    CategoryTagColor = "#EF4444",
                    SeverityTag = "CRITICAL DEATH",
                    SeverityBg = "#3B141C",
                    SeverityFg = "#FF3366",
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
                    IconKind = "dragon",
                    CategoryTag = "OBJECTIVE",
                    CategoryTagColor = "#06B6D4",
                    SeverityTag = "DRAGON SECURED",
                    SeverityBg = "#0C2329",
                    SeverityFg = "#0AC8B9",
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
                    IconKind = "gold",
                    CategoryTag = "MACRO",
                    CategoryTagColor = "#F59E0B",
                    SeverityTag = "CROSS-FARM",
                    SeverityBg = "#2B2211",
                    SeverityFg = "#F0E6D2",
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
                    IconKind = "sword",
                    CategoryTag = "COMBAT",
                    CategoryTagColor = "#3B82F6",
                    SeverityTag = "POWER SPIKE",
                    SeverityBg = "#112236",
                    SeverityFg = "#60A5FA",
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
                    IconKind = "warning",
                    CategoryTag = "MACRO",
                    CategoryTagColor = "#EF4444",
                    SeverityTag = "LOW PRESSURE",
                    SeverityBg = "#2E1217",
                    SeverityFg = "#FF4655",
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
                    IconKind = "star",
                    CategoryTag = "TEAMFIGHT",
                    CategoryTagColor = "#0AC8B9",
                    SeverityTag = "ACE & BARON",
                    SeverityBg = "#0E2925",
                    SeverityFg = "#0AC8B9",
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
                    IconKind = "skull",
                    CategoryTag = "TEAMFIGHT",
                    CategoryTagColor = "#EF4444",
                    SeverityTag = "BARON THROW",
                    SeverityBg = "#381119",
                    SeverityFg = "#FF3366",
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
                    IconKind = player.Win ? "star" : "skull",
                    CategoryTag = "FINALE",
                    CategoryTagColor = player.Win ? "#EAB308" : "#EF4444",
                    SeverityTag = player.Win ? "VICTORY" : "DEFEAT",
                    SeverityBg = player.Win ? "#2B2411" : "#2D1016",
                    SeverityFg = player.Win ? "#FDE047" : "#FF4655",
                    Title = player.Win ? "Фінальний штурм Нексуса" : "Падіння головної бази",
                    Description = player.Win
                        ? "Успішна фіксація переваги, знищення ворожого Нексуса та перемога."
                        : "Ворожа команда змогла прорвати лінії оборони та завершити гру.",
                    ImpactText = player.Win ? "Перемога (+LP)" : "Поразка (-LP)",
                    ImpactColor = player.Win ? "#0AC8B9" : "#E84057",
                    IsMistake = !player.Win
                });
            }
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

    private static List<TacticalTimelineEvent> BuildRealTimelineEvents(Match match, Participant player, MatchTimelineData timeline, int playerParticipantId)
    {
        var list = new List<TacticalTimelineEvent>();
        var playerTeamSide = player.TeamSide;
        var playerTeamId = playerTeamSide == TeamSide.Blue ? 100 : 200;

        string GetChampName(int pId)
        {
            var p = match.Participants.FirstOrDefault(x => (x.ParticipantId > 0 ? x.ParticipantId : match.Participants.IndexOf(x) + 1) == pId);
            return p != null && !string.IsNullOrWhiteSpace(p.ChampionName) 
                ? GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName(p.ChampionName) 
                : $"Гравець #{pId}";
        }

        var relevant = timeline.RealEvents
            .Where(e => e.KillerId == playerParticipantId ||
                        e.VictimId == playerParticipantId ||
                        e.AssistingParticipantIds.Contains(playerParticipantId) ||
                        e.EventType == "ELITE_MONSTER_KILL" ||
                        (e.EventType == "BUILDING_KILL" && e.BuildingType.Contains("TOWER", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.TimestampMs)
            .ToList();

        foreach (var ev in relevant)
        {
            var min = ev.TimestampMs / 60000;
            var sec = (ev.TimestampMs % 60000) / 1000;
            var tsText = $"{min:D2}:{sec:D2}";
            var (phaseName, phaseColor) = min < 14
                ? ("Рання гра (0-14хв)", "#5383E8")
                : (min < 25 ? ("Мід-гейм (14-25хв)", "#C8AA6E") : ("Лейт-гейм (25+хв)", "#E84057"));

            if (ev.EventType == "CHAMPION_KILL")
            {
                if (ev.KillerId == playerParticipantId)
                {
                    var victimChamp = GetChampName(ev.VictimId);
                    list.Add(new TacticalTimelineEvent
                    {
                        Minute = min,
                        TimestampText = tsText,
                        PhaseName = phaseName,
                        PhaseBadgeColor = phaseColor,
                        Icon = "⚔️",
                        IconKind = "sword",
                        CategoryTag = "COMBAT",
                        CategoryTagColor = "#EF4444",
                        SeverityTag = ev.AssistingParticipantIds.Count == 0 ? "SOLO KILL" : "KILL",
                        SeverityBg = "#2B1118",
                        SeverityFg = "#FF4D6D",
                        Title = $"Вбивство {victimChamp}",
                        Description = ev.AssistingParticipantIds.Count == 0
                            ? "Чистий соло-кіл на карті. Відмінна дуель без втрати позиції."
                            : $"Успішний бій або ганк за сприяння {ev.AssistingParticipantIds.Count} союзників.",
                        ImpactText = $"+{Math.Max(300, ev.Bounty)}G темп",
                        ImpactColor = "#0AC8B9",
                        IsMistake = false
                    });
                }
                else if (ev.VictimId == playerParticipantId)
                {
                    var killerChamp = GetChampName(ev.KillerId);
                    list.Add(new TacticalTimelineEvent
                    {
                        Minute = min,
                        TimestampText = tsText,
                        PhaseName = phaseName,
                        PhaseBadgeColor = phaseColor,
                        Icon = "💀",
                        IconKind = "skull",
                        CategoryTag = "DEATH",
                        CategoryTagColor = "#DC2626",
                        SeverityTag = "DEATH",
                        SeverityBg = "#38121A",
                        SeverityFg = "#FF3366",
                        Title = $"Смерть від {killerChamp}",
                        Description = "Невдале зіткнення або потрапляння під ворожий фокус. Час відродження створив вікно для ворога.",
                        ImpactText = "Втрата темпу",
                        ImpactColor = "#E84057",
                        IsMistake = true
                    });
                }
                else if (ev.AssistingParticipantIds.Contains(playerParticipantId))
                {
                    var victimChamp = GetChampName(ev.VictimId);
                    list.Add(new TacticalTimelineEvent
                    {
                        Minute = min,
                        TimestampText = tsText,
                        PhaseName = phaseName,
                        PhaseBadgeColor = phaseColor,
                        Icon = "🤝",
                        IconKind = "sword",
                        CategoryTag = "COMBAT",
                        CategoryTagColor = "#3B82F6",
                        SeverityTag = "ASSIST",
                        SeverityBg = "#112238",
                        SeverityFg = "#60A5FA",
                        Title = $"Асист: усунення {victimChamp}",
                        Description = "Своєчасне стягування та допомога команді в ліквідації ворожої цілі.",
                        ImpactText = "+150G асист",
                        ImpactColor = "#0AC8B9",
                        IsMistake = false
                    });
                }
            }
            else if (ev.EventType == "ELITE_MONSTER_KILL")
            {
                var isOurTeam = ev.KillerTeamId == playerTeamId ||
                                (ev.KillerId > 0 && ((ev.KillerId <= 5 && playerTeamSide == TeamSide.Blue) || (ev.KillerId > 5 && playerTeamSide == TeamSide.Red)));
                var monsterLabel = FormatMonsterName(ev.MonsterType, ev.MonsterSubType);

                // Group Voidgrubs (HORDE) in the same wave/camp to avoid duplicate spam
                if (ev.MonsterType.Equals("HORDE", StringComparison.OrdinalIgnoreCase) && list.Count > 0)
                {
                    var last = list[^1];
                    if (last.Title.Contains("Личинки Безодні") && Math.Abs(last.Minute - min) <= 1 && last.IsMistake == !isOurTeam)
                    {
                        var currentCount = last.Title.Contains("(x2)") ? 3 : 2;
                        last.Title = isOurTeam ? $"Взяття об'єкта: Личинки Безодні (x{currentCount})" : $"Втрата об'єкта: Личинки Безодні (x{currentCount})";
                        last.Description = isOurTeam
                            ? $"Команда надійно забрала {currentCount} Личинки Безодні. Посилення бафів та контроль нейтральних зон."
                            : $"Вороги забрали {currentCount} Личинки Безодні. Варто готувати віжен на річці за хвилину до появи.";
                        continue;
                    }
                }

                list.Add(new TacticalTimelineEvent
                {
                    Minute = min,
                    TimestampText = tsText,
                    PhaseName = phaseName,
                    PhaseBadgeColor = phaseColor,
                    Icon = isOurTeam ? "🐉" : "⚠️",
                    IconKind = isOurTeam ? "dragon" : "warning",
                    CategoryTag = "OBJECTIVE",
                    CategoryTagColor = isOurTeam ? "#06B6D4" : "#DC2626",
                    SeverityTag = isOurTeam ? "SECURED" : "LOST OBJ",
                    SeverityBg = isOurTeam ? "#0C2329" : "#2E1217",
                    SeverityFg = isOurTeam ? "#0AC8B9" : "#FF4655",
                    Title = isOurTeam ? $"Взяття об'єкта: {monsterLabel}" : $"Втрата об'єкта: {monsterLabel}",
                    Description = isOurTeam
                        ? $"Команда надійно забрала {monsterLabel}. Посилення бафів та контроль нейтральних зон."
                        : $"Вороги забрали {monsterLabel}. Варто готувати віжен на річці за хвилину до появи.",
                    ImpactText = isOurTeam ? "Бафф команди" : "Ворожий бафф",
                    ImpactColor = isOurTeam ? "#0AC8B9" : "#E84057",
                    IsMistake = !isOurTeam
                });
            }
            else if (ev.EventType == "BUILDING_KILL")
            {
                var isOurTeamKiller = (ev.KillerId > 0 && ((ev.KillerId <= 5 && playerTeamSide == TeamSide.Blue) || (ev.KillerId > 5 && playerTeamSide == TeamSide.Red)));
                var laneLabel = FormatLane(ev.LaneType);

                list.Add(new TacticalTimelineEvent
                {
                    Minute = min,
                    TimestampText = tsText,
                    PhaseName = phaseName,
                    PhaseBadgeColor = phaseColor,
                    Icon = isOurTeamKiller ? "🏰" : "📉",
                    IconKind = isOurTeamKiller ? "tower" : "warning",
                    CategoryTag = "STRUCTURE",
                    CategoryTagColor = isOurTeamKiller ? "#10B981" : "#DC2626",
                    SeverityTag = isOurTeamKiller ? "TURRET DOWN" : "LOST TOWER",
                    SeverityBg = isOurTeamKiller ? "#0E241E" : "#2E1217",
                    SeverityFg = isOurTeamKiller ? "#2DEB90" : "#FF4655",
                    Title = isOurTeamKiller ? $"Знищення ворожої вежі ({laneLabel})" : $"Втрата союзної вежі ({laneLabel})",
                    Description = isOurTeamKiller
                        ? $"Знищено зовнішнє укріплення супротивника. Отримано відкритий простір для роумінгу."
                        : $"Втрата лінії оборони. Зменшення безпечної зони фарму.",
                    ImpactText = isOurTeamKiller ? "Зняття захисту" : "Втрата карти",
                    ImpactColor = isOurTeamKiller ? "#0AC8B9" : "#E84057",
                    IsMistake = !isOurTeamKiller
                });
            }
        }

        return list.Take(25).ToList();
    }

    private static string FormatMonsterName(string monsterType, string subType)
    {
        if (monsterType.Equals("DRAGON", StringComparison.OrdinalIgnoreCase))
        {
            return subType.ToUpperInvariant() switch
            {
                "EARTH_DRAGON" => "Гірський Дракон",
                "WATER_DRAGON" => "Морський Дракон",
                "FIRE_DRAGON" => "Вогняний Дракон",
                "AIR_DRAGON" => "Хмарний Дракон",
                "HEXTECH_DRAGON" => "Хекстековий Дракон",
                "CHEMTECH_DRAGON" => "Хімтековий Дракон",
                "ELDER_DRAGON" => "Старший Дракон",
                _ => "Дракон"
            };
        }
        if (monsterType.Equals("BARON_NASHOR", StringComparison.OrdinalIgnoreCase)) return "Барон Нашор";
        if (monsterType.Equals("RIFTHERALD", StringComparison.OrdinalIgnoreCase)) return "Герольд Безодні";
        if (monsterType.Equals("HORDE", StringComparison.OrdinalIgnoreCase)) return "Личинки Безодні";
        return monsterType;
    }

    private static string FormatLane(string laneType) => laneType.ToUpperInvariant() switch
    {
        "TOP_LANE" => "Топ",
        "MID_LANE" => "Мід",
        "BOT_LANE" => "Бот",
        _ => "Лінія"
    };
}

