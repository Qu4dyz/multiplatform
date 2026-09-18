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

        // Default seed player: Qu4dyz#qu4
        var seedPuuid = "bzDlB4kNrJtV9j-bPmAm__MC_x7XqrrCjR_Y-dehpd3ShWEB1oAHuY4n_-oGab-nFwe2wPHw2N_3VA";
        try
        {
            var profile = await apiClient.GetSummonerByRiotIdAsync("Qu4dyz", "qu4");
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Puuid))
            {
                seedPuuid = profile.Puuid;
            }
        }
        catch { /* Fallback to default PUUID */ }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("[VPS ML Trainer] Отримано сигнал завершення роботи (SIGINT/Ctrl+C)...");
        };

        await worker.RunContinuousTrainingAsync(seedPuuid, batchSize: 20, epochDelay: TimeSpan.FromMinutes(2), Console.WriteLine, cts.Token);
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
