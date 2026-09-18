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
        [6672] = "🏷️ Kraken Slayer\n\n💰 Вартість: 3100 Gold\n\n+50 Сила атаки\n+40% Швидкість атаки\n+7% Швидкість пересування\nПасивно: кожна третя автоатака завдає великої додаткової шкоди.",
        [6676] = "🏷️ The Collector\n\n💰 Вартість: 3200 Gold\n\n+60 Сила атаки\n+25% Шанс критичного удару\n+12 Смертоносність\nПасивно: страчує ворогів із менш ніж 5% здоров'я.",
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
}

