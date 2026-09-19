namespace GameAnalytics.Core.Helpers;

public static class SpellRuneHelper
{
    private static readonly Dictionary<int, string> SpellIdToName = new()
    {
        [1] = "SummonerBoost",        // Cleanse
        [3] = "SummonerExhaust",      // Exhaust
        [4] = "SummonerFlash",        // Flash
        [6] = "SummonerHaste",        // Ghost
        [7] = "SummonerHeal",         // Heal
        [11] = "SummonerSmite",       // Smite
        [12] = "SummonerTeleport",    // Teleport
        [13] = "SummonerMana",        // Clarity
        [14] = "SummonerDot",         // Ignite
        [21] = "SummonerBarrier",     // Barrier
        [32] = "SummonerSnowball",    // Snowball / Mark (ARAM)
        [39] = "SummonerSnowURFSnowball_Mark"
    };

    private static readonly Dictionary<int, string> RuneIdToPath = new()
    {
        // Precision (8000)
        [8005] = "perk-images/Styles/Precision/PressTheAttack/PressTheAttack.png",
        [8008] = "perk-images/Styles/Precision/LethalTempo/LethalTempoTemp.png",
        [8010] = "perk-images/Styles/Precision/Conqueror/Conqueror.png",
        [8021] = "perk-images/Styles/Precision/FleetFootwork/FleetFootwork.png",

        // Domination (8100)
        [8112] = "perk-images/Styles/Domination/Electrocute/Electrocute.png",
        [8124] = "perk-images/Styles/Domination/Predator/Predator.png",
        [8128] = "perk-images/Styles/Domination/DarkHarvest/DarkHarvest.png",
        [9923] = "perk-images/Styles/Domination/HailOfBlades/HailOfBlades.png",

        // Sorcery (8200)
        [8214] = "perk-images/Styles/Sorcery/SummonAery/SummonAery.png",
        [8229] = "perk-images/Styles/Sorcery/ArcaneComet/ArcaneComet.png",
        [8230] = "perk-images/Styles/Sorcery/PhaseRush/PhaseRush.png",

        // Resolve (8400)
        [8437] = "perk-images/Styles/Resolve/GraspOfTheUndying/GraspOfTheUndying.png",
        [8439] = "perk-images/Styles/Resolve/VeteranAftershock/VeteranAftershock.png",
        [8465] = "perk-images/Styles/Resolve/Guardian/Guardian.png",

        // Inspiration (8300)
        [8351] = "perk-images/Styles/Inspiration/GlacialAugment/GlacialAugment.png",
        [8360] = "perk-images/Styles/Inspiration/UnsealedSpellbook/UnsealedSpellbook.png",
        [8369] = "perk-images/Styles/Inspiration/FirstStrike/FirstStrike.png"
    };

    private static readonly Dictionary<int, string> RuneStyleIdToPath = new()
    {
        [8000] = "perk-images/Styles/7201_Precision.png",
        [8100] = "perk-images/Styles/7200_Domination.png",
        [8200] = "perk-images/Styles/7202_Sorcery.png",
        [8300] = "perk-images/Styles/7203_Whimsy.png",
        [8400] = "perk-images/Styles/7204_Resolve.png"
    };

    public static string GetSpellIconUrl(int spellId, string version)
    {
        if (SpellIdToName.TryGetValue(spellId, out var spellName))
        {
            return $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{spellName}.png";
        }

        // Fallback default Flash if unknown
        return spellId > 0 ? $"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/SummonerFlash.png" : string.Empty;
    }

    public static string GetRuneIconUrl(int runeId)
    {
        if (RuneIdToPath.TryGetValue(runeId, out var relPath))
        {
            return $"https://ddragon.leagueoflegends.com/cdn/img/{relPath}";
        }

        // Fallback to Conqueror if unknown keystone
        return runeId > 0 ? "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/Precision/Conqueror/Conqueror.png" : string.Empty;
    }

    public static string GetRuneStyleIconUrl(int styleId)
    {
        if (RuneStyleIdToPath.TryGetValue(styleId, out var relPath))
        {
            return $"https://ddragon.leagueoflegends.com/cdn/img/{relPath}";
        }

        return styleId > 0 ? "https://ddragon.leagueoflegends.com/cdn/img/perk-images/Styles/7201_Precision.png" : string.Empty;
    }

    public static string GetSpellName(int spellId) => spellId switch
    {
        1 => "Cleanse",
        3 => "Exhaust",
        4 => "Flash",
        6 => "Ghost",
        7 => "Heal",
        11 => "Smite",
        12 => "Teleport",
        13 => "Clarity",
        14 => "Ignite",
        21 => "Barrier",
        32 => "Mark",
        _ => "Summoner Spell"
    };

    public static bool IsConquerorChampion(string? champName)
    {
        if (string.IsNullOrWhiteSpace(champName)) return false;
        var clean = champName.Trim().ToLowerInvariant();
        return clean is "aatrox" or "darius" or "garen" or "mordekaiser" or "irelia" or "riven" 
            or "renekton" or "olaf" or "sett" or "jax" or "leesin" or "lee sin" or "diana" 
            or "kayn" or "sylas" or "swain" or "pantheon" or "illaoi" or "fiora" or "briar" or "xinzhao" or "xin zhao";
    }

    public static (int PrimaryRune, int SecondaryStyle, int Spell1, int Spell2) GetRecommendedRunesAndSpells(string? champName, GameAnalytics.Core.Enums.Position pos)
    {
        var clean = champName?.Trim().ToLowerInvariant() ?? string.Empty;

        // Specific champion signatures
        switch (clean)
        {
            case "lux":
                return (8369, 8200, 4, 21); // First Strike + Sorcery, Flash + Barrier
            case "volibear":
                return (8005, 8400, 4, 12); // Press the Attack + Resolve, Flash + Teleport
            case "yone":
            case "yasuo":
                return (8008, 8400, 4, 12); // Lethal Tempo + Resolve, Flash + Teleport
            case "ahri":
                return (8112, 8200, 4, 14); // Electrocute + Sorcery, Flash + Ignite
            case "jinx":
            case "caitlyn":
            case "ashe":
            case "varus":
            case "sivir":
                return (8008, 8300, 4, 7);  // Lethal Tempo + Inspiration, Flash + Heal
            case "ezreal":
                return (8005, 8300, 4, 12); // Press the Attack + Inspiration, Flash + Teleport
            case "vayne":
            case "kaisa":
            case "kai'sa":
                return (8008, 8300, 4, 6);  // Lethal Tempo + Inspiration, Flash + Ghost
            case "kennen":
                return (8230, 8100, 4, 12); // Phase Rush + Domination, Flash + Teleport
            case "fiddlesticks":
                return (8369, 8100, 4, 11); // First Strike + Domination, Flash + Smite
            case "zed":
            case "talon":
            case "leblanc":
            case "akali":
                return (8112, 8200, 4, 14); // Electrocute + Sorcery, Flash + Ignite
            case "syndra":
            case "viktor":
            case "orianna":
            case "hwei":
                return (8369, 8200, 4, 12); // First Strike + Sorcery, Flash + Teleport
            case "malphite":
                return (8229, 8400, 4, 12); // Arcane Comet + Resolve, Flash + Teleport
            case "thresh":
            case "nautilus":
            case "leona":
            case "alistar":
                return (8439, 8300, 4, 14); // Aftershock + Inspiration, Flash + Ignite
            case "blitzcrank":
                return (8351, 8300, 4, 14); // Glacial Augment + Inspiration, Flash + Ignite
            case "lulu":
            case "janna":
            case "soraka":
            case "yuumi":
            case "nami":
                return (8214, 8300, 4, 3);  // Summon Aery + Inspiration, Flash + Exhaust
            case "teemo":
                return (8005, 8400, 4, 14); // Press the Attack + Resolve, Flash + Ignite
            case "leesin":
            case "lee sin":
            case "viego":
            case "jarvaniv":
            case "jarvan iv":
                return (8010, 8300, 4, 11); // Conqueror + Inspiration, Flash + Smite
            case "graves":
            case "kindred":
                return (8005, 8100, 4, 11); // Press the Attack + Domination, Flash + Smite
            case "hecarim":
                return (8230, 8000, 6, 11); // Phase Rush + Precision, Ghost + Smite
        }

        // Role-based fallbacks
        return pos switch
        {
            GameAnalytics.Core.Enums.Position.Jungle => (8010, 8300, 4, 11),
            GameAnalytics.Core.Enums.Position.Bottom => (8008, 8300, 4, 7),
            GameAnalytics.Core.Enums.Position.Utility => (8439, 8300, 4, 14),
            GameAnalytics.Core.Enums.Position.Middle => (8112, 8200, 4, 14),
            GameAnalytics.Core.Enums.Position.Top => (8010, 8400, 4, 12),
            _ => (8005, 8400, 4, 12)
        };
    }
}
