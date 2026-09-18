using CommunityToolkit.Mvvm.ComponentModel;

namespace GameAnalytics.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private PlayerAnalyticsViewModel _playerAnalytics;

    [ObservableProperty]
    private DraftPredictionViewModel _draftPrediction;

    [ObservableProperty]
    private MatchHistoryViewModel _matchHistory;

    [ObservableProperty]
    private SettingsViewModel _settings;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _appTitle = "GameAnalytics v1.0 — Кросплатформна ML-система аналізу LoL";

    [ObservableProperty]
    private string _osInfo;

    public MainViewModel(
        PlayerAnalyticsViewModel playerAnalytics,
        DraftPredictionViewModel draftPrediction,
        MatchHistoryViewModel matchHistory,
        SettingsViewModel settings)
    {
        _playerAnalytics = playerAnalytics;
        _draftPrediction = draftPrediction;
        _matchHistory = matchHistory;
        _settings = settings;

        var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        _osInfo = $"Платформа: {os} ({arch}) | Рантайм: .NET {Environment.Version}";
    }
}
