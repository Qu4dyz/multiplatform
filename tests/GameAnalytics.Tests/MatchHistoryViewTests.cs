using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GameAnalytics.Tests;

public sealed class MatchHistoryViewTests : IDisposable
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"ga_history_{Guid.NewGuid():N}.db");
    private readonly GameAnalyticsDbContext _context;
    private readonly MatchRepository _repo;

    public MatchHistoryViewTests()
    {
        var options = new DbContextOptionsBuilder<GameAnalyticsDbContext>()
            .UseSqlite($"Data Source={_dbFile};Pooling=False")
            .Options;
        _context = new GameAnalyticsDbContext(options);
        _context.Database.EnsureCreated();
        _repo = new MatchRepository(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        try { File.Delete(_dbFile); } catch { }
    }

    private async Task SeedAsync(int count)
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            await _repo.SaveMatchAsync(new Match
            {
                MatchId = $"EUW1_{i:D4}",
                GameCreation = start.AddMinutes(i),
                GameDurationSeconds = 1500,
                WinningTeam = i % 2 == 0 ? TeamSide.Blue : TeamSide.Red,
                Participants = new List<Participant>
                {
                    new() { Puuid = i % 3 == 0 ? "p-target" : $"p-{i}", SummonerName = $"Player{i}#EUW", ChampionName = "Ahri", TeamSide = TeamSide.Blue }
                }
            });
        }
    }

    [Fact]
    public async Task GetRecentMatchesPageAsync_ReturnsNewestFirstPages()
    {
        await SeedAsync(7);

        var first = await _repo.GetRecentMatchesPageAsync(0, 3);
        var second = await _repo.GetRecentMatchesPageAsync(3, 3);
        var last = await _repo.GetRecentMatchesPageAsync(6, 3);

        Assert.Equal(new[] { "EUW1_0006", "EUW1_0005", "EUW1_0004" }, first.Select(m => m.MatchId));
        Assert.Equal(new[] { "EUW1_0003", "EUW1_0002", "EUW1_0001" }, second.Select(m => m.MatchId));
        Assert.Single(last);
        Assert.All(first, m => Assert.Single(m.Participants));
    }

    [Fact]
    public async Task GetMatchesByPuuidAsync_FiltersAndLimits()
    {
        await SeedAsync(10);

        var matches = await _repo.GetMatchesByPuuidAsync("p-target", limit: 3);

        Assert.Equal(new[] { "EUW1_0009", "EUW1_0006", "EUW1_0003" }, matches.Select(m => m.MatchId));
        Assert.Empty(await _repo.GetMatchesByPuuidAsync("", 5));
    }

    [Fact]
    public async Task MatchHistoryViewModel_LoadsOnlyFirstPageThenMore()
    {
        await SeedAsync(MatchHistoryViewModel.PageSize + 5);
        var vm = new MatchHistoryViewModel(_repo, autoLoad: false);

        await vm.LoadMatchesAsync();

        Assert.Equal(MatchHistoryViewModel.PageSize + 5, vm.TotalMatchesCount);
        Assert.Equal(MatchHistoryViewModel.PageSize, vm.Matches.Count);
        Assert.True(vm.CanLoadMore);
        Assert.Equal(vm.Matches[0], vm.SelectedMatch);

        await vm.LoadMoreMatchesAsync();

        Assert.Equal(MatchHistoryViewModel.PageSize + 5, vm.Matches.Count);
        Assert.False(vm.CanLoadMore);
        Assert.Equal(vm.Matches.Count, vm.Matches.Select(m => m.MatchId).Distinct().Count());
    }

    [Fact]
    public async Task MatchHistoryViewModel_RefreshKeepsSelectionByMatchId()
    {
        await SeedAsync(5);
        var vm = new MatchHistoryViewModel(_repo, autoLoad: false);
        await vm.LoadMatchesAsync();
        vm.SelectedMatch = vm.Matches[2];
        var selectedId = vm.SelectedMatch.MatchId;

        await vm.LoadMatchesAsync();

        Assert.NotNull(vm.SelectedMatch);
        Assert.Equal(selectedId, vm.SelectedMatch!.MatchId);
        Assert.Contains(vm.SelectedMatch, vm.Matches);
    }

    [Fact]
    public async Task MatchHistoryViewModel_ClearNeedsSecondClick()
    {
        await SeedAsync(3);
        var vm = new MatchHistoryViewModel(_repo, autoLoad: false);
        await vm.LoadMatchesAsync();

        await vm.ClearHistoryAsync();
        Assert.True(vm.IsClearConfirmPending);
        Assert.Equal(3, await _repo.GetTotalMatchesCountAsync());

        vm.CancelClear();
        Assert.False(vm.IsClearConfirmPending);

        await vm.ClearHistoryAsync();
        await vm.ClearHistoryAsync();
        Assert.False(vm.IsClearConfirmPending);
        Assert.Equal(0, await _repo.GetTotalMatchesCountAsync());
        Assert.Empty(vm.Matches);
        Assert.Null(vm.SelectedMatch);
    }

    [Fact]
    public void UtcToLocalTimeConverter_TreatsUnspecifiedAsUtc()
    {
        var stored = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Unspecified);
        var expected = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc).ToLocalTime();

        Assert.Equal(expected, UtcToLocalTimeConverter.ToLocal(stored));
        var local = DateTime.Now;
        Assert.Equal(local, UtcToLocalTimeConverter.ToLocal(local));
    }

    [Fact]
    public void TeamSideBrushConverter_PicksTeamColor()
    {
        var c = TeamSideBrushConverter.Instance;
        Assert.Same(TeamSideBrushConverter.RedBrush, c.Convert(TeamSide.Red, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(TeamSideBrushConverter.BlueBrush, c.Convert(TeamSide.Blue, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(512L, "0.5 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void SettingsViewModel_FormatFileSize_UsesReadableUnits(long bytes, string expected)
    {
        var text = SettingsViewModel.FormatFileSize(bytes).Replace(',', '.');
        Assert.Equal(expected, text);
    }
}
