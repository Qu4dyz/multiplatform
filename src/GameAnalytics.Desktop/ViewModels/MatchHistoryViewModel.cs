using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Desktop.ViewModels;

public partial class MatchHistoryViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly IMatchRepository _repository;

    [ObservableProperty]
    private ObservableCollection<Match> _matches = new();

    [ObservableProperty]
    private Match? _selectedMatch;

    [ObservableProperty]
    private int _totalMatchesCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Завантаження локальної історії з SQLite...";

    public MatchHistoryViewModel(IMatchAnalyticsService analyticsService, IMatchRepository repository)
    {
        _analyticsService = analyticsService;
        _repository = repository;
        _ = LoadMatchesAsync();
    }

    [RelayCommand]
    public async Task LoadMatchesAsync()
    {
        try
        {
            IsLoading = true;
            var list = await _analyticsService.GetSavedMatchHistoryAsync();
            Matches.Clear();
            foreach (var m in list)
            {
                Matches.Add(m);
            }

            TotalMatchesCount = Matches.Count;
            if (Matches.Count > 0 && SelectedMatch == null)
            {
                SelectedMatch = Matches[0];
            }

            StatusMessage = $"В базі SQLite збережено {TotalMatchesCount} матчів.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка завантаження бази даних: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        try
        {
            IsLoading = true;
            await _repository.ClearAllMatchesAsync();
            Matches.Clear();
            SelectedMatch = null;
            TotalMatchesCount = 0;
            StatusMessage = "Базу даних SQLite очищено.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка очищення: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

