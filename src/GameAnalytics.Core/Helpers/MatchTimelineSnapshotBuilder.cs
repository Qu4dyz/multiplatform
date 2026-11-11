using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

/// <summary>
/// Builds honest at-horizon snapshots from cached MATCH-V5 frames + events.
/// Never uses a frame or event after the horizon, and never falls back to end-of-game totals.
/// </summary>
public static class MatchTimelineSnapshotBuilder
{
    /// <summary>
    /// Closest available frame at or before <paramref name="minute"/> (no forward peek, no last-frame fallback).
    /// Returns null when no such frame exists.
    /// </summary>
    public static TimelineFrameSnapshot? SelectFrameAtOrBefore(MatchTimelineData timeline, int minute)
    {
        if (timeline.Frames.Count == 0 || minute <= 0) return null;
        var horizonMs = minute * 60_000;
        return timeline.Frames
            .Where(f => f.TimestampMs <= horizonMs)
            .OrderByDescending(f => f.TimestampMs)
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds a phase snapshot at ~15 / ~25 / ~35 (or any positive minute).
    /// Returns null when there is no frame ≤ horizon (do not invent / pad).
    /// </summary>
    public static TimelineHorizonSnapshot? Build(MatchTimelineData timeline, int minute, Match? match = null)
    {
        if (minute <= 0) return null;
        var frame = SelectFrameAtOrBefore(timeline, minute);
        if (frame == null || frame.Participants.Count == 0) return null;

        var horizonMs = minute * 60_000;
        var priorMinute = Math.Max(5, minute - 5);
        var priorMs = priorMinute * 60_000;

        var snap = new TimelineHorizonSnapshot
        {
            AnchorMinute = minute,
            PriorMinute = priorMinute,
            FrameTimestampMs = frame.TimestampMs
        };

        SumFrameResources(frame, snap);

        // Events strictly ≤ horizon — never TeamStats end-game TowerKills / DragonKills.
        foreach (var ev in timeline.RealEvents)
        {
            if (ev.TimestampMs > horizonMs) continue;
            ApplyEvent(ev, snap, horizonMs);
        }

        // Older caches may omit WARD_PLACED / TURRET_PLATE from RealEvents.
        // Plates finish by ~14'; reuse frozen 15' plate counts when horizon ≥ 15 and event plates are empty.
        if (minute >= 15 && snap.PlatesBlue == 0 && snap.PlatesRed == 0
            && (timeline.PlatesAt15Blue != 0 || timeline.PlatesAt15Red != 0))
        {
            snap.PlatesBlue = timeline.PlatesAt15Blue;
            snap.PlatesRed = timeline.PlatesAt15Red;
        }

        if (snap.VisionWardsBlue == 0 && snap.VisionWardsRed == 0
            && snap.ControlWardsBlue == 0 && snap.ControlWardsRed == 0
            && minute >= 15)
        {
            snap.VisionWardsBlue = timeline.VisionWardsAt15Blue;
            snap.VisionWardsRed = timeline.VisionWardsAt15Red;
            snap.ControlWardsBlue = timeline.ControlWardsAt15Blue;
            snap.ControlWardsRed = timeline.ControlWardsAt15Red;
        }

        // If first-obj flags never appeared in RealEvents (empty), fall back to timeline 15' flags when ≤ horizon.
        if (!HasAnyFirstObjective(snap) && minute >= 15)
        {
            snap.BlueFirstBlood = timeline.BlueFirstBlood;
            snap.BlueFirstTower = timeline.BlueFirstTower;
            snap.BlueFirstDragon = timeline.BlueFirstDragon;
            snap.FirstBloodTimeMs = timeline.FirstBloodTimeMs;
            snap.FirstTowerTimeMs = timeline.FirstTowerTimeMs;
            snap.FirstDragonTimeMs = timeline.FirstDragonTimeMs;
            snap.FirstDragonSubType = timeline.FirstDragonSubType;
        }

        // Prior anchor (H−5) for momentum — also frame ≤ prior only.
        var priorFrame = SelectFrameAtOrBefore(timeline, priorMinute);
        if (priorFrame != null && priorFrame.Participants.Count > 0)
        {
            var prior = new TimelineHorizonSnapshot();
            SumFrameResources(priorFrame, prior);
            foreach (var ev in timeline.RealEvents)
            {
                if (ev.TimestampMs > priorMs) continue;
                ApplyKillOnly(ev, prior);
            }

            snap.GoldDiffPrior = prior.GoldDiff;
            snap.KillDiffPrior = prior.KillDiff;
            snap.CsDiffPrior = prior.CsDiff;
            snap.XpDiffPrior = prior.XpDiff;
        }

        snap.CarryGoldDiff = CarryGoldDiff(frame);
        if (match != null)
            ApplyLaneGoldDiffs(match, frame, snap);

        return snap;
    }

    private static bool HasAnyFirstObjective(TimelineHorizonSnapshot snap) =>
        snap.FirstBloodTimeMs > 0 || snap.FirstTowerTimeMs > 0 || snap.FirstDragonTimeMs > 0
        || snap.BlueFirstBlood || snap.BlueFirstTower || snap.BlueFirstDragon
        || snap.KillsBlue > 0 || snap.KillsRed > 0;

    private static void SumFrameResources(TimelineFrameSnapshot frame, TimelineHorizonSnapshot snap)
    {
        foreach (var p in frame.Participants)
        {
            var isBlue = p.ParticipantId <= 5;
            if (isBlue)
            {
                snap.GoldBlue += p.TotalGold;
                snap.CsBlue += p.Cs;
                snap.XpBlue += p.Xp;
            }
            else
            {
                snap.GoldRed += p.TotalGold;
                snap.CsRed += p.Cs;
                snap.XpRed += p.Xp;
            }
        }
    }

    private static void ApplyEvent(TimelineEventRecord ev, TimelineHorizonSnapshot snap, int horizonMs)
    {
        switch (ev.EventType)
        {
            case "CHAMPION_KILL":
                ApplyKillOnly(ev, snap);
                if (ev.KillerId > 0 && snap.FirstBloodTimeMs == 0)
                {
                    snap.BlueFirstBlood = ev.KillerId <= 5;
                    snap.FirstBloodTimeMs = ev.TimestampMs;
                }
                break;

            case "ELITE_MONSTER_KILL":
            {
                var isBlue = ev.KillerTeamId == 100 || (ev.KillerTeamId == 0 && ev.KillerId > 0 && ev.KillerId <= 5);
                if (ev.MonsterType.Equals("DRAGON", StringComparison.OrdinalIgnoreCase))
                {
                    if (snap.FirstDragonTimeMs == 0)
                    {
                        snap.BlueFirstDragon = isBlue;
                        snap.FirstDragonTimeMs = ev.TimestampMs;
                        snap.FirstDragonSubType = ev.MonsterSubType;
                    }
                    if (isBlue) snap.DragonsBlue++;
                    else snap.DragonsRed++;
                }
                else if (ev.MonsterType.Equals("HORDE", StringComparison.OrdinalIgnoreCase))
                {
                    if (isBlue) snap.VoidgrubsBlue++;
                    else snap.VoidgrubsRed++;
                }
                else if (ev.MonsterType.Equals("RIFTHERALD", StringComparison.OrdinalIgnoreCase))
                {
                    if (isBlue) snap.HeraldsBlue++;
                    else snap.HeraldsRed++;
                }
                break;
            }

            case "BUILDING_KILL":
                if (ev.BuildingType.Contains("TOWER", StringComparison.OrdinalIgnoreCase))
                {
                    // KillerTeamId stores Riot teamId of the destroyed tower's owner when available:
                    // 200 ⇒ red tower fell ⇒ blue; 100 ⇒ blue tower fell ⇒ red.
                    bool blueTook;
                    if (ev.KillerTeamId == 200 || ev.KillerTeamId == 100)
                        blueTook = ev.KillerTeamId == 200;
                    else if (ev.KillerId > 0)
                        blueTook = ev.KillerId <= 5;
                    else
                        break; // cannot attribute without inventing

                    if (snap.FirstTowerTimeMs == 0)
                    {
                        snap.BlueFirstTower = blueTook;
                        snap.FirstTowerTimeMs = ev.TimestampMs;
                    }
                    if (blueTook) snap.TowersBlue++;
                    else snap.TowersRed++;
                }
                break;

            case "TURRET_PLATE_DESTROYED":
            {
                bool bluePlate;
                if (ev.KillerTeamId == 200 || ev.KillerTeamId == 100)
                    bluePlate = ev.KillerTeamId == 200; // owner side lost the plate
                else if (ev.KillerId > 0)
                    bluePlate = ev.KillerId <= 5;
                else
                    break;
                if (bluePlate) snap.PlatesBlue++;
                else snap.PlatesRed++;
                break;
            }

            case "WARD_PLACED":
            {
                // Creator id is stored in KillerId; ward type in MonsterSubType.
                var creator = ev.KillerId;
                if (creator <= 0) break;
                var isBlue = creator <= 5;
                var wardType = ev.MonsterSubType ?? string.Empty;
                if (string.IsNullOrWhiteSpace(wardType)
                    || wardType.Equals("UNDEFINED", StringComparison.OrdinalIgnoreCase))
                    break;
                var isControl = wardType.Contains("CONTROL", StringComparison.OrdinalIgnoreCase);
                if (isControl)
                {
                    if (isBlue) snap.ControlWardsBlue++;
                    else snap.ControlWardsRed++;
                }
                else
                {
                    if (isBlue) snap.VisionWardsBlue++;
                    else snap.VisionWardsRed++;
                }
                break;
            }
        }
    }

    private static void ApplyKillOnly(TimelineEventRecord ev, TimelineHorizonSnapshot snap)
    {
        if (!ev.EventType.Equals("CHAMPION_KILL", StringComparison.OrdinalIgnoreCase)) return;
        if (ev.KillerId > 0)
        {
            if (ev.KillerId <= 5) snap.KillsBlue++;
            else snap.KillsRed++;
        }
        if (ev.VictimId > 0)
        {
            if (ev.VictimId <= 5) snap.DeathsBlue++;
            else snap.DeathsRed++;
        }
    }

    private static float CarryGoldDiff(TimelineFrameSnapshot frame)
    {
        var blueMax = 0;
        var redMax = 0;
        foreach (var p in frame.Participants)
        {
            if (p.ParticipantId <= 5) blueMax = Math.Max(blueMax, p.TotalGold);
            else redMax = Math.Max(redMax, p.TotalGold);
        }
        return blueMax - redMax;
    }

    private static void ApplyLaneGoldDiffs(Match match, TimelineFrameSnapshot frame, TimelineHorizonSnapshot snap)
    {
        if (match.Participants.Count == 0) return;
        var goldByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.TotalGold);
        var levelByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.Level);

        float RoleGold(TeamSide side, Position pos)
        {
            var players = match.Participants.Where(p => p.TeamSide == side && p.Position == pos).ToList();
            if (players.Count == 0) return 0;
            return (float)players.Average(p =>
                goldByPid.TryGetValue(MatchTimelineApplier.ResolveParticipantId(match, p), out var g) ? g : 0);
        }

        float SideLevels(TeamSide side) =>
            match.Participants.Where(p => p.TeamSide == side)
                .Sum(p => levelByPid.TryGetValue(MatchTimelineApplier.ResolveParticipantId(match, p), out var lvl) ? lvl : 0);

        snap.TopGoldDiff = RoleGold(TeamSide.Blue, Position.Top) - RoleGold(TeamSide.Red, Position.Top);
        snap.JungleGoldDiff = RoleGold(TeamSide.Blue, Position.Jungle) - RoleGold(TeamSide.Red, Position.Jungle);
        snap.MidGoldDiff = RoleGold(TeamSide.Blue, Position.Middle) - RoleGold(TeamSide.Red, Position.Middle);
        snap.BotDuoGoldDiff =
            (RoleGold(TeamSide.Blue, Position.Bottom) + RoleGold(TeamSide.Blue, Position.Utility))
            - (RoleGold(TeamSide.Red, Position.Bottom) + RoleGold(TeamSide.Red, Position.Utility));
        snap.LevelDiff = SideLevels(TeamSide.Blue) - SideLevels(TeamSide.Red);
    }
}
