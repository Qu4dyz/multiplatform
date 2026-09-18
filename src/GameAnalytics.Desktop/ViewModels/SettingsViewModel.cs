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
    private readonly IVpsSyncService _vpsSyncService;
    private readonly IMatchAnalyticsService _analyticsService;

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

    [ObservableProperty]
    private string _vpsServerUrl = "http://45.77.53.46:5050";

    [ObservableProperty]
    private string _vpsStatusBadge = "⚪ Натисніть 'Перевірити статус' для підключення до хмари";

    [ObservableProperty]
    private bool _isVpsOnline;

    [ObservableProperty]
    private bool _isSyncingModel;

    [ObservableProperty]
    private bool _isSyncingDatabase;

    [ObservableProperty]
    private string _vpsSyncMessage = string.Empty;

    public SettingsViewModel(
        RiotApiOptions options,
        IRiotApiClient apiClient,
        IMatchRepository matchRepository,
        IVpsSyncService vpsSyncService,
        IMatchAnalyticsService analyticsService)
    {
        _options = options;
        _apiClient = apiClient;
        _matchRepository = matchRepository;
        _vpsSyncService = vpsSyncService;
        _analyticsService = analyticsService;

        ApiKey = options.ApiKey;
        VpsServerUrl = string.IsNullOrWhiteSpace(options.VpsServerUrl) ? "http://45.77.53.46:5050" : options.VpsServerUrl;
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
        _options.VpsServerUrl = VpsServerUrl.Trim();

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _options.SaveToFile(configPath);

        StatusMessage = "Налаштування успішно збережено у appsettings.json та активовано в системі.";
    }

    [RelayCommand]
    public async Task CheckVpsStatusAsync()
    {
        VpsSyncMessage = "⏳ Перевірка зв'язку з VPS Cloud Hub...";
        try
        {
            var status = await _vpsSyncService.GetServerStatusAsync(VpsServerUrl);
            if (status != null)
            {
                IsVpsOnline = true;
                VpsStatusBadge = $"🟢 Онлайн (Епоха #{status.Epoch} | {status.TotalMatches} матчів | Точність: {status.Accuracy:P1} | AUC: {status.AreaUnderRocCurve:F3})";
                VpsSyncMessage = $"Зв'язок з сервером успішний! Модель {status.ActiveAlgorithm} важить {status.ModelFileSize / 1024.0:F1} KB, всього {status.TotalMatches} реальних матчів.";
            }
            else
            {
                IsVpsOnline = false;
                VpsStatusBadge = "🔴 Офлайн або порт 5050 недоступний";
                VpsSyncMessage = "Не вдалося отримати статус сервера. Перевірте статус сервісу gameanalytics-trainer на VPS.";
            }
        }
        catch (Exception ex)
        {
            IsVpsOnline = false;
            VpsStatusBadge = "❌ Помилка з'єднання";
            VpsSyncMessage = $"Помилка перевірки статусу: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SyncModelFromVpsAsync()
    {
        IsSyncingModel = true;
        VpsSyncMessage = "⏳ Завантаження найсвіжішої ML-моделі з VPS сервера...";

        try
        {
            var projectModelPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "models", "fasttree_model.zip");
            var baseModelPath = Path.Combine(AppContext.BaseDirectory, "models", "fasttree_model.zip");
            var debugModelPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "bin", "Debug", "net9.0", "models", "fasttree_model.zip");
            var releaseModelPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "bin", "Release", "net9.0", "models", "fasttree_model.zip");

            var success = await _vpsSyncService.DownloadModelAsync(VpsServerUrl, projectModelPath);
            if (success)
            {
                // Replicate to output directories
                try
                {
                    var dir = Path.GetDirectoryName(baseModelPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(projectModelPath, baseModelPath, overwrite: true);

                    var debugDir = Path.GetDirectoryName(debugModelPath);
                    if (!string.IsNullOrEmpty(debugDir) && Directory.Exists(debugDir))
                        File.Copy(projectModelPath, debugModelPath, overwrite: true);

                    var relDir = Path.GetDirectoryName(releaseModelPath);
                    if (!string.IsNullOrEmpty(relDir) && Directory.Exists(relDir))
                        File.Copy(projectModelPath, releaseModelPath, overwrite: true);
                }
                catch { }

                _analyticsService.ReloadModel();

                var info = new FileInfo(projectModelPath);
                VpsSyncMessage = $"✅ Модель FastTree успішно оновлено з VPS ({info.Length / 1024.0:F1} KB) та перезавантажено в пам'ять! ML прогнози використовують найсвіжіші ваги.";
                StatusMessage = VpsSyncMessage;
            }
            else
            {
                VpsSyncMessage = "❌ Не вдалося завантажити модель. Перевірте роботу сервера або спробуйте пізніше.";
            }
        }
        catch (Exception ex)
        {
            VpsSyncMessage = $"❌ Помилка завантаження моделі: {ex.Message}";
        }
        finally
        {
            IsSyncingModel = false;
        }
    }

    [RelayCommand]
    public async Task SyncDatabaseFromVpsAsync()
    {
        IsSyncingDatabase = true;
        VpsSyncMessage = "⏳ Завантаження актуальної бази даних матчів (SQLite) з VPS...";

        try
        {
            var projectDbPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "game_analytics.db");
            var rootDbPath = Path.Combine(Directory.GetCurrentDirectory(), "game_analytics.db");

            var success = await _vpsSyncService.DownloadDatabaseAsync(VpsServerUrl, DatabasePath);
            if (success)
            {
                try
                {
                    if (File.Exists(DatabasePath))
                    {
                        if (DatabasePath != projectDbPath) File.Copy(DatabasePath, projectDbPath, overwrite: true);
                        if (DatabasePath != rootDbPath) File.Copy(DatabasePath, rootDbPath, overwrite: true);
                    }
                }
                catch { }

                UpdateDatabaseSize();
                VpsSyncMessage = $"✅ Базу матчів SQLite успішно завантажено з VPS! Розмір: {DatabaseSizeText}. Історія матчів тепер містить актуальні дані.";
                StatusMessage = VpsSyncMessage;
            }
            else
            {
                VpsSyncMessage = "❌ Не вдалося завантажити базу даних. Перевірте з'єднання з VPS.";
            }
        }
        catch (Exception ex)
        {
            VpsSyncMessage = $"❌ Помилка завантаження бази даних: {ex.Message}";
        }
        finally
        {
            IsSyncingDatabase = false;
        }
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
