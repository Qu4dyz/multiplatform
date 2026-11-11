using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Training;
using Xunit;

namespace GameAnalytics.Tests;

public class VpsLabHangHardenTests
{
    [Fact]
    public void LabCadence_IsRarerAndLighterThanPrior12kEvery10()
    {
        Assert.Equal(20, VpsTrainingWorker.LabHorizonIntervalEpochs);
        Assert.InRange(VpsTrainingWorker.LabHorizonMaxRows, 4_000, 6_000);
        Assert.True(VpsTrainingWorker.LabHorizonIntervalEpochs > VpsTrainingWorker.RetrainIntervalEpochs);
    }

    [Fact]
    public void ParseMemAvailable_ReadsProcStyleKb()
    {
        const string sample = """
            MemTotal:        1992832 kB
            MemFree:           81920 kB
            MemAvailable:     301000 kB
            SwapTotal:       2097148 kB
            """;
        var bytes = VpsMemoryGuard.ParseMemAvailableBytes(sample);
        Assert.NotNull(bytes);
        Assert.Equal(301000L * 1024, bytes.Value);
    }

    [Fact]
    public void ParseCgroupBytes_HandlesMaxAndNumbers()
    {
        Assert.Null(VpsMemoryGuard.ParseCgroupBytes("max"));
        Assert.Equal(1_468_000_000L, VpsMemoryGuard.ParseCgroupBytes("1468000000"));
        Assert.Null(VpsMemoryGuard.ParseCgroupBytes(""));
    }

    [Fact]
    public void SlimLabCandidatePool_TakesNewestEligibleOnly()
    {
        var matches = new List<Match>
        {
            Make("old-long", daysAgo: 30, durationMin: 40),
            Make("short", daysAgo: 1, durationMin: 18),
            Make("new-long", daysAgo: 0, durationMin: 38),
            Make("mid-long", daysAgo: 5, durationMin: 36),
            Make("aram", daysAgo: 0, durationMin: 40, queueId: 450),
        };

        var pool = VpsTrainingWorker.SlimLabCandidatePool(matches, maxRows: 2);
        Assert.Equal(2, pool.Count);
        Assert.Equal("new-long", pool[0].MatchId);
        Assert.Equal("mid-long", pool[1].MatchId);
        Assert.DoesNotContain(pool, m => m.MatchId == "short");
        Assert.DoesNotContain(pool, m => m.MatchId == "aram");
    }

    [Fact]
    public void ExtractFeatures_LabMaxRows_PreferNewestDurationEligible()
    {
        var older = MakeHorizon("OLD", daysAgo: 10, durationMin: 40, goldBlue: 30000, goldRed: 28000);
        var newer = MakeHorizon("NEW", daysAgo: 1, durationMin: 40, goldBlue: 36000, goldRed: 30000);
        var shortGame = MakeHorizon("SHORT", daysAgo: 0, durationMin: 20, goldBlue: 20000, goldRed: 19000);

        MatchTimelineData? Lookup(string id) => id switch
        {
            "OLD" => TimelineAt(25, 30000, 28000),
            "NEW" => TimelineAt(25, 36000, 30000),
            "SHORT" => TimelineAt(25, 20000, 19000),
            _ => null
        };

        var rows = RealMatchDatasetCollector.ExtractFeaturesFromMatches(
            new[] { older, newer, shortGame },
            PredictionHorizons.Mid,
            Lookup,
            maxRows: 1);

        Assert.Single(rows);
        Assert.Equal(6000f, rows[0].GoldDiff15); // NEW only
    }

    [Fact]
    public void ExtractFeatures_EarlyMaxRows_CountsOnlyTimelineEligibleRows()
    {
        var older = MakeEarly("OLD", daysAgo: 10, goldBlue: 26000, goldRed: 25000);
        var newer = MakeEarly("NEW", daysAgo: 2, goldBlue: 27000, goldRed: 24000);
        // Newest game has no real 15' timeline — must not use up the cap and push out real rows.
        var placeholder = MakeEarly("PLACEHOLDER", daysAgo: 0, goldBlue: 25000, goldRed: 25000);
        placeholder.HasMinute15Objectives = false;

        var rows = RealMatchDatasetCollector.ExtractFeaturesFromMatches(
            new[] { placeholder, older, newer }, PredictionHorizons.Early, timelineLookup: null, maxRows: 2);

        Assert.Equal(new[] { 1000f, 3000f }, rows.Select(r => r.GoldDiff15).ToArray());
    }

    private static Match MakeEarly(string id, int daysAgo, int goldBlue, int goldRed)
    {
        var m = MakeHorizon(id, daysAgo, durationMin: 30, goldBlue, goldRed);
        m.HasMinute15Objectives = true;
        m.HasCsXp10Features = true;
        m.HasVisionDeathFeatures = true;
        return m;
    }

    private static Match Make(string id, int daysAgo, int durationMin, int queueId = 420)
    {
        var m = new Match
        {
            MatchId = id,
            GameCreation = DateTime.UtcNow.AddDays(-daysAgo),
            GameDurationSeconds = durationMin * 60,
            QueueId = queueId,
            IsRemake = false,
            WinningTeam = TeamSide.Blue,
            Teams =
            {
                new TeamStats { TeamSide = TeamSide.Blue },
                new TeamStats { TeamSide = TeamSide.Red }
            }
        };
        foreach (var side in new[] { TeamSide.Blue, TeamSide.Red })
        {
            foreach (var pos in new[] { Position.Top, Position.Jungle, Position.Middle, Position.Bottom, Position.Utility })
            {
                m.Participants.Add(new Participant
                {
                    TeamSide = side,
                    Position = pos,
                    ChampionName = side == TeamSide.Blue ? "Garen" : "Darius",
                    Win = side == TeamSide.Blue
                });
            }
        }
        return m;
    }

    private static Match MakeHorizon(string id, int daysAgo, int durationMin, int goldBlue, int goldRed)
    {
        var m = Make(id, daysAgo, durationMin);
        m.Teams[0].GoldAt15 = goldBlue;
        m.Teams[1].GoldAt15 = goldRed;
        return m;
    }

    private static MatchTimelineData TimelineAt(int minute, int blueGold, int redGold)
    {
        var f = new TimelineFrameSnapshot { TimestampMs = minute * 60_000 };
        for (var i = 1; i <= 5; i++)
            f.Participants.Add(new TimelineParticipantPos { ParticipantId = i, TotalGold = blueGold / 5, Level = 11 });
        for (var i = 6; i <= 10; i++)
            f.Participants.Add(new TimelineParticipantPos { ParticipantId = i, TotalGold = redGold / 5, Level = 10 });
        return new MatchTimelineData { Frames = { f } };
    }
}
