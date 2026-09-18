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
}

