using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Desktop.ViewModels;

namespace GameAnalytics.Tests;

public class PlayerProfileDisplayTests
{
    [Fact]
    public void BuildRankProgress_HidesForUnranked()
    {
        var (show, _, text) = PlayerAnalyticsViewModel.BuildRankProgress(GameTier.Unranked, "", 0);
        Assert.False(show);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void BuildRankProgress_DivisionTiersCountToNextDivision()
    {
        var (show, value, text) = PlayerAnalyticsViewModel.BuildRankProgress(GameTier.Platinum, "II", 48);
        Assert.True(show);
        Assert.Equal(48, value);
        Assert.Equal("48 / 100 LP до Platinum I", text);

        var promo = PlayerAnalyticsViewModel.BuildRankProgress(GameTier.Diamond, "I", 75);
        Assert.Equal("75 / 100 LP до Master", promo.Text);
    }

    [Fact]
    public void BuildRankProgress_ApexTiersHaveNoHundredCap()
    {
        var (show, value, text) = PlayerAnalyticsViewModel.BuildRankProgress(GameTier.Grandmaster, "I", 512);
        Assert.True(show);
        Assert.Equal(100, value);
        Assert.Equal("512 LP · без дивізіонів", text);
    }

    [Fact]
    public async Task ResolveMasteryNamesAsync_ReplacesPlaceholderNames()
    {
        ChampionNameHelper.RegisterChampion(60, "Elise");
        var profile = new SummonerProfile
        {
            TopMasteries =
            {
                new ChampionMasteryInfo { ChampionId = 60, ChampionName = "Champion 60" },
                new ChampionMasteryInfo { ChampionId = 777, ChampionName = "" },
                new ChampionMasteryInfo { ChampionId = 103, ChampionName = "Ahri" }
            }
        };
        var vm = new PlayerAnalyticsViewModel();

        await vm.ResolveMasteryNamesAsync(profile);

        Assert.Equal(new[] { "Elise", "Yone", "Ahri" }, profile.TopMasteries.Select(m => m.ChampionName));
    }
}

public class MainWindowStatusTests
{
    [Fact]
    public void ConnectionStatus_FollowsPlayerAnalyticsKeyState()
    {
        var analytics = new PlayerAnalyticsViewModel();
        var main = new MainViewModel(analytics, null!, null!, null!);

        Assert.False(main.HasRiotApiKey);
        Assert.Contains("Демо", main.ConnectionStatusText);

        analytics.HasApiKey = true;

        Assert.True(main.HasRiotApiKey);
        Assert.Contains("ключ активний", main.ConnectionStatusText);
    }
}
