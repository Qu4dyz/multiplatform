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
    private string _appTitle = "GameAnalytics v1.0 | League of Legends Match Analytics & Prediction";

    [ObservableProperty]
    private string _osInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScale100))]
    [NotifyPropertyChangedFor(nameof(IsScale115))]
    [NotifyPropertyChangedFor(nameof(IsScale125))]
    [NotifyPropertyChangedFor(nameof(Scale100Bg))]
    [NotifyPropertyChangedFor(nameof(Scale100Fg))]
    [NotifyPropertyChangedFor(nameof(Scale115Bg))]
    [NotifyPropertyChangedFor(nameof(Scale115Fg))]
    [NotifyPropertyChangedFor(nameof(Scale125Bg))]
    [NotifyPropertyChangedFor(nameof(Scale125Fg))]
    private double _uiScale = 1.0;

    public bool IsScale100 => Math.Abs(UiScale - 1.0) < 0.01;
    public bool IsScale115 => Math.Abs(UiScale - 1.15) < 0.01;
    public bool IsScale125 => Math.Abs(UiScale - 1.25) < 0.01;

    public string Scale100Bg => IsScale100 ? "#0AC8B9" : "Transparent";
    public string Scale100Fg => IsScale100 ? "#010A13" : "#8A93A5";
    public string Scale115Bg => IsScale115 ? "#0AC8B9" : "Transparent";
    public string Scale115Fg => IsScale115 ? "#010A13" : "#8A93A5";
    public string Scale125Bg => IsScale125 ? "#0AC8B9" : "Transparent";
    public string Scale125Fg => IsScale125 ? "#010A13" : "#8A93A5";

    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale100Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale115Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale125Command { get; }

    internal MainViewModel()
    {
        _playerAnalytics = null!;
        _draftPrediction = null!;
        _matchHistory = null!;
        _settings = null!;

        SetScale100Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => UiScale = 1.0);
        SetScale115Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => UiScale = 1.15);
        SetScale125Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => UiScale = 1.25);

        var os = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        _osInfo = $"Платформа: {os} ({arch}) | Рантайм: .NET {Environment.Version}";
    }

    public MainViewModel(
        PlayerAnalyticsViewModel playerAnalytics,
        DraftPredictionViewModel draftPrediction,
        MatchHistoryViewModel matchHistory,
        SettingsViewModel settings) : this()
    {
        _playerAnalytics = playerAnalytics;
        _draftPrediction = draftPrediction;
        _matchHistory = matchHistory;
        _settings = settings;
    }
}
