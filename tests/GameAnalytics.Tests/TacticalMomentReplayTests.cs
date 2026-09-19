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
        var (left, top) = TacticalMomentReplayBuilder.ToMapPoint(0, 0, 280);
        Assert.True(left < 20);
        Assert.True(top > 240);

        var (left2, top2) = TacticalMomentReplayBuilder.ToMapPoint(14870, 14870, 280);
        Assert.True(left2 > 240);
        Assert.True(top2 < 20);
    }

    [Fact]
    public void BuildCombatMoment_AssistTitle_NotLabeledAsKill()
    {
        var match = BuildTenManMatch(focusChampion: "Akali", focusId: 3);
        var kill = new TimelineEventRecord
        {
            TimestampMs = 265_000,
            EventType = "CHAMPION_KILL",
            KillerId = 6,
            VictimId = 2,
            AssistingParticipantIds = new List<int> { 3 },
            PositionX = 7500,
            PositionY = 7500
        };
        var simultaneousDeath = new TimelineEventRecord
        {
            TimestampMs = 265_000,
            EventType = "CHAMPION_KILL",
            KillerId = 7,
            VictimId = 3,
            PositionX = 7400,
            PositionY = 7600
        };

        var timeline = new MatchTimelineData
        {
            RealEvents = { simultaneousDeath, kill },
            Frames =
            {
                new TimelineFrameSnapshot
                {
                    TimestampMs = 240_000,
                    Participants = Enumerable.Range(1, 10).Select(i => new TimelineParticipantPos
                    {
                        ParticipantId = i,
                        X = 2000 + i * 800,
                        Y = 3000 + i * 500,
                        Level = 6,
                        TotalGold = 3000
                    }).ToList()
                }
            }
        };

        var moment = TacticalMomentReplayBuilder.BuildCombatMoment(
            match, timeline, kill, focusParticipantId: 3, TacticalMomentRole.Assist);

        Assert.NotNull(moment);
        Assert.StartsWith("Асист на", moment!.Title);
        Assert.Contains("ваш асист", moment.Subtitle);
        Assert.Contains("у ту ж секунду вас убив", moment.Subtitle);
        Assert.False(string.IsNullOrWhiteSpace(moment.MapImageUrl));
        Assert.Contains("minimap", moment.MapImageUrl, StringComparison.OrdinalIgnoreCase);
        Assert.True(moment.HasKillMarker);
        Assert.Single(moment.ContextLines, l => l.IsCurrent);
        Assert.Contains(moment.ContextLines, l => l.IsCurrent && l.Text.Contains("→") && !l.Text.Contains("★"));
    }

    [Fact]
    public void BuildCombatMoment_OnlyExactKillIsCurrent_WhenTwoKillsShareTimestamp()
    {
        var match = BuildTenManMatch("Yasuo", 1);
        var selected = new TimelineEventRecord
        {
            TimestampMs = 265_000,
            EventType = "CHAMPION_KILL",
            KillerId = 6,
            VictimId = 1,
            PositionX = 7000,
            PositionY = 7000
        };
        var other = new TimelineEventRecord
        {
            TimestampMs = 265_000,
            EventType = "CHAMPION_KILL",
            KillerId = 7,
            VictimId = 3,
            PositionX = 7100,
            PositionY = 7100
        };

        var timeline = new MatchTimelineData
        {
            RealEvents = { other, selected },
            Frames =
            {
                new TimelineFrameSnapshot
                {
                    TimestampMs = 240_000,
                    Participants = Enumerable.Range(1, 10).Select(i => new TimelineParticipantPos
                    {
                        ParticipantId = i,
                        X = 1000 * i,
                        Y = 1000 * i,
                        Level = 5,
                        TotalGold = 2500
                    }).ToList()
                }
            }
        };

        var moment = TacticalMomentReplayBuilder.BuildCombatMoment(
            match, timeline, selected, focusParticipantId: 1, TacticalMomentRole.Death);

        Assert.NotNull(moment);
        Assert.StartsWith("Смерть на", moment!.Title);
        Assert.Equal(1, moment.ContextLines.Count(l => l.IsCurrent));
        Assert.Contains(moment.ContextLines, l => l.IsCurrent && l.Text.Contains("★"));
    }

    [Fact]
    public void TimelineEvent_MomentHint_ReflectsOpenAndViewedState()
    {
        var match = BuildTenManMatch("Vex", 1);
        var kill = new TimelineEventRecord
        {
            TimestampMs = 100_000,
            EventType = "CHAMPION_KILL",
            KillerId = 1,
            VictimId = 6,
            PositionX = 7000,
            PositionY = 7000
        };
        var timeline = new MatchTimelineData
        {
            RealEvents = { kill },
            Frames =
            {
                new TimelineFrameSnapshot
                {
                    TimestampMs = 90_000,
                    Participants = Enumerable.Range(1, 10).Select(i => new TimelineParticipantPos
                    {
                        ParticipantId = i,
                        X = 1000 * i,
                        Y = 1000 * i,
                        Level = 5,
                        TotalGold = 2000
                    }).ToList()
                }
            }
        };

        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, match.Participants[0], timeline);
        var evt = Assert.Single(report.TimelineEvents, e => e.HasMomentReplay);
        Assert.Equal("▶ Відкрити момент", evt.MomentHint);

        evt.WasMomentViewed = true;
        evt.IsMomentOpen = true;
        Assert.Equal("▼ Закрити момент", evt.MomentHint);
        Assert.Equal("#FBBF24", evt.MomentHintColor);

        evt.IsMomentOpen = false;
        Assert.Equal("✓ Переглянуто · відкрити", evt.MomentHint);
        Assert.Equal("#94A3B8", evt.MomentHintColor);
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
        Assert.StartsWith("Смерть на", death.Moment!.Title);
    }

    private static Match BuildTenManMatch(string focusChampion, int focusId)
    {
        return new Match
        {
            MatchId = "EUW1_MOMENT",
            Participants = Enumerable.Range(1, 10).Select(i => new Participant
            {
                ParticipantId = i,
                ChampionName = i == focusId ? focusChampion : (i <= 5 ? $"Blue{i}" : $"Red{i}"),
                TeamSide = i <= 5 ? TeamSide.Blue : TeamSide.Red
            }).ToList()
        };
    }
}
