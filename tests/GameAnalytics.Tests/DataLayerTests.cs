using GameAnalytics.Core.Entities;
using GameAnalytics.Infrastructure.Services;
using Xunit;

namespace GameAnalytics.Tests;

public class DataLayerTests
{
    [Fact]
    public async Task DataDragonService_GetAllChampions_ReturnsPopulatedList()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var service = new DataDragonService(httpClient);

        // Act
        var champions = await service.GetAllChampionsAsync();

        // Assert
        Assert.NotNull(champions);
        Assert.NotEmpty(champions);
        Assert.Contains(champions, c => c.Name == "Ahri" || c.Id == "Ahri");
    }

    [Fact]
    public async Task DataDragonService_GetChampionsByRole_FiltersCorrectly()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var service = new DataDragonService(httpClient);

        // Act
        var mages = await service.GetChampionsByRoleAsync("Mage");

        // Assert
        Assert.NotNull(mages);
        Assert.NotEmpty(mages);
        Assert.All(mages, m => Assert.Contains("Mage", m.Roles));
    }

    [Fact]
    public void MatchTimelineData_ToInputFeatures_CalculatesDiffsCorrectly()
    {
        // Arrange
        var timeline = new MatchTimelineData
        {
            MatchId = "EUN1_123456",
            GoldAt15Blue = 28500,
            GoldAt15Red = 25000,
            KillsAt15Blue = 10,
            KillsAt15Red = 4,
            TowersAt15Blue = 2,
            TowersAt15Red = 0,
            DragonsAt15Blue = 1,
            DragonsAt15Red = 0,
            BlueFirstBlood = true,
            BlueFirstTower = true,
            BlueFirstDragon = true
        };

        // Act
        var features = timeline.ToInputFeatures(52.5f, 48.5f);

        // Assert
        Assert.Equal(3500, features.GoldDiffAt15);
        Assert.Equal(6, features.KillDiffAt15);
        Assert.True(features.BlueFirstBlood);
        Assert.True(features.BlueFirstTower);
        Assert.Equal(52.5f, features.BlueTeamAvgWinRate);
        Assert.Equal(48.5f, features.RedTeamAvgWinRate);
    }

    [Fact]
    public async Task RiotRateLimiter_AllowsSuccessiveCalls()
    {
        // Arrange
        var rateLimiter = new RiotRateLimiter();

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            await rateLimiter.WaitForSlotAsync();
        }

        Assert.True(true);
    }

    [Fact]
    public void Match_QueueName_MapsCorrectly()
    {
        var normalDraft = new Match { QueueId = 400 };
        var rankedSolo = new Match { QueueId = 420 };
        var aram = new Match { QueueId = 450 };
        var custom = new Match { QueueId = 9999 };

        Assert.Equal("Normal Draft", normalDraft.QueueName);
        Assert.Equal("Ranked Solo", rankedSolo.QueueName);
        Assert.Equal("ARAM", aram.QueueName);
        Assert.Equal("Normal Game", custom.QueueName);
    }

    [Fact]
    public void SummonerProfile_ProfileIconUrl_FormatsProperly()
    {
        var profile = new SummonerProfile { ProfileIconId = 2072 };
        Assert.Equal("https://ddragon.leagueoflegends.com/cdn/14.18.1/img/profileicon/2072.png", profile.ProfileIconUrl);
    }
}

