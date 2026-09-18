using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Desktop.ViewModels;
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

    [Fact]
    public async Task RiotRateLimiter_IsolatesRoutingRegions()
    {
        var limiter = new RiotRateLimiter();

        await limiter.WaitForSlotAsync("euw1");
        await limiter.WaitForSlotAsync("europe");

        Assert.Equal(2, limiter.ActiveBucketCount);
    }

    [Fact]
    public async Task RiotRateLimiter_NotifyRateLimitHit_BlocksOnlyTargetRegion()
    {
        var limiter = new RiotRateLimiter();

        // Hit rate limit on euw1 for 600ms
        limiter.NotifyRateLimitHit("euw1", TimeSpan.FromMilliseconds(600));

        // Call to europe should complete almost instantaneously (<100ms)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitForSlotAsync("europe");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 250, $"europe was blocked unexpectedly: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void PlayerMatchItemViewModel_DetectsRemake_ByFlagAndDuration()
    {
        var remakeByFlag = new Match
        {
            MatchId = "EUN1_1001",
            IsRemake = true,
            GameDurationSeconds = 600,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var remakeByDuration = new Match
        {
            MatchId = "EUN1_1002",
            IsRemake = false,
            GameDurationSeconds = 210, // < 300s
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var normalVictory = new Match
        {
            MatchId = "EUN1_1003",
            IsRemake = false,
            GameDurationSeconds = 1800,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var vmFlag = PlayerMatchItemViewModel.FromMatch(remakeByFlag, "test-puuid");
        var vmDuration = PlayerMatchItemViewModel.FromMatch(remakeByDuration, "test-puuid");
        var vmNormal = PlayerMatchItemViewModel.FromMatch(normalVictory, "test-puuid");

        Assert.True(vmFlag.IsRemake);
        Assert.False(vmFlag.IsVictory);
        Assert.Equal("РЕМЕЙК", vmFlag.ResultText);
        Assert.Equal("#A0A8B6", vmFlag.ResultColor);
        Assert.Equal("#1C222B", vmFlag.ResultBgColor);

        Assert.True(vmDuration.IsRemake);
        Assert.False(vmDuration.IsVictory);
        Assert.Equal("РЕМЕЙК", vmDuration.ResultText);

        Assert.False(vmNormal.IsRemake);
        Assert.True(vmNormal.IsVictory);
        Assert.Equal("ПЕРЕМОГА", vmNormal.ResultText);
    }

    [Fact]
    public void PlayerMatchItemViewModel_AramQueue_SetsRoleToAram()
    {
        var aramMatchById = new Match
        {
            MatchId = "EUN1_2001",
            QueueId = 450,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Middle, ChampionName = "Lux" }
            }
        };

        var aramMatchByName = new Match
        {
            MatchId = "EUN1_2002",
            QueueId = 0,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Bottom, ChampionName = "Jinx" }
            }
        };
        // QueueName = "ARAM" when QueueId = 450
        var vmAramId = PlayerMatchItemViewModel.FromMatch(aramMatchById, "test-puuid");
        var vmSummoners = PlayerMatchItemViewModel.FromMatch(new Match
        {
            MatchId = "EUN1_2003",
            QueueId = 420,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Middle, ChampionName = "Lux" }
            }
        }, "test-puuid");

        Assert.Equal("ARAM", vmAramId.PositionName);
        Assert.Equal("🎲", vmAramId.PositionIcon);

        Assert.Equal("MID", vmSummoners.PositionName);
        Assert.Equal("⚡", vmSummoners.PositionIcon);
    }

    [Fact]
    public void PlayerAnalyticsViewModel_CalculateSessionSummary_ExcludesRemakesFromWinRate()
    {
        var vm = new PlayerAnalyticsViewModel();

        var matches = new List<PlayerMatchItemViewModel>
        {
            new() { MatchId = "1", IsVictory = true, IsRemake = false, ChampionName = "Ahri", Kills = 5, Deaths = 1, Assists = 4, KillParticipationPercent = 60 },
            new() { MatchId = "2", IsVictory = true, IsRemake = false, ChampionName = "Ahri", Kills = 6, Deaths = 2, Assists = 5, KillParticipationPercent = 50 },
            new() { MatchId = "3", IsVictory = true, IsRemake = false, ChampionName = "Zed", Kills = 10, Deaths = 3, Assists = 2, KillParticipationPercent = 70 },
            new() { MatchId = "4", IsVictory = false, IsRemake = false, ChampionName = "Zed", Kills = 2, Deaths = 8, Assists = 3, KillParticipationPercent = 40 },
            new() { MatchId = "5", IsVictory = false, IsRemake = true, ChampionName = "Ahri", Kills = 0, Deaths = 0, Assists = 0, KillParticipationPercent = 0 }
        };

        vm.CalculateSessionSummary(matches);

        // 5 total games, 3 wins, 1 loss, 1 remake
        // Win rate should be 3 / (3 + 1) * 100 = 75.0%
        Assert.Equal(5, vm.SessionTotalGames);
        Assert.Equal(3, vm.SessionWins);
        Assert.Equal(1, vm.SessionLosses);
        Assert.Equal(75.0, vm.SessionWinRate);
        Assert.Contains("(1 ремейк)", vm.SessionRecordText);

        // Ahri has 2 wins out of 2 non-remake games (100% win rate)
        var ahriStat = vm.TopRecentChampions.FirstOrDefault(c => c.ChampionName == "Ahri");
        Assert.NotNull(ahriStat);
        Assert.Equal(3, ahriStat.GamesCount); // 3 total games played
        Assert.Equal(2, ahriStat.WinsCount);  // 2 wins
        Assert.Equal(100.0, ahriStat.WinRate); // 2/2 non-remake = 100%
    }

    [Fact]
    public void PlayerAnalyticsViewModel_ApplyQueueFilter_RecalculatesTopChampionsAndSessionSummary()
    {
        var vm = new PlayerAnalyticsViewModel();

        var m1 = new PlayerMatchItemViewModel { MatchId = "1", GameModeText = "Normal Draft", IsVictory = true, ChampionName = "Ahri", PositionName = "MID", PositionIcon = "⚡" };
        var m2 = new PlayerMatchItemViewModel { MatchId = "2", GameModeText = "Normal Draft", IsVictory = false, ChampionName = "Lux", PositionName = "SUP", PositionIcon = "✨" };
        var m3 = new PlayerMatchItemViewModel { MatchId = "3", GameModeText = "ARAM", IsVictory = true, ChampionName = "Jinx", PositionName = "ARAM", PositionIcon = "🎲" };
        var m4 = new PlayerMatchItemViewModel { MatchId = "4", GameModeText = "ARAM", IsVictory = true, ChampionName = "Jinx", PositionName = "ARAM", PositionIcon = "🎲" };

        vm.RecentMatches.Add(m1);
        vm.RecentMatches.Add(m2);
        vm.RecentMatches.Add(m3);
        vm.RecentMatches.Add(m4);

        // Filter by Normal Draft
        vm.SelectedQueueFilter = "Normal Draft";
        vm.ApplyQueueFilter();

        Assert.Equal(2, vm.FilteredRecentMatches.Count);
        Assert.All(vm.FilteredRecentMatches, m => Assert.Equal("Normal Draft", m.GameModeText));
        Assert.Equal(2, vm.SessionTotalGames);
        Assert.Equal(1, vm.SessionWins);
        Assert.Equal(1, vm.SessionLosses);
        Assert.Equal(50.0, vm.SessionWinRate);

        // Top champions only include Ahri and Lux, not Jinx
        Assert.DoesNotContain(vm.TopRecentChampions, c => c.ChampionName == "Jinx");
        Assert.Contains(vm.TopRecentChampions, c => c.ChampionName == "Ahri");
        Assert.Contains(vm.TopRecentChampions, c => c.ChampionName == "Lux");
    }
}

