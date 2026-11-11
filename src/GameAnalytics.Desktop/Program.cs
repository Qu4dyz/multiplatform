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
        AttachCrashLogging();

        var curveIndex = Array.FindIndex(args, a => a.Equals("--train-curve", StringComparison.OrdinalIgnoreCase));
        if (curveIndex >= 0)
        {
            await RunLearningCurveAsync(curveIndex + 1 < args.Length ? args[curveIndex + 1] : "20000,40000,60000");
            return;
        }

        if (args.Any(a => a.Equals("--vps-train", StringComparison.OrdinalIgnoreCase) || a.Equals("--train", StringComparison.OrdinalIgnoreCase)))
        {
            await RunHeadlessVpsTrainerAsync(args);
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    private static void AttachCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteCrashLog(ex);
            else
                WriteCrashLog(new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception"));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception);
            e.SetObserved();
        };
    }

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            Console.Error.WriteLine(ex);
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameAnalytics",
                "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }

    /// <summary>
    /// One-off experiment: train the early model on the newest N eligible matches for each N and append
    /// the metrics to models-curve/curve.csv. Models go to models-curve/N, production zips stay untouched.
    /// Each N uses a temporal holdout of its own newest 20%, so all test sets cover the latest matches.
    /// </summary>
    private static async Task RunLearningCurveAsync(string sizesArg)
    {
        var sizes = sizesArg
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
            .Where(n => n > 0)
            .ToList();

        using var context = new GameAnalyticsDbContext();
        var repo = new MatchRepository(context);
        var outDir = Path.Combine(AppContext.BaseDirectory, "models-curve");
        Directory.CreateDirectory(outDir);
        var csv = Path.Combine(outDir, "curve.csv");
        if (!File.Exists(csv))
            File.WriteAllText(csv, "utc,rows,loaded,accuracy,auc,f1,brier,confident_accuracy,confident_coverage,seconds\n");

        foreach (var rows in sizes)
        {
            var started = DateTime.UtcNow;
            var matches = await repo.GetNewestMatchesAsync(
                rows + 250, GameAnalytics.Core.Entities.PredictionHorizons.Early * 60, requireMinute15Timeline: true);
            var engine = new MatchPredictionEngine(Path.Combine(outDir, rows.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                MaxTrainRows = rows,
                AblationIntervalEpochs = int.MaxValue // ablation only measures; skip it to keep runs short
            };
            Console.WriteLine($"[Curve] rows={rows}: loaded {matches.Count} matches, training...");
            await engine.TrainModelAsync(matches);

            var m = engine.CurrentModelMetrics;
            var seconds = (DateTime.UtcNow - started).TotalSeconds;
            var line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow:o},{rows},{matches.Count},{m?.Accuracy:F4},{m?.AreaUnderRocCurve:F4},{m?.F1Score:F4},{m?.BrierScore:F4},{m?.AccuracyWhenConfident:F4},{m?.ConfidentCoverage:F4},{seconds:F0}");
            File.AppendAllText(csv, line + "\n");
            Console.WriteLine($"[Curve] {line}");

            repo.ResetTrackedState();
            GC.Collect();
        }
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
        // Headless crawl must wait out Riot Retry-After instead of failing fast like the desktop UI.
        riotOptions.MaxRateLimitBlockSeconds = 0;
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

        // 2GB VPS knobs (personal Riot app still caps at 20/1s + 100/120s per region).
        // Early retrain every 4th epoch; lab m25/m35 every 20th + memory guard + 5k row cap.
        // Documented in docs/vps-deploy.md — do not push past app limits without a production key.
        if (predictionEngine is MatchPredictionEngine mpe)
        {
            mpe.AblationIntervalEpochs = 3;      // ~every 12 epochs with retrain%/4
            mpe.SpecialistIntervalEpochs = 2;    // ~every 8 epochs; reuse prior specialist zips otherwise
            mpe.MaxTrainRows = VpsTrainingWorker.EarlyTrainMaxRows;
        }

        // Wrap training with post-epoch match-count refresh for the status API
        await worker.RunContinuousTrainingAsync(
            seedPuuid,
            batchSize: 56,
            epochDelay: TimeSpan.FromSeconds(90),
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
