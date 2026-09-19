using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class SummonerProfile
{
    public string Puuid { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string TagLine { get; set; } = string.Empty;
    public int SummonerLevel { get; set; } = 1;
    public int ProfileIconId { get; set; }

    public GameTier Tier { get; set; } = GameTier.Unranked;
    public string Rank { get; set; } = "IV";
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    public int TotalGames => Wins + Losses;
    public double WinRate => TotalGames > 0 ? Math.Round((double)Wins / TotalGames * 100, 1) : 0.0;
    public string FullName => $"{GameName}#{TagLine}";
    public string ProfileIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/profileicon/{ProfileIconId}.png";

    public List<ChampionMasteryInfo> TopMasteries { get; set; } = new();
}

public class ChampionMasteryInfo
{
    public long ChampionId { get; set; }
    public string ChampionName { get; set; } = string.Empty;
    public int ChampionLevel { get; set; }
    public int ChampionPoints { get; set; }
    public long LastPlayTime { get; set; }
    public string FormattedPoints => ChampionPoints >= 1000000 
        ? $"{ChampionPoints / 1000000.0:F1}M" 
        : (ChampionPoints >= 1000 ? $"{ChampionPoints / 1000.0:F0}k" : $"{ChampionPoints}");
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{Helpers.ChampionNameHelper.ToDDragonImageKey(ChampionName)}.png";
}

public class ItemDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Plaintext { get; set; } = string.Empty;
    public string FormattedDescription { get; set; } = string.Empty;
    public int Gold { get; set; }

    public string ToolTipText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return string.Empty;
            var parts = new List<string> { $"🏷️ {Name}" };
            if (Gold > 0) parts.Add($"💰 Вартість: {Gold} Gold");
            if (!string.IsNullOrWhiteSpace(Plaintext)) parts.Add($"💡 {Plaintext}");
            if (!string.IsNullOrWhiteSpace(FormattedDescription)) parts.Add(FormattedDescription);
            return string.Join("\n\n", parts);
        }
    }
}

public static class GameConstants
{
    public static string DDragonVersion { get; set; } = "16.18.1";

    /// <summary>
    /// Local embedded Summoner's Rift minimap (resolved by Desktop BitmapAssetValueConverter).
    /// Avoids black boards from async CDN loads that never refresh the Image binding.
    /// </summary>
    public const string SummonersRiftMinimapAsset = "asset://summoners_rift_minimap";

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ItemDefinition> Items = new();

    public static string GetItemTooltip(int itemId)
    {
        if (itemId <= 0) return string.Empty;
        if (Items.TryGetValue(itemId, out var item) && !string.IsNullOrWhiteSpace(item.ToolTipText))
        {
            return item.ToolTipText;
        }

        return FallbackTooltips.TryGetValue(itemId, out var fallback) ? fallback : $"Предмет #{itemId}";
    }

    private static readonly Dictionary<int, string> FallbackTooltips = new()
    {
        [1001] = "🏷️ Boots\n\n💰 Вартість: 300 Gold\n\n+25 Швидкість пересування",
        [1054] = "🏷️ Doran's Shield\n\n💰 Вартість: 450 Gold\n\n+110 Здоров'я\nПасивно: відновлює здоров'я при отриманні шкоди.",
        [1055] = "🏷️ Doran's Blade\n\n💰 Вартість: 450 Gold\n\n+10 Сила атаки\n+100 Здоров'я\n+3.5% Всебічне висмоктування життя",
        [1056] = "🏷️ Doran's Ring\n\n💰 Вартість: 400 Gold\n\n+18 Сила вмінь\n+90 Здоров'я\nПасивно: відновлення мани при добиванні міньйонів.",
        [3006] = "🏷️ Berserker's Greaves\n\n💰 Вартість: 1100 Gold\n\n+30% Швидкість атаки\n+45 Швидкість пересування",
        [3009] = "🏷️ Boots of Swiftness\n\n💰 Вартість: 1000 Gold\n\n+60 Швидкість пересування\nЗменшує дію сповільнень на 25%.",
        [3020] = "🏷️ Sorcerer's Shoes\n\n💰 Вартість: 1100 Gold\n\n+18 Магічне проникнення\n+45 Швидкість пересування",
        [3031] = "🏷️ Infinity Edge\n\n💰 Вартість: 3400 Gold\n\n+80 Сила атаки\n+25% Шанс критичного удару\n+40% Шкода критичного удару",
        [3033] = "🏷️ Mortal Reminder\n\n💰 Вартість: 3000 Gold\n\n+35 Сила атаки\n+25% Шанс критичного удару\n+35% Пробивання броні\nНакладає Страшні рани (Grievous Wounds).",
        [3036] = "🏷️ Lord Dominik's Regards\n\n💰 Вартість: 3000 Gold\n\n+45 Сила атаки\n+25% Шанс критичного удару\n+40% Пробивання броні",
        [3047] = "🏷️ Plated Steelcaps\n\n💰 Вартість: 1200 Gold\n\n+25 Броня\n+45 Швидкість пересування\nЗменшує шкоду від автоатак на 12%.",
        [3078] = "🏷️ Trinity Force\n\n💰 Вартість: 3333 Gold\n\n+45 Сила атаки\n+33% Швидкість атаки\n+300 Здоров'я\n+15 Прискорення вмінь\nПасивно: Spellblade наносить 200% базового AD після вміння.",
        [3089] = "🏷️ Rabadon's Deathcap\n\n💰 Вартість: 3600 Gold\n\n+140 Сила вмінь\nПасивно: Збільшує загальну силу вмінь (AP) на 35%.",
        [3111] = "🏷️ Mercury's Treads\n\n💰 Вартість: 1200 Gold\n\n+25 Опір магії\n+45 Швидкість пересування\n+30% Стійкість (Tenacity)",
        [3153] = "🏷️ Blade of the Ruined King\n\n💰 Вартість: 3200 Gold\n\n+50 Сила атаки\n+25% Швидкість атаки\n+10% Висмоктування життя\nПасивно: автоатаки наносять до 10% поточного здоров'я ворога як додаткову шкоду.",
        [3157] = "🏷️ Zhonya's Hourglass\n\n💰 Вартість: 3250 Gold\n\n+105 Сила вмінь\n+50 Броня\nАктивно: Стазис на 2.5 секунди (невразливість).",
        [3071] = "🏷️ Black Cleaver\n\n💰 Вартість: 3000 Gold\n\n+55 Сила атаки\n+400 Здоров'я\n+20 Прискорення вмінь\nПасивно: руйнує до 24% броні цілі.",
        [3072] = "🏷️ Bloodthirster\n\n💰 Вартість: 3400 Gold\n\n+80 Сила атаки\n+18% Висмоктування життя\nПасивно: створює щит від надлишкового лікування.",
        [3091] = "🏷️ Wit's End\n\n💰 Вартість: 3100 Gold\n\n+55% Швидкість атаки\n+45 Опір магії\n+20% Стійкість\nПасивно: автоатаки наносять магічну шкоду.",
        [3046] = "🏷️ Phantom Dancer\n\n💰 Вартість: 2600 Gold\n\n+60% Швидкість атаки\n+25% Шанс критичного удару\n+10% Швидкість пересування\nПасивно: Ghosting (прохід крізь юнітів).",
        [3172] = "🏷️ Gunmetal Greaves (Zephyr Upgrade)\n\n💰 Вартість: 1100 Gold\n\n+45% Швидкість атаки\n+45 Швидкість пересування\n+5% Висмоктування життя\nMobility and Tenacity.",
        [3175] = "🏷️ Stormsurge\n\n💰 Вартість: 2900 Gold\n\n+90 Сила вмінь\n+10 Магічне проникнення\n+5% Швидкість пересування\nПасивно: Squall викликає грозовий удар після вибухової шкоди.",
        [3118] = "🏷️ Malignance\n\n💰 Вартість: 2700 Gold\n\n+80 Сила вмінь\n+25 Прискорення вмінь\n+600 Мана\nПасивно: Hatefog підпалює землю під ультимейтом.",
        [3363] = "🏷️ Farsight Alteration\n\n💰 Вартість: 0 Gold\n\nАктивно: встановлює тотем на великій дистанції.",
        [3364] = "🏷️ Oracle Lens\n\n💰 Вартість: 0 Gold\n\nАктивно: сканує місцевість навколо на наявність ворожих вардів і пасток.",
        [4645] = "🏷️ Shadowflame\n\n💰 Вартість: 3200 Gold\n\n+115 Сила вмінь\n+12 Магічне проникнення\nПасивно: Cinderbloom завдає критичної шкоди ворогам з низьким рівнем здоров'я.",
        [6333] = "🏷️ Sundered Sky\n\n💰 Вартість: 3100 Gold\n\n+45 Сила атаки\n+450 Здоров'я\n+15 Прискорення вмінь\nПасивно: Lightshield Strike наносить гарантований критичний удар і зцілює.",
        [6653] = "🏷️ Liandry's Torment\n\n💰 Вартість: 3000 Gold\n\n+90 Сила вмінь\n+300 Здоров'я\nПасивно: підпалює ворогів відсотковою шкодою від макс. HP.",
        [6655] = "🏷️ Luden's Companion\n\n💰 Вартість: 2900 Gold\n\n+90 Сила вмінь\n+20 Прискорення вмінь\n+600 Мана\nПасивно: накопичує заряди та вистрілює снарядами при попаданні вмінням.",
        [6670] = "🏷️ Noonquiver\n\n💰 Вартість: 1300 Gold\n\n+20 Сила атаки\n+20% Швидкість атаки\nКомпонент предметів для стрільців.",
        [6672] = "🏷️ Kraken Slayer\n\n💰 Вартість: 3100 Gold\n\n+50 Сила атаки\n+40% Швидкість атаки\n+7% Швидкість пересування\nПасивно: кожна третя автоатака завдає великої додаткової шкоди.",
        [6673] = "🏷️ Immortal Shieldbow\n\n💰 Вартість: 3000 Gold\n\n+55 Сила атаки\n+25% Шанс критичного удару\nПасивно: Lifeline дає щит при падінні здоров'я нижче 30%.",
        [6676] = "🏷️ The Collector\n\n💰 Вартість: 3200 Gold\n\n+60 Сила атаки\n+25% Шанс критичного удару\n+12 Смертоносність\nПасивно: страчує ворогів із менш ніж 5% здоров'я.",
        [6698] = "🏷️ Profane Hydra\n\n💰 Вартість: 3300 Gold\n\n+60 Сила атаки\n+18 Смертоносність\n+20 Прискорення вмінь\nАктивно: розсікаючий удар по області навколо.",
        [6699] = "🏷️ Voltaic Cyclosword\n\n💰 Вартість: 2900 Gold\n\n+55 Сила атаки\n+18 Смертоносність\n+15 Прискорення вмінь\nПасивно: заряджена автоатака сповільнює на 99%.",
        [6696] = "🏷️ Opportunity\n\n💰 Вартість: 2700 Gold\n\n+55 Сила атаки\n+18 Смертоносність\n+4% Швидкість пересування\nПасивно: збільшує летальність на початку бою.",
        [3340] = "🏷️ Stealth Ward\n\n💰 Вартість: 0 Gold\n\nАктивно: встановлює невидимий тотем огляду на 90-120 секунд."
    };
}

public class PredictionResult
{
    public double BlueWinProbability { get; set; }
    public double RedWinProbability { get; set; }
    public TeamSide PredictedWinner { get; set; }
    public double ConfidenceScore { get; set; }
    public List<string> KeyFactors { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string FormattedBluePercent => $"{Math.Round(BlueWinProbability * 100, 1)}%";
    public string FormattedRedPercent => $"{Math.Round(RedWinProbability * 100, 1)}%";
}

public class MatchInputFeatures
{
    public bool BlueFirstBlood { get; set; }
    public bool BlueFirstTower { get; set; }
    public bool BlueFirstDragon { get; set; }
    public int GoldDiffAt15 { get; set; }
    public int KillDiffAt15 { get; set; }
    public float BlueTeamAvgWinRate { get; set; } = 50.0f;
    public float RedTeamAvgWinRate { get; set; } = 50.0f;
    public int BlueDragonCount { get; set; }
    public int RedDragonCount { get; set; }
    public int BlueTowerCount { get; set; }
    public int RedTowerCount { get; set; }
    public int CsDiffAt15 { get; set; }
    public int XpDiffAt15 { get; set; }
    public int BlueVoidgrubs { get; set; }
    public int RedVoidgrubs { get; set; }
    public int VoidgrubDiff => BlueVoidgrubs - RedVoidgrubs;
    public int BlueHeralds { get; set; }
    public int RedHeralds { get; set; }
    public int HeraldDiff => BlueHeralds - RedHeralds;

    // Dragon Soul & Team Scaling Features
    public DragonSoulType SoulType { get; set; } = DragonSoulType.None;
    public TeamSide SoulOwner { get; set; } = TeamSide.Blue;
    public bool HasDragonSoul => SoulType != DragonSoulType.None && (BlueDragonCount >= 4 || RedDragonCount >= 4);

    public double BlueScalingAdvantage { get; set; } // Positive = Blue scales better in late game, Negative = Red scales better
}

