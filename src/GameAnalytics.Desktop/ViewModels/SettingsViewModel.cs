using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Infrastructure.Configuration;

namespace GameAnalytics.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly RiotApiOptions _options;
    private readonly IRiotApiClient _apiClient;
    private readonly IMatchRepository _matchRepository;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private ServerRegionInfo _selectedRegion;

    public IReadOnlyList<ServerRegionInfo> AvailableRegions => RiotApiOptions.AvailableRegions;

    [ObservableProperty]
    private GameTier _selectedDemoTier;

    public GameTier[] AvailableTiers => Enum.GetValues<GameTier>();

    [ObservableProperty]
    private bool _useMockFallback = true;

    [ObservableProperty]
    private string _databasePath;

    [ObservableProperty]
    private string _databaseSizeText = "Не створено або порожньо";

    [ObservableProperty]
    private string _statusMessage = "Налаштування готові.";

    [ObservableProperty]
    private string _osDescription;

    [ObservableProperty]
    private string _dotnetVersion;

    [ObservableProperty]
    private string _vpsHostInfo = "45.77.53.46 (Ubuntu 24.04 LTS Noble, x86_64, .NET 9.0.318)";

    public SettingsViewModel(
        RiotApiOptions options,
        IRiotApiClient apiClient,
        IMatchRepository matchRepository)
    {
        _options = options;
        _apiClient = apiClient;
        _matchRepository = matchRepository;

        ApiKey = options.ApiKey;
        SelectedRegion = AvailableRegions.FirstOrDefault(r => r.PlatformId.Equals(options.PlatformRegion, StringComparison.OrdinalIgnoreCase))
                         ?? AvailableRegions[0];
        SelectedDemoTier = options.DemoTier;
        UseMockFallback = options.UseMockFallback;

        var baseDb = Path.Combine(AppContext.BaseDirectory, "game_analytics.db");
        var projDb = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "game_analytics.db");
        var rootDb = Path.Combine(Directory.GetCurrentDirectory(), "game_analytics.db");

        DatabasePath = File.Exists(baseDb) ? baseDb : (File.Exists(projDb) ? projDb : (File.Exists(rootDb) ? rootDb : baseDb));
        UpdateDatabaseSize();

        OsDescription = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";
        DotnetVersion = Environment.Version.ToString();
    }

    partial void OnSelectedRegionChanged(ServerRegionInfo value)
    {
        if (value != null)
        {
            _options.SetRegionByCode(value.PlatformId);
        }
    }

    partial void OnSelectedDemoTierChanged(GameTier value)
    {
        _options.DemoTier = value;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _options.ApiKey = ApiKey.Trim();
        _options.PlatformRegion = SelectedRegion.PlatformId;
        _options.RoutingRegion = SelectedRegion.RoutingRegion;
        _options.UseMockFallback = UseMockFallback;
        _options.DemoTier = SelectedDemoTier;

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _options.SaveToFile(configPath);

        StatusMessage = "Налаштування успішно збережено у appsettings.json та активовано в системі.";
    }

    [RelayCommand]
    public void OpenRiotPortal()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://developer.riotgames.com/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не вдалося відкрити браузер: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (!_options.HasValidApiKey)
        {
            StatusMessage = "Увага: Riot API Key не вказано або має недійсний формат. Система працює в автономному демо-режимі.";
            return;
        }

        StatusMessage = $"Перевірка зв'язку з Riot Games API на сервері {SelectedRegion.Code} ({SelectedRegion.RoutingRegion})...";
        try
        {
            var profile = await _apiClient.GetSummonerByRiotIdAsync("Qu4dyz", SelectedRegion.DefaultTag);
            if (profile != null)
            {
                StatusMessage = $"✅ Зв'язок з Riot API встановлено! Отримано дані гравця: {profile.FullName}, Рівень: {profile.SummonerLevel}, Ранг: {profile.Tier} {profile.Rank}.";
            }
            else
            {
                StatusMessage = "⚠️ Відповідь Riot API: акаунт не знайдено, або ключ закінчився (403). Оновіть ключ на developer.riotgames.com.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Помилка підключення до API: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ClearDatabaseAsync()
    {
        await _matchRepository.ClearAllMatchesAsync();
        UpdateDatabaseSize();
        StatusMessage = "Базу даних SQLite очищено.";
    }

    private void UpdateDatabaseSize()
    {
        if (File.Exists(DatabasePath))
        {
            var info = new FileInfo(DatabasePath);
            DatabaseSizeText = $"{info.Length / 1024.0:F1} KB ({info.Length} байт)";
        }
        else
        {
            DatabaseSizeText = "Файл БД ще не створено (буде створено автоматично при першому збереженні).";
        }
    }
}
