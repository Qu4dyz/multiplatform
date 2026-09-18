using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string _platformRegion = "eun1";

    [ObservableProperty]
    private string _routingRegion = "europe";

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
        PlatformRegion = options.PlatformRegion;
        RoutingRegion = options.RoutingRegion;
        UseMockFallback = options.UseMockFallback;

        DatabasePath = Path.Combine(AppContext.BaseDirectory, "game_analytics.db");
        UpdateDatabaseSize();

        OsDescription = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})";
        DotnetVersion = Environment.Version.ToString();
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _options.ApiKey = ApiKey.Trim();
        _options.PlatformRegion = PlatformRegion;
        _options.RoutingRegion = RoutingRegion;
        _options.UseMockFallback = UseMockFallback;

        StatusMessage = "Налаштування успішно збережено та активовано в системі.";
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        StatusMessage = "Перевірка зв'язку з Riot Games API...";
        try
        {
            var profile = await _apiClient.GetSummonerByRiotIdAsync("Qu4dyz", "EUW");
            if (profile != null)
            {
                StatusMessage = $"Підключення успішне! Отримано тестовий профіль {profile.FullName} (Ранг: {profile.Tier}).";
            }
            else
            {
                StatusMessage = "API повернув порожню відповідь. Перевірте валідність Riot API ключа.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка перевірки: {ex.Message}";
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
