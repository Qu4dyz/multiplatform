using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;

namespace GameAnalytics.ML.Engine;

public static class ChampionScalingAnalyzer
{
    private static readonly Dictionary<string, (int Early, int Mid, int Late, string Strength)> KnownProfiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // HYPER-SCALERS / LATE GAME (Late >= 9)
            ["Kayle"] = (3, 6, 10, "Абсолютний лейт-гейм гіперкеррі (16+ рівень)"),
            ["Kassadin"] = (3, 6, 10, "Нестримний маг пізньої гри (Riftwalk 16 ур.)"),
            ["Smolder"] = (4, 6, 10, "Нескінченне накопичення стаків Dragon Practice"),
            ["Aurelion Sol"] = (4, 6, 10, "Глобальний скейлінг Stardust і чорних дір"),
            ["Jinx"] = (4, 7, 9, "Вибуховий AoE DPS зі стаками Get Excited!"),
            ["Vayne"] = (4, 7, 9, "Чиста відсоткова шкода Silver Bolts по танках"),
            ["Senna"] = (4, 7, 9, "Нескінченна дальність автоатаки та крит зі душ"),
            ["Vladimir"] = (3, 7, 9, "Невразливість у пулі та масовий вампіризм"),
            ["Kog'Maw"] = (3, 7, 9, "Абсолютна відсоткова магічна шкода по цілях"),
            ["Twitch"] = (4, 6, 9, "Прихований масовий розстріл команди з ультимейту"),
            ["Gangplank"] = (4, 7, 9, "Критичні ланцюгові бочки, що ігнорують броню"),
            ["Veigar"] = (4, 6, 9, "Нескінченна сила вмінь (AP) від пасивки"),
            ["Nasus"] = (3, 7, 8, "Нескінченні стаки Q та потужний спліт-пуш"),
            ["Sivir"] = (4, 7, 9, "Швидка зачистка хвиль і масовий рикошет"),
            ["Aphelios"] = (4, 7, 9, "Комплексний арсенал зброї пізньої гри"),
            ["Cassiopeia"] = (4, 7, 9, "Кулеметний Twin Fang без потреби у черевиках"),
            ["Master Yi"] = (4, 6, 9, "Чиста шкода й скидання перезарядок під Highlander"),
            ["Sion"] = (4, 7, 8, "Нескінченне здоров'я W та потужний фронтлайн"),
            ["Cho'Gath"] = (4, 7, 8, "Нескінченне здоров'я Feast і контроль"),
            ["Sona"] = (3, 7, 9, "Постійне масове прискорення, щити та лікування"),
            ["Yuumi"] = (3, 6, 8, "Невразливе підсилення гіперкеррі у лейті"),
            ["Taric"] = (3, 7, 9, "Групова невразливість Cosmic Radiance"),

            // EARLY GAME BULLIES / AGGRO (Early >= 8, Late <= 5)
            ["Renekton"] = (9, 6, 4, "Домінування на лінії, ранній тиск та розбивання веж"),
            ["Lee Sin"] = (9, 7, 4, "Високий темп ганків та агресивний контроль лісу"),
            ["Elise"] = (9, 6, 3, "Ранні дайви під вежу через паутину та контроль"),
            ["Draven"] = (9, 6, 5, "Ранній сноубол завдяки золоту з сокир Adoration"),
            ["Pantheon"] = (9, 6, 4, "Точкові ганки з картою Grand Starfall"),
            ["Lucian"] = (8, 7, 5, "Агресивні дуелі на ранніх рівнях з Lightslinger"),
            ["Blitzcrank"] = (8, 6, 4, "Смертоносні хуки 1-го рівня та тиск на лініях"),
            ["Xin Zhao"] = (8, 7, 4, "Потужні дуелі на 3-му рівні та контроль крабів"),
            ["Talon"] = (8, 7, 4, "Швидкі роумінги через стіни та ранні вбивства"),
            ["LeBlanc"] = (8, 7, 5, "Висока мобільність та вибухова шкода на ранніх фазах"),
            ["Jayce"] = (8, 7, 5, "Далекобійний пок shock blast і тиск на топі"),
            ["Nidalee"] = (9, 6, 3, "Швидка зачистка лісу та агресивний контрджангл"),
            ["Olaf"] = (9, 7, 4, "Нестримні ранні дуелі під Ragnarok"),
            ["Darius"] = (8, 7, 5, "Кровотеча та повний контроль хвилі на топ-лейні"),
            ["Warwick"] = (8, 6, 4, "Полювання за пораненими та сильний ранній драфт"),
            ["Kalista"] = (9, 6, 4, "Мобільні стрибки та повний контроль цілей через Rend"),
            ["Pyke"] = (8, 6, 4, "Страта через Your Cut і розподіл додаткового золота"),
            ["Leona"] = (8, 7, 5, "Агресивний ол-ін з 2-го рівня та ланцюговий стан"),
            ["Karma"] = (8, 7, 4, "Мантра-щити та далекобійний тиск на лінії"),

            // MID GAME SPIKERS (Mid >= 8)
            ["Ahri"] = (6, 8, 6, "Мобільність Spirit Rush та ловля цілей через Charm"),
            ["Orianna"] = (5, 8, 8, "Командний розрив ультимейтом Shockwave"),
            ["Syndra"] = (6, 8, 7, "Точкове знищення ключової цілі Unleashed Power"),
            ["Sejuani"] = (6, 8, 6, "Ланцюговий контроль та ініціація Glacial Prison"),
            ["Vi"] = (7, 8, 5, "Гарантована ініціація у керрі через Cease and Desist"),
            ["Rumble"] = (7, 9, 6, "Зонування командних боїв килимом The Equalizer"),
            ["Kai'Sa"] = (5, 8, 8, "Еволюція вмінь та врив до беклайну"),
            ["Ezreal"] = (6, 8, 6, "Стрибок сили на двох предметах (Trinity/Muramana)"),
            ["Jhin"] = (6, 8, 6, "Контроль бою здалеку з Curtain's Call"),
            ["Hecarim"] = (6, 8, 6, "Швидкісний врив ультимейтом Onslaught of Shadows"),
            ["Sylas"] = (5, 8, 7, "Крадіжка ворожих ультимейтів у тімфайтах"),
            ["Yone"] = (5, 8, 8, "Подвійний крит та контроль Soul Unbound"),
            ["Zed"] = (6, 8, 5, "Знищення ворожого керрі під Death Mark"),
            ["Diana"] = (6, 8, 6, "Масовий притяг Moonfall у командних боях"),
            ["Viego"] = (5, 8, 7, "Ланцюгові володіння тілами переможених ворогів"),
            ["Akali"] = (6, 8, 6, "Невловимий туман і серія стрибків Perfect Execution"),
            ["Viktor"] = (5, 8, 8, "Еволюція Chaos Storm і велика площа шкоди"),
            ["Ashe"] = (6, 7, 6, "Глобальна ініціація Crystal Arrow та сповільнення"),
            ["Varus"] = (7, 8, 6, "Зонування стрілами або ланцюговий Chain of Corruption"),
            ["Xayah"] = (5, 8, 7, "Самозахист Featherstorm і повернення пір'я"),
            ["Thresh"] = (7, 8, 6, "Ліхтарик для порятунку союзника та ініціація"),
            ["Rakan"] = (6, 8, 6, "Надшвидке зачарування The Quickness у тімфайтах"),
            ["Aatrox"] = (7, 8, 6, "Стійкість та розмашисті удари World Ender"),
            ["Ornn"] = (5, 8, 8, "Кування покращених предметів для команди на 13+ рівні"),
            ["Nautilus"] = (7, 8, 5, "Невідворотний контроль Depth Charge")
        };

    public static ChampionScalingProfile AnalyzeChampion(string championName, List<string>? roles = null)
    {
        var cleanName = ChampionNameHelper.NormalizeForDataDragon(championName);

        if (KnownProfiles.TryGetValue(cleanName, out var profile))
        {
            return new ChampionScalingProfile
            {
                ChampionName = championName,
                EarlyGameScore = profile.Early,
                MidGameScore = profile.Mid,
                LateGameScore = profile.Late,
                PrimaryStrength = profile.Strength
            };
        }

        // Heuristics by roles/classes if champion not explicitly mapped
        var early = 6;
        var mid = 6;
        var late = 6;
        var strength = "Збалансований універсальний темп";

        if (roles != null)
        {
            if (roles.Contains("Marksman"))
            {
                early = 4;
                mid = 7;
                late = 8;
                strength = "Поступове нарощування DPS зі збором предметів";
            }
            else if (roles.Contains("Assassin"))
            {
                early = 8;
                mid = 7;
                late = 5;
                strength = "Пошук швидких вбивств та тиск на ранніх лініях";
            }
            else if (roles.Contains("Mage"))
            {
                early = 5;
                mid = 8;
                late = 7;
                strength = "Контроль території та вибухова магічна шкода";
            }
            else if (roles.Contains("Tank"))
            {
                early = 5;
                mid = 7;
                late = 7;
                strength = "Фронтлайн, блокування шкоди та надійний контроль";
            }
            else if (roles.Contains("Fighter"))
            {
                early = 7;
                mid = 7;
                late = 6;
                strength = "Сильні дуелі 1v1 та бічні лінії";
            }
            else if (roles.Contains("Support"))
            {
                early = 7;
                mid = 7;
                late = 5;
                strength = "Утиліті, огляд карти та збереження команди";
            }
        }

        return new ChampionScalingProfile
        {
            ChampionName = championName,
            EarlyGameScore = early,
            MidGameScore = mid,
            LateGameScore = late,
            PrimaryStrength = strength
        };
    }

    public static TeamCompositionAnalysis AnalyzeTeam(IEnumerable<string> championNames, TeamSide side)
    {
        var validChamps = championNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var analysis = new TeamCompositionAnalysis { TeamSide = side };

        if (validChamps.Count == 0)
        {
            analysis.EarlyGamePower = 50.0;
            analysis.MidGamePower = 50.0;
            analysis.LateGamePower = 50.0;
            analysis.WinConditionSummary = "Склад команди не визначено.";
            return analysis;
        }

        var profiles = validChamps.Select(c => AnalyzeChampion(c)).ToList();

        // Scale scores (1..10) to percentages (0..100)
        analysis.EarlyGamePower = Math.Round(profiles.Average(p => p.EarlyGameScore) * 10.0, 1);
        analysis.MidGamePower = Math.Round(profiles.Average(p => p.MidGameScore) * 10.0, 1);
        analysis.LateGamePower = Math.Round(profiles.Average(p => p.LateGameScore) * 10.0, 1);

        // Determine Peak Phase
        if (analysis.EarlyGamePower >= analysis.MidGamePower && analysis.EarlyGamePower >= analysis.LateGamePower)
        {
            analysis.PeakPhase = ScalingPhase.EarlyGame;
            analysis.ArchetypeTitle = "Агресивний ранній темп (Early Aggro / Snowball)";
            analysis.WinConditionSummary = "Необхідно тиснути на лініях, забирати личинок безодні та закріплювати перевагу за вежами до 20-25 хвилини.";
        }
        else if (analysis.LateGamePower > analysis.MidGamePower && analysis.LateGamePower > analysis.EarlyGamePower)
        {
            analysis.PeakPhase = ScalingPhase.LateGame;
            analysis.ArchetypeTitle = "Лейт-гейм гіперскейлінг (Late Game Hyper-Scale)";
            analysis.WinConditionSummary = "Головне — не віддати гру на старті. З 3-ма предметами та 16-м рівнем композиція переважає супротивника у тімфайтах.";
        }
        else
        {
            analysis.PeakPhase = ScalingPhase.MidGame;
            analysis.ArchetypeTitle = "Командні бої міду (Mid Game Teamfight Spike)";
            analysis.WinConditionSummary = "Пік сили припадає на 15-25 хвилини (1-2 предмети). Використовуйте цей візит сили для контролю Драконів та Барона.";
        }

        return analysis;
    }

    public static DraftPowerSpikeComparison CompareDraftCompositions(IEnumerable<string> blueChamps, IEnumerable<string> redChamps)
    {
        var blue = AnalyzeTeam(blueChamps, TeamSide.Blue);
        var red = AnalyzeTeam(redChamps, TeamSide.Red);

        var comparison = new DraftPowerSpikeComparison
        {
            BlueTeam = blue,
            RedTeam = red
        };

        var earlyDiff = blue.EarlyGamePower - red.EarlyGamePower;
        var lateDiff = blue.LateGamePower - red.LateGamePower;

        if (Math.Abs(earlyDiff) >= 6.0 || Math.Abs(lateDiff) >= 6.0)
        {
            if (earlyDiff > 0 && lateDiff < 0)
            {
                comparison.ComparativeVerdict =
                    $"Синя команда веде у ранньому темпі (+{earlyDiff:F0}% Early), проте Червона сильніша у лейті (+{-lateDiff:F0}% Late). Синім критично важливо закінчувати гру швидко.";
            }
            else if (earlyDiff < 0 && lateDiff > 0)
            {
                comparison.ComparativeVerdict =
                    $"Червона команда має сильніший старт (+{-earlyDiff:F0}% Early), тоді як Сині мають значно кращий лейт-гейм (+{lateDiff:F0}% Late). Синім вигідно грати від захисту та затягувати матч.";
            }
            else if (earlyDiff > 0 && lateDiff >= 0)
            {
                comparison.ComparativeVerdict = "Синя команда володіє загальною перевагою сили композиції на всіх ключових стадіях матчу.";
            }
            else
            {
                comparison.ComparativeVerdict = "Червона команда володіє загальною перевагою сили композиції на всіх ключових стадіях матчу.";
            }
        }
        else
        {
            comparison.ComparativeVerdict = "Композиції збалансовані за кривою темпу; результат вирішуватиме індивідуальне виконання та контроль нейтральних об'єктів.";
        }

        return comparison;
    }
}
