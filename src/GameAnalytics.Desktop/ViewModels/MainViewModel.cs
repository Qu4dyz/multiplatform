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
    [NotifyPropertyChangedFor(nameof(IsTab0))]
    [NotifyPropertyChangedFor(nameof(IsTab1))]
    [NotifyPropertyChangedFor(nameof(IsTab2))]
    [NotifyPropertyChangedFor(nameof(IsTab3))]
    [NotifyPropertyChangedFor(nameof(Tab0Bg))]
    [NotifyPropertyChangedFor(nameof(Tab1Bg))]
    [NotifyPropertyChangedFor(nameof(Tab2Bg))]
    [NotifyPropertyChangedFor(nameof(Tab3Bg))]
    [NotifyPropertyChangedFor(nameof(Tab0Fg))]
    [NotifyPropertyChangedFor(nameof(Tab1Fg))]
    [NotifyPropertyChangedFor(nameof(Tab2Fg))]
    [NotifyPropertyChangedFor(nameof(Tab3Fg))]
    [NotifyPropertyChangedFor(nameof(Tab0Border))]
    [NotifyPropertyChangedFor(nameof(Tab1Border))]
    [NotifyPropertyChangedFor(nameof(Tab2Border))]
    [NotifyPropertyChangedFor(nameof(Tab3Border))]
    private int _selectedTabIndex;

    public bool IsTab0 => SelectedTabIndex == 0;
    public bool IsTab1 => SelectedTabIndex == 1;
    public bool IsTab2 => SelectedTabIndex == 2;
    public bool IsTab3 => SelectedTabIndex == 3;

    public string Tab0Bg => IsTab0 ? "#7C3AED" : "Transparent";
    public string Tab1Bg => IsTab1 ? "#7C3AED" : "Transparent";
    public string Tab2Bg => IsTab2 ? "#7C3AED" : "Transparent";
    public string Tab3Bg => IsTab3 ? "#7C3AED" : "Transparent";

    public string Tab0Fg => IsTab0 ? "#FFFFFF" : "#94A3B8";
    public string Tab1Fg => IsTab1 ? "#FFFFFF" : "#94A3B8";
    public string Tab2Fg => IsTab2 ? "#FFFFFF" : "#94A3B8";
    public string Tab3Fg => IsTab3 ? "#FFFFFF" : "#94A3B8";

    public string Tab0Border => IsTab0 ? "#A78BFA" : "Transparent";
    public string Tab1Border => IsTab1 ? "#A78BFA" : "Transparent";
    public string Tab2Border => IsTab2 ? "#A78BFA" : "Transparent";
    public string Tab3Border => IsTab3 ? "#A78BFA" : "Transparent";

    public CommunityToolkit.Mvvm.Input.IRelayCommand SelectTab0Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SelectTab1Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SelectTab2Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SelectTab3Command { get; }

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

    public string Scale100Bg => IsScale100 ? "#7C3AED" : "Transparent";
    public string Scale100Fg => IsScale100 ? "#FFFFFF" : "#94A3B8";
    public string Scale115Bg => IsScale115 ? "#7C3AED" : "Transparent";
    public string Scale115Fg => IsScale115 ? "#FFFFFF" : "#94A3B8";
    public string Scale125Bg => IsScale125 ? "#7C3AED" : "Transparent";
    public string Scale125Fg => IsScale125 ? "#FFFFFF" : "#94A3B8";

    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale100Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale115Command { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand SetScale125Command { get; }

    internal MainViewModel()
    {
        _playerAnalytics = null!;
        _draftPrediction = null!;
        _matchHistory = null!;
        _settings = null!;

        SelectTab0Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SelectedTabIndex = 0);
        SelectTab1Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SelectedTabIndex = 1);
        SelectTab2Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SelectedTabIndex = 2);
        SelectTab3Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SelectedTabIndex = 3);

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
