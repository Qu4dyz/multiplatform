namespace GameAnalytics.Core.Helpers;

/// <summary>Maps Riot dragon subtypes to relative mid-game value for ML features.</summary>
public static class DragonValueHelper
{
    public const int FifteenMinMs = 15 * 60 * 1000;

    /// <summary>
    /// Relative strength of the first dragon type (soul path). Values are relative to Mountain = 1.0.
    /// </summary>
    public static float ValueForSubType(string? monsterSubType)
    {
        if (string.IsNullOrWhiteSpace(monsterSubType)) return 1.0f;
        var key = monsterSubType.Trim().ToUpperInvariant();
        return key switch
        {
            "FIRE_DRAGON" or "INFERNAL_DRAGON" => 1.25f,
            "HEXTECH_DRAGON" => 1.15f,
            "CHEMTECH_DRAGON" => 1.10f,
            "EARTH_DRAGON" or "MOUNTAIN_DRAGON" => 1.00f,
            "WATER_DRAGON" or "OCEAN_DRAGON" => 0.95f,
            "AIR_DRAGON" or "CLOUD_DRAGON" => 0.90f,
            "ELDER_DRAGON" => 1.40f, // rare before 15', but keep honest
            _ => 1.0f
        };
    }

    /// <summary>
    /// Earlier objective ⇒ larger magnitude. Blue success is positive, red is negative.
    /// Returns 0 if the event did not happen by the deadline.
    /// </summary>
    public static float SignedTempo(bool blueGotIt, int timeMs, int deadlineMs = FifteenMinMs)
    {
        if (timeMs <= 0 || timeMs > deadlineMs) return 0f;
        var minutesLeft = (deadlineMs - timeMs) / 60_000f;
        return blueGotIt ? minutesLeft : -minutesLeft;
    }

    /// <summary>Blue first-dragon value, or negative if red took it.</summary>
    public static float SignedDragonValue(bool blueGotIt, string? subType, int timeMs, int deadlineMs = FifteenMinMs)
    {
        if (timeMs <= 0 || timeMs > deadlineMs) return 0f;
        var v = ValueForSubType(subType);
        return blueGotIt ? v : -v;
    }
}
