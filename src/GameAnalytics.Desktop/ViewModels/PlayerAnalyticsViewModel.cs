using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.Infrastructure.Configuration;

namespace GameAnalytics.Desktop.ViewModels;

public class RecentChampionStatViewModel
{
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{ChampionName}.png";
    public int GamesCount { get; set; }
    public int WinsCount { get; set; }
    public double WinRate => GamesCount > 0 ? Math.Round((double)WinsCount / GamesCount * 100, 1) : 0;
    public double AvgKda { get; set; }
    public string WinRateText => $"{WinRate:F0}% ({WinsCount}W {GamesCount - WinsCount}L)";
    public string WinRateColor => WinRate >= 60 ? "#0AC8B9" : (WinRate >= 50 ? "#5383E8" : "#E84057");
    public string KdaText => $"{AvgKda:F2}:1 KDA";
}

public partial class PlayerAnalyticsViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly RiotApiOptions _options;

    [ObservableProperty]
    private string _gameName = "Qu4dyz";

    [ObservableProperty]
    private string _tagLine = "qu4";

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
    private ObservableCollection<PlayerMatchItemViewModel> _filteredRecentMatches = new();

    [ObservableProperty]
    private ObservableCollection<RecentChampionStatViewModel> _topRecentChampions = new();

    [ObservableProperty]
    private string _selectedQueueFilter = "Всі черги";

    public IReadOnlyList<string> AvailableQueueFilters { get; } = new[]
    {
        "Всі черги",
        "Normal Draft",
        "Ranked Solo",
        "ARAM"
    };

    [ObservableProperty]
    private int _sessionTotalGames;

    [ObservableProperty]
    private int _sessionWins;

    [ObservableProperty]
    private int _sessionLosses;

    [ObservableProperty]
    private double _sessionWinRate;

    [ObservableProperty]
    private string _sessionRecordText = string.Empty;

    [ObservableProperty]
    private string _avgKdaNumbers = string.Empty;

    [ObservableProperty]
    private string _sessionKdaRatioText = string.Empty;

    [ObservableProperty]
    private string _sessionKdaRatioColor = "#F0E6D2";

    [ObservableProperty]
    private string _avgKpText = "P/Kill 0%";

    [ObservableProperty]
    private string _preferredRoleText = "MID ⚡";

    [ObservableProperty]
    private bool _hasSessionData;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _canLoadMore = true;

    [ObservableProperty]
    private string _statusMessage = "Введіть Riot ID (наприклад, Qu4dyz #qu4) та оберіть сервер";

    private int _currentMatchCount = 10;

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

    partial void OnSelectedQueueFilterChanged(string value)
    {
        ApplyQueueFilter();
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
            _currentMatchCount = 10;
            CanLoadMore = true;
            StatusMessage = $"Завантаження даних для {GameName}#{TagLine} ({SelectedRegion.DisplayName})...";

            var profile = await _analyticsService.FetchAndCacheSummonerAsync(GameName.Trim(), TagLine.Trim());
            if (profile != null)
            {
                Summoner = profile;
                TierBadgeColor = GetTierColor(profile.Tier);
                StatusMessage = HasApiKey
                    ? $"Профіль {profile.FullName} успішно завантажено з Riot API."
                    : $"Профіль {profile.FullName} (Демо-режим). Отримання матчів...";

                var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(profile.Puuid, _currentMatchCount);

                // Preload profile icon and champion icons for instant zero-flicker rendering
                var iconsToPreload = new List<string> { profile.ProfileIconUrl };
                foreach (var m in matches)
                {
                    foreach (var p in m.Participants)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ChampionName))
                        {
                            iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{p.ChampionName}.png");
                        }
                    }
                }
                await BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

                RecentMatches.Clear();
                foreach (var m in matches)
                {
                    RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(m, profile.Puuid, profile.GameName));
                }

                ApplyQueueFilter();
                CalculateSessionSummary();

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

    [RelayCommand]
    public async Task LoadMoreMatchesAsync()
    {
        if (Summoner == null || IsLoading || IsLoadingMore || !CanLoadMore) return;

        try
        {
            IsLoadingMore = true;
            _currentMatchCount += 5;
            StatusMessage = $"Завантаження наступних матчів (всього: {_currentMatchCount})...";

            var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(Summoner.Puuid, _currentMatchCount);

            // Preload any newly fetched icons
            var iconsToPreload = new List<string>();
            foreach (var m in matches)
            {
                foreach (var p in m.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(p.ChampionName))
                    {
                        iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/14.18.1/img/champion/{p.ChampionName}.png");
                    }
                }
            }
            await BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

            RecentMatches.Clear();
            foreach (var m in matches)
            {
                RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(m, Summoner.Puuid, Summoner.GameName));
            }

            ApplyQueueFilter();
            CalculateSessionSummary();

            if (matches.Count < _currentMatchCount)
            {
                CanLoadMore = false;
                StatusMessage = $"Завантажено всі доступні матчі ({RecentMatches.Count} шт.).";
            }
            else
            {
                StatusMessage = $"Завантажено {RecentMatches.Count} матчів для {Summoner.FullName}.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка завантаження: {ex.Message}";
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void ApplyQueueFilter()
    {
        if (RecentMatches.Count == 0)
        {
            FilteredRecentMatches.Clear();
            return;
        }

        IEnumerable<PlayerMatchItemViewModel> filtered = RecentMatches;

        if (SelectedQueueFilter == "Normal Draft")
        {
            filtered = filtered.Where(m => m.GameModeText.Contains("Normal", StringComparison.OrdinalIgnoreCase) || m.GameModeText.Contains("Draft", StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedQueueFilter == "Ranked Solo")
        {
            filtered = filtered.Where(m => m.GameModeText.Contains("Ranked", StringComparison.OrdinalIgnoreCase) || m.GameModeText.Contains("Solo", StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedQueueFilter == "ARAM")
        {
            filtered = filtered.Where(m => m.GameModeText.Contains("ARAM", StringComparison.OrdinalIgnoreCase));
        }

        FilteredRecentMatches.Clear();
        foreach (var m in filtered)
        {
            FilteredRecentMatches.Add(m);
        }
    }

    private void CalculateSessionSummary()
    {
        if (RecentMatches.Count == 0)
        {
            HasSessionData = false;
            TopRecentChampions.Clear();
            return;
        }

        HasSessionData = true;
        SessionTotalGames = RecentMatches.Count;
        SessionWins = RecentMatches.Count(m => m.IsVictory);
        SessionLosses = RecentMatches.Count(m => !m.IsVictory);
        SessionWinRate = SessionTotalGames > 0 ? Math.Round((double)SessionWins / SessionTotalGames * 100, 1) : 0;
        SessionRecordText = $"{SessionTotalGames} ігор: {SessionWins}W - {SessionLosses}L";

        var avgK = RecentMatches.Average(m => m.Kills);
        var avgD = RecentMatches.Average(m => m.Deaths);
        var avgA = RecentMatches.Average(m => m.Assists);
        AvgKdaNumbers = $"{avgK:F1} / {avgD:F1} / {avgA:F1}";

        var avgRatio = avgD > 0 ? (avgK + avgA) / avgD : (avgK + avgA);
        SessionKdaRatioText = $"{avgRatio:F2}:1 KDA";
        SessionKdaRatioColor = (avgRatio >= 5.0 || avgD == 0) ? "#E6B328" : (avgRatio >= 3.0 ? "#0AC8B9" : "#F0E6D2");

        var avgKp = (int)Math.Round(RecentMatches.Average(m => m.KillParticipationPercent));
        AvgKpText = $"P/Kill {avgKp}%";

        // Top 3 most played champions in recent session
        TopRecentChampions.Clear();
        var champGroups = RecentMatches
            .GroupBy(m => m.ChampionName)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Count(m => m.IsVictory))
            .Take(3);

        foreach (var g in champGroups)
        {
            var champGames = g.Count();
            var champWins = g.Count(m => m.IsVictory);
            var champAvgD = g.Average(m => m.Deaths);
            var champKda = champAvgD > 0
                ? (g.Average(m => m.Kills) + g.Average(m => m.Assists)) / champAvgD
                : (g.Average(m => m.Kills) + g.Average(m => m.Assists));

            TopRecentChampions.Add(new RecentChampionStatViewModel
            {
                ChampionName = g.Key,
                GamesCount = champGames,
                WinsCount = champWins,
                AvgKda = Math.Round(champKda, 2)
            });
        }

        // Preferred role
        var topRole = RecentMatches
            .GroupBy(m => m.PositionName)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (topRole != null)
        {
            var pct = (int)Math.Round((double)topRole.Count() / SessionTotalGames * 100);
            var icon = topRole.FirstOrDefault()?.PositionIcon ?? "⚡";
            PreferredRoleText = $"{topRole.Key} {icon} ({pct}%)";
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
