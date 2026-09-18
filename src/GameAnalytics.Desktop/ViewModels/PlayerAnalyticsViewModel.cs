using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Desktop.ViewModels;

public partial class PlayerAnalyticsViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;

    [ObservableProperty]
    private string _gameName = "Qu4dyz";

    [ObservableProperty]
    private string _tagLine = "EUW";

    [ObservableProperty]
    private SummonerProfile? _summoner;

    [ObservableProperty]
    private ObservableCollection<Match> _recentMatches = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Введіть Riot ID (наприклад, Qu4dyz #EUW) та натисніть «Знайти»";

    public PlayerAnalyticsViewModel(IMatchAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
        // Pre-load default profile
        _ = SearchPlayerAsync();
    }

    [RelayCommand]
    public async Task SearchPlayerAsync()
    {
        if (string.IsNullOrWhiteSpace(GameName))
        {
            StatusMessage = "Помилка: введіть ім'я гравця (Game Name)";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = $"Завантаження даних для {GameName}#{TagLine}...";

            var profile = await _analyticsService.FetchAndCacheSummonerAsync(GameName.Trim(), TagLine.Trim());
            if (profile != null)
            {
                Summoner = profile;
                StatusMessage = $"Профіль {profile.FullName} успішно завантажено. Отримання останніх матчів...";

                var matches = await _analyticsService.FetchAndSaveRecentMatchesAsync(profile.Puuid, 5);
                RecentMatches.Clear();
                foreach (var m in matches)
                {
                    RecentMatches.Add(m);
                }

                StatusMessage = $"Завантажено {matches.Count} останніх матчів (кешовано в SQLite).";
            }
            else
            {
                StatusMessage = $"Гравця {GameName}#{TagLine} не знайдено.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка підключення: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

