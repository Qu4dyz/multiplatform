using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.ML.Training;

namespace GameAnalytics.Tests;

public class MultiHorizonLeakageTests
{
    [Fact]
    public void SelectFrameAtOrBefore_NeverReturnsPostHorizonFrame()
    {
        var timeline = new MatchTimelineData
        {
            Frames =
            {
                Frame(14 * 60_000, blueGold: 20000, redGold: 19000),
                Frame(25 * 60_000, blueGold: 35000, redGold: 30000),
                Frame(40 * 60_000, blueGold: 60000, redGold: 40000) // end-ish
            }
        };

        var at25 = MatchTimelineSnapshotBuilder.SelectFrameAtOrBefore(timeline, 25);
        Assert.NotNull(at25);
        Assert.Equal(25 * 60_000, at25!.TimestampMs);
        Assert.True(at25.TimestampMs <= 25 * 60_000);

        var at15 = MatchTimelineSnapshotBuilder.SelectFrameAtOrBefore(timeline, 15);
        Assert.NotNull(at15);
        Assert.Equal(14 * 60_000, at15!.TimestampMs);

        // No frame ≤ 10' → null (never LastOrDefault past horizon)
        Assert.Null(MatchTimelineSnapshotBuilder.SelectFrameAtOrBefore(timeline, 10));
    }

    [Fact]
    public void Build_At25_IgnoresPost25DragonsAndTowers()
    {
        var timeline = new MatchTimelineData
        {
            Frames =
            {
                Frame(25 * 60_000, blueGold: 34000, redGold: 31000, blueCs: 400, redCs: 380)
            },
            RealEvents =
            {
                Kill(8 * 60_000, killerId: 1, victimId: 6),
                Dragon(12 * 60_000, blue: true),
                Dragon(22 * 60_000, blue: true),
                Dragon(30 * 60_000, blue: true), // AFTER 25' — must not count
                Tower(18 * 60_000, killerId: 2, ownerTeamId: 200),
                Tower(28 * 60_000, killerId: 3, ownerTeamId: 200) // AFTER 25'
            }
        };

        var snap = MatchTimelineSnapshotBuilder.Build(timeline, PredictionHorizons.Mid);
        Assert.NotNull(snap);
        Assert.Equal(2, snap!.DragonsBlue);
        Assert.Equal(0, snap.DragonsRed);
        Assert.Equal(1, snap.TowersBlue);
        Assert.Equal(0, snap.TowersRed);
        Assert.Equal(1, snap.KillsBlue);
        Assert.True(snap.FrameTimestampMs <= 25 * 60_000);
    }

    [Fact]
    public void Build_At35_DoesNotUseEndGameTeamStats()
    {
        // Even if someone stuffed end-game tower totals onto a Match, snapshot must ignore them.
        var match = new Match
        {
            MatchId = "X",
            GameDurationSeconds = 2400,
            Teams =
            {
                new TeamStats { TeamSide = TeamSide.Blue, TowerKills = 11, DragonKills = 4, BaronKills = 2 },
                new TeamStats { TeamSide = TeamSide.Red, TowerKills = 2, DragonKills = 0, BaronKills = 0 }
            }
        };

        var timeline = new MatchTimelineData
        {
            Frames = { Frame(35 * 60_000, blueGold: 50000, redGold: 45000) },
            RealEvents =
            {
                Tower(20 * 60_000, killerId: 1, ownerTeamId: 200),
                Tower(33 * 60_000, killerId: 2, ownerTeamId: 200),
                Dragon(15 * 60_000, blue: true),
                Dragon(36 * 60_000, blue: true) // after 35'
            }
        };

        var snap = MatchTimelineSnapshotBuilder.Build(timeline, PredictionHorizons.Late, match);
        Assert.NotNull(snap);
        Assert.Equal(2, snap!.TowersBlue);   // not 11
        Assert.Equal(1, snap.DragonsBlue);   // not 4
        Assert.NotEqual(11, snap.TowersBlue);
    }

    [Fact]
    public void ExtractFeatures_At25_SkipsShortGames_AndNeedsTimeline()
    {
        var shortMatch = MakeHorizonMatch("SHORT", durationSec: 20 * 60, goldAt15: 1000);
        var longMatch = MakeHorizonMatch("LONG", durationSec: 40 * 60, goldAt15: 1200);

        var timeline = new MatchTimelineData
        {
            Frames = { Frame(25 * 60_000, blueGold: 36000, redGold: 30000, blueCs: 450, redCs: 400) },
            RealEvents = { Kill(10 * 60_000, 1, 6), Dragon(14 * 60_000, true) }
        };

        MatchTimelineData? Lookup(string id) => id == "LONG" ? timeline : null;

        var rows = RealMatchDatasetCollector.ExtractFeaturesFromMatches(
            new[] { shortMatch, longMatch },
            PredictionHorizons.Mid,
            Lookup);

        Assert.Single(rows);
        Assert.Equal(6000f, rows[0].GoldDiff15); // at-horizon column reuse
        Assert.Equal(1f, rows[0].DragonDiff);
    }

    [Fact]
    public void ExtractFeatures_At25_WithoutLookup_YieldsNoRows()
    {
        var match = MakeHorizonMatch("NOLU", durationSec: 40 * 60, goldAt15: 800);
        var rows = RealMatchDatasetCollector.ExtractFeaturesFromMatches(
            new[] { match }, PredictionHorizons.Mid, timelineLookup: null);
        Assert.Empty(rows);
    }

    [Fact]
    public void EarlyExtract_StillWorks_UnchangedPath()
    {
        var match = MakeHorizonMatch("E15", durationSec: 30 * 60, goldAt15: 1500);
        var rows = RealMatchDatasetCollector.ExtractFeaturesFromMatches(new[] { match });
        Assert.Single(rows);
        Assert.Equal(1500f, rows[0].GoldDiff15);
    }

    private static TimelineFrameSnapshot Frame(
        int tsMs, int blueGold, int redGold, int blueCs = 0, int redCs = 0)
    {
        var f = new TimelineFrameSnapshot { TimestampMs = tsMs };
        for (var i = 1; i <= 5; i++)
            f.Participants.Add(new TimelineParticipantPos
            {
                ParticipantId = i,
                TotalGold = blueGold / 5,
                Cs = blueCs / 5,
                Level = 11
            });
        for (var i = 6; i <= 10; i++)
            f.Participants.Add(new TimelineParticipantPos
            {
                ParticipantId = i,
                TotalGold = redGold / 5,
                Cs = redCs / 5,
                Level = 10
            });
        return f;
    }

    private static TimelineEventRecord Kill(int ts, int killerId, int victimId) => new()
    {
        TimestampMs = ts,
        EventType = "CHAMPION_KILL",
        KillerId = killerId,
        VictimId = victimId
    };

    private static TimelineEventRecord Dragon(int ts, bool blue) => new()
    {
        TimestampMs = ts,
        EventType = "ELITE_MONSTER_KILL",
        MonsterType = "DRAGON",
        MonsterSubType = "FIRE_DRAGON",
        KillerId = blue ? 1 : 6,
        KillerTeamId = blue ? 100 : 200
    };

    private static TimelineEventRecord Tower(int ts, int killerId, int ownerTeamId) => new()
    {
        TimestampMs = ts,
        EventType = "BUILDING_KILL",
        BuildingType = "TOWER_BUILDING",
        KillerId = killerId,
        KillerTeamId = ownerTeamId
    };

    private static Match MakeHorizonMatch(string id, int durationSec, int goldAt15)
    {
        var match = new Match
        {
            MatchId = id,
            QueueId = 420,
            GameDurationSeconds = durationSec,
            GameCreation = DateTime.UtcNow,
            WinningTeam = TeamSide.Blue,
            HasMinute15Objectives = true,
            HasCsXp10Features = true,
            HasVisionDeathFeatures = true,
            Teams =
            {
                new TeamStats
                {
                    TeamSide = TeamSide.Blue,
                    GoldAt15 = 24000 + goldAt15,
                    KillsAt15 = 8,
                    CsAt15 = 300,
                    XpAt15 = 12000,
                    TowersAt15 = 1,
                    DragonsAt15 = 1,
                    FirstBlood = true
                },
                new TeamStats
                {
                    TeamSide = TeamSide.Red,
                    GoldAt15 = 24000,
                    KillsAt15 = 5,
                    CsAt15 = 280,
                    XpAt15 = 11000,
                    TowersAt15 = 0,
                    DragonsAt15 = 0
                }
            }
        };

        foreach (var side in new[] { TeamSide.Blue, TeamSide.Red })
        {
            foreach (var pos in new[] { Position.Top, Position.Jungle, Position.Middle, Position.Bottom, Position.Utility })
            {
                match.Participants.Add(new Participant
                {
                    TeamSide = side,
                    Position = pos,
                    ChampionName = side == TeamSide.Blue ? "Garen" : "Darius",
                    Win = side == TeamSide.Blue
                });
            }
        }

        return match;
    }
}
