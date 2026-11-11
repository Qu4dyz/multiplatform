using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class CoachingTrendTests
{
    private static CoachingHabitAnalyzer.GameSample Sample(int hoursAgo, int deaths, bool win, int cs = 220)
    {
        var player = new Participant
        {
            Puuid = "p",
            Position = Position.Jungle,
            TeamSide = TeamSide.Blue,
            Kills = 5,
            Deaths = deaths,
            Assists = 5,
            TotalMinionsKilled = cs,
            Win = win
        };
        var match = new Match
        {
            MatchId = $"T{hoursAgo}",
            GameDurationSeconds = 1800,
            GameCreation = new DateTime(2026, 10, 1).AddHours(-hoursAgo),
            Participants = new List<Participant>
            {
                player,
                new() { Puuid = "ally", TeamSide = TeamSide.Blue, Kills = 10 }
            }
        };
        return new CoachingHabitAnalyzer.GameSample(match, player);
    }

    [Fact]
    public void FewerDeathsInLatestGames_IsReportedAsImprovement()
    {
        var games = new List<CoachingHabitAnalyzer.GameSample>();
        for (var i = 0; i < 10; i++) games.Add(Sample(i, deaths: 3, win: i % 2 == 0));
        for (var i = 10; i < 20; i++) games.Add(Sample(i, deaths: 8, win: i % 2 == 0));

        var trends = CoachingTrendAnalyzer.Analyze(games, games, Position.Jungle);

        var deaths = Assert.Single(trends, t => t.Metric == "Смерті за гру");
        Assert.Equal(1, deaths.Direction);
        Assert.Contains("8.0 → 3.0", deaths.Text);
        Assert.Contains("краще", deaths.Text);
    }

    [Fact]
    public void LowerFarmInLatestGames_IsReportedAsDecline()
    {
        var games = new List<CoachingHabitAnalyzer.GameSample>();
        for (var i = 0; i < 10; i++) games.Add(Sample(i, deaths: 4, win: true, cs: 150));
        for (var i = 10; i < 18; i++) games.Add(Sample(i, deaths: 4, win: true, cs: 240));

        var trends = CoachingTrendAnalyzer.Analyze(games, games, Position.Jungle);

        var cs = Assert.Single(trends, t => t.Metric == "CS/хв");
        Assert.Equal(-1, cs.Direction);
    }

    [Fact]
    public void TooFewOlderGames_ProducesNoTrends()
    {
        var games = Enumerable.Range(0, 12).Select(i => Sample(i, deaths: 4, win: true)).ToList();

        Assert.Empty(CoachingTrendAnalyzer.Analyze(games, games, Position.Jungle));
    }
}
