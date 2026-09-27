using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Engine;
using GameAnalytics.ML.Training;

namespace GameAnalytics.Desktop.ViewModels;

public partial class DraftPredictionViewModel : ViewModelBase
{
    private readonly IMatchAnalyticsService _analyticsService;
    private readonly IDataDragonService _dataDragonService;
    private List<ChampionInfo> _allChampions = new();

    // 5v5 Draft Slots
    [ObservableProperty]
    private ObservableCollection<DraftSlotViewModel> _blueDraftSlots = new();

    [ObservableProperty]
    private ObservableCollection<DraftSlotViewModel> _redDraftSlots = new();

    [ObservableProperty]
    private DraftSlotViewModel? _activeSlot;

    // Champion Picker state
    [ObservableProperty]
    private ObservableCollection<ChampionInfo> _filteredChampions = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedRoleFilter = "All";

    [ObservableProperty]
    private string _patchVersion = GameConstants.DDragonVersion;

    // In-game early metrics
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
    private float _blueTeamAvgWinRate = 52.4f;

    [ObservableProperty]
    private float _redTeamAvgWinRate = 49.6f;

    [ObservableProperty]
    private int _blueTowerCount = 2;

    [ObservableProperty]
    private int _redTowerCount = 0;

    [ObservableProperty]
    private int _blueDragonCount = 2;

    [ObservableProperty]
    private int _redDragonCount = 0;

    [ObservableProperty]
    private int _blueVoidgrubs = 3;

    [ObservableProperty]
    private int _redVoidgrubs = 0;

    [ObservableProperty]
    private int _blueHeralds = 1;

    [ObservableProperty]
    private int _redHeralds = 0;

    [ObservableProperty]
    private int _csDiffAt15;

    [ObservableProperty]
    private int _xpDiffAt15;

    // Dragon Soul Selection
    [ObservableProperty]
    private DragonSoulType _selectedSoulType = DragonSoulType.None;

    public DragonSoulType[] AvailableSoulTypes => Enum.GetValues<DragonSoulType>();

    [ObservableProperty]
    private TeamSide _soulOwner = TeamSide.Blue;

    public TeamSide[] AvailableSides => new[] { TeamSide.Blue, TeamSide.Red };

    /// <summary>Lobby rank context — same gold lead converts differently by elo.</summary>
    [ObservableProperty]
    private GameTier _selectedLobbyTier = GameTier.Emerald;

    public GameTier[] AvailableLobbyTiers => Enum.GetValues<GameTier>()
        .Where(t => t != GameTier.Unranked)
        .ToArray();

    partial void OnSelectedSoulTypeChanged(DragonSoulType value) => CalculatePrediction();
    partial void OnSoulOwnerChanged(TeamSide value) => CalculatePrediction();
    partial void OnSelectedLobbyTierChanged(GameTier value) => CalculatePrediction();

    // Composition Scaling Analysis
    [ObservableProperty]
    private DraftPowerSpikeComparison? _powerSpikes;

    // Prediction results & ML engine
    [ObservableProperty]
    private PredictionResult? _prediction;

    [ObservableProperty]
    private bool _isCalculated;

    [ObservableProperty]
    private MLAlgorithmType _selectedAlgorithm = MLAlgorithmType.FastTree;

    public MLAlgorithmType[] AvailableAlgorithms => Enum.GetValues<MLAlgorithmType>();

    [ObservableProperty]
    private ModelBenchmarkReport? _benchmarkReport;

    [ObservableProperty]
    private bool _isBenchmarking;

    [ObservableProperty]
    private string _trainingStatusText = "🟢 ML.NET FastTree (Модель готова до передбачення)";

    [ObservableProperty]
    private bool _isRetraining;

    [ObservableProperty]
    private string _liveGameStatusText = "⚪ Очікування активного матчу LoL (порт 2999)";

    [ObservableProperty]
    private bool _isLiveGameActive;

    [ObservableProperty]
    private bool _isCheckingLiveGame;

    private readonly ILiveGameService _liveGameService;

    public bool IsTrainedOnRealData => _analyticsService.IsTrainedOnRealData;
    public int TrainingDatasetSize => _analyticsService.TrainingDatasetSize;

    public DraftPredictionViewModel(
        IMatchAnalyticsService analyticsService, 
        IDataDragonService dataDragonService,
        ILiveGameService liveGameService)
    {
        _analyticsService = analyticsService;
        _dataDragonService = dataDragonService;
        _liveGameService = liveGameService;

        if (_analyticsService.IsTrainedOnRealData)
        {
            var size = _analyticsService.TrainingDatasetSize;
            _trainingStatusText = size > 0 
                ? $"🟢 ML.NET FastTree (Активна навчена модель з VPS | {size} матчів)"
                : "🟢 ML.NET FastTree (Активна навчена модель з VPS на реальних матчах)";
        }

        InitializeSlots();
        _ = LoadChampionsAsync();
        CalculatePrediction();
    }

    private void InitializeSlots()
    {
        var positions = new[] { Position.Top, Position.Jungle, Position.Middle, Position.Bottom, Position.Utility };

        BlueDraftSlots.Clear();
        foreach (var pos in positions)
        {
            BlueDraftSlots.Add(new DraftSlotViewModel(pos, TeamSide.Blue));
        }

        RedDraftSlots.Clear();
        foreach (var pos in positions)
        {
            RedDraftSlots.Add(new DraftSlotViewModel(pos, TeamSide.Red));
        }

        ActiveSlot = BlueDraftSlots[0];
    }

    private async Task LoadChampionsAsync()
    {
        try
        {
            PatchVersion = await _dataDragonService.GetLatestGameVersionAsync();
            _allChampions = (await _dataDragonService.GetAllChampionsAsync()).ToList();

            // Preset default champions for visual appeal
            SetInitialDraftPreset();

            ApplyChampionFilter();
            RecalculateWinRatesFromDraft();
        }
        catch
        {
            // Fallback handled
        }
    }

    private void SetInitialDraftPreset()
    {
        if (_allChampions.Count < 10) return;

        var aatrox = _allChampions.FirstOrDefault(c => c.Name == "Aatrox");
        var leesin = _allChampions.FirstOrDefault(c => c.Name == "Lee Sin");
        var ahri = _allChampions.FirstOrDefault(c => c.Name == "Ahri");
        var jinx = _allChampions.FirstOrDefault(c => c.Name == "Jinx");
        var thresh = _allChampions.FirstOrDefault(c => c.Name == "Thresh");

        var ornn = _allChampions.FirstOrDefault(c => c.Name == "Ornn");
        var sejuani = _allChampions.FirstOrDefault(c => c.Name == "Sejuani");
        var syndra = _allChampions.FirstOrDefault(c => c.Name == "Syndra");
        var kaisa = _allChampions.FirstOrDefault(c => c.Name == "Kai'Sa");
        var nautilus = _allChampions.FirstOrDefault(c => c.Name == "Nautilus");

        if (aatrox != null) BlueDraftSlots[0].Champion = aatrox;
        if (leesin != null) BlueDraftSlots[1].Champion = leesin;
        if (ahri != null) BlueDraftSlots[2].Champion = ahri;
        if (jinx != null) BlueDraftSlots[3].Champion = jinx;
        if (thresh != null) BlueDraftSlots[4].Champion = thresh;

        if (ornn != null) RedDraftSlots[0].Champion = ornn;
        if (sejuani != null) RedDraftSlots[1].Champion = sejuani;
        if (syndra != null) RedDraftSlots[2].Champion = syndra;
        if (kaisa != null) RedDraftSlots[3].Champion = kaisa;
        if (nautilus != null) RedDraftSlots[4].Champion = nautilus;
    }

    partial void OnSearchQueryChanged(string value) => ApplyChampionFilter();

    [RelayCommand]
    public void SelectSlot(DraftSlotViewModel slot)
    {
        ActiveSlot = slot;
    }

    [RelayCommand]
    public void PickChampion(ChampionInfo champion)
    {
        if (ActiveSlot != null)
        {
            ActiveSlot.Champion = champion;
            RecalculateWinRatesFromDraft();

            // Advance to next slot
            AdvanceToNextSlot();
        }
    }

    private void AdvanceToNextSlot()
    {
        var allSlots = BlueDraftSlots.Concat(RedDraftSlots).ToList();
        var currentIndex = allSlots.IndexOf(ActiveSlot!);
        if (currentIndex >= 0 && currentIndex < allSlots.Count - 1)
        {
            ActiveSlot = allSlots[currentIndex + 1];
        }
    }

    [RelayCommand]
    public void FilterByRole(string role)
    {
        SelectedRoleFilter = role;
        ApplyChampionFilter();
    }

    private void ApplyChampionFilter()
    {
        var list = _allChampions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedRoleFilter) && SelectedRoleFilter != "All")
        {
            list = list.Where(c => c.Roles.Any(r => r.Equals(SelectedRoleFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            list = list.Where(c => c.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        FilteredChampions.Clear();
        foreach (var champ in list)
        {
            FilteredChampions.Add(champ);
        }
    }

    private void RecalculateWinRatesFromDraft()
    {
        var blueChamps = BlueDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.WinRate).ToList();
        var redChamps = RedDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.WinRate).ToList();

        if (blueChamps.Count > 0)
        {
            BlueTeamAvgWinRate = (float)Math.Round(blueChamps.Average(), 1);
        }

        if (redChamps.Count > 0)
        {
            RedTeamAvgWinRate = (float)Math.Round(redChamps.Average(), 1);
        }

        // Calculate composition scaling power spikes
        var blueNames = BlueDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.Name);
        var redNames = RedDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.Name);
        PowerSpikes = ChampionScalingAnalyzer.CompareDraftCompositions(blueNames, redNames);

        CalculatePrediction();
    }

    [RelayCommand]
    public void ResetDraft()
    {
        foreach (var slot in BlueDraftSlots) slot.Champion = null;
        foreach (var slot in RedDraftSlots) slot.Champion = null;
        BlueTeamAvgWinRate = 50.0f;
        RedTeamAvgWinRate = 50.0f;
        ActiveSlot = BlueDraftSlots[0];
        CalculatePrediction();
    }

    partial void OnSelectedAlgorithmChanged(MLAlgorithmType value)
    {
        _analyticsService.SetActiveMLAlgorithm(value);
        CalculatePrediction();
    }

    [RelayCommand]
    public async Task RetrainOnRealDataAsync()
    {
        IsRetraining = true;
        TrainingStatusText = "⏳ Навчання моделі на реальних матчах з бази даних...";
        try
        {
            var success = await _analyticsService.RetrainModelOnSavedMatchesAsync();
            if (success)
            {
                var count = _analyticsService.TrainingDatasetSize;
                var acc = _analyticsService.CurrentModelMetrics?.Accuracy ?? 0.85;
                TrainingStatusText = $"🟢 Навчено на реальних даних ({count} матчів | Точність: {acc:P1})";
            }
            else
            {
                TrainingStatusText = "ℹ️ Недостатньо матчів у базі (потрібно мінімум 5). Знайдіть гравця в першій вкладці для збереження матчів.";
            }
            CalculatePrediction();
        }
        catch (Exception ex)
        {
            TrainingStatusText = $"Помилка навчання: {ex.Message}";
        }
        finally
        {
            IsRetraining = false;
        }
    }

    [RelayCommand]
    public async Task RunBenchmarkAsync()
    {
        IsBenchmarking = true;
        try
        {
            BenchmarkReport = await Task.Run(() => _analyticsService.RunBenchmark(2000));
        }
        finally
        {
            IsBenchmarking = false;
        }
    }

    [RelayCommand]
    public void CalculatePrediction()
    {
        var blueNames = BlueDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.Name).ToList();
        var redNames = RedDraftSlots.Where(s => s.Champion != null).Select(s => s.Champion!.Name).ToList();
        var (engageDiff, tankDiff, adApDiff) = ChampionCompositionHelper.ComputeDiffs(blueNames, redNames);

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
            RedDragonCount = RedDragonCount,
            BlueVoidgrubs = BlueVoidgrubs,
            RedVoidgrubs = RedVoidgrubs,
            BlueHeralds = BlueHeralds,
            RedHeralds = RedHeralds,
            CsDiffAt15 = CsDiffAt15,
            XpDiffAt15 = XpDiffAt15,
            SoulType = SelectedSoulType,
            SoulOwner = SoulOwner,
            BlueScalingAdvantage = PowerSpikes?.LateAdvantage ?? 0.0,
            EarlyPowerDiff = (float)(PowerSpikes?.EarlyAdvantage ?? 0.0),
            LatePowerDiff = (float)(PowerSpikes?.LateAdvantage ?? 0.0),
            AvgRankScore = RankScoreHelper.FromTier(SelectedLobbyTier),
            EngageDiff = engageDiff,
            TankDiff = tankDiff,
            AdApBalanceDiff = adApDiff,
            GoldDiff10 = GoldDiffAt15 * 0.65f,
            GoldMomentum15 = GoldDiffAt15 * 0.35f,
            CarryGoldDiff15 = GoldDiffAt15 * 0.35f,
            FirstBloodTempo = BlueFirstBlood ? 6f : 0f,
            FirstTowerTempo = BlueFirstTower ? 5f : 0f,
            FirstDragonTempo = BlueFirstDragon ? 4f : 0f,
            FirstDragonValue = BlueFirstDragon ? 1.0f : 0f,
            DeathDiff15 = -KillDiffAt15 * 0.9f,
            VisionWardDiff15 = 0f,
            ControlWardDiff15 = 0f,
            CsDiff10 = CsDiffAt15 * 0.65f,
            XpDiff10 = XpDiffAt15 * 0.65f
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
        BlueTowerCount = 0;
        RedTowerCount = 0;
        BlueDragonCount = 0;
        RedDragonCount = 0;
        BlueVoidgrubs = 0;
        RedVoidgrubs = 0;
        BlueHeralds = 0;
        RedHeralds = 0;
        CsDiffAt15 = 0;
        XpDiffAt15 = 0;
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
        BlueTowerCount = 3;
        RedTowerCount = 0;
        BlueDragonCount = 2;
        RedDragonCount = 0;
        BlueVoidgrubs = 5;
        RedVoidgrubs = 1;
        BlueHeralds = 1;
        RedHeralds = 0;
        CsDiffAt15 = 35;
        XpDiffAt15 = 1400;
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
        BlueTowerCount = 0;
        RedTowerCount = 3;
        BlueDragonCount = 0;
        RedDragonCount = 2;
        BlueVoidgrubs = 1;
        RedVoidgrubs = 5;
        BlueHeralds = 0;
        RedHeralds = 1;
        CsDiffAt15 = -35;
        XpDiffAt15 = -1400;
        CalculatePrediction();
    }

    [RelayCommand]
    public async Task ScanLiveGameAsync()
    {
        IsCheckingLiveGame = true;
        LiveGameStatusText = "📡 Підключення до локального League Client API (127.0.0.1:2999)...";

        try
        {
            var liveStats = await _liveGameService.GetLiveGameStatsAsync();
            if (liveStats.IsActiveGame)
            {
                IsLiveGameActive = true;
                LiveGameStatusText = $"🔴 Знайдено активний матч! Час гри: {liveStats.FormattedGameTime} ({liveStats.GameMode}). Метрики перенесено в симуляцію!";

                BlueFirstBlood = liveStats.BlueFirstBlood;
                BlueFirstTower = liveStats.BlueFirstTower;
                BlueFirstDragon = liveStats.BlueFirstDragon;

                GoldDiffAt15 = Math.Clamp(liveStats.EstimatedGoldDiff, -6000, 6000);
                KillDiffAt15 = Math.Clamp(liveStats.KillDiff, -15, 15);
                CsDiffAt15 = Math.Clamp(liveStats.CsDiff, -100, 100);
                XpDiffAt15 = Math.Clamp(liveStats.EstimatedXpDiff, -4000, 4000);

                BlueTowerCount = Math.Clamp(liveStats.BlueTowers, 0, 5);
                RedTowerCount = Math.Clamp(liveStats.RedTowers, 0, 5);
                BlueDragonCount = Math.Clamp(liveStats.BlueDragons, 0, 4);
                RedDragonCount = Math.Clamp(liveStats.RedDragons, 0, 4);
                BlueVoidgrubs = Math.Clamp(liveStats.BlueVoidgrubs, 0, 6);
                RedVoidgrubs = Math.Clamp(liveStats.RedVoidgrubs, 0, 6);
                BlueHeralds = Math.Clamp(liveStats.BlueHeralds, 0, 1);
                RedHeralds = Math.Clamp(liveStats.RedHeralds, 0, 1);

                // Auto-match champions to slots if found
                if (liveStats.BlueChampions.Count > 0 && _allChampions.Count > 0)
                {
                    for (int i = 0; i < Math.Min(liveStats.BlueChampions.Count, BlueDraftSlots.Count); i++)
                    {
                        var name = liveStats.BlueChampions[i];
                        var champ = _allChampions.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                                      c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (champ != null) BlueDraftSlots[i].Champion = champ;
                    }
                }

                if (liveStats.RedChampions.Count > 0 && _allChampions.Count > 0)
                {
                    for (int i = 0; i < Math.Min(liveStats.RedChampions.Count, RedDraftSlots.Count); i++)
                    {
                        var name = liveStats.RedChampions[i];
                        var champ = _allChampions.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                                      c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (champ != null) RedDraftSlots[i].Champion = champ;
                    }
                }

                RecalculateWinRatesFromDraft();
                CalculatePrediction();
            }
            else
            {
                IsLiveGameActive = false;
                LiveGameStatusText = "⚪ Активний матч League of Legends не виявлено (гра не запущена або триває завантаження).";
            }
        }
        catch (Exception ex)
        {
            IsLiveGameActive = false;
            LiveGameStatusText = $"❌ Помилка сканування Live Client API: {ex.Message}";
        }
        finally
        {
            IsCheckingLiveGame = false;
        }
    }

    // ── Lab multi-horizon (add-on; does not replace production 15' section) ──

    [ObservableProperty]
    private int _labSelectedPhaseMinute = PredictionHorizons.Early;

    public int[] LabPhaseMinutes { get; } = PredictionHorizons.All;

    [ObservableProperty]
    private string _labEarlyProbText = "—";

    [ObservableProperty]
    private string _labMidProbText = "—";

    [ObservableProperty]
    private string _labLateProbText = "—";

    [ObservableProperty]
    private string _labPhaseStatsText = "Оберіть матч з історії або натисніть «Оновити lab».";

    [ObservableProperty]
    private string _labStatusText = "Lab mid/late — окремі FastTree-моделі; 15′ лишається production.";

    [ObservableProperty]
    private string? _labMatchId;

    [ObservableProperty]
    private ObservableCollection<string> _labMatchIds = new();

    [ObservableProperty]
    private bool _labIsBusy;

    partial void OnLabSelectedPhaseMinuteChanged(int value) => UpdateLabPhaseStatsPanel();
    partial void OnLabMatchIdChanged(string? value) => _ = RefreshLabHorizonsAsync();

    private TimelineHorizonSnapshot? _labSnap15;
    private TimelineHorizonSnapshot? _labSnap25;
    private TimelineHorizonSnapshot? _labSnap35;

    [RelayCommand]
    private async Task LoadLabMatchListAsync()
    {
        try
        {
            LabIsBusy = true;
            var matches = await _analyticsService.GetSavedMatchHistoryAsync();
            var ids = matches
                .Where(m => !m.IsRemake && m.GameDurationSeconds >= PredictionHorizons.Late * 60)
                .OrderByDescending(m => m.GameCreation)
                .Select(m => m.MatchId)
                .Take(40)
                .ToList();
            LabMatchIds = new ObservableCollection<string>(ids);
            LabStatusText = ids.Count == 0
                ? "Немає збережених матчів ≥35′ — lab покаже оцінки з поточних слайдерів 15′."
                : $"Завантажено {ids.Count} матчів для lab. Оберіть MatchId.";
            if (ids.Count > 0 && string.IsNullOrWhiteSpace(LabMatchId))
                LabMatchId = ids[0];
        }
        catch (Exception ex)
        {
            LabStatusText = $"Не вдалося завантажити історію: {ex.Message}";
        }
        finally
        {
            LabIsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLabHorizonsAsync()
    {
        try
        {
            LabIsBusy = true;
            _labSnap15 = _labSnap25 = _labSnap35 = null;

            MatchTimelineData? timeline = null;
            Match? match = null;
            if (!string.IsNullOrWhiteSpace(LabMatchId))
            {
                var all = await _analyticsService.GetSavedMatchHistoryAsync();
                match = all.FirstOrDefault(m => m.MatchId == LabMatchId);
                timeline = await _analyticsService.GetMatchTimelineAsync(LabMatchId);
            }

            if (timeline != null && timeline.Frames.Count > 0)
            {
                _labSnap15 = MatchTimelineSnapshotBuilder.Build(timeline, PredictionHorizons.Early, match);
                _labSnap25 = MatchTimelineSnapshotBuilder.Build(timeline, PredictionHorizons.Mid, match);
                _labSnap35 = MatchTimelineSnapshotBuilder.Build(timeline, PredictionHorizons.Late, match);

                LabEarlyProbText = FormatLabProb(_labSnap15, PredictionHorizons.Early);
                LabMidProbText = FormatLabProb(_labSnap25, PredictionHorizons.Mid);
                LabLateProbText = FormatLabProb(_labSnap35, PredictionHorizons.Late);

                var has25 = _analyticsService.HasHorizonModel(PredictionHorizons.Mid);
                var has35 = _analyticsService.HasHorizonModel(PredictionHorizons.Late);
                LabStatusText =
                    $"Match {LabMatchId}: snapshots " +
                    $"15={(_labSnap15 != null ? "ok" : "n/a")} " +
                    $"25={(_labSnap25 != null ? "ok" : "n/a")} " +
                    $"35={(_labSnap35 != null ? "ok" : "n/a")} | " +
                    $"models m25={(has25 ? "loaded" : "missing")} m35={(has35 ? "loaded" : "missing")}";
            }
            else
            {
                // No timeline: reuse current section-2 metrics for early only.
                CalculatePrediction();
                LabEarlyProbText = Prediction != null
                    ? $"{Prediction.BlueWinProbability * 100:F1}% blue"
                    : "—";
                LabMidProbText = "немає timeline";
                LabLateProbText = "немає timeline";
                LabStatusText = "Немає timeline-кадру — early з секції 2; mid/late недоступні.";
            }

            UpdateLabPhaseStatsPanel();
        }
        catch (Exception ex)
        {
            LabStatusText = $"Lab error: {ex.Message}";
        }
        finally
        {
            LabIsBusy = false;
        }
    }

    private string FormatLabProb(TimelineHorizonSnapshot? snap, int minute)
    {
        if (snap == null) return "n/a";
        var features = RealMatchDatasetCollector.SnapshotToInputFeatures(
            snap,
            BlueTeamAvgWinRate,
            RedTeamAvgWinRate,
            RankScoreHelper.FromTier(SelectedLobbyTier),
            (float)(PowerSpikes?.EarlyAdvantage ?? 0),
            (float)(PowerSpikes?.LateAdvantage ?? 0));
        var result = _analyticsService.PredictOutcome(features, minute);
        return $"{result.BlueWinProbability * 100:F1}% blue";
    }

    private void UpdateLabPhaseStatsPanel()
    {
        var snap = LabSelectedPhaseMinute switch
        {
            PredictionHorizons.Mid => _labSnap25,
            PredictionHorizons.Late => _labSnap35,
            _ => _labSnap15
        };

        if (snap == null)
        {
            if (LabSelectedPhaseMinute == PredictionHorizons.Early)
            {
                LabPhaseStatsText =
                    $"Фаза 15′ (секція 2 / snapshot): Gold {GoldDiffAt15:+0;-#;0} | " +
                    $"Kills {KillDiffAt15:+0;-#;0} | CS {CsDiffAt15:+0;-#;0} | " +
                    $"Towers {BlueTowerCount}-{RedTowerCount} | Dragons {BlueDragonCount}-{RedDragonCount}";
            }
            else
            {
                LabPhaseStatsText = $"Немає чесного кадру ≤{LabSelectedPhaseMinute}' для цього матчу.";
            }
            return;
        }

        LabPhaseStatsText =
            $"@{snap.AnchorMinute}' (frame {snap.FrameTimestampMs / 1000}s) — окремий stats-блок:\n" +
            $"Gold {snap.GoldDiff:+0;-#;0} (B {snap.GoldBlue} / R {snap.GoldRed}) | " +
            $"Kills {snap.KillDiff:+0;-#;0} | CS {snap.CsDiff:+0;-#;0} | XP {snap.XpDiff:+0;-#;0}\n" +
            $"Towers {snap.TowersBlue}-{snap.TowersRed} | Dragons {snap.DragonsBlue}-{snap.DragonsRed} | " +
            $"Grubs {snap.VoidgrubsBlue}-{snap.VoidgrubsRed} | Herald {snap.HeraldsBlue}-{snap.HeraldsRed}\n" +
            $"Plates {snap.PlatesDiff:+0;-#;0} | Deaths {snap.DeathDiff:+0;-#;0} | " +
            $"Vision {snap.VisionWardDiff:+0;-#;0} / Control {snap.ControlWardDiff:+0;-#;0}";
    }
}
