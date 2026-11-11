using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class CoachingHabitTests
{
    private const string Puuid = "habit-player";
    private static int _seq;

    private static Match Game(Position position, string champion, int deaths, bool win, int cs = 230, int queueId = 420)
    {
        var id = Interlocked.Increment(ref _seq);
        var match = new Match
        {
            MatchId = $"EUW1_H{id}",
            QueueId = queueId,
            GameDurationSeconds = 1800,
            GameCreation = new DateTime(2026, 9, 1).AddHours(id),
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = Puuid,
                    SummonerName = "Habit#EUW",
                    ChampionName = champion,
                    Position = position,
                    TeamSide = TeamSide.Blue,
                    Win = win,
                    Kills = 6,
                    Deaths = deaths,
                    Assists = 7,
                    TotalMinionsKilled = cs,
                    TotalDamageDealtToChampions = 20000
                },
                new() { Puuid = "ally", Position = Position.Bottom, TeamSide = TeamSide.Blue, Kills = 12, TotalDamageDealtToChampions = 24000 },
                new() { Puuid = "enemy", Position = position, TeamSide = TeamSide.Red, Kills = 8, TotalDamageDealtToChampions = 20000 }
            }
        };
        match.Teams.Add(new TeamStats { TeamSide = TeamSide.Blue, DragonKills = 2, TowerKills = 5 });
        return match;
    }

    [Fact]
    public void HighDeathGamesThatLose_RankDeathsAsTopHabitWithEvidence()
    {
        var matches = new List<Match>();
        for (var i = 0; i < 6; i++) matches.Add(Game(Position.Jungle, "Shyvana", deaths: 9, win: i == 0));
        for (var i = 0; i < 6; i++) matches.Add(Game(Position.Jungle, "Shyvana", deaths: 2, win: i != 0));

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        var top = Assert.IsType<CoachingHabit>(report.TopHabits.FirstOrDefault());
        Assert.Equal("deaths", top.Key);
        Assert.Equal(1, top.Rank);
        Assert.Contains("1/6", top.Evidence);
        Assert.Contains("5/6", top.Evidence);
        Assert.Contains("Головне зараз", report.CoachAdvice);
    }

    [Fact]
    public void WideChampionPool_IsReportedWithBestChampions()
    {
        var champs = new[] { "Shyvana", "Elise", "Sylas", "Talon", "Briar", "Wukong", "Nidalee", "Viego", "Shyvana", "Elise" };
        var matches = champs.Select((c, i) => Game(Position.Jungle, c, deaths: 4, win: i % 2 == 0)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        var pool = Assert.Single(report.TopHabits, h => h.Key == "pool");
        Assert.Contains("8 різних чемпіонів за 10 ігор", pool.Evidence);
    }

    [Fact]
    public void BigWinRateGapBetweenRoles_SuggestsBetterRole()
    {
        var matches = new List<Match>();
        for (var i = 0; i < 8; i++) matches.Add(Game(Position.Jungle, "Shyvana", deaths: 4, win: i < 3));
        for (var i = 0; i < 6; i++) matches.Add(Game(Position.Middle, "Yone", deaths: 4, win: i < 5));

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        var role = Assert.Single(report.TopHabits, h => h.Key == "role");
        Assert.Contains("MID", role.Title);
        Assert.Contains("5/6", role.Evidence);
        Assert.Contains("3/8", role.Evidence);
    }

    [Fact]
    public void SolidGames_ProduceStrengthsAndNoInventedHabits()
    {
        var matches = Enumerable.Range(0, 8).Select(i => Game(Position.Jungle, "Shyvana", deaths: 2, win: i % 2 == 0, cs: 270)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        Assert.DoesNotContain(report.TopHabits, h => h.Key is "deaths" or "farm" or "pool");
        Assert.Contains(report.Highlights, s => s.StartsWith("Фарм"));
        Assert.Contains(report.Highlights, s => s.StartsWith("Живучість"));
    }

    [Fact]
    public void LowFarmJungler_GetsFarmHabitAgainstTierMedian()
    {
        var matches = Enumerable.Range(0, 8).Select(i => Game(Position.Jungle, "Shyvana", deaths: 4, win: i % 2 == 0, cs: 150)).ToList();

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        var farm = Assert.Single(report.TopHabits, h => h.Key == "farm");
        Assert.Contains("5.0 CS/хв", farm.Evidence);
        Assert.Contains("Platinum", farm.Evidence);
    }

    [Fact]
    public void TopHabits_AreCappedAtThreeAndRankedByPriority()
    {
        var champs = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };
        var matches = champs.Select((c, i) => Game(Position.Jungle, c, deaths: i < 6 ? 10 : 2, win: i >= 6, cs: 140)).ToList();
        for (var i = 0; i < 6; i++) matches.Add(Game(Position.Middle, "Yone", deaths: 3, win: true));

        var report = GlobalCoachingAnalyzer.Analyze(matches, Puuid, GameTier.Platinum);

        Assert.InRange(report.TopHabits.Count, 1, 3);
        Assert.Equal(report.TopHabits.OrderByDescending(h => h.Priority).Select(h => h.Key), report.TopHabits.Select(h => h.Key));
        Assert.Equal(Enumerable.Range(1, report.TopHabits.Count), report.TopHabits.Select(h => h.Rank));
    }
}
