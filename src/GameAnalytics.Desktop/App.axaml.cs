using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Desktop.Views;
using GameAnalytics.Infrastructure.Clients;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Persistence;
using GameAnalytics.Infrastructure.Repositories;
using GameAnalytics.Infrastructure.Services;
using GameAnalytics.ML.Engine;
using GameAnalytics.ML.Training;
using Microsoft.Extensions.DependencyInjection;

namespace GameAnalytics.Desktop;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        Services = serviceCollection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Persistence & EF Core SQLite
        services.AddDbContext<GameAnalyticsDbContext>();
        services.AddScoped<IMatchRepository, MatchRepository>();

        // HTTP Client, Rate Limiter & Riot API
        services.AddSingleton<RiotRateLimiter>();
        services.AddHttpClient<IRiotApiClient, RiotApiClient>();
        services.AddHttpClient<IDataDragonService, DataDragonService>();
        services.AddHttpClient<IVpsSyncService, VpsSyncService>();
        services.AddSingleton<ILiveGameService>(sp =>
        {
            var client = LiveGameService.CreateLiveClient();
            return new LiveGameService(client);
        });
        // Riot API configuration loaded from appsettings.json
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            var projectConfig = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "appsettings.json");
            if (File.Exists(projectConfig)) configPath = projectConfig;
        }
        var riotOptions = RiotApiOptions.LoadFromFile(configPath);
        services.AddSingleton(riotOptions);

        // ML Engine
        services.AddSingleton<IPredictionEngine, MatchPredictionEngine>();

        // Domain Services
        services.AddScoped<IMatchAnalyticsService, MatchAnalyticsService>();
        services.AddScoped<VpsTrainingWorker>();

        // ViewModels
        services.AddTransient<PlayerAnalyticsViewModel>();
        services.AddTransient<DraftPredictionViewModel>();
        services.AddTransient<MatchHistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
    }
}