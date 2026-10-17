using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameAnalytics.Tests;

public class RepositoryTests
{
    private GameAnalyticsDbContext CreateTestContext()
    {
        var options = new DbContextOptionsBuilder<GameAnalyticsDbContext>()
            .UseSqlite($"Data Source=test_{Guid.NewGuid():N}.db")
            .Options;

        var context = new GameAnalyticsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task SaveMatchAsync_PersistsMatchAndParticipants()
    {
        // Arrange
        using var context = CreateTestContext();
        var repo = new MatchRepository(context);

        var match = new Match
        {
            MatchId = "EUN1_999999",
            GameDurationSeconds = 1800,
            GameVersion = "14.18.1",
            WinningTeam = TeamSide.Blue,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "p-1",
                    SummonerName = "Qu4dyz",
                    ChampionName = "Jinx",
                    Kills = 10,
                    Deaths = 2,
                    Assists = 8,
                    Win = true,
                    TeamSide = TeamSide.Blue,
                    Position = Position.Bottom
                }
            },
            Teams = new List<TeamStats>
            {
                new()
                {
                    TeamSide = TeamSide.Blue,
                    Win = true,
                    FirstBlood = true,
                    FirstTower = true,
                    TowerKills = 9,
                    GoldAt15 = 28000
                }
            }
        };

        // Act
        await repo.SaveMatchAsync(match);
        var fetched = await repo.GetMatchByMatchIdAsync("EUN1_999999");

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal("EUN1_999999", fetched.MatchId);
        Assert.Single(fetched.Participants);
        Assert.Equal("Qu4dyz", fetched.Participants[0].SummonerName);
        Assert.Single(fetched.Teams);
        Assert.True(fetched.Teams[0].FirstBlood);

        // Cleanup
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task SaveMatchAsync_DuplicateMatchId_DoesNotThrowOrDuplicate()
    {
        // Arrange
        using var context = CreateTestContext();
        var repo = new MatchRepository(context);

        var match1 = new Match { MatchId = "DUPLICATE_ID", GameDurationSeconds = 1200 };
        var match2 = new Match { MatchId = "DUPLICATE_ID", GameDurationSeconds = 1300 };

        // Act
        await repo.SaveMatchAsync(match1);
        await repo.SaveMatchAsync(match2);
        var totalCount = await repo.GetTotalMatchesCountAsync();

        // Assert
        Assert.Equal(1, totalCount);

        // Cleanup
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task SaveMatchAsync_PersistsQueueIdAndChampLevel()
    {
        // Arrange
        using var context = CreateTestContext();
        var repo = new MatchRepository(context);

        var match = new Match
        {
            MatchId = "EUW1_7983198392",
            QueueId = 400,
            GameDurationSeconds = 1920,
            GameVersion = "14.18.1",
            WinningTeam = TeamSide.Blue,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "puuid-ambessa",
                    SummonerName = "Qu4dyz#qu4",
                    ChampionName = "Ambessa",
                    ChampLevel = 18,
                    Kills = 18,
                    Deaths = 3,
                    Assists = 9,
                    Win = true,
                    TeamSide = TeamSide.Blue
                }
            }
        };

        // Act
        await repo.SaveMatchAsync(match);
        var fetched = await repo.GetMatchByMatchIdAsync("EUW1_7983198392");

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal(400, fetched.QueueId);
        Assert.Equal("Normal Draft", fetched.QueueName);
        Assert.Single(fetched.Participants);
        Assert.Equal(18, fetched.Participants[0].ChampLevel);

        // Cleanup
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task SaveMatchAsync_PersistsParticipantId()
    {
        // Arrange
        using var context = CreateTestContext();
        var repo = new MatchRepository(context);

        var match = new Match
        {
            MatchId = "EUW1_PARTICIPANT_TEST",
            GameDurationSeconds = 1500,
            GameVersion = "14.18.1",
            WinningTeam = TeamSide.Blue,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "puuid-1",
                    SummonerName = "Qu4dyz",
                    ChampionName = "Ambessa",
                    ParticipantId = 4,
                    ChampLevel = 14,
                    Kills = 10,
                    Deaths = 1,
                    Assists = 5,
                    Win = true,
                    TeamSide = TeamSide.Blue
                }
            }
        };

        // Act
        await repo.SaveMatchAsync(match);
        var fetched = await repo.GetMatchByMatchIdAsync("EUW1_PARTICIPANT_TEST");

        // Assert
        Assert.NotNull(fetched);
        Assert.Single(fetched.Participants);
        Assert.Equal(4, fetched.Participants[0].ParticipantId);

        // Cleanup
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task ApplyMissingColumns_AddsParticipantIdToLegacySchema()
    {
        // Arrange: manually create legacy Participants table without ParticipantId
        var dbName = $"test_legacy_{Guid.NewGuid():N}.db";
        var options = new DbContextOptionsBuilder<GameAnalyticsDbContext>()
            .UseSqlite($"Data Source={dbName}")
            .Options;

        using (var initialContext = new GameAnalyticsDbContext(options))
        {
            initialContext.Database.OpenConnection();
            using var cmd = initialContext.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE Matches (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MatchId TEXT NOT NULL,
                    GameDurationSeconds INTEGER NOT NULL,
                    GameVersion TEXT,
                    GameCreation TEXT NOT NULL,
                    WinningTeam INTEGER NOT NULL
                );
                CREATE TABLE Participants (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MatchEntityId INTEGER NOT NULL,
                    Puuid TEXT,
                    SummonerName TEXT,
                    ChampionName TEXT,
                    ChampionId INTEGER NOT NULL,
                    TeamSide INTEGER NOT NULL,
                    Position INTEGER NOT NULL,
                    Kills INTEGER NOT NULL,
                    Deaths INTEGER NOT NULL,
                    Assists INTEGER NOT NULL,
                    TotalDamageDealtToChampions INTEGER NOT NULL,
                    GoldEarned INTEGER NOT NULL,
                    TotalMinionsKilled INTEGER NOT NULL,
                    Win INTEGER NOT NULL,
                    FOREIGN KEY (MatchEntityId) REFERENCES Matches(Id) ON DELETE CASCADE
                );
            ";
            cmd.ExecuteNonQuery();
        }

        // Act: constructing MatchRepository applies ApplyMissingColumns()
        using (var repoContext = new GameAnalyticsDbContext(options))
        {
            var repo = new MatchRepository(repoContext);
            var matches = await repo.GetAllMatchesAsync();

            // Assert
            Assert.Empty(matches);
            repoContext.Database.EnsureDeleted();
        }
    }

    private static Match NewTrainMatch(
        string id, DateTime created, int queueId = 420, int durationSeconds = 1800,
        bool timeline = true, bool remake = false, int goldAt15 = 25000)
    {
        var participants = new List<Participant>();
        for (var i = 0; i < 10; i++)
        {
            participants.Add(new Participant
            {
                Puuid = $"{id}-p{i}",
                ChampionName = $"Champ{i}",
                ParticipantId = i + 1,
                TeamSide = i < 5 ? TeamSide.Blue : TeamSide.Red,
                Win = i < 5,
                TurretPlatesTaken = i,
                ChallengesJson = "{\"big\":\"payload\"}"
            });
        }

        return new Match
        {
            MatchId = id,
            QueueId = queueId,
            GameCreation = created,
            GameDurationSeconds = durationSeconds,
            IsRemake = remake,
            WinningTeam = TeamSide.Blue,
            HasMinute15Objectives = timeline,
            HasCsXp10Features = timeline,
            HasVisionDeathFeatures = timeline,
            Participants = participants,
            Teams = new List<TeamStats>
            {
                new() { TeamSide = TeamSide.Blue, Win = true, GoldAt15 = goldAt15 },
                new() { TeamSide = TeamSide.Red, Win = false, GoldAt15 = goldAt15 > 0 ? goldAt15 - 1500 : 0 }
            }
        };
    }

    [Fact]
    public async Task GetNewestMatchesAsync_ReturnsNewestEligibleAscendingWithoutChallengesJson()
    {
        using var context = CreateTestContext();
        var repo = new MatchRepository(context);
        var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        // Six eligible solo games, one hour apart, saved out of order.
        foreach (var i in new[] { 3, 0, 5, 1, 4, 2 })
            await repo.SaveMatchAsync(NewTrainMatch($"OK_{i}", t0.AddHours(i)));

        // Newer than every eligible game, but each fails one early-train rule.
        var late = t0.AddDays(1);
        await repo.SaveMatchAsync(NewTrainMatch("FLEX", late, queueId: 440));
        await repo.SaveMatchAsync(NewTrainMatch("NO_TIMELINE", late.AddMinutes(1), timeline: false));
        await repo.SaveMatchAsync(NewTrainMatch("REMAKE", late.AddMinutes(2), remake: true));
        await repo.SaveMatchAsync(NewTrainMatch("SHORT", late.AddMinutes(3), durationSeconds: 840));
        await repo.SaveMatchAsync(NewTrainMatch("NO_GOLD", late.AddMinutes(4), goldAt15: 0));

        var newest = await repo.GetNewestMatchesAsync(take: 3);

        Assert.Equal(new[] { "OK_3", "OK_4", "OK_5" }, newest.Select(m => m.MatchId).ToArray());
        Assert.All(newest, m =>
        {
            Assert.Equal(10, m.Participants.Count);
            Assert.Equal(2, m.Teams.Count);
            Assert.All(m.Participants, p => Assert.Equal(string.Empty, p.ChallengesJson));
            Assert.Equal(Enumerable.Range(0, 10), m.Participants.Select(p => p.TurretPlatesTaken));
        });

        // Lab mode (no 15' timeline requirement) still keeps queue/remake/duration filters.
        var lab = await repo.GetNewestMatchesAsync(
            take: 10, minDurationSeconds: 25 * 60, requireMinute15Timeline: false);
        Assert.Equal(
            new[] { "OK_0", "OK_1", "OK_2", "OK_3", "OK_4", "OK_5", "NO_TIMELINE", "NO_GOLD" },
            lab.Select(m => m.MatchId).ToArray());

        Assert.Empty(await repo.GetNewestMatchesAsync(take: 0));
        context.Database.EnsureDeleted();
    }
}
