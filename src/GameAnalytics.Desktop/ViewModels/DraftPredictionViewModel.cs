using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Desktop.ViewModels;

public partial class DraftPredictionViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly IDataDragonService _dataDragonService;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<ChampionInfo> _availableChampions = new();

    [ObservableProperty]
    private ChampionInfo? _selectedChampion;

    [ObservableProperty]
    private string _patchVersion = "14.18.1";

    [ObservableProperty]
    private bool _blueFirstBlood = true;

    [ObservableProperty]
    private bool _blueFirstTower = true;

    [ObservableProperty]
    private bool _blueFirstDragon = true;

    [ObservableProperty]
    private int _goldDiffAt15 = 2400;

    [ObservableProperty]
    private int _killDiffAt15 = 4;

    [ObservableProperty]
    private float _blueTeamAvgWinRate = 53.2f;

    [ObservableProperty]
    private float _redTeamAvgWinRate = 48.8f;

    [ObservableProperty]
    private int _blueTowerCount = 2;

    [ObservableProperty]
    private int _redTowerCount = 0;

    [ObservableProperty]
    private int _blueDragonCount = 2;

    [ObservableProperty]
    private int _redDragonCount = 0;

    [ObservableProperty]
    private PredictionResult? _prediction;

    [ObservableProperty]
    private bool _isCalculated;

    public DraftPredictionViewModel(IMatchAnalyticsService analyticsService, IDataDragonService dataDragonService)
    {
        _analyticsService = analyticsService;
        _dataDragonService = dataDragonService;
        _ = LoadChampionsAsync();
        CalculatePrediction();
    }

    private async Task LoadChampionsAsync()
    {
        try
        {
            PatchVersion = await _dataDragonService.GetLatestGameVersionAsync();
            var list = await _dataDragonService.GetAllChampionsAsync();
            AvailableChampions.Clear();
            foreach (var champ in list)
            {
                AvailableChampions.Add(champ);
            }
        }
        catch
        {
            // Handled via fallback
        }
    }

    [RelayCommand]
    public void CalculatePrediction()
    {
        var features = new MatchInputFeatures
        {
            BlueFirstBlood = BlueFirstBlood,
            BlueFirstTower = BlueFirstTower,
            BlueFirstDragon = BlueFirstDragon,
            GoldDiffAt15 = GoldDiffAt15,
            KillDiffAt15 = KillDiffAt15,
            BlueTeamAvgWinRate = BlueTeamAvgWinRate,
            RedTeamAvgWinRate = RedTeamAvgWinRate,
            BlueTowerCount = BlueTowerCount,
            RedTowerCount = RedTowerCount,
            BlueDragonCount = BlueDragonCount,
            RedDragonCount = RedDragonCount
        };

        Prediction = _analyticsService.PredictOutcome(features);
        IsCalculated = true;
    }

    [RelayCommand]
    public void ResetToBalanced()
    {
        BlueFirstBlood = false;
        BlueFirstTower = false;
        BlueFirstDragon = false;
        GoldDiffAt15 = 0;
        KillDiffAt15 = 0;
        BlueTeamAvgWinRate = 50.0f;
        RedTeamAvgWinRate = 50.0f;
        BlueTowerCount = 0;
        RedTowerCount = 0;
        BlueDragonCount = 0;
        RedDragonCount = 0;
        CalculatePrediction();
    }

    [RelayCommand]
    public void SetBlueAdvantagePreset()
    {
        BlueFirstBlood = true;
        BlueFirstTower = true;
        BlueFirstDragon = true;
        GoldDiffAt15 = 3500;
        KillDiffAt15 = 6;
        BlueTeamAvgWinRate = 54.0f;
        RedTeamAvgWinRate = 48.0f;
        BlueTowerCount = 3;
        RedTowerCount = 0;
        BlueDragonCount = 2;
        RedDragonCount = 0;
        CalculatePrediction();
    }

    [RelayCommand]
    public void SetRedAdvantagePreset()
    {
        BlueFirstBlood = false;
        BlueFirstTower = false;
        BlueFirstDragon = false;
        GoldDiffAt15 = -3500;
        KillDiffAt15 = -5;
        BlueTeamAvgWinRate = 47.5f;
        RedTeamAvgWinRate = 53.5f;
        BlueTowerCount = 0;
        RedTowerCount = 3;
        BlueDragonCount = 0;
        RedDragonCount = 2;
        CalculatePrediction();
    }
}

