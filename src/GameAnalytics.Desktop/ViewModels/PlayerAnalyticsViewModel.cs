using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.ML.Engine;

namespace GameAnalytics.Desktop.ViewModels;

public partial class RecentChampionStatViewModel : ObservableObject
{
    public string ChampionName { get; set; } = string.Empty;
    public string NormalizedChampionName => PlayerMatchItemViewModel.NormalizeChampionName(ChampionName);
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{NormalizedChampionName}.png";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _championIcon;

    public int GamesCount { get; set; }
    public int WinsCount { get; set; }
    public double CustomWinRate { get; set; } = -1;
    public double WinRate => CustomWinRate >= 0 ? CustomWinRate : (GamesCount > 0 ? Math.Round((double)WinsCount / GamesCount * 100, 1) : 0);
    public double AvgKda { get; set; }
    public string WinRateText => $"{WinRate:F0}% ({WinsCount}W {GamesCount - WinsCount}L)";
    public string WinRateColor => WinRate >= 60 ? "#0AC8B9" : (WinRate >= 50 ? "#5383E8" : "#E84057");
    public string KdaText => $"{AvgKda:F2}:1 KDA";
}

public partial class PlayerAnalyticsViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly RiotApiOptions _options;
    private readonly IDataDragonService? _dataDragonService;

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
    private Avalonia.Media.Imaging.Bitmap? _profileIconBitmap;

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
    private string _preferredRoleText = "MID";

    [ObservableProperty]
    private bool _hasSessionData;

    [ObservableProperty]
    private int _sessionNetLp;

    [ObservableProperty]
    private string _sessionNetLpText = "0 LP";

    [ObservableProperty]
    private string _sessionNetLpColor = "#8A93A5";

    [ObservableProperty]
    private string _nextTierProgressText = string.Empty;

    [ObservableProperty]
    private int _nextTierProgressValue = 0;

    [ObservableProperty]
    private bool _hasRankProgress;

    [ObservableProperty]
    private GlobalCoachingReport? _coachingReport;

    [ObservableProperty]
    private bool _hasCoachingData;

    [ObservableProperty]
    private int _momentumScore = 50;

    [ObservableProperty]
    private string _momentumScoreText = "50/100";

    [ObservableProperty]
    private string _momentumStatusText = "⚖️ Нейтральна форма";

    [ObservableProperty]
    private string _momentumStatusColor = "#A0A8B6";

    [ObservableProperty]
    private string _momentumStreakText = string.Empty;

    [ObservableProperty]
    private double _nextGameWinProbability = 50.0;

    [ObservableProperty]
    private string _nextGameWinChanceText = "50% Шанс успіху";

    [ObservableProperty]
    private string _aiAdviceText = string.Empty;

    [ObservableProperty]
    private bool _hasMomentumData;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadMoreButtonText))]
    private bool _isLoadingMore;

    public string LoadMoreButtonText => IsLoadingMore ? "ЗАВАНТАЖЕННЯ..." : "ЗАВАНТАЖИТИ ЩЕ 10 МАТЧІВ";

    [ObservableProperty]
    private bool _canLoadMore = true;

    [ObservableProperty]
    private string _statusMessage = "Введіть Riot ID (наприклад, Qu4dyz #qu4) та оберіть сервер";

    /// <summary>Default quiet poll while a profile is open (~100s). Bumps to Retry-After on 429.</summary>
    public static readonly TimeSpan DefaultAutoRefreshInterval = TimeSpan.FromSeconds(100);

    private CancellationTokenSource? _autoRefreshCts;
    private TimeSpan _autoRefreshCooldown = DefaultAutoRefreshInterval;
    private DateTime _nextAutoRefreshUtc = DateTime.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoRefreshToggleText))]
    private bool _isAutoRefreshEnabled = true;

    public string AutoRefreshToggleText => IsAutoRefreshEnabled ? "Пауза авто" : "Увімкнути авто";

    [ObservableProperty]
    private bool _isAutoRefreshing;

    [ObservableProperty]
    private string _autoRefreshStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasLoadedProfile;

    private int _currentMatchCount = 20;
    private List<Match> _rawMatches = new();
    internal List<Match> RawMatches { get => _rawMatches; set => _rawMatches = value; }

    // Internal constructor for unit testing
    internal PlayerAnalyticsViewModel()
    {
        _analyticsService = null!;
        _options = new RiotApiOptions();
        _selectedRegion = AvailableRegions[0];
        UpdateAutoRefreshStatusText();
    }

    public PlayerAnalyticsViewModel(IMatchAnalyticsService analyticsService, RiotApiOptions options, IDataDragonService? dataDragonService = null)
    {
        _analyticsService = analyticsService;
        _options = options;
        _dataDragonService = dataDragonService;

        _selectedRegion = AvailableRegions.FirstOrDefault(r => r.PlatformId.Equals(options.PlatformRegion, StringComparison.OrdinalIgnoreCase))
                          ?? AvailableRegions[0];
        _selectedDemoTier = options.DemoTier;
        _apiKeyInput = options.ApiKey;

        UpdateApiStatus();
        UpdateAutoRefreshStatusText();
        _ = _dataDragonService?.PreloadItemDataAsync();
        _ = SearchPlayerAsync();
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value && HasLoadedProfile && Summoner != null)
        {
            ScheduleNextAutoRefresh(_autoRefreshCooldown);
            StartAutoRefreshLoop();
        }
        else
        {
            StopAutoRefreshLoop();
        }

        UpdateAutoRefreshStatusText();
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
    public async Task SelectPlayerAsync(string? riotId)
    {
        if (string.IsNullOrWhiteSpace(riotId)) return;

        var clean = riotId.Trim();
        if (clean.Contains('#'))
        {
            var parts = clean.Split('#', 2);
            GameName = parts[0].Trim();
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                TagLine = parts[1].Trim();
            }
        }
        else
        {
            GameName = clean;
        }

        await SearchPlayerAsync();
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
            HasLoadedProfile = false;
            StopAutoRefreshLoop();
            _currentMatchCount = 20;
            CanLoadMore = true;
            var gameName = GameName.Trim();
            var tagLine = TagLine.Trim();
            StatusMessage = $"Завантаження даних для {gameName}#{tagLine} ({SelectedRegion.DisplayName})...";

            // Paint local SQLite history immediately so a rate-limited Riot key cannot leave the UI empty.
            var hadCached = await TryShowCachedMatchesAsync(gameName, tagLine);

            SummonerProfile? profile;
            try
            {
                profile = await _analyticsService.FetchAndCacheSummonerAsync(gameName, tagLine);
            }
            catch (GameAnalytics.Core.Exceptions.RiotApiException ex) when (ex.IsRateLimited)
            {
                var sec = ex.RetryAfter.HasValue ? Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)) : 60;
                ApplyRateLimitCooldown(ex.RetryAfter);
                StatusMessage = hadCached
                    ? $"Показано кеш ({RecentMatches.Count} матчів). Riot API ліміт (429) — авто через ~{sec}с. Можливо VPS-краулер вичерпав ключ."
                    : $"Riot API ліміт запитів (429). Повторіть через ~{sec}с. Перевірте, чи VPS-краулер не використовує той самий ключ.";
                if (hadCached)
                {
                    HasLoadedProfile = true;
                    StartAutoRefreshLoop();
                }
                return;
            }
            catch (GameAnalytics.Core.Exceptions.RiotApiException ex) when (ex.IsUnauthorized)
            {
                StatusMessage = hadCached
                    ? $"Показано кеш ({RecentMatches.Count} матчів). Riot API ключ відхилено (401/403) — оновіть ключ у Налаштуваннях."
                    : "Riot API ключ відхилено (401/403). Оновіть ключ у Налаштуваннях або developer.riotgames.com.";
                if (hadCached)
                    HasLoadedProfile = true;
                return;
            }

            if (profile != null)
            {
                Summoner = profile;
                TierBadgeColor = GetTierColor(profile.Tier);
                ProfileIconBitmap = BitmapAssetValueConverter.GetOrLoadBitmap(profile.ProfileIconUrl, bmp => ProfileIconBitmap = bmp);
                StatusMessage = HasApiKey
                    ? $"Профіль {profile.FullName} успішно завантажено з Riot API. Оновлення матчів..."
                    : $"Профіль {profile.FullName} (Демо-режим). Отримання матчів...";

                // Refresh cache filter with real PUUID if we already showed name-matched rows
                if (!hadCached)
                {
                    try
                    {
                        var cachedMatches = await _analyticsService.GetSavedMatchesForPlayerAsync(gameName, tagLine, _currentMatchCount);
                        if (cachedMatches.Count == 0)
                        {
                            var allCached = await _analyticsService.GetSavedMatchHistoryAsync();
                            cachedMatches = allCached
                                .Where(m => m.Participants.Any(p => p.Puuid == profile.Puuid))
                                .OrderByDescending(m => m.GameCreation)
                                .Take(_currentMatchCount)
                                .ToList();
                        }

                        if (cachedMatches.Count > 0 && RecentMatches.Count == 0)
                        {
                            await PopulateMatchesAsync(cachedMatches, profile);
                        }
                    }
                    catch { /* Non-critical cache warm-up */ }
                }

                IReadOnlyList<Match> matches;
                try
                {
                    matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(profile.Puuid, _currentMatchCount);
                }
                catch (GameAnalytics.Core.Exceptions.RiotApiException ex) when (ex.IsRateLimited)
                {
                    var sec = ex.RetryAfter.HasValue ? Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)) : 60;
                    ApplyRateLimitCooldown(ex.RetryAfter);
                    StatusMessage = RecentMatches.Count > 0
                        ? $"Профіль OK, матчі з кешу ({RecentMatches.Count}). Оновлення з Riot заблоковано лімітом — авто через ~{sec}с."
                        : $"Профіль завантажено, але Riot API ліміт (429) під час історії матчів. Повторіть через ~{sec}с.";
                    HasLoadedProfile = true;
                    StartAutoRefreshLoop();
                    return;
                }

                UpdateMomentumAnalysis(matches, profile.Puuid);

                // Preload profile icon, champion icons, and item icons for instant zero-flicker rendering
                var iconsToPreload = new List<string> { profile.ProfileIconUrl };
                foreach (var m in matches)
                {
                    foreach (var p in m.Participants)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ChampionName))
                        {
                            var cleanName = PlayerMatchItemViewModel.NormalizeChampionName(p.ChampionName);
                            iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{cleanName}.png");
                        }
                        if (p.Item0 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item0}.png");
                        if (p.Item1 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item1}.png");
                        if (p.Item2 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item2}.png");
                        if (p.Item3 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item3}.png");
                        if (p.Item4 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item4}.png");
                        if (p.Item5 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item5}.png");
                        if (p.Item6 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item6}.png");
                    }
                }
                // Preload profile icon, champion icons, and item icons in background
                _ = BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

                await _analyticsService.TrackAndReconstructLpAsync(profile, matches);
                await PopulateMatchesAsync(matches, profile);

                StatusMessage = $"Завантажено {RecentMatches.Count} матчів для {profile.FullName} ({SelectedRegion.Code}).";
                HasLoadedProfile = true;
                ScheduleNextAutoRefresh(DefaultAutoRefreshInterval);
                StartAutoRefreshLoop();
            }
            else
            {
                HasLoadedProfile = RecentMatches.Count > 0;
                StatusMessage = hadCached
                    ? $"Показано кеш ({RecentMatches.Count} матчів). Онлайн-профіль {gameName}#{tagLine} на {SelectedRegion.Code} не знайдено."
                    : $"Гравця {gameName}#{tagLine} не знайдено на сервері {SelectedRegion.Code}. Перевірте правильність написання нікнейму та тегу.";
                if (HasLoadedProfile)
                {
                    ScheduleNextAutoRefresh(DefaultAutoRefreshInterval);
                    StartAutoRefreshLoop();
                }
            }
        }
        catch (GameAnalytics.Core.Exceptions.RiotApiException ex) when (ex.IsRateLimited)
        {
            var sec = ex.RetryAfter.HasValue ? Math.Max(1, (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds)) : 60;
            ApplyRateLimitCooldown(ex.RetryAfter);
            StatusMessage = RecentMatches.Count > 0
                ? $"Кеш на екрані ({RecentMatches.Count}). Riot 429 — авто-оновлення через ~{sec}с."
                : $"Riot API ліміт (429). Повторіть через ~{sec}с.";
            if (RecentMatches.Count > 0)
            {
                HasLoadedProfile = true;
                StartAutoRefreshLoop();
            }
        }
        catch (GameAnalytics.Core.Exceptions.RiotApiException ex)
        {
            StatusMessage = $"Помилка Riot API ({ex.StatusCode}): {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            UpdateAutoRefreshStatusText();
        }
    }

    private async Task<bool> TryShowCachedMatchesAsync(string gameName, string tagLine)
    {
        try
        {
            var cachedMatches = await _analyticsService.GetSavedMatchesForPlayerAsync(gameName, tagLine, _currentMatchCount);
            if (cachedMatches.Count == 0)
                return false;

            var sample = cachedMatches
                .SelectMany(m => m.Participants)
                .FirstOrDefault(p =>
                    p.SummonerName.Equals($"{gameName}#{tagLine}", StringComparison.OrdinalIgnoreCase) ||
                    p.SummonerName.StartsWith(gameName + "#", StringComparison.OrdinalIgnoreCase));

            var puuid = sample?.Puuid ?? string.Empty;

            var profile = Summoner;
            if (profile == null || !profile.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                profile = new SummonerProfile
                {
                    Puuid = puuid,
                    GameName = gameName,
                    TagLine = tagLine,
                    SummonerLevel = Summoner?.SummonerLevel ?? 0,
                    ProfileIconId = Summoner?.ProfileIconId ?? 1,
                    Tier = Summoner?.Tier ?? GameTier.Unranked,
                    Rank = Summoner?.Rank ?? string.Empty,
                    LeaguePoints = Summoner?.LeaguePoints ?? 0,
                    Wins = Summoner?.Wins ?? 0,
                    Losses = Summoner?.Losses ?? 0
                };
                Summoner = profile;
                TierBadgeColor = GetTierColor(profile.Tier);
            }

            await PopulateMatchesAsync(cachedMatches, profile);
            StatusMessage = $"Показано {RecentMatches.Count} матчів з локального кешу для {gameName}#{tagLine}. Оновлення з Riot API...";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task PopulateMatchesAsync(IReadOnlyList<Match> matches, SummonerProfile profile)
    {
        UpdateMomentumAnalysis(matches, profile.Puuid);
        var lpMap = await _analyticsService.GetPlayerMatchLpMapAsync(profile.Puuid);

        _rawMatches = matches.ToList();
        RecentMatches.Clear();
        var currentTier = Summoner?.Tier ?? GameTier.Emerald;
        foreach (var m in matches)
        {
            int? overrideLp = lpMap.TryGetValue(m.MatchId, out var delta) ? delta : null;
            RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(
                m,
                profile.Puuid,
                profile.GameName,
                SelectPlayerCommand,
                currentTier,
                mId => _analyticsService.GetMatchTimelineAsync(mId),
                overrideLp,
                profile.WinRate));
        }

        ApplyQueueFilter();
    }

    [RelayCommand]
    public async Task LoadMoreMatchesAsync()
    {
        if (Summoner == null || IsLoading || IsLoadingMore || !CanLoadMore) return;

        try
        {
            IsLoadingMore = true;
            _currentMatchCount += 10;
            StatusMessage = $"Завантаження наступних матчів (всього: {_currentMatchCount})...";

            var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(Summoner.Puuid, _currentMatchCount);

            UpdateMomentumAnalysis(matches, Summoner.Puuid);

            // Preload any newly fetched icons
            var iconsToPreload = new List<string>();
            foreach (var m in matches)
            {
                foreach (var p in m.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(p.ChampionName))
                    {
                        var cleanName = PlayerMatchItemViewModel.NormalizeChampionName(p.ChampionName);
                        iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{cleanName}.png");
                    }
                    if (p.Item0 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item0}.png");
                    if (p.Item1 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item1}.png");
                    if (p.Item2 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item2}.png");
                    if (p.Item3 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item3}.png");
                    if (p.Item4 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item4}.png");
                    if (p.Item5 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item5}.png");
                    if (p.Item6 > 0) iconsToPreload.Add($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{p.Item6}.png");
                }
            }
            // Preload newly fetched icons in background
            _ = BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

            await _analyticsService.TrackAndReconstructLpAsync(Summoner, matches);
            var lpMap = await _analyticsService.GetPlayerMatchLpMapAsync(Summoner.Puuid);

            _rawMatches = matches.ToList();
            RecentMatches.Clear();
            var moreTier = Summoner?.Tier ?? GameTier.Emerald;
            var currentPuuid = Summoner?.Puuid ?? string.Empty;
            var currentGameName = Summoner?.GameName ?? string.Empty;
            var currentFullName = Summoner?.FullName ?? "Player";
            foreach (var m in matches)
            {
                int? overrideLp = lpMap.TryGetValue(m.MatchId, out var delta) ? delta : null;
                RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(
                    m, 
                    currentPuuid, 
                    currentGameName, 
                    SelectPlayerCommand, 
                    moreTier, 
                    mId => _analyticsService.GetMatchTimelineAsync(mId),
                    overrideLp,
                    Summoner?.WinRate ?? 50.0));
            }

            ApplyQueueFilter();

            if (matches.Count < _currentMatchCount)
            {
                CanLoadMore = false;
                StatusMessage = $"Завантажено всі доступні матчі ({RecentMatches.Count} шт.).";
            }
            else
            {
                StatusMessage = $"Завантажено {RecentMatches.Count} матчів для {currentFullName}.";
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

    [RelayCommand]
    public void ToggleAutoRefresh()
    {
        IsAutoRefreshEnabled = !IsAutoRefreshEnabled;
    }

    private void ScheduleNextAutoRefresh(TimeSpan cooldown)
    {
        _autoRefreshCooldown = cooldown < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : cooldown;
        _nextAutoRefreshUtc = DateTime.UtcNow + _autoRefreshCooldown;
        UpdateAutoRefreshStatusText();
    }

    private void ApplyRateLimitCooldown(TimeSpan? retryAfter)
    {
        var retry = retryAfter ?? TimeSpan.FromSeconds(60);
        // Wait out Riot Retry-After, but never poll faster than the default quiet interval.
        var seconds = Math.Max(DefaultAutoRefreshInterval.TotalSeconds, retry.TotalSeconds);
        ScheduleNextAutoRefresh(TimeSpan.FromSeconds(seconds));
    }

    private void StartAutoRefreshLoop()
    {
        StopAutoRefreshLoop();
        if (!IsAutoRefreshEnabled || !HasLoadedProfile || Summoner == null)
        {
            UpdateAutoRefreshStatusText();
            return;
        }

        if (_nextAutoRefreshUtc < DateTime.UtcNow)
            ScheduleNextAutoRefresh(_autoRefreshCooldown);

        _autoRefreshCts = new CancellationTokenSource();
        _ = RunAutoRefreshLoopAsync(_autoRefreshCts.Token);
        UpdateAutoRefreshStatusText();
    }

    private void StopAutoRefreshLoop()
    {
        try { _autoRefreshCts?.Cancel(); }
        catch { /* ignore */ }
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
        IsAutoRefreshing = false;
        UpdateAutoRefreshStatusText();
    }

    private async Task RunAutoRefreshLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var remaining = _nextAutoRefreshUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    UpdateAutoRefreshStatusText();
                    var delay = remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
                    await Task.Delay(delay, ct);
                    continue;
                }

                await QuietRefreshMatchesAsync(ct);
                ScheduleNextAutoRefresh(DefaultAutoRefreshInterval);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (GameAnalytics.Core.Exceptions.RiotApiException ex) when (ex.IsRateLimited)
            {
                ApplyRateLimitCooldown(ex.RetryAfter);
                var sec = (int)Math.Ceiling(_autoRefreshCooldown.TotalSeconds);
                StatusMessage = $"Riot 429 — авто-оновлення призупинено на ~{sec}с (MaxRateLimitBlock / Retry-After).";
            }
            catch (Exception ex)
            {
                ScheduleNextAutoRefresh(DefaultAutoRefreshInterval);
                StatusMessage = $"Авто-оновлення: {ex.Message}";
            }
        }
    }

    private async Task QuietRefreshMatchesAsync(CancellationToken ct)
    {
        if (Summoner == null || IsLoading || IsLoadingMore || IsAutoRefreshing)
            return;

        IsAutoRefreshing = true;
        UpdateAutoRefreshStatusText();
        try
        {
            var previousNewest = RecentMatches.FirstOrDefault()?.MatchId;
            var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(Summoner.Puuid, _currentMatchCount, ct);
            ct.ThrowIfCancellationRequested();

            var newest = matches.OrderByDescending(m => m.GameCreation).FirstOrDefault()?.MatchId;
            var countChanged = matches.Count != _rawMatches.Count;
            var setChanged = !matches.Select(m => m.MatchId).OrderBy(x => x)
                .SequenceEqual(_rawMatches.Select(m => m.MatchId).OrderBy(x => x));

            if (countChanged || setChanged || !string.Equals(previousNewest, newest, StringComparison.OrdinalIgnoreCase))
            {
                await _analyticsService.TrackAndReconstructLpAsync(Summoner, matches, ct);
                await PopulateMatchesAsync(matches, Summoner);
                StatusMessage = $"Авто-оновлення: {RecentMatches.Count} матчів для {Summoner.FullName}.";
            }
            else
            {
                StatusMessage = $"Авто-перевірка: нових матчів немає ({Summoner.FullName}).";
            }
        }
        finally
        {
            IsAutoRefreshing = false;
            UpdateAutoRefreshStatusText();
        }
    }

    private void UpdateAutoRefreshStatusText()
    {
        if (!IsAutoRefreshEnabled)
        {
            AutoRefreshStatusText = "Авто-оновлення: пауза";
            return;
        }

        if (!HasLoadedProfile || Summoner == null)
        {
            AutoRefreshStatusText = "Авто-оновлення: очікує профіль";
            return;
        }

        if (IsAutoRefreshing)
        {
            AutoRefreshStatusText = "Авто-оновлення: синхронізація...";
            return;
        }

        var remaining = _nextAutoRefreshUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        var secs = (int)Math.Ceiling(remaining.TotalSeconds);
        AutoRefreshStatusText = secs <= 0
            ? "Авто-оновлення: зараз..."
            : $"Авто-оновлення через {secs}с";
    }

    internal void UpdateMomentumAnalysis(IReadOnlyList<Match> matches, string puuidOrName)
    {
        if (matches == null || matches.Count == 0)
        {
            HasMomentumData = false;
            return;
        }

        var report = PlayerMomentumAnalyzer.Analyze(matches, puuidOrName);
        MomentumScore = report.MomentumScore;
        MomentumScoreText = report.MomentumScoreText;
        MomentumStatusText = report.StatusText;
        MomentumStatusColor = report.StatusColor;
        MomentumStreakText = report.StreakText;
        NextGameWinProbability = report.NextGameWinProbability;
        NextGameWinChanceText = report.NextGameWinChanceText;
        AiAdviceText = report.AiAdvice;
        HasMomentumData = true;
    }

    internal void ApplyQueueFilter()
    {
        if (RecentMatches.Count == 0)
        {
            FilteredRecentMatches.Clear();
            CalculateSessionSummary(FilteredRecentMatches);
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

        CalculateSessionSummary(FilteredRecentMatches);
        UpdateCoachingReport();
    }

    internal void CalculateSessionSummary(IEnumerable<PlayerMatchItemViewModel> matchesList)
    {
        var list = matchesList.ToList();
        if (list.Count == 0)
        {
            HasSessionData = false;
            SessionNetLp = 0;
            SessionNetLpText = "0 LP";
            SessionNetLpColor = "#8A93A5";
            HasRankProgress = false;
            TopRecentChampions.Clear();
            return;
        }

        HasSessionData = true;

        var regularMatches = list.Where(m => !m.IsRemake).ToList();
        var remakeCount = list.Count(m => m.IsRemake);

        SessionTotalGames = list.Count;
        SessionWins = regularMatches.Count(m => m.IsVictory);
        SessionLosses = regularMatches.Count(m => !m.IsVictory);
        SessionWinRate = regularMatches.Count > 0 ? Math.Round((double)SessionWins / regularMatches.Count * 100, 1) : 0;

        // Net Session LP calculation
        var rankedMatches = list.Where(m => m.HasLpChange).ToList();
        var netLp = rankedMatches.Sum(m => m.LpDelta);
        SessionNetLp = netLp;
        SessionNetLpText = netLp > 0 ? $"+{netLp} LP" : (netLp < 0 ? $"{netLp} LP" : "0 LP");
        SessionNetLpColor = netLp > 0 ? "#0AC8B9" : (netLp < 0 ? "#E84057" : "#8A93A5");

        if (Summoner != null)
        {
            var lp = Summoner.LeaguePoints;
            NextTierProgressValue = Math.Clamp(lp, 0, 100);
            var nextTierName = GetNextTierName(Summoner.Tier, Summoner.Rank);
            NextTierProgressText = $"{lp} / 100 LP до {nextTierName}";
            HasRankProgress = true;
        }
        else
        {
            HasRankProgress = false;
        }

        SessionRecordText = remakeCount > 0
            ? $"{SessionTotalGames} ігор: {SessionWins}W - {SessionLosses}L ({remakeCount} ремейк)"
            : $"{SessionTotalGames} ігор: {SessionWins}W - {SessionLosses}L";

        var avgK = list.Average(m => m.Kills);
        var avgD = list.Average(m => m.Deaths);
        var avgA = list.Average(m => m.Assists);
        AvgKdaNumbers = $"{avgK:F1} / {avgD:F1} / {avgA:F1}";

        var avgRatio = avgD > 0 ? (avgK + avgA) / avgD : (avgK + avgA);
        SessionKdaRatioText = $"{avgRatio:F2}:1 KDA";
        SessionKdaRatioColor = (avgRatio >= 5.0 || avgD == 0) ? "#E6B328" : (avgRatio >= 3.0 ? "#0AC8B9" : "#F0E6D2");

        var avgKp = (int)Math.Round(list.Average(m => m.KillParticipationPercent));
        AvgKpText = $"P/Kill {avgKp}%";

        // Top 3 most played champions in THIS filtered subset
        TopRecentChampions.Clear();
        var champGroups = list
            .GroupBy(m => m.ChampionName)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Count(m => m.IsVictory && !m.IsRemake))
            .Take(3);

        foreach (var g in champGroups)
        {
            var champGames = g.Count();
            var champWins = g.Count(m => m.IsVictory && !m.IsRemake);
            var champDecided = g.Count(m => !m.IsRemake);
            var champWinRate = champDecided > 0 ? Math.Round((double)champWins / champDecided * 100, 1) : 0;
            var champAvgD = g.Average(m => m.Deaths);
            var champKda = champAvgD > 0
                ? (g.Average(m => m.Kills) + g.Average(m => m.Assists)) / champAvgD
                : (g.Average(m => m.Kills) + g.Average(m => m.Assists));

            var champStatVm = new RecentChampionStatViewModel
            {
                ChampionName = g.Key,
                GamesCount = champGames,
                WinsCount = champWins,
                CustomWinRate = champWinRate,
                AvgKda = Math.Round(champKda, 2)
            };
            champStatVm.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(champStatVm.ChampionIconUrl, bmp => champStatVm.ChampionIcon = bmp);
            TopRecentChampions.Add(champStatVm);
        }

        // Preferred role in THIS filtered subset (ignore ARAM for lane distribution unless only ARAM games exist)
        var nonAramGames = list.Where(m => m.PositionName != "ARAM").ToList();
        if (nonAramGames.Count > 0)
        {
            var topRole = nonAramGames
                .GroupBy(m => m.PositionName)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (topRole != null)
            {
                var pct = (int)Math.Round((double)topRole.Count() / nonAramGames.Count * 100);
                PreferredRoleText = $"{topRole.Key} · {pct}%";
            }
        }
        else
        {
            PreferredRoleText = "ARAM · 100%";
        }
    }

    internal void UpdateCoachingReport()
    {
        if (_rawMatches.Count == 0)
        {
            HasCoachingData = false;
            CoachingReport = null;
            return;
        }

        IEnumerable<Match> matches = _rawMatches;
        if (SelectedQueueFilter == "Normal Draft")
        {
            matches = matches.Where(m => m.QueueName.Contains("Normal", StringComparison.OrdinalIgnoreCase) || m.QueueName.Contains("Draft", StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedQueueFilter == "Ranked Solo")
        {
            matches = matches.Where(m => m.QueueName.Contains("Ranked", StringComparison.OrdinalIgnoreCase) || m.QueueName.Contains("Solo", StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedQueueFilter == "ARAM")
        {
            matches = matches.Where(m => m.QueueName.Contains("ARAM", StringComparison.OrdinalIgnoreCase));
        }

        var list = matches.ToList();
        if (list.Count == 0)
        {
            HasCoachingData = false;
            CoachingReport = null;
            return;
        }

        var report = GlobalCoachingAnalyzer.Analyze(list, Summoner?.Puuid ?? GameName);
        CoachingReport = report;
        HasCoachingData = report != null && report.TotalMatchesAnalyzed > 0;
    }

    private static string GetNextTierName(GameTier tier, string rank)
    {
        var cleanRank = rank?.Trim().ToUpperInvariant() ?? "IV";
        return cleanRank switch
        {
            "IV" => $"{tier} III",
            "III" => $"{tier} II",
            "II" => $"{tier} I",
            "I" => tier switch
            {
                GameTier.Iron => "Bronze IV",
                GameTier.Bronze => "Silver IV",
                GameTier.Silver => "Gold IV",
                GameTier.Gold => "Platinum IV",
                GameTier.Platinum => "Emerald IV",
                GameTier.Emerald => "Diamond IV",
                GameTier.Diamond => "Master",
                GameTier.Master => "Grandmaster",
                GameTier.Grandmaster => "Challenger",
                _ => "Peak Rank"
            },
            _ => $"{tier} {cleanRank}"
        };
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
