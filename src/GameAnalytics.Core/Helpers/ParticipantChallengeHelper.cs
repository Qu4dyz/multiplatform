using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Match-v5 challenges helpers. Challenges are mostly end-of-game stats —
/// persist for coach/UI, never train the 15' win model on them.
/// </summary>
public static class ParticipantChallengeHelper
{
    public static bool HasChallengeSignal(Participant p)
    {
        if (!string.IsNullOrWhiteSpace(p.ChallengesJson)) return true;
        if (p.KillParticipation > 0) return true;
        if (p.GoldPerMinute > 0) return true;
        if (p.VisionScorePerMinute > 0) return true;
        if (p.DamagePerMinute > 0) return true;
        if (p.TeamDamagePercentage > 0) return true;
        if (p.ControlWardsPlaced > 0) return true;
        if (p.EffectiveHealAndShielding > 0) return true;
        if (p.DamageDealtToObjectivesChallenge > 0) return true;
        if (p.SoloKills > 0) return true;
        if (p.TurretPlatesTaken > 0) return true;
        if (p.JungleCsBefore10Minutes > 0) return true;
        if (p.LaneMinionsFirst10Minutes > 0) return true;
        if (p.SkillshotsDodged > 0 || p.SkillshotsHit > 0) return true;
        if (p.TakedownsFirstXMinutes > 0) return true;
        if (p.EpicMonsterSteals > 0 || p.SoloBaronKills > 0) return true;
        if (p.KdaChallenge > 0) return true;
        return false;
    }

    public static bool MatchHasChallengeData(Match match)
        => match.Participants.Any(HasChallengeSignal);

    public static void RefreshHasChallengesFlag(Match match)
        => match.HasChallenges = MatchHasChallengeData(match);

    /// <summary>
    /// Copies challenge fields from source onto target (timeline / ML flags preserved).
    /// </summary>
    public static void CopyChallengeFields(Participant target, Participant source)
    {
        target.SoloKills = source.SoloKills;
        target.TurretPlatesTaken = source.TurretPlatesTaken;
        target.KillParticipation = source.KillParticipation;
        target.VisionScorePerMinute = source.VisionScorePerMinute;
        target.GoldPerMinute = source.GoldPerMinute;
        target.DamagePerMinute = source.DamagePerMinute;
        target.TeamDamagePercentage = source.TeamDamagePercentage;
        target.ControlWardsPlaced = source.ControlWardsPlaced;
        target.EffectiveHealAndShielding = source.EffectiveHealAndShielding;
        target.DamageDealtToObjectivesChallenge = source.DamageDealtToObjectivesChallenge;
        target.ChallengesJson = source.ChallengesJson ?? string.Empty;
        target.JungleCsBefore10Minutes = source.JungleCsBefore10Minutes;
        target.LaneMinionsFirst10Minutes = source.LaneMinionsFirst10Minutes;
        target.SkillshotsDodged = source.SkillshotsDodged;
        target.SkillshotsHit = source.SkillshotsHit;
        target.TakedownsFirstXMinutes = source.TakedownsFirstXMinutes;
        target.EpicMonsterSteals = source.EpicMonsterSteals;
        target.SoloBaronKills = source.SoloBaronKills;
        target.KdaChallenge = source.KdaChallenge;
    }

    /// <summary>
    /// Merges challenges from a freshly fetched match-v5 payload onto a stored match
    /// without wiping timeline-derived @10/@15 features.
    /// </summary>
    public static bool MergeChallengesFromDetails(Match stored, Match details)
    {
        if (details.Participants.Count == 0) return false;

        var merged = 0;
        foreach (var target in stored.Participants)
        {
            var source = details.Participants.FirstOrDefault(p =>
                target.ParticipantId > 0 && p.ParticipantId == target.ParticipantId)
                ?? details.Participants.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(target.Puuid)
                    && string.Equals(p.Puuid, target.Puuid, StringComparison.OrdinalIgnoreCase));

            if (source == null || !HasChallengeSignal(source)) continue;
            CopyChallengeFields(target, source);
            merged++;
        }

        if (merged == 0) return false;
        RefreshHasChallengesFlag(stored);
        return stored.HasChallenges;
    }
}