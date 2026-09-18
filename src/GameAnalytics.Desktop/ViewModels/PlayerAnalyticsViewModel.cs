using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Infrastructure.Configuration;

namespace GameAnalytics.Desktop.ViewModels;

public partial class PlayerAnalyticsViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly RiotApiOptions _options;

    [ObservableProperty]
    private string _gameName = "Qu4dyz";

    [ObservableProperty]
    private string _tagLine = "EUW";

    [ObservableProperty]
    private ServerRegionInfo _selectedRegion;

    public IReadOnlyList<ServerRegionInfo> AvailableRegions => RiotApiOptions.AvailableRegions;

    [ObservableProperty]
    private GameTier _selectedDemoTier;

    public GameTier[] AvailableTiers => Enum.GetValues<GameTier>();

    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private bool _hasApiKey;

    [ObservableProperty]
    private string _apiStatusText = string.Empty;

    [ObservableProperty]
    private SummonerProfile? _summoner;

    [ObservableProperty]
    private string _tierBadgeColor = "#E6B328";

    [ObservableProperty]
    private ObservableCollection<PlayerMatchItemViewModel> _recentMatches = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Введіть Riot ID (наприклад, Qu4dyz #EUW) та оберіть сервер";

    public PlayerAnalyticsViewModel(IMatchAnalyticsService analyticsService, RiotApiOptions options)
    {
        _analyticsService = analyticsService;
        _options = options;

        _selectedRegion = AvailableRegions.FirstOrDefault(r => r.PlatformId.Equals(options.PlatformRegion, StringComparison.OrdinalIgnoreCase))
                          ?? AvailableRegions[0];
        _selectedDemoTier = options.DemoTier;
        _apiKeyInput = options.ApiKey;

        UpdateApiStatus();
        _ = SearchPlayerAsync();
    }

    partial void OnSelectedRegionChanged(ServerRegionInfo value)
    {
        if (value != null)
        {
            _options.SetRegionByCode(value.PlatformId);
            if (string.IsNullOrWhiteSpace(TagLine) || AvailableRegions.Any(r => r.DefaultTag.Equals(TagLine, StringComparison.OrdinalIgnoreCase)))
            {
                TagLine = value.DefaultTag;
            }
        }
    }

    partial void OnSelectedDemoTierChanged(GameTier value)
    {
        _options.DemoTier = value;
        if (!_options.HasValidApiKey && Summoner != null)
        {
            Summoner.Tier = value;
            TierBadgeColor = GetTierColor(value);
        }
    }

    private void UpdateApiStatus()
    {
        HasApiKey = _options.HasValidApiKey;
        ApiStatusText = HasApiKey
            ? "🟢 Live Riot API (Живі дані з офіційних серверів)"
            : "🟡 Демо-режим (введіть Riot API Key для реальної статистики)";
    }

    [RelayCommand]
    public void SaveApiKey()
    {
        _options.ApiKey = ApiKeyInput.Trim();
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _options.SaveToFile(configPath);
        UpdateApiStatus();
        StatusMessage = HasApiKey
            ? "Ключ Riot API збережено! Завантаження реальної статистики..."
            : "Ключ скинуто. Перемкнено в автономний демо-режим.";

        _ = SearchPlayerAsync();
    }

    [RelayCommand]
    public async Task SearchPlayerAsync()
    {
        // Parse "Name#TAG" if entered into GameName input
        if (GameName.Contains('#'))
        {
            var parts = GameName.Split('#', 2);
            GameName = parts[0].Trim();
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                TagLine = parts[1].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(GameName))
        {
            StatusMessage = "Помилка: введіть ігрове ім'я (Game Name)";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Завантаження даних для {GameName}#{TagLine} ({SelectedRegion.DisplayName})...";

            var profile = await _analyticsService.FetchAndCacheSummonerAsync(GameName.Trim(), TagLine.Trim());
            if (profile != null)
            {
                Summoner = profile;
                TierBadgeColor = GetTierColor(profile.Tier);
                StatusMessage = HasApiKey
                    ? $"Профіль {profile.FullName} успішно завантажено з Riot API."
                    : $"Профіль {profile.FullName} (Демо-режим). Отримання матчів...";

                var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(profile.Puuid, 5);
                RecentMatches.Clear();
                foreach (var m in matches)
                {
                    RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(m, profile.GameName));
                }

                StatusMessage = $"Завантажено {RecentMatches.Count} матчів для {profile.FullName} ({SelectedRegion.Code}).";
            }
            else
            {
                StatusMessage = $"Гравця {GameName}#{TagLine} не знайдено на сервері {SelectedRegion.Code}. Перевірте правильність написання нікнейму та тегу.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetTierColor(GameTier tier) => tier switch
    {
        GameTier.Iron => "#8C8C8C",
        GameTier.Bronze => "#A3654C",
        GameTier.Silver => "#8BA3A8",
        GameTier.Gold => "#E6B328",
        GameTier.Platinum => "#2CC496",
        GameTier.Emerald => "#00B26E",
        GameTier.Diamond => "#576BCE",
        GameTier.Master => "#9D4EDB",
        GameTier.Grandmaster => "#E84057",
        GameTier.Challenger => "#F1B942",
        _ => "#A09B8C"
    };
}
