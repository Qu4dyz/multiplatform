using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Applies MATCH-V5 timeline snapshots onto Match / TeamStats for ML features and desktop cache.
/// Shared by the VPS collector and MatchAnalyticsService (no ML project dependency).
/// </summary>
public static class MatchTimelineApplier
{
    /// <summary>
    /// Applies a minute-15 timeline snapshot onto team rows.
    /// Intentionally overwrites Voidgrub/Herald with at-15 counts so training never sees end-game leakage.
    /// Also derives per-role gold diffs and level diffs when frames + positions are available.
    /// </summary>
    public static bool ApplyTimelineSnapshot(Match match, MatchTimelineData timeline)
    {
        var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
        var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
        if (blue == null || red == null) return false;

        blue.GoldAt15 = timeline.GoldAt15Blue;
        red.GoldAt15 = timeline.GoldAt15Red;
        blue.KillsAt15 = timeline.KillsAt15Blue;
        red.KillsAt15 = timeline.KillsAt15Red;
        blue.CsAt15 = timeline.CsAt15Blue;
        red.CsAt15 = timeline.CsAt15Red;
        blue.XpAt15 = timeline.XpAt15Blue;
        red.XpAt15 = timeline.XpAt15Red;
        blue.TowersAt15 = timeline.TowersAt15Blue;
        red.TowersAt15 = timeline.TowersAt15Red;
        blue.DragonsAt15 = timeline.DragonsAt15Blue;
        red.DragonsAt15 = timeline.DragonsAt15Red;
        blue.VoidgrubKills = timeline.VoidgrubsAt15Blue;
        red.VoidgrubKills = timeline.VoidgrubsAt15Red;
        blue.RiftHeraldKills = timeline.HeraldsAt15Blue;
        red.RiftHeraldKills = timeline.HeraldsAt15Red;
        blue.FirstBlood = timeline.BlueFirstBlood;
        blue.FirstTower = timeline.BlueFirstTower;
        blue.FirstDragon = timeline.BlueFirstDragon;
        blue.FirstVoidgrub = timeline.VoidgrubsAt15Blue > 0;
        blue.FirstRiftHerald = timeline.HeraldsAt15Blue > 0;
        match.HasMinute15Objectives = true;

        match.GoldDiff10 = timeline.GoldDiffAt10;
        match.KillDiff10 = timeline.KillDiffAt10;
        match.CsDiff10 = timeline.CsDiffAt10;
        match.XpDiff10 = timeline.XpDiffAt10;
        match.HasCsXp10Features = true;

        match.FirstBloodTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstBlood, timeline.FirstBloodTimeMs);
        match.FirstTowerTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstTower, timeline.FirstTowerTimeMs);
        match.FirstDragonTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstDragon, timeline.FirstDragonTimeMs);
        match.FirstDragonValue = DragonValueHelper.SignedDragonValue(
            timeline.BlueFirstDragon, timeline.FirstDragonSubType, timeline.FirstDragonTimeMs);
        if (timeline.CarryGoldDiff15 != 0)
            match.CarryGoldDiff15 = timeline.CarryGoldDiff15;
        match.PlatesDiff15 = timeline.PlatesDiffAt15;
        match.DeathDiff15 = timeline.DeathDiffAt15;
        match.VisionWardDiff15 = timeline.VisionWardDiffAt15;
        match.ControlWardDiff15 = timeline.ControlWardDiffAt15;
        match.HasVisionDeathFeatures = true;

        ApplyLaneAndLevelDiffs(match, timeline);
        return true;
    }

    public static void ApplyLaneAndLevelDiffs(Match match, MatchTimelineData timeline)
    {
        if (timeline.Frames.Count == 0 || match.Participants.Count == 0) return;

        const int fifteenMs = 15 * 60 * 1000;
        var frame = timeline.Frames
            .Where(f => f.TimestampMs <= fifteenMs)
            .OrderByDescending(f => f.TimestampMs)
            .FirstOrDefault()
            ?? timeline.Frames.LastOrDefault();
        if (frame == null || frame.Participants.Count == 0) return;

        var goldByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.TotalGold);
        var levelByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.Level);

        float RoleGold(TeamSide side, Position pos)
        {
            var players = match.Participants.Where(p => p.TeamSide == side && p.Position == pos).ToList();
            if (players.Count == 0) return 0;
            return (float)players.Average(p =>
                goldByPid.TryGetValue(ResolveParticipantId(match, p), out var g) ? g : 0);
        }

        float SideLevels(TeamSide side) =>
            match.Participants.Where(p => p.TeamSide == side)
                .Sum(p => levelByPid.TryGetValue(ResolveParticipantId(match, p), out var lvl) ? lvl : 0);

        match.TopGoldDiff15 = RoleGold(TeamSide.Blue, Position.Top) - RoleGold(TeamSide.Red, Position.Top);
        match.JungleGoldDiff15 = RoleGold(TeamSide.Blue, Position.Jungle) - RoleGold(TeamSide.Red, Position.Jungle);
        match.MidGoldDiff15 = RoleGold(TeamSide.Blue, Position.Middle) - RoleGold(TeamSide.Red, Position.Middle);
        match.BotDuoGoldDiff15 =
            (RoleGold(TeamSide.Blue, Position.Bottom) + RoleGold(TeamSide.Blue, Position.Utility))
            - (RoleGold(TeamSide.Red, Position.Bottom) + RoleGold(TeamSide.Red, Position.Utility));
        match.LevelDiff15 = SideLevels(TeamSide.Blue) - SideLevels(TeamSide.Red);

        float SideMaxGold(TeamSide side) =>
            match.Participants.Where(p => p.TeamSide == side)
                .Select(p => goldByPid.TryGetValue(ResolveParticipantId(match, p), out var g) ? g : 0)
                .DefaultIfEmpty(0)
                .Max();
        if (match.CarryGoldDiff15 == 0)
            match.CarryGoldDiff15 = SideMaxGold(TeamSide.Blue) - SideMaxGold(TeamSide.Red);
    }

    public static int ResolveParticipantId(Match match, Participant p)
    {
        if (p.ParticipantId > 0) return p.ParticipantId;
        var idx = match.Participants.IndexOf(p);
        return idx >= 0 ? idx + 1 : 0;
    }
}
