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
}

