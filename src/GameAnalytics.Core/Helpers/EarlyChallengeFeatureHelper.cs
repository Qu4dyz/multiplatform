using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Safe-at-15' challenge-derived features for the win model.
/// Only uses Riot challenge keys that are defined as early-game windows
/// (before 10' / plate phase) — never end-game rates (GPM, KP, KDA, baron, …).
/// </summary>
public static class EarlyChallengeFeatureHelper
{
    /// <summary>
    /// Team diffs from early-window challenges. Returns zeros when
    /// <see cref="Match.HasChallenges"/> is false (do not impute).
    /// </summary>
    public static EarlyChallengeFeatures Extract(Match match)
    {
        if (!match.HasChallenges || match.Participants.Count == 0)
            return EarlyChallengeFeatures.Empty;

        float blueJungle = 0, redJungle = 0;
        float blueLane = 0, redLane = 0;
        float blueTakedowns = 0, redTakedowns = 0;
        float bluePlates = 0, redPlates = 0;
        var anySignal = false;

        foreach (var p in match.Participants)
        {
            var isBlue = p.TeamSide == TeamSide.Blue;
            if (p.JungleCsBefore10Minutes > 0 || p.LaneMinionsFirst10Minutes > 0
                || p.TakedownsFirstXMinutes > 0 || p.TurretPlatesTaken > 0)
                anySignal = true;

            if (isBlue)
            {
                blueJungle += p.JungleCsBefore10Minutes;
                blueLane += p.LaneMinionsFirst10Minutes;
                blueTakedowns += p.TakedownsFirstXMinutes;
                bluePlates += p.TurretPlatesTaken;
            }
            else
            {
                redJungle += p.JungleCsBefore10Minutes;
                redLane += p.LaneMinionsFirst10Minutes;
                redTakedowns += p.TakedownsFirstXMinutes;
                redPlates += p.TurretPlatesTaken;
            }
        }

        if (!anySignal)
            return EarlyChallengeFeatures.Empty;

        return new EarlyChallengeFeatures
        {
            HasEarlyChallenges = 1f,
            ChallengeJungleCs10Diff = blueJungle - redJungle,
            ChallengeLaneCs10Diff = blueLane - redLane,
            EarlyTakedownDiff = blueTakedowns - redTakedowns,
            ChallengePlatesDiff = bluePlates - redPlates
        };
    }

    /// <summary>
    /// Signed progress toward dragon soul from @15 dragon counts only.
    /// Soul itself almost never lands by 15' — never use end-game soul ownership.
    /// </summary>
    public static float DragonSoulRace(int blueDragonsAt15, int redDragonsAt15)
        => (blueDragonsAt15 - redDragonsAt15) / 4f;

    /// <summary>
    /// Side / first-objective pressure: how many of FB / FT / FD blue claimed (0–3),
    /// centered at 0 so red leads go negative.
    /// </summary>
    public static float SideObjectiveLead(bool blueFirstBlood, bool blueFirstTower, bool blueFirstDragon)
    {
        var blue = (blueFirstBlood ? 1 : 0) + (blueFirstTower ? 1 : 0) + (blueFirstDragon ? 1 : 0);
        return blue - 1.5f; // 0 claims → -1.5, all three → +1.5
    }
}

public readonly struct EarlyChallengeFeatures
{
    public static EarlyChallengeFeatures Empty => default;

    public float HasEarlyChallenges { get; init; }
    public float ChallengeJungleCs10Diff { get; init; }
    public float ChallengeLaneCs10Diff { get; init; }
    public float EarlyTakedownDiff { get; init; }
    public float ChallengePlatesDiff { get; init; }
}
