using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using GameAnalytics.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GameAnalytics.Tests;

public sealed class DatabaseSyncSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ga_dbswap_{Guid.NewGuid():N}");

    public DatabaseSyncSafetyTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class FakeVpsSync : IVpsSyncService
    {
        private readonly string? _sourceFile;
        private readonly byte[]? _payload;

        public FakeVpsSync(string? sourceFile = null, byte[]? payload = null)
        {
            _sourceFile = sourceFile;
            _payload = payload;
        }

        public List<string> DatabaseTargets { get; } = new();

        public Task<VpsServerStatus?> GetServerStatusAsync(string vpsBaseUrl, CancellationToken ct = default)
            => Task.FromResult<VpsServerStatus?>(null);

        public Task<bool> DownloadModelAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default)
            => Task.FromResult(false);

        public async Task<bool> DownloadDatabaseAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default)
        {
            DatabaseTargets.Add(targetFilePath);
            if (_sourceFile != null)
                File.Copy(_sourceFile, targetFilePath, overwrite: true);
            else if (_payload != null)
                await File.WriteAllBytesAsync(targetFilePath, _payload, ct);
            else
                return false;
            return true;
        }
    }

    private sealed class RecordingEngine : IPredictionEngine
    {
        public List<Match> Trained { get; } = new();

        public PredictionResult PredictMatchOutcome(MatchInputFeatures features) => throw new NotSupportedException();

        public Task TrainModelAsync(IEnumerable<Match> historicalMatches, CancellationToken ct = default)
        {
            Trained.AddRange(historicalMatches);
            return Task.CompletedTask;
        }

        public ModelBenchmarkReport RunAlgorithmsBenchmark(int sampleCount = 2000) => throw new NotSupportedException();
        public void SetActiveAlgorithm(MLAlgorithmType algorithm) { }
        public bool ReloadModel() => false;
        public MLAlgorithmType ActiveAlgorithm => default;
        public ModelMetrics? CurrentModelMetrics => null;
        public bool IsTrainedOnRealData => Trained.Count > 0;
        public int TrainingDatasetSize => Trained.Count;
        public DateTime? LastTrainedAt => null;
    }

    private static GameAnalyticsDbContext CreateContext(string path, bool pooling = true)
    {
        var options = new DbContextOptionsBuilder<GameAnalyticsDbContext>()
            .UseSqlite($"Data Source={path};Pooling={pooling}")
            .Options;
        var context = new GameAnalyticsDbContext(options);
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        return context;
    }

    private static async Task SeedAsync(MatchRepository repo, string prefix, int count)
    {
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
        {
            await repo.SaveMatchAsync(new Match
            {
                MatchId = $"{prefix}_{i:D4}",
                GameCreation = start.AddMinutes(i),
                GameDurationSeconds = 1500,
                WinningTeam = i % 2 == 0 ? TeamSide.Blue : TeamSide.Red,
                Participants = new List<Participant>
                {
                    new() { Puuid = $"{prefix}-p{i}", SummonerName = $"P{i}#EUW", ChampionName = "Ahri", TeamSide = TeamSide.Blue }
                }
            });
        }
    }

    private async Task<string> CreateRemoteDatabaseAsync(int count)
    {
        var remote = Path.Combine(_dir, "remote.db");
        await using (var context = CreateContext(remote, pooling: false))
        {
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;");
            await SeedAsync(new MatchRepository(context), "VPS", count);
        }
        return remote;
    }

    private static SettingsViewModel CreateSettings(IMatchRepository repo, IVpsSyncService vps, string databasePath)
    {
        var vm = new SettingsViewModel(new RiotApiOptions(), apiClient: null!, repo, vps, analyticsService: null!);
        vm.DatabasePath = databasePath;
        return vm;
    }

    private static bool AnySidecar(string dbPath) =>
        SqliteDatabaseFile.SidecarSuffixes.Any(s => File.Exists(dbPath + s));

    [Fact]
    public async Task ReplaceAsync_SwapsFileHeldByPooledConnection_AndDropsOldWal()
    {
        var live = Path.Combine(_dir, "live.db");
        // The context is disposed, but its pooled connection still holds the file and its -wal/-shm.
        await using (var context = CreateContext(live))
            await SeedAsync(new MatchRepository(context), "OLD", 2);
        Assert.True(AnySidecar(live));
        var remote = await CreateRemoteDatabaseAsync(3);
        var temp = SqliteDatabaseFile.CreateDownloadTempPath(live);
        File.Copy(remote, temp);

        await SqliteDatabaseFile.ReplaceAsync(temp, live);

        Assert.False(File.Exists(temp));
        Assert.False(AnySidecar(live));
        await using var check = CreateContext(live, pooling: false);
        var ids = await check.Matches.Select(m => m.MatchId).ToListAsync();
        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.StartsWith("VPS_", id));
    }

    [Fact]
    public async Task ReplaceAsync_DeletesStaleSidecarsLeftByACrashedSession()
    {
        var live = Path.Combine(_dir, "live.db");
        var remote = await CreateRemoteDatabaseAsync(2);
        File.Copy(remote, live);
        await File.WriteAllBytesAsync(live + "-wal", new byte[] { 1, 2, 3 });
        await File.WriteAllBytesAsync(live + "-shm", new byte[] { 4, 5, 6 });
        var temp = SqliteDatabaseFile.CreateDownloadTempPath(live);
        File.Copy(remote, temp);

        await SqliteDatabaseFile.ReplaceAsync(temp, live);

        Assert.False(AnySidecar(live));
        Assert.True(SqliteDatabaseFile.HasSqliteHeader(live));
    }

    [Fact]
    public async Task HasSqliteHeader_RejectsNonSqlitePayloads()
    {
        var remote = await CreateRemoteDatabaseAsync(1);
        var html = Path.Combine(_dir, "error.html");
        await File.WriteAllTextAsync(html, "<html>502 Bad Gateway</html>");

        Assert.True(SqliteDatabaseFile.HasSqliteHeader(remote));
        Assert.False(SqliteDatabaseFile.HasSqliteHeader(html));
        Assert.False(SqliteDatabaseFile.HasSqliteHeader(Path.Combine(_dir, "missing.db")));
    }

    [Fact]
    public async Task SyncDatabaseFromVps_DownloadsToTempThenSwaps_AndHistoryReadsNewData()
    {
        var live = Path.Combine(_dir, "live.db");
        await using var context = CreateContext(live);
        var repo = new MatchRepository(context);
        await SeedAsync(repo, "OLD", 2);

        var vps = new FakeVpsSync(sourceFile: await CreateRemoteDatabaseAsync(4));
        var settings = CreateSettings(repo, vps, live);
        var history = new MatchHistoryViewModel(repo, autoLoad: false);
        await history.LoadMatchesAsync();
        Assert.Equal(2, history.TotalMatchesCount);

        var changed = 0;
        settings.DatabaseChanged += (_, _) => changed++;

        await settings.SyncDatabaseFromVpsAsync();

        var target = Assert.Single(vps.DatabaseTargets);
        Assert.NotEqual(live, target);
        Assert.Equal(Path.GetDirectoryName(live), Path.GetDirectoryName(target));
        Assert.False(File.Exists(target));
        Assert.False(AnySidecar(live));
        Assert.Equal(1, changed);
        Assert.False(settings.IsSyncingDatabase);
        Assert.StartsWith("✅", settings.VpsSyncMessage);
        // No extra multi-GB copies next to the live file.
        Assert.Equal(new[] { "live.db", "remote.db" },
            Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray());

        // The same long-lived repository/context now reads the swapped-in file.
        await history.LoadMatchesAsync();
        Assert.Equal(4, history.TotalMatchesCount);
        Assert.All(history.Matches, m => Assert.StartsWith("VPS_", m.MatchId));
    }

    [Fact]
    public async Task SyncDatabaseFromVps_KeepsLocalDatabase_WhenPayloadIsNotSqlite()
    {
        var live = Path.Combine(_dir, "live.db");
        await using var context = CreateContext(live);
        var repo = new MatchRepository(context);
        await SeedAsync(repo, "OLD", 2);

        var vps = new FakeVpsSync(payload: "<html>oops</html>"u8.ToArray());
        var settings = CreateSettings(repo, vps, live);
        var changed = 0;
        settings.DatabaseChanged += (_, _) => changed++;

        await settings.SyncDatabaseFromVpsAsync();

        Assert.Equal(0, changed);
        Assert.StartsWith("❌", settings.VpsSyncMessage);
        Assert.False(File.Exists(Assert.Single(vps.DatabaseTargets)));
        Assert.Equal(2, await repo.GetTotalMatchesCountAsync());
    }

    [Fact]
    public async Task ClearDatabase_RaisesDatabaseChanged_AfterConfirmation()
    {
        var live = Path.Combine(_dir, "live.db");
        await using var context = CreateContext(live);
        var repo = new MatchRepository(context);
        await SeedAsync(repo, "OLD", 3);

        var settings = CreateSettings(repo, new FakeVpsSync(), live);
        var changed = 0;
        settings.DatabaseChanged += (_, _) => changed++;

        await settings.ClearDatabaseAsync();
        Assert.Equal(0, changed);

        await settings.ClearDatabaseAsync();
        Assert.Equal(1, changed);
        Assert.Equal(0, await repo.GetTotalMatchesCountAsync());
    }

    [Fact]
    public async Task RetrainModelOnSavedMatches_UsesOnlyNewestSlice()
    {
        var path = Path.Combine(_dir, "train.db");
        await using var context = CreateContext(path, pooling: false);
        var repo = new MatchRepository(context);
        await SeedAsync(repo, "M", 8);
        var engine = new RecordingEngine();
        var service = new MatchAnalyticsService(apiClient: null!, repo, engine);

        Assert.True(await service.RetrainModelOnSavedMatchesAsync(maxMatches: 5));

        Assert.Equal(new[] { "M_0007", "M_0006", "M_0005", "M_0004", "M_0003" },
            engine.Trained.Select(m => m.MatchId).ToArray());
    }

    [Fact]
    public async Task RetrainModelOnSavedMatches_DefaultIsCappedAndStillNeedsFiveMatches()
    {
        var path = Path.Combine(_dir, "train.db");
        await using var context = CreateContext(path, pooling: false);
        var repo = new MatchRepository(context);
        await SeedAsync(repo, "M", 4);
        var engine = new RecordingEngine();
        var service = new MatchAnalyticsService(apiClient: null!, repo, engine);

        Assert.InRange(MatchAnalyticsService.DesktopRetrainMaxMatches, 5, 20_000);
        Assert.False(await service.RetrainModelOnSavedMatchesAsync());
        Assert.Empty(engine.Trained);
    }
}
