namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Lightweight draft tags used as ML features (engage / tank / AD vs AP balance).
/// Scores are team-count diffs — available at draft time, no end-game leakage.
/// </summary>
public static class ChampionCompositionHelper
{
    private static readonly HashSet<string> Engage =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Malphite", "Ornn", "Amumu", "Sejuani", "Leona", "Nautilus", "Alistar", "Rakan",
            "Zac", "JarvanIV", "Wukong", "MonkeyKing", "Nunu", "Maokai", "Rell", "Sett",
            "Vi", "Hecarim", "Nocturne", "RekSai", "Rek'Sai", "Skarner", "Sion", "Galio",
            "Poppy", "Braum", "Blitzcrank", "Thresh", "Pyke", "Neeko", "Orianna", "Taliyah"
        };

    private static readonly HashSet<string> Tanks =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Ornn", "Sion", "Malphite", "ChoGath", "Cho'Gath", "Maokai", "Shen", "DrMundo",
            "Dr. Mundo", "Nautilus", "Leona", "Rammus", "Zac", "Sejuani", "Poppy", "KSante",
            "K'Sante", "TahmKench", "Tahm Kench", "Galio", "Braum", "Rell", "Amumu", "Nunu"
        };

    private static readonly HashSet<string> AdCarries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Jinx", "KaiSa", "Kai'Sa", "Ashe", "Jhin", "Ezreal", "Caitlyn", "Lucian", "Vayne",
            "Twitch", "Sivir", "Xayah", "Aphelios", "Smolder", "Zeri", "Draven", "Varus",
            "MissFortune", "Miss Fortune", "Kalista", "Samira", "Nilah", "KogMaw", "Kog'Maw",
            "Tristana", "Yunara"
        };

    private static readonly HashSet<string> ApCarries =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Ahri", "Syndra", "Orianna", "Viktor", "Veigar", "Annie", "Lux", "Ziggs", "Xerath",
            "Velkoz", "Vel'Koz", "Brand", "Swain", "Cassiopeia", "Anivia", "AurelionSol",
            "Aurelion Sol", "Hwei", "Azir", "Ryze", "Kassadin", "Vladimir", "Karma", "Seraphine",
            "Neeko", "Taliyah", "Malzahar", "Heimerdinger", "Zyra", "Lillia", "Karthus"
        };

    public static (float EngageDiff, float TankDiff, float AdApBalanceDiff) ComputeDiffs(
        IReadOnlyList<string> blueChamps,
        IReadOnlyList<string> redChamps)
    {
        float BlueScore(HashSet<string> set) => blueChamps.Count(c => Matches(c, set));
        float RedScore(HashSet<string> set) => redChamps.Count(c => Matches(c, set));

        var engageDiff = BlueScore(Engage) - RedScore(Engage);
        var tankDiff = BlueScore(Tanks) - RedScore(Tanks);
        var blueAdAp = BlueScore(AdCarries) - BlueScore(ApCarries);
        var redAdAp = RedScore(AdCarries) - RedScore(ApCarries);
        // Positive = blue more AD-skewed relative to red
        var adApBalanceDiff = blueAdAp - redAdAp;

        return (engageDiff, tankDiff, adApBalanceDiff);
    }

    private static bool Matches(string championName, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(championName)) return false;
        if (set.Contains(championName)) return true;
        var key = ChampionNameHelper.NormalizeForDataDragon(championName);
        return set.Contains(key);
    }
}
