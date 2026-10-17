using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class CoachingDataQualityTests
{
    private const string Puuid = "target-puuid";

    private static Match BuildMatch(int index, Position position, int queueId = 420, int visionScore = 30, int deaths = 4, bool withTeams = true, string puuid = Puuid)
    {
        var match = new Match
        {
            MatchId = $"EUW1_{index}",
            QueueId = queueId,
            GameDurationSeconds = 1800,
            GameCreation = new DateTime(2026, 9, 1).AddHours(index),
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = puuid,
                    SummonerName = "Target#EUW",
                    Position = position,
                    TeamSide = TeamSide.Blue,
                    Win = index % 2 == 0,
                    Kills = 6,
                    Deaths = deaths,
                    Assists = 6,
                    TotalMinionsKilled = 210,
                    TotalDamageDealtToChampions = 20000,
                    VisionScore = visionScore,
                    WardsPlaced = visionScore > 0 ? 10 : 0
                },
                new()
                {
                    Puuid = "ally",
                    SummonerName = "Ally#EUW",
                    Position = Position.Bottom,
                    TeamSide = TeamSide.Blue,
                    Kills = 10,
                    TotalDamageDealtToChampions = 25000
                },
                new()
                {
                    Puuid = "enemy",
                    SummonerName = "Enemy#EUW",
                    Position = position,
                    TeamSide = TeamSide.Red,
                    Kills = 5,
                    TotalDamageDealtToChampions = 18000
                }
            }
        };

        if (withTeams)
        {
            match.Teams.Add(new TeamStats { TeamSide = TeamSide.Blue, DragonKills = 2, TowerKills = 6 });
        }

        return match;
    }

    [Fact]
    public void Analyze_MissingVisionData_MarksPillarAsNoDataInsteadOfZero()
    {
        var matches = Enumerable.Range(0, 5).Select(i => BuildMatch(i, Position.Jungle, visionScore: 0)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid);

        Assert.False(report.Vision.HasData);
        Assert.Equal("Немає даних", report.Vision.PlayerValueText);
        Assert.DoesNotContain("vision", report.CoachAdvice, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.Combat.HasData);
    }

    [Fact]
    public void Analyze_ExcludesAramAndOffRoleGamesFromRoleStats()
    {
        var matches = new List<Match>();
        matches.AddRange(Enumerable.Range(0, 4).Select(i => BuildMatch(i, Position.Jungle)));
        matches.AddRange(Enumerable.Range(10, 2).Select(i => BuildMatch(i, Position.Middle)));
        matches.AddRange(Enumerable.Range(20, 3).Select(i => BuildMatch(i, Position.Middle, queueId: 450)));

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid);

        Assert.Equal(Position.Jungle, report.PrimaryRole);
        Assert.Equal(4, report.TotalMatchesAnalyzed);
        Assert.Contains("2 на інших ролях", report.SampleNote);
        Assert.Contains("3 ARAM", report.SampleNote);
    }

    [Fact]
    public void Analyze_OnlyAramGames_ReturnsNoRoleCoaching()
    {
        var matches = Enumerable.Range(0, 3).Select(i => BuildMatch(i, Position.Middle, queueId: 450)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid);

        Assert.Equal(0, report.TotalMatchesAnalyzed);
        Assert.Contains("Summoner's Rift", report.CoachAdvice);
    }

    [Fact]
    public void Analyze_PlayerNotInMatch_DoesNotFallBackToAnotherParticipant()
    {
        var matches = Enumerable.Range(0, 3).Select(i => BuildMatch(i, Position.Jungle, puuid: "someone-else")).ToList();
        foreach (var m in matches)
        {
            m.Participants[0].SummonerName = "SomeoneElse#EUW";
        }

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid);

        Assert.Equal(0, report.TotalMatchesAnalyzed);
    }

    [Fact]
    public void Analyze_HigherTier_UsesStricterDeathBenchmark()
    {
        var matches = Enumerable.Range(0, 5).Select(i => BuildMatch(i, Position.Jungle, deaths: 5)).ToList();

        var silver = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Silver);
        var master = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Master);

        Assert.True(silver.Survival.Score > master.Survival.Score);
    }

    [Fact]
    public void Analyze_MissingTeamStats_MarksObjectivesAsNoData()
    {
        var matches = Enumerable.Range(0, 4).Select(i => BuildMatch(i, Position.Jungle, withTeams: false)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid);

        Assert.False(report.Objectives.HasData);
        Assert.True(report.OverallScore > 0);
    }

    [Fact]
    public void FindTargetPlayer_PrefersExactPuuidOverNameMatch()
    {
        var match = BuildMatch(0, Position.Jungle);
        match.Participants[1].SummonerName = Puuid; // name collides with the searched value

        var player = GlobalCoachingAnalyzer.FindTargetPlayer(match, Puuid);

        Assert.Same(match.Participants[0], player);
    }
}
