using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Infrastructure.Clients;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using GameAnalytics.Infrastructure.Services;
using GameAnalytics.ML.Engine;
using GameAnalytics.ML.Training;
using Microsoft.Extensions.DependencyInjection;

namespace GameAnalytics.Desktop;

sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        if (args.Any(a => a.Equals("--vps-train", StringComparison.OrdinalIgnoreCase) || a.Equals("--train", StringComparison.OrdinalIgnoreCase)))
        {
            await RunHeadlessVpsTrainerAsync(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task RunHeadlessVpsTrainerAsync(string[] args)
    {
        Console.WriteLine("=========================================================");
        Console.WriteLine("  GameAnalytics Autonomous ML Training Worker (VPS Mode) ");
        Console.WriteLine("=========================================================");

        var services = new ServiceCollection();
        services.AddDbContext<GameAnalyticsDbContext>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddSingleton<RiotRateLimiter>();
        services.AddHttpClient<IRiotApiClient, RiotApiClient>();
        services.AddHttpClient<IDataDragonService, DataDragonService>();

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            var projectConfig = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "appsettings.json");
            if (File.Exists(projectConfig)) configPath = projectConfig;
        }
        var riotOptions = RiotApiOptions.LoadFromFile(configPath);
        services.AddSingleton(riotOptions);
        services.AddSingleton<IPredictionEngine, MatchPredictionEngine>();
        services.AddScoped<VpsTrainingWorker>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var worker = scope.ServiceProvider.GetRequiredService<VpsTrainingWorker>();
        var apiClient = scope.ServiceProvider.GetRequiredService<IRiotApiClient>();
        var predictionEngine = scope.ServiceProvider.GetRequiredService<IPredictionEngine>();
        var matchRepo = scope.ServiceProvider.GetRequiredService<IMatchRepository>();

        var baseDir = AppContext.BaseDirectory;
        var modelDir = Path.Combine(baseDir, "models");
        if (!Directory.Exists(modelDir)) Directory.CreateDirectory(modelDir);
        var dbPath = Path.Combine(baseDir, "game_analytics.db");

        // Cache match count to avoid concurrent DbContext access from HTTP status endpoint
        // while the training loop is using the same scoped repository.
        var cachedMatchCount = new int[1];
        try { cachedMatchCount[0] = await matchRepo.GetTotalMatchesCountAsync(); } catch { }

        var syncServer = new VpsHttpSyncServer(
            port: 5050,
            modelDirectory: modelDir,
            databasePath: dbPath,
            statusProvider: () =>
            {
                var modelFile = Path.Combine(modelDir, "fasttree_model.zip");
                var fi = File.Exists(modelFile) ? new FileInfo(modelFile) : null;
                var metrics = predictionEngine.CurrentModelMetrics;
                var matchesCount = Volatile.Read(ref cachedMatchCount[0]);

                return new GameAnalytics.Core.Entities.VpsServerStatus
                {
                    Status = "online",
                    ServerTime = DateTime.UtcNow.ToString("o"),
                    ActiveAlgorithm = predictionEngine.ActiveAlgorithm.ToString(),
                    TotalMatches = matchesCount,
                    TotalTeamStats = matchesCount * 2,
                    Accuracy = metrics?.Accuracy ?? 0,
                    AreaUnderRocCurve = metrics?.AreaUnderRocCurve ?? 0,
                    F1Score = metrics?.F1Score ?? 0,
                    AccuracyWhenConfident = metrics?.AccuracyWhenConfident ?? 0,
                    ConfidentCoverage = metrics?.ConfidentCoverage ?? 0,
                    BrierScore = metrics?.BrierScore ?? 0,
                    ModelFileExists = fi != null,
                    ModelFileSize = fi?.Length ?? 0,
                    LastModelUpdate = fi?.LastWriteTimeUtc.ToString("o") ?? string.Empty,
                    Epoch = worker.CurrentEpoch
                };
            });

        using var cts = new CancellationTokenSource();
        syncServer.Start(Console.WriteLine, cts.Token);
        var seedPuuid = "bzDlB4kNrJtV9j-bPmAm__MC_x7XqrrCjR_Y-dehpd3ShWEB1oAHuY4n_-oGab-nFwe2wPHw2N_3VA";
        try
        {
            var profile = await apiClient.GetSummonerByRiotIdAsync("Qu4dyz", "qu4");
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Puuid) && !profile.Puuid.StartsWith("puuid-", StringComparison.OrdinalIgnoreCase))
            {
                seedPuuid = profile.Puuid;
            }
        }
        catch { /* Fallback to default PUUID */ }

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("[VPS ML Trainer] Отримано сигнал завершення роботи (SIGINT/Ctrl+C)...");
        };

        // Near personal-key ceiling: app limit is 20/1s + 100/120s per region
        // (confirmed via X-App-Rate-Limit). Match crawl uses europe; rank uses euw1 —
        // separate buckets. Rate limiter paces requests; keep only a short idle gap.
        if (predictionEngine is MatchPredictionEngine mpe)
            mpe.AblationIntervalEpochs = 4;

        // Wrap training with post-epoch match-count refresh for the status API
        await worker.RunContinuousTrainingAsync(
            seedPuuid,
            batchSize: 50,
            epochDelay: TimeSpan.FromMinutes(1),
            logger: msg =>
            {
                Console.WriteLine(msg);
                if (msg.Contains("Всього матчів у базі даних:", StringComparison.Ordinal))
                {
                    // Parse "Всього матчів у базі даних: 951 (...)"
                    var parts = msg.Split(':');
                    if (parts.Length >= 2)
                    {
                        var numPart = parts[1].Trim().Split(' ')[0];
                        if (int.TryParse(numPart, out var n))
                        {
                            Volatile.Write(ref cachedMatchCount[0], n);
                        }
                    }
                }
            },
            ct: cts.Token);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
