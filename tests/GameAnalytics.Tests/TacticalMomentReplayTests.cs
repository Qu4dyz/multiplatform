using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class TacticalMomentReplayTests
{
    [Fact]
    public void ToMapPoint_InvertsYAndScalesToCanvas()
    {
        // Blue base-ish corner (low x, low y) should land bottom-left on canvas.
        var (left, top) = TacticalMomentReplayBuilder.ToMapPoint(0, 0, 280);
        Assert.True(left < 20);
        Assert.True(top > 240);

        // Red base-ish corner (high x, high y) should land top-right.
        var (left2, top2) = TacticalMomentReplayBuilder.ToMapPoint(14870, 14870, 280);
        Assert.True(left2 > 240);
        Assert.True(top2 < 20);
    }

    [Fact]
    public void BuildCombatMoment_AttachesMinimapMarkersAndContextForDeath()
    {
        var match = new Match
        {
            MatchId = "EUW1_MOMENT",
            Participants = Enumerable.Range(1, 10).Select(i => new Participant
            {
                ParticipantId = i,
                ChampionName = i <= 5 ? "Ahri" : "Zed",
                TeamSide = i <= 5 ? TeamSide.Blue : TeamSide.Red
            }).ToList()
        };
        match.Participants[0].ChampionName = "Yasuo";
        match.Participants[5].ChampionName = "Jinx";

        var kill = new TimelineEventRecord
        {
            TimestampMs = 14 * 60_000 + 32_000,
            EventType = "CHAMPION_KILL",
            KillerId = 6,
            VictimId = 1,
            AssistingParticipantIds = new List<int> { 7 },
            PositionX = 7500,
            PositionY = 7500,
            Bounty = 300
        };

        var timeline = new MatchTimelineData
        {
            MatchId = match.MatchId,
            RealEvents =
            {
                new TimelineEventRecord
                {
                    TimestampMs = kill.TimestampMs - 20_000,
                    EventType = "ELITE_MONSTER_KILL",
                    MonsterType = "DRAGON",
                    KillerId = 6,
                    KillerTeamId = 200
                },
                kill,
                new TimelineEventRecord
                {
                    TimestampMs = kill.TimestampMs + 15_000,
                    EventType = "BUILDING_KILL",
                    BuildingType = "TOWER_BUILDING",
                    LaneType = "MID_LANE",
                    KillerId = 6
                }
            },
            Frames =
            {
                new TimelineFrameSnapshot
                {
                    TimestampMs = 14 * 60_000,
                    Participants = Enumerable.Range(1, 10).Select(i => new TimelineParticipantPos
                    {
                        ParticipantId = i,
                        X = 2000 + i * 800,
                        Y = 3000 + i * 500,
                        Level = 9,
                        TotalGold = 5000 + i * 100
                    }).ToList()
                }
            }
        };

        var moment = TacticalMomentReplayBuilder.BuildCombatMoment(match, timeline, kill, focusParticipantId: 1, isDeath: true);

        Assert.NotNull(moment);
        Assert.True(moment!.IsDeath);
        Assert.Equal("14:32", moment.TimestampText);
        Assert.Equal(10, moment.Markers.Count);
        Assert.Contains(moment.Markers, m => m.IsFocus && m.RoleLabel == "ВИ");
        Assert.Contains(moment.Markers, m => m.RoleLabel == "KILLER");
        Assert.Contains(moment.ContextLines, l => l.IsCurrent);
        Assert.True(moment.ContextLines.Count >= 2);
    }

    [Fact]
    public void AnalyzeMatchTactics_DeathEvents_IncludeMomentReplayWhenFramesExist()
    {
        var match = new Match
        {
            MatchId = "EUW1_TL",
            GameDurationSeconds = 1800,
            WinningTeam = TeamSide.Red,
            Participants = Enumerable.Range(1, 10).Select(i => new Participant
            {
                ParticipantId = i,
                ChampionName = i == 1 ? "LeeSin" : (i <= 5 ? "Ahri" : "Zed"),
                TeamSide = i <= 5 ? TeamSide.Blue : TeamSide.Red,
                Win = i > 5,
                Kills = i == 1 ? 2 : 3,
                Deaths = i == 1 ? 4 : 2,
                Assists = 5,
                TotalMinionsKilled = 120,
                GoldEarned = 9000,
                TotalDamageDealtToChampions = 12000,
                VisionScore = 20,
                Position = Position.Jungle
            }).ToList()
        };

        var kill = new TimelineEventRecord
        {
            TimestampMs = 600_000,
            EventType = "CHAMPION_KILL",
            KillerId = 8,
            VictimId = 1,
            PositionX = 4000,
            PositionY = 9000
        };

        var timeline = new MatchTimelineData
        {
            RealEvents = { kill },
            Frames =
            {
                new TimelineFrameSnapshot
                {
                    TimestampMs = 600_000,
                    Participants = Enumerable.Range(1, 10).Select(i => new TimelineParticipantPos
                    {
                        ParticipantId = i,
                        X = 1000 * i,
                        Y = 1000 * i,
                        Level = 8,
                        TotalGold = 4000
                    }).ToList()
                }
            }
        };

        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, match.Participants[0], timeline);
        var death = Assert.Single(report.TimelineEvents, e => e.IsMistake && e.Title.StartsWith("Смерть"));
        Assert.True(death.HasMomentReplay);
        Assert.NotNull(death.Moment);
        Assert.Equal(10, death.Moment!.Markers.Count);
    }
}
