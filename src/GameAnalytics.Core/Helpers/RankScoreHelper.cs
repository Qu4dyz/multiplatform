using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Maps ranked tiers to a 1–10 continuum so the model can learn that the same
/// gold/kill lead converts differently in Iron vs Challenger lobbies.
/// </summary>
public static class RankScoreHelper
{
    public const float DefaultMixedLobby = 5.5f; // ~Emerald average of a mixed crawl pool

    public static float FromTier(GameTier tier) => tier switch
    {
        GameTier.Iron => 1.0f,
        GameTier.Bronze => 2.0f,
        GameTier.Silver => 3.0f,
        GameTier.Gold => 4.0f,
        GameTier.Platinum => 5.0f,
        GameTier.Emerald => 6.0f,
        GameTier.Diamond => 7.0f,
        GameTier.Master => 8.0f,
        GameTier.Grandmaster => 9.0f,
        GameTier.Challenger => 10.0f,
        _ => DefaultMixedLobby
    };

    public static GameTier FromScore(float score)
    {
        var rounded = (int)Math.Round(Math.Clamp(score, 1.0, 10.0));
        return rounded switch
        {
            1 => GameTier.Iron,
            2 => GameTier.Bronze,
            3 => GameTier.Silver,
            4 => GameTier.Gold,
            5 => GameTier.Platinum,
            6 => GameTier.Emerald,
            7 => GameTier.Diamond,
            8 => GameTier.Master,
            9 => GameTier.Grandmaster,
            10 => GameTier.Challenger,
            _ => GameTier.Emerald
        };
    }
}
