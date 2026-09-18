namespace GameAnalytics.Core.Helpers;

public static class ChampionNameHelper
{
    private static readonly Dictionary<string, string> InternalToDisplay = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MonkeyKing"] = "Wukong",
        ["Nunu"] = "Nunu & Willump",
        ["NunuWillump"] = "Nunu & Willump",
        ["Renata"] = "Renata Glasc",
        ["RenataGlasc"] = "Renata Glasc",
        ["FiddleSticks"] = "Fiddlesticks",
        ["DrMundo"] = "Dr. Mundo",
        ["JarvanIV"] = "Jarvan IV",
        ["MasterYi"] = "Master Yi",
        ["MissFortune"] = "Miss Fortune",
        ["TahmKench"] = "Tahm Kench",
        ["TwistedFate"] = "Twisted Fate",
        ["XinZhao"] = "Xin Zhao",
        ["AurelionSol"] = "Aurelion Sol",
        ["LeeSin"] = "Lee Sin",
        ["KSante"] = "K'Sante",
        ["KogMaw"] = "Kog'Maw",
        ["RekSai"] = "Rek'Sai",
        ["Chogath"] = "Cho'Gath",
        ["ChoGath"] = "Cho'Gath",
        ["Kaisa"] = "Kai'Sa",
        ["KaiSa"] = "Kai'Sa",
        ["Khazix"] = "Kha'Zix",
        ["KhaZix"] = "Kha'Zix",
        ["Velkoz"] = "Vel'Koz",
        ["VelKoz"] = "Vel'Koz",
        ["Belveth"] = "Bel'Veth",
        ["BelVeth"] = "Bel'Veth",
        ["Leblanc"] = "LeBlanc",
        ["LeBlanc"] = "LeBlanc"
    };

    private static readonly Dictionary<string, string> CanonicalDDragonKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wukong"] = "MonkeyKing",
        ["monkeyking"] = "MonkeyKing",
        ["renata"] = "Renata",
        ["renataglasc"] = "Renata",
        ["nunu"] = "Nunu",
        ["nunu&willump"] = "Nunu",
        ["nunuwillump"] = "Nunu",
        ["kaisa"] = "Kaisa",
        ["khazix"] = "Khazix",
        ["chogath"] = "Chogath",
        ["leblanc"] = "Leblanc",
        ["belveth"] = "Belveth",
        ["velkoz"] = "Velkoz",
        ["fiddlesticks"] = "Fiddlesticks",
        ["drmundo"] = "DrMundo",
        ["jarvaniv"] = "JarvanIV",
        ["masteryi"] = "MasterYi",
        ["missfortune"] = "MissFortune",
        ["tahmkench"] = "TahmKench",
        ["twistedfate"] = "TwistedFate",
        ["xinzhao"] = "XinZhao",
        ["aurelionsol"] = "AurelionSol",
        ["leesin"] = "LeeSin",
        ["ksante"] = "KSante",
        ["kogmaw"] = "KogMaw",
        ["reksai"] = "RekSai"
    };

    /// <summary>
    /// Returns the human-readable champion display name (e.g. "Wukong", "Nunu & Willump", "Renata Glasc").
    /// </summary>
    public static string ToDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var trimmed = name.Trim();
        if (InternalToDisplay.TryGetValue(trimmed, out var displayName))
        {
            return displayName;
        }

        // Check without spaces / punctuation
        var clean = trimmed.Replace(" ", "").Replace("'", "").Replace(".", "");
        if (InternalToDisplay.TryGetValue(clean, out var cleanDisplay))
        {
            return cleanDisplay;
        }

        return trimmed;
    }

    /// <summary>
    /// Returns the canonical DataDragon image file key (e.g. "MonkeyKing", "Nunu", "Renata").
    /// </summary>
    public static string ToDDragonImageKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        var clean = name.Replace(" ", "").Replace("'", "").Replace(".", "").Trim();
        if (CanonicalDDragonKeys.TryGetValue(clean, out var key))
        {
            return key;
        }

        return clean;
    }

    public static string NormalizeForDataDragon(string? name) => ToDDragonImageKey(name);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> IdToName = new()
    {
        [103] = "Ahri",
        [84] = "Akali",
        [12] = "Alistar",
        [32] = "Amumu",
        [22] = "Ashe",
        [266] = "Aatrox",
        [51] = "Caitlyn",
        [81] = "Ezreal",
        [64] = "Lee Sin",
        [99] = "Lux",
        [222] = "Jinx",
        [157] = "Yasuo",
        [777] = "Yone",
        [238] = "Zed",
        [412] = "Thresh",
        [236] = "Lucian",
        [53] = "Blitzcrank",
        [145] = "Kai'Sa",
        [518] = "Neeko",
        [875] = "Sett"
    };

    public static void RegisterChampion(int id, string name)
    {
        IdToName[id] = name;
    }

    public static string GetChampionNameById(int id)
    {
        if (IdToName.TryGetValue(id, out var name)) return name;
        return $"Champion {id}";
    }
}

