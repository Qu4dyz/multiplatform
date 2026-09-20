using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

public static class GoldCurveBuilder
{
    public sealed record GoldCurvePoint(
        int Minute,
        int PlayerGold,
        int AllyTeamGold,
        int EnemyTeamGold,
        int GoldDiff);

    /// <summary>
    /// Builds per-minute gold samples from timeline frames (last frame in each minute bucket).
    /// Participant IDs 1–5 = blue, 6–10 = red (Riot convention).
    /// </summary>
    public static IReadOnlyList<GoldCurvePoint> Build(
        MatchTimelineData timeline,
        int playerParticipantId,
        TeamSide playerSide)
    {
        if (timeline.Frames.Count == 0)
            return Array.Empty<GoldCurvePoint>();

        var byMinute = new Dictionary<int, TimelineFrameSnapshot>();
        foreach (var frame in timeline.Frames.OrderBy(f => f.TimestampMs))
        {
            var minute = Math.Max(0, frame.TimestampMs / 60_000);
            byMinute[minute] = frame;
        }

        var points = new List<GoldCurvePoint>(byMinute.Count);
        foreach (var minute in byMinute.Keys.OrderBy(m => m))
        {
            var frame = byMinute[minute];
            int blueGold = 0, redGold = 0, playerGold = 0;
            foreach (var p in frame.Participants)
            {
                if (p.ParticipantId >= 1 && p.ParticipantId <= 5)
                    blueGold += p.TotalGold;
                else if (p.ParticipantId >= 6 && p.ParticipantId <= 10)
                    redGold += p.TotalGold;

                if (p.ParticipantId == playerParticipantId)
                    playerGold = p.TotalGold;
            }

            var ally = playerSide == TeamSide.Blue ? blueGold : redGold;
            var enemy = playerSide == TeamSide.Blue ? redGold : blueGold;
            points.Add(new GoldCurvePoint(minute, playerGold, ally, enemy, ally - enemy));
        }

        return points;
    }
}
