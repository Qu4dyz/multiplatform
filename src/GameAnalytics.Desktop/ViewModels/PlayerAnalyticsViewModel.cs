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

public class RecentChampionStatViewModel
{
    public string ChampionName { get; set; } = string.Empty;
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{ChampionName}.png";
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

    public string LoadMoreButtonText => IsLoadingMore ? "ЗАВАНТАЖЕННЯ..." : "ЗАВАНТАЖИТИ ЩЕ 5 МАТЧІВ";

    [ObservableProperty]
    private bool _canLoadMore = true;

    [ObservableProperty]
    private string _statusMessage = "Введіть Riot ID (наприклад, Qu4dyz #qu4) та оберіть сервер";

    private int _currentMatchCount = 10;
    private List<Match> _rawMatches = new();
    internal List<Match> RawMatches { get => _rawMatches; set => _rawMatches = value; }

    // Internal constructor for unit testing
    internal PlayerAnalyticsViewModel()
    {
        _analyticsService = null!;
        _options = new RiotApiOptions();
        _selectedRegion = AvailableRegions[0];
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
        _ = _dataDragonService?.PreloadItemDataAsync();
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
                await BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

                _rawMatches = matches.ToList();
                RecentMatches.Clear();
                foreach (var m in matches)
                {
                    RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(m, profile.Puuid, profile.GameName, SelectPlayerCommand));
                }

                ApplyQueueFilter();

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
            await BitmapAssetValueConverter.PreloadImagesAsync(iconsToPreload);

            _rawMatches = matches.ToList();
            RecentMatches.Clear();
            foreach (var m in matches)
            {
                RecentMatches.Add(PlayerMatchItemViewModel.FromMatch(m, Summoner.Puuid, Summoner.GameName, SelectPlayerCommand));
            }

            ApplyQueueFilter();

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

            TopRecentChampions.Add(new RecentChampionStatViewModel
            {
                ChampionName = g.Key,
                GamesCount = champGames,
                WinsCount = champWins,
                CustomWinRate = champWinRate,
                AvgKda = Math.Round(champKda, 2)
            });
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
                var icon = topRole.FirstOrDefault()?.PositionIcon ?? "⚡";
                PreferredRoleText = $"{topRole.Key} {icon} ({pct}%)";
            }
        }
        else
        {
            PreferredRoleText = "ARAM 🎲 (100%)";
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
