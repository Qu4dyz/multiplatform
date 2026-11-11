using System.Net;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using GameAnalytics.Infrastructure.Services;
using GameAnalytics.ML.Engine;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameAnalytics.Tests;

/// <summary>Draft simulator view model on the real prediction engine and an empty SQLite file.</summary>
public sealed class DraftPredictionViewModelTests : IDisposable
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"ga_draft_{Guid.NewGuid():N}.db");
    private readonly GameAnalyticsDbContext _context;
    private readonly MatchRepository _repo;

    public DraftPredictionViewModelTests()
    {
        var options = new DbContextOptionsBuilder<GameAnalyticsDbContext>()
            .UseSqlite($"Data Source={_dbFile};Pooling=False")
            .Options;
        _context = new GameAnalyticsDbContext(options);
        _repo = new MatchRepository(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private DraftPredictionViewModel CreateViewModel(LiveGameStats? live = null, IRiotApiClient? api = null)
    {
        var analytics = new MatchAnalyticsService(api!, _repo, new MatchPredictionEngine());
        return new DraftPredictionViewModel(analytics, new FakeDataDragon(), new FakeLiveGame(live));
    }

    [Fact]
    public void Constructor_LoadsChampionsPresetAndPrediction()
    {
        var vm = CreateViewModel();

        Assert.Equal(FakeDataDragon.Champions.Count, vm.FilteredChampions.Count);
        Assert.Equal("Aatrox", vm.BlueDraftSlots[0].Champion?.Name);
        Assert.Equal("Nautilus", vm.RedDraftSlots[4].Champion?.Name);
        Assert.True(vm.IsCalculated);
        Assert.NotNull(vm.Prediction);
        Assert.InRange(vm.Prediction!.BlueWinProbability, 0, 1);
        Assert.NotNull(vm.PowerSpikes);
    }

    [Fact]
    public void FilterAndSearch_NarrowChampionList()
    {
        var vm = CreateViewModel();

        vm.FilterByRole("Mage");
        Assert.All(vm.FilteredChampions, c => Assert.Contains("Mage", c.Roles));
        Assert.Contains(vm.FilteredChampions, c => c.Name == "Syndra");

        vm.SearchQuery = "ah";
        Assert.Equal("Ahri", Assert.Single(vm.FilteredChampions).Name);

        vm.FilterByRole("All");
        vm.SearchQuery = string.Empty;
        Assert.Equal(FakeDataDragon.Champions.Count, vm.FilteredChampions.Count);
    }

    [Fact]
    public void PickChampion_FillsActiveSlotAndAdvances()
    {
        var vm = CreateViewModel();
        vm.ResetDraft();
        Assert.All(vm.BlueDraftSlots.Concat(vm.RedDraftSlots), s => Assert.Null(s.Champion));
        Assert.Equal(50.0f, vm.BlueTeamAvgWinRate);

        var jinx = FakeDataDragon.Champions.Single(c => c.Name == "Jinx");
        vm.SelectSlot(vm.BlueDraftSlots[3]);
        vm.PickChampion(jinx);

        Assert.Same(jinx, vm.BlueDraftSlots[3].Champion);
        Assert.Same(vm.BlueDraftSlots[4], vm.ActiveSlot);
        Assert.Equal(jinx.WinRate, vm.BlueTeamAvgWinRate, 1);
    }

    [Fact]
    public void Presets_MoveProbabilityTowardsTheLeadingSide()
    {
        var vm = CreateViewModel();

        vm.SetBlueAdvantagePreset();
        var blue = vm.Prediction!.BlueWinProbability;
        vm.ResetToBalanced();
        var balanced = vm.Prediction!.BlueWinProbability;
        vm.SetRedAdvantagePreset();
        var red = vm.Prediction!.BlueWinProbability;

        Assert.True(blue > balanced, $"blue {blue} vs balanced {balanced}");
        Assert.True(red < balanced, $"red {red} vs balanced {balanced}");
        Assert.Equal(0, vm.GoldDiffAt15 + 3500);
    }

    [Fact]
    public void ChangingContext_Recalculates()
    {
        var vm = CreateViewModel();
        vm.SetBlueAdvantagePreset();

        vm.SelectedLobbyTier = GameTier.Iron;
        vm.SelectedSoulType = DragonSoulType.Infernal;
        vm.SoulOwner = TeamSide.Red;
        vm.SelectedAlgorithm = MLAlgorithmType.FastTree;

        Assert.NotNull(vm.Prediction);
        Assert.Contains(GameTier.Challenger, vm.AvailableLobbyTiers);
        Assert.DoesNotContain(GameTier.Unranked, vm.AvailableLobbyTiers);
        Assert.Equal(2, vm.AvailableSides.Length);
        Assert.NotEmpty(vm.AvailableAlgorithms);
    }

    [Fact]
    public async Task Retrain_WithEmptyDatabase_ExplainsWhy()
    {
        var vm = CreateViewModel();

        await vm.RetrainOnRealDataAsync();

        Assert.False(vm.IsRetraining);
        Assert.Contains("5", vm.TrainingStatusText);
    }

    [Fact]
    public async Task ScanLiveGame_ActiveGame_CopiesStateIntoSimulation()
    {
        var live = new LiveGameStats
        {
            IsActiveGame = true,
            GameMode = "CLASSIC",
            GameTimeSeconds = 900,
            BlueKills = 30,
            RedKills = 2,
            BlueTowers = 9,
            RedDragons = 2,
            BlueVoidgrubs = 3,
            BlueHeralds = 1,
            BlueFirstBlood = true,
            EstimatedGoldDiff = 9000,
            EstimatedXpDiff = 1200,
            BlueChampions = new List<string> { "Garen", "Vi" },
            RedChampions = new List<string> { "ahri" }
        };
        var vm = CreateViewModel(live);

        await vm.ScanLiveGameAsync();

        Assert.True(vm.IsLiveGameActive);
        Assert.False(vm.IsCheckingLiveGame);
        Assert.Equal(6000, vm.GoldDiffAt15);   // clamped
        Assert.Equal(15, vm.KillDiffAt15);     // clamped
        Assert.Equal(5, vm.BlueTowerCount);    // clamped
        Assert.Equal("Garen", vm.BlueDraftSlots[0].Champion?.Name);
        Assert.Equal("Ahri", vm.RedDraftSlots[0].Champion?.Name);
    }

    [Fact]
    public async Task ScanLiveGame_NoGame_AndClientError_AreReported()
    {
        var idle = CreateViewModel(new LiveGameStats { IsActiveGame = false });
        await idle.ScanLiveGameAsync();
        Assert.False(idle.IsLiveGameActive);

        var broken = new DraftPredictionViewModel(
            new MatchAnalyticsService(null!, _repo, new MatchPredictionEngine()),
            new FakeDataDragon(),
            new FakeLiveGame(null, fail: true));
        await broken.ScanLiveGameAsync();
        Assert.False(broken.IsLiveGameActive);
        Assert.Contains("client down", broken.LiveGameStatusText);
    }

    [Fact]
    public async Task Benchmark_ProducesReport()
    {
        var vm = CreateViewModel();

        await vm.RunBenchmarkAsync();

        Assert.False(vm.IsBenchmarking);
        Assert.NotNull(vm.BenchmarkReport);
    }

    [Fact]
    public async Task Lab_WithoutTimeline_FallsBackToSliders()
    {
        var vm = CreateViewModel();

        await vm.LoadLabMatchListCommand.ExecuteAsync(null);
        Assert.Empty(vm.LabMatchIds);

        await vm.RefreshLabHorizonsCommand.ExecuteAsync(null);
        Assert.EndsWith("% blue", vm.LabEarlyProbText);
        Assert.Equal("немає timeline", vm.LabMidProbText);

        vm.LabSelectedPhaseMinute = PredictionHorizons.Mid;
        Assert.Contains("25", vm.LabPhaseStatsText);
        vm.LabSelectedPhaseMinute = PredictionHorizons.Early;
        Assert.StartsWith("Фаза 15", vm.LabPhaseStatsText);
    }

    [Fact]
    public async Task Lab_WithTimeline_BuildsSnapshotsForEachHorizon()
    {
        await _repo.SaveMatchAsync(new Match
        {
            MatchId = "EUW1_LAB",
            QueueId = 420,
            GameDurationSeconds = 40 * 60,
            GameCreation = DateTime.UtcNow,
            WinningTeam = TeamSide.Blue
        });
        var api = RiotApiClientTests.CreateClient(url => url.EndsWith("/timeline")
            ? RiotApiClientTests.Json(RiotApiClientTests.TimelineJson(minutes: 40))
            : RiotApiClientTests.Status(HttpStatusCode.NotFound));
        var vm = CreateViewModel(api: api);

        // Setting the id starts a fire-and-forget refresh; wait for it before touching the shared DbContext again.
        vm.LabMatchId = "EUW1_LAB";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!vm.LabStatusText.StartsWith("Match ", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.Contains("15=ok", vm.LabStatusText);
        await vm.LoadLabMatchListCommand.ExecuteAsync(null);
        Assert.Contains("EUW1_LAB", vm.LabMatchIds);
        Assert.EndsWith("% blue", vm.LabEarlyProbText);
        vm.LabSelectedPhaseMinute = PredictionHorizons.Late;
        Assert.StartsWith("@", vm.LabPhaseStatsText);
    }

    private sealed class FakeDataDragon : IDataDragonService
    {
        public static readonly List<ChampionInfo> Champions = new[]
        {
            ("Aatrox", "Fighter", 50.5f), ("Lee Sin", "Fighter", 48.9f), ("Ahri", "Mage", 51.2f),
            ("Jinx", "Marksman", 52.3f), ("Thresh", "Support", 50.1f), ("Ornn", "Tank", 51.0f),
            ("Sejuani", "Tank", 50.4f), ("Syndra", "Mage", 49.6f), ("Kai'Sa", "Marksman", 49.9f),
            ("Nautilus", "Tank", 50.8f), ("Garen", "Fighter", 51.5f), ("Vi", "Fighter", 50.2f)
        }.Select((c, i) => new ChampionInfo
        {
            Id = c.Item1.Replace(" ", "").Replace("'", ""),
            Key = i + 1,
            Name = c.Item1,
            Roles = new List<string> { c.Item2 },
            WinRate = c.Item3
        }).ToList();

        public Task<string> GetLatestGameVersionAsync(CancellationToken ct = default) => Task.FromResult("15.20.1");
        public Task<IReadOnlyList<ChampionInfo>> GetAllChampionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChampionInfo>>(Champions);
        public Task<ChampionInfo?> GetChampionByKeyAsync(int key, CancellationToken ct = default)
            => Task.FromResult(Champions.FirstOrDefault(c => c.Key == key));
        public Task<ChampionInfo?> GetChampionByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(Champions.FirstOrDefault(c => c.Id == id));
        public Task<IReadOnlyList<ChampionInfo>> GetChampionsByRoleAsync(string role, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChampionInfo>>(Champions.Where(c => c.Roles.Contains(role)).ToList());
        public Task PreloadItemDataAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ItemDefinition?> GetItemByIdAsync(int itemId, CancellationToken ct = default)
            => Task.FromResult<ItemDefinition?>(null);
    }

    private sealed class FakeLiveGame(LiveGameStats? stats, bool fail = false) : ILiveGameService
    {
        public Task<LiveGameStats> GetLiveGameStatsAsync(CancellationToken ct = default)
            => fail
                ? Task.FromException<LiveGameStats>(new HttpRequestException("client down"))
                : Task.FromResult(stats ?? new LiveGameStats());

        public Task<bool> IsLiveGameRunningAsync(CancellationToken ct = default)
            => Task.FromResult(!fail && stats?.IsActiveGame == true);
    }
}
