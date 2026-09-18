using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

public static class LpCalculationHelper
{
    public static int CalculateTotalLp(GameTier tier, string? rank, int lp)
    {
        var tierBase = tier switch
        {
            GameTier.Iron => 0,
            GameTier.Bronze => 400,
            GameTier.Silver => 800,
            GameTier.Gold => 1200,
            GameTier.Platinum => 1600,
            GameTier.Emerald => 2000,
            GameTier.Diamond => 2400,
            GameTier.Master or GameTier.Grandmaster or GameTier.Challenger => 2800,
            _ => 1200
        };

        var rankOffset = (rank?.Trim().ToUpperInvariant()) switch
        {
            "IV" or "4" => 0,
            "III" or "3" => 100,
            "II" or "2" => 200,
            "I" or "1" => 300,
            _ => 0
        };

        return tierBase + rankOffset + lp;
    }
}
