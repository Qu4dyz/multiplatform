using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Desktop.ViewModels;

public partial class MatchHistoryViewModel : ViewModelBase
{
    /// <summary>Rows fetched per page. A VPS-synced database can hold tens of thousands of matches.</summary>
    public const int PageSize = 50;

    private readonly IMatchRepository _repository;

    [ObservableProperty]
    private ObservableCollection<Match> _matches = new();

    [ObservableProperty]
    private Match? _selectedMatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    [NotifyPropertyChangedFor(nameof(LoadMoreText))]
    private int _totalMatchesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    [NotifyPropertyChangedFor(nameof(LoadMoreText))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClearButtonText))]
    private bool _isClearConfirmPending;

    [ObservableProperty]
    private string _statusMessage = "Завантаження локальної історії з SQLite...";

    public bool CanLoadMore => !IsLoading && Matches.Count < TotalMatchesCount;

    public string LoadMoreText => IsLoading
        ? "Завантаження..."
        : $"Показати ще ({Matches.Count} з {TotalMatchesCount})";

    public string ClearButtonText => IsClearConfirmPending ? "⚠ Точно очистити?" : "🗑 Очистити кеш";

    public MatchHistoryViewModel(IMatchRepository repository) : this(repository, autoLoad: true)
    {
    }

    internal MatchHistoryViewModel(IMatchRepository repository, bool autoLoad)
    {
        _repository = repository;
        if (autoLoad)
            _ = LoadMatchesAsync();
    }

    [RelayCommand]
    public async Task LoadMatchesAsync()
    {
        var previousId = SelectedMatch?.MatchId;
        try
        {
            IsLoading = true;
            IsClearConfirmPending = false;
            TotalMatchesCount = await _repository.GetTotalMatchesCountAsync();
            var page = await _repository.GetRecentMatchesPageAsync(0, PageSize);

            Matches.Clear();
            foreach (var m in page)
                Matches.Add(m);

            // Keep the same match selected after a refresh instead of a stale detached instance.
            SelectedMatch = Matches.FirstOrDefault(m => m.MatchId == previousId) ?? Matches.FirstOrDefault();
            UpdateStatus();
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
    public async Task LoadMoreMatchesAsync()
    {
        if (!CanLoadMore) return;

        try
        {
            IsLoading = true;
            var page = await _repository.GetRecentMatchesPageAsync(Matches.Count, PageSize);
            var known = Matches.Select(m => m.MatchId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var m in page.Where(m => known.Add(m.MatchId)))
                Matches.Add(m);

            // Rows deleted underneath us: stop offering more instead of looping on empty pages.
            if (page.Count == 0)
                TotalMatchesCount = Matches.Count;

            UpdateStatus();
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

    /// <summary>First click arms the action, the second one wipes the database.</summary>
    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        if (!IsClearConfirmPending)
        {
            IsClearConfirmPending = true;
            StatusMessage = "Натисніть ще раз, щоб видалити всі матчі з локальної бази SQLite.";
            return;
        }

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
            IsClearConfirmPending = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CancelClear()
    {
        if (!IsClearConfirmPending) return;
        IsClearConfirmPending = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(LoadMoreText));
        StatusMessage = TotalMatchesCount > Matches.Count
            ? $"В базі SQLite збережено {TotalMatchesCount} матчів. Показано найновіші {Matches.Count}."
            : $"В базі SQLite збережено {TotalMatchesCount} матчів.";
    }
}
