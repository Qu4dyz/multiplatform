using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.ML.Engine;
using Avalonia;
using Avalonia.Input;

namespace GameAnalytics.Desktop.ViewModels;

public partial class ItemSlotViewModel : ObservableObject
{
    private int _itemId;
    public int ItemId
    {
        get => _itemId;
        set
        {
            if (_itemId != value)
            {
                _itemId = value;
                OnPropertyChanged(nameof(ItemId));
                OnPropertyChanged(nameof(HasItem));
                OnPropertyChanged(nameof(IconUrl));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }
    public bool HasItem => ItemId > 0;
    public string IconUrl => HasItem ? $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/item/{ItemId}.png" : string.Empty;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _itemIcon;

    public string ToolTipText => ItemId > 0 ? GameConstants.GetItemTooltip(ItemId) : string.Empty;
}

public partial class DetailedParticipantViewModel : ObservableObject
{
    public string SummonerName { get; set; } = string.Empty;
    public string FullRiotId { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public string NormalizedChampionName => PlayerMatchItemViewModel.NormalizeChampionName(ChampionName);
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{NormalizedChampionName}.png";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _championIcon;

    public int ChampLevel { get; set; } = 1;
    public string ChampLevelText => $"{ChampLevel}";
    public bool IsCurrentPlayer { get; set; }
    public string PositionName { get; set; } = "MID";
    public string PositionIcon { get; set; } = "⚡";

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public string KdaText => $"{Kills}/{Deaths}/{Assists}";
    public string KdaRatioText => Deaths == 0 ? "Perfect" : $"{(Kills + Assists) / (double)Deaths:F2}:1";

    public int DamageDealt { get; set; }
    public string DamageText => $"{DamageDealt / 1000.0:F1}k";
    public double DamagePercentOfMax { get; set; }
    public double DamageBarWidth => Math.Max(4, Math.Round(DamagePercentOfMax * 0.7)); // Max 70px

    public int GoldEarned { get; set; }
    public string GoldText => $"{GoldEarned / 1000.0:F1}k";

    public int MinionsKilled { get; set; }
    public string CsText => $"{MinionsKilled}";

    public List<ItemSlotViewModel> Items { get; set; } = new();
    public ItemSlotViewModel Trinket { get; set; } = new();

    public string NameColor => IsCurrentPlayer ? "#F59E0B" : "#F0E6D2";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
    public string DisplayName => SummonerName;
    public string ToolTipText => IsCurrentPlayer ? $"{FullRiotId} (Ви)" : $"Переглянути профіль {FullRiotId} (клікніть)";

    public string BadgeText { get; set; } = string.Empty;
    public string BadgeBg { get; set; } = "Transparent";
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    public string MatchGrade { get; set; } = "B";
    public string MatchGradeColor { get; set; } = "#5383E8";

    // Summoner Spells & Runes
    public int Summoner1Id { get; set; } = 4;
    public int Summoner2Id { get; set; } = 14;
    public string Summoner1IconUrl => SpellRuneHelper.GetSpellIconUrl(Summoner1Id, GameConstants.DDragonVersion);
    public string Summoner2IconUrl => SpellRuneHelper.GetSpellIconUrl(Summoner2Id, GameConstants.DDragonVersion);
    public string Summoner1Tooltip => SpellRuneHelper.GetSpellName(Summoner1Id);
    public string Summoner2Tooltip => SpellRuneHelper.GetSpellName(Summoner2Id);

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _summoner1Icon;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _summoner2Icon;

    public int PrimaryRuneId { get; set; } = 8010;
    public int SecondaryRuneStyleId { get; set; } = 8100;
    public string PrimaryRuneIconUrl => SpellRuneHelper.GetRuneIconUrl(PrimaryRuneId);
    public string SecondaryRuneIconUrl => SpellRuneHelper.GetRuneStyleIconUrl(SecondaryRuneStyleId);

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _primaryRuneIcon;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _secondaryRuneIcon;

    public int VisionScore { get; set; }
    public string VisionText => $"{VisionScore}";
    public int WardsPlaced { get; set; }
    public int ControlWardsBought { get; set; }
    public int TurretPlatesTaken { get; set; }
    public int SoloKills { get; set; }
    public string ChallengesText =>
        SoloKills > 0 || TurretPlatesTaken > 0
            ? $"Solo {SoloKills} · Plates {TurretPlatesTaken}"
            : string.Empty;
    public bool HasChallengesText => !string.IsNullOrEmpty(ChallengesText);

    public System.Windows.Input.ICommand? SelectPlayerCommand { get; set; }
}

public partial class MiniParticipantViewModel : ObservableObject
{
    public string SummonerName { get; set; } = string.Empty;
    public string FullRiotId { get; set; } = string.Empty;
    public string ChampionName { get; set; } = string.Empty;
    public string NormalizedChampionName => PlayerMatchItemViewModel.NormalizeChampionName(ChampionName);
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{NormalizedChampionName}.png";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _championIcon;

    public bool IsCurrentPlayer { get; set; }
    public string FormattedKda { get; set; } = string.Empty;
    public string DisplayName => SummonerName;
    public string NameColor => IsCurrentPlayer ? "#F59E0B" : "#94A3B8";
    public string NameWeight => IsCurrentPlayer ? "Bold" : "Normal";
    public string ToolTipText => IsCurrentPlayer ? $"{FullRiotId} (Ви)" : $"Переглянути профіль {FullRiotId} (клікніть)";

    public System.Windows.Input.ICommand? SelectPlayerCommand { get; set; }
}

public partial class PlayerMatchItemViewModel : ObservableObject
{
    public string MatchId { get; set; } = string.Empty;
    public bool IsRemake { get; set; }
    public bool IsVictory { get; set; }
    public string ResultText => IsRemake ? "РЕМЕЙК" : (IsVictory ? "ПЕРЕМОГА" : "ПОРАЗКА");
    public string ResultColor => IsRemake ? "#A0A8B6" : (IsVictory ? "#5383E8" : "#E84057");
    public string ResultBgColor => IsRemake ? "#181D26" : (IsVictory ? "#15273C" : "#2A1620");
    public string ResultBorderColor => IsRemake ? "#2A3240" : (IsVictory ? "#1D3D60" : "#4A1E2B");
    public string AccentBarColor => IsRemake ? "#758092" : (IsVictory ? "#5383E8" : "#E84057");

    public string PerformanceBadgeText { get; set; } = string.Empty;
    public string PerformanceBadgeBg { get; set; } = "Transparent";
    public string PerformanceBadgeFg { get; set; } = "#010A13";
    public bool HasPerformanceBadge => !string.IsNullOrEmpty(PerformanceBadgeText);

    // LP Tracking (Ranked games)
    public int LpDelta { get; set; }
    public string LpChangeText { get; set; } = string.Empty;
    public string LpChangeBg { get; set; } = "Transparent";
    public string LpChangeFg { get; set; } = "#8A93A5";
    public bool HasLpChange => !string.IsNullOrEmpty(LpChangeText) && LpChangeText != "-";

    // Match Grade & Coaching Tag
    public string MatchGrade { get; set; } = "B";
    public string MatchGradeBg { get; set; } = "#5383E8";
    public string MatchGradeFg { get; set; } = "#FFFFFF";
    public string CoachingBadgeText { get; set; } = string.Empty;
    public bool HasCoachingBadge => !string.IsNullOrEmpty(CoachingBadgeText);

    public string GameModeText { get; set; } = "Normal Draft";
    public string FormattedDuration { get; set; } = string.Empty;
    public string FormattedTimeAgo { get; set; } = string.Empty;

    public string ChampionName { get; set; } = string.Empty;
    public string NormalizedChampionName => NormalizeChampionName(ChampionName);
    public int ChampLevel { get; set; } = 1;
    public string ChampLevelText => $"{ChampLevel}";
    public string ChampionIconUrl => $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{NormalizedChampionName}.png";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _championIcon;

    public string PositionName { get; set; } = "MID";
    public string PositionIcon { get; set; } = "⚡";

    // Summoner Spells & Runes
    public int Summoner1Id { get; set; } = 4;
    public int Summoner2Id { get; set; } = 14;
    public string Summoner1IconUrl => SpellRuneHelper.GetSpellIconUrl(Summoner1Id, GameConstants.DDragonVersion);
    public string Summoner2IconUrl => SpellRuneHelper.GetSpellIconUrl(Summoner2Id, GameConstants.DDragonVersion);
    public string Summoner1Tooltip => SpellRuneHelper.GetSpellName(Summoner1Id);
    public string Summoner2Tooltip => SpellRuneHelper.GetSpellName(Summoner2Id);

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _summoner1Icon;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _summoner2Icon;

    public int PrimaryRuneId { get; set; } = 8010;
    public int SecondaryRuneStyleId { get; set; } = 8100;
    public string PrimaryRuneIconUrl => SpellRuneHelper.GetRuneIconUrl(PrimaryRuneId);
    public string SecondaryRuneIconUrl => SpellRuneHelper.GetRuneStyleIconUrl(SecondaryRuneStyleId);

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _primaryRuneIcon;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _secondaryRuneIcon;

    // Multikill & Vision Badges
    public string MultiKillText { get; set; } = string.Empty;
    public string MultiKillBg { get; set; } = "Transparent";
    public bool HasMultiKill => !string.IsNullOrEmpty(MultiKillText);

    public int VisionScore { get; set; }
    public string VisionScoreText => $"Віжн: {VisionScore}";
    public int WardsPlaced { get; set; }
    public int ControlWardsBought { get; set; }
    public int SoloKills { get; set; }
    public int TurretPlatesTaken { get; set; }
    public string ChallengesBadgeText =>
        SoloKills > 0 || TurretPlatesTaken > 0
            ? $"Solo {SoloKills} · Plates {TurretPlatesTaken}"
            : string.Empty;
    public bool HasChallengesBadge => !string.IsNullOrEmpty(ChallengesBadgeText);

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double KdaRatio { get; set; }
    public string KdaText => $"{Kills} / {Deaths} / {Assists}";
    public string KdaRatioText => Deaths == 0 ? "Perfect KDA" : $"{KdaRatio:F2}:1 KDA";
    public string KdaRatioColor => (Deaths == 0 || KdaRatio >= 5.0) ? "#E6B328" : (KdaRatio >= 3.0 ? "#0AC8B9" : "#F0E6D2");

    public int KillParticipationPercent { get; set; }
    public string KillParticipationText => $"P/Kill {KillParticipationPercent}%";
    public string KillParticipationColor => KillParticipationPercent >= 60 ? "#E84057" : (KillParticipationPercent >= 45 ? "#C8AA6E" : "#8A93A5");

    public int DamageDealt { get; set; }
    public string DamageText => $"{DamageDealt / 1000.0:F1}k DMG";

    public int GoldEarned { get; set; }
    public string GoldText => $"{GoldEarned / 1000.0:F1}k Gold";

    public int MinionsKilled { get; set; }
    public double CsPerMinute { get; set; }
    public string CsText => $"{MinionsKilled} CS ({CsPerMinute:F1}/хв)";

    // Items for current player
    public List<ItemSlotViewModel> PlayerItems { get; set; } = new();
    public ItemSlotViewModel PlayerTrinket { get; set; } = new();

    private static readonly ItemSlotViewModel EmptySlot = new() { ItemId = 0 };
    public ItemSlotViewModel ItemSlot0 => PlayerItems.ElementAtOrDefault(0) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot1 => PlayerItems.ElementAtOrDefault(1) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot2 => PlayerItems.ElementAtOrDefault(2) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot3 => PlayerItems.ElementAtOrDefault(3) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot4 => PlayerItems.ElementAtOrDefault(4) ?? EmptySlot;
    public ItemSlotViewModel ItemSlot5 => PlayerItems.ElementAtOrDefault(5) ?? EmptySlot;

    // Compact lists
    public List<MiniParticipantViewModel> BlueTeam { get; set; } = new();
    public List<MiniParticipantViewModel> RedTeam { get; set; } = new();

    // Detailed lists for expanded view
    public List<DetailedParticipantViewModel> BlueTeamDetailed { get; set; } = new();
    public List<DetailedParticipantViewModel> RedTeamDetailed { get; set; } = new();

    // Tactical Breakdown & Peer Gap Analysis
    [ObservableProperty]
    private MatchTacticalReport? _tacticalReport;

    [ObservableProperty]
    private bool _hasGoldCurve;

    [ObservableProperty]
    private string _goldCurveSummary = string.Empty;

    [ObservableProperty]
    private Points? _goldDiffChartPoints = new();

    [ObservableProperty]
    private Points? _playerGoldChartPoints = new();

    private Match? _underlyingMatch;
    private Participant? _underlyingPlayer;
    private GameTier _playerTier = GameTier.Emerald;
    private Func<string, Task<MatchTimelineData?>>? _loadTimelineFunc;
    private bool _hasLoadedRealTimeline;

    [ObservableProperty]
    private bool _isLoadingTimeline;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMoment))]
    private TacticalMomentReplay? _selectedMoment;

    [ObservableProperty]
    private TacticalTimelineEvent? _selectedTimelineEvent;

    public bool HasSelectedMoment => SelectedMoment != null;

    [RelayCommand]
    private async Task OpenMoment(TacticalTimelineEvent? timelineEvent)
    {
        if (timelineEvent?.Moment == null || !timelineEvent.HasMomentReplay) return;

        // Toggle off if the same row is clicked again.
        if (ReferenceEquals(SelectedTimelineEvent, timelineEvent) && timelineEvent.IsMomentOpen)
        {
            timelineEvent.IsMomentOpen = false;
            SelectedTimelineEvent = null;
            SelectedMoment = null;
            return;
        }

        if (SelectedTimelineEvent != null)
            SelectedTimelineEvent.IsMomentOpen = false;

        // Ensure minimap pixels are in cache BEFORE the board becomes visible
        // (async CDN loads otherwise leave a permanent black square).
        var moment = timelineEvent.Moment;
        if (string.IsNullOrWhiteSpace(moment.MapImageUrl))
            moment.MapImageUrl = GameConstants.SummonersRiftMinimapAsset;

        await BitmapAssetValueConverter.PreloadImagesAsync(new[] { moment.MapImageUrl });
        if (!BitmapAssetValueConverter.IsCached(moment.MapImageUrl))
        {
            moment.MapImageUrl = TacticalMomentReplayBuilder.SummonersRiftMapUrlFallback;
            await BitmapAssetValueConverter.PreloadImagesAsync(new[] { moment.MapImageUrl });
        }

        timelineEvent.WasMomentViewed = true;
        timelineEvent.IsMomentOpen = true;
        SelectedTimelineEvent = timelineEvent;
        SelectedMoment = moment;
    }

    [RelayCommand]
    private void CloseMoment()
    {
        if (SelectedTimelineEvent != null)
            SelectedTimelineEvent.IsMomentOpen = false;
        SelectedTimelineEvent = null;
        SelectedMoment = null;
    }

    [RelayCommand]
    public async Task CopyCoachReportAsync()
    {
        if (TacticalReport == null || _underlyingMatch == null || _underlyingPlayer == null) return;

        IsExporting = true;
        try
        {
            var report = CoachReportExporter.GenerateMarkdownReport(_underlyingMatch, _underlyingPlayer, TacticalReport);

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard != null)
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(report));
                await desktop.MainWindow.Clipboard.SetDataAsync(data);
                ExportStatusMessage = "✅ Повний звіт тренера скопійовано в буфер обміну!";
            }
            else
            {
                ExportStatusMessage = "📋 Текст звіту згенеровано.";
            }
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"❌ Помилка копіювання: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    public async Task SaveCoachReportToFileAsync()
    {
        if (TacticalReport == null || _underlyingMatch == null || _underlyingPlayer == null) return;

        IsExporting = true;
        try
        {
            var report = CoachReportExporter.GenerateMarkdownReport(_underlyingMatch, _underlyingPlayer, TacticalReport);
            var reportsDir = Path.Combine(Directory.GetCurrentDirectory(), "reports");
            if (!Directory.Exists(reportsDir)) Directory.CreateDirectory(reportsDir);

            var safeMatchId = MatchId.Replace(":", "_").Replace("/", "_");
            var filePath = Path.Combine(reportsDir, $"coach_report_{safeMatchId}.md");
            await File.WriteAllTextAsync(filePath, report);

            ExportStatusMessage = $"✅ Звіт тренера збережено у: reports/coach_report_{safeMatchId}.md";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"❌ Помилка збереження: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task EnsureRealTimelineLoadedAsync()
    {
        if (_hasLoadedRealTimeline || _loadTimelineFunc == null || _underlyingMatch == null || _underlyingPlayer == null)
            return;

        _hasLoadedRealTimeline = true;
        IsLoadingTimeline = true;
        try
        {
            var tl = await _loadTimelineFunc(MatchId);
            if (tl != null && (tl.RealEvents.Count > 0 || tl.Frames.Count > 0))
            {
                TacticalReport = MatchTacticalAnalyzer.AnalyzeMatchTactics(_underlyingMatch, _underlyingPlayer, tl, _playerTier);
                ApplyGoldCurveFromReport(TacticalReport);
                SelectedTimelineEvent = null;
                SelectedMoment = null;
            }
        }
        catch
        {
            // Keep fallback tactical report
        }
        finally
        {
            IsLoadingTimeline = false;
        }
    }

    private void ApplyGoldCurveFromReport(MatchTacticalReport? report)
    {
        if (report?.GoldCurve == null || report.GoldCurve.Count < 2)
        {
            HasGoldCurve = false;
            GoldCurveSummary = string.Empty;
            GoldDiffChartPoints = new Points();
            PlayerGoldChartPoints = new Points();
            return;
        }

        const double width = 560;
        const double height = 100;
        var curve = report.GoldCurve;
        var maxAbsDiff = Math.Max(1, curve.Max(p => Math.Abs(p.GoldDiff)));
        var maxPlayerGold = Math.Max(1, curve.Max(p => p.PlayerGold));
        var n = curve.Count;

        var diffPts = new Points();
        var playerPts = new Points();
        for (var i = 0; i < n; i++)
        {
            var p = curve[i];
            var x = n == 1 ? 0 : i * (width / (n - 1));
            var yDiff = height / 2.0 - (p.GoldDiff / (double)maxAbsDiff) * (height / 2.0 - 4);
            var yPlayer = height - 4 - (p.PlayerGold / (double)maxPlayerGold) * (height - 8);
            diffPts.Add(new Point(x, yDiff));
            playerPts.Add(new Point(x, yPlayer));
        }

        GoldDiffChartPoints = diffPts;
        PlayerGoldChartPoints = playerPts;
        HasGoldCurve = true;

        var at10 = curve.LastOrDefault(p => p.Minute <= 10);
        var at15 = curve.LastOrDefault(p => p.Minute <= 15);
        var last = curve[^1];
        GoldCurveSummary =
            $"@10 Δ{(at10?.GoldDiff ?? 0):+#;-#;0}  ·  @15 Δ{(at15?.GoldDiff ?? 0):+#;-#;0}  ·  фініш Δ{last.GoldDiff:+#;-#;0}";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScoreboardTabActive))]
    [NotifyPropertyChangedFor(nameof(IsTacticalTabActive))]
    [NotifyPropertyChangedFor(nameof(ScoreboardTabBg))]
    [NotifyPropertyChangedFor(nameof(ScoreboardTabFg))]
    [NotifyPropertyChangedFor(nameof(TacticalTabBg))]
    [NotifyPropertyChangedFor(nameof(TacticalTabFg))]
    private int _activeDetailTab; // 0 = 5v5 Scoreboard, 1 = Tactical Breakdown

    public bool IsScoreboardTabActive => ActiveDetailTab == 0;
    public bool IsTacticalTabActive => ActiveDetailTab == 1;

    public string ScoreboardTabBg => ActiveDetailTab == 0 ? "#7C3AED" : "#141726";
    public string ScoreboardTabFg => ActiveDetailTab == 0 ? "#FFFFFF" : "#94A3B8";
    public string TacticalTabBg => ActiveDetailTab == 1 ? "#7C3AED" : "#141726";
    public string TacticalTabFg => ActiveDetailTab == 1 ? "#FFFFFF" : "#94A3B8";

    [RelayCommand]
    public void ShowScoreboardTab() => ActiveDetailTab = 0;

    [RelayCommand]
    public async Task ShowTacticalTab()
    {
        ActiveDetailTab = 1;
        await EnsureRealTimelineLoadedAsync();
    }

    // Accordion Expansion state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandButtonText))]
    [NotifyPropertyChangedFor(nameof(ExpandButtonColor))]
    private bool _isExpanded;

    public string ExpandButtonText => IsExpanded ? "▲ Згорнути" : "▼ Деталі";
    public string ExpandButtonColor => IsExpanded ? "#A78BFA" : "#94A3B8";

    [RelayCommand]
    public async Task ToggleExpand()
    {
        IsExpanded = !IsExpanded;
        if (IsExpanded && IsTacticalTabActive)
        {
            await EnsureRealTimelineLoadedAsync();
        }
    }

    public static PlayerMatchItemViewModel FromMatch(
        Match match, 
        string searchedPuuidOrName, 
        string fallbackName = "", 
        System.Windows.Input.ICommand? selectPlayerCommand = null,
        GameTier playerTier = GameTier.Emerald,
        Func<string, Task<MatchTimelineData?>>? loadTimelineFunc = null,
        int? overrideLpDelta = null,
        double playerWinRate = 50.0)
    {
        var cleanSearched = searchedPuuidOrName?.Trim() ?? string.Empty;
        var cleanFallback = fallbackName?.Trim() ?? string.Empty;

        var player = match.Participants.FirstOrDefault(p =>
            (!string.IsNullOrWhiteSpace(p.Puuid) && !string.IsNullOrWhiteSpace(cleanSearched) && p.Puuid.Equals(cleanSearched, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(p.SummonerName) && !string.IsNullOrWhiteSpace(cleanSearched) && p.SummonerName.Contains(cleanSearched, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(p.SummonerName) && !string.IsNullOrWhiteSpace(cleanFallback) && p.SummonerName.Contains(cleanFallback, StringComparison.OrdinalIgnoreCase)));

        // If not matched, default to 4th player (sample)
        player ??= match.Participants.ElementAtOrDefault(3) ?? match.Participants.FirstOrDefault();

        var isRemake = match.IsRemake || (match.GameDurationSeconds > 0 && match.GameDurationSeconds < 300);
        var isVictory = !isRemake && (player?.Win ?? (match.WinningTeam == TeamSide.Blue));
        var durationMinutes = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;

        var isAram = match.QueueId == 450 || match.QueueName.Equals("ARAM", StringComparison.OrdinalIgnoreCase);

        var (posName, posIcon) = isAram
            ? ("ARAM", "🎲")
            : (player?.Position ?? Position.Middle) switch
            {
                Position.Top => ("TOP", "🛡️"),
                Position.Jungle => ("JGL", "🌲"),
                Position.Middle => ("MID", "⚡"),
                Position.Bottom => ("BOT", "🏹"),
                Position.Utility => ("SUP", "✨"),
                _ => ("MID", "⚔️")
            };

        var timeDiff = DateTime.UtcNow - match.GameCreation;
        var timeAgo = timeDiff.TotalDays >= 1
            ? $"{(int)timeDiff.TotalDays} дн. тому"
            : timeDiff.TotalHours >= 1
                ? $"{(int)timeDiff.TotalHours} год. тому"
                : $"{(int)Math.Max(1, timeDiff.TotalMinutes)} хв. тому";

        // Kill participation
        var playerSide = player?.TeamSide ?? TeamSide.Blue;
        var teamTotalKills = match.Participants.Where(p => p.TeamSide == playerSide).Sum(p => p.Kills);
        var playerKillsAndAssists = (player?.Kills ?? 0) + (player?.Assists ?? 0);
        var kpPercent = teamTotalKills > 0 ? (int)Math.Round((double)playerKillsAndAssists / teamTotalKills * 100) : 0;

        var vm = new PlayerMatchItemViewModel
        {
            MatchId = match.MatchId,
            IsRemake = isRemake,
            IsVictory = isVictory,
            GameModeText = match.QueueName,
            FormattedDuration = match.FormattedDuration,
            FormattedTimeAgo = timeAgo,
            ChampionName = ChampionNameHelper.ToDisplayName(player?.ChampionName ?? "Aatrox"),
            ChampLevel = player?.ChampLevel > 0 ? player.ChampLevel : 1,
            PositionName = posName,
            PositionIcon = posIcon,
            Kills = player?.Kills ?? 0,
            Deaths = player?.Deaths ?? 0,
            Assists = player?.Assists ?? 0,
            KdaRatio = player?.KdaRatio ?? 0,
            KillParticipationPercent = kpPercent,
            DamageDealt = player?.TotalDamageDealtToChampions ?? 0,
            GoldEarned = player?.GoldEarned ?? 0,
            MinionsKilled = player?.TotalMinionsKilled ?? 0,
            CsPerMinute = Math.Round((player?.TotalMinionsKilled ?? 0) / Math.Max(1.0, durationMinutes), 1),
            PlayerItems = new List<ItemSlotViewModel>
            {
                new() { ItemId = player?.Item0 ?? 0 },
                new() { ItemId = player?.Item1 ?? 0 },
                new() { ItemId = player?.Item2 ?? 0 },
                new() { ItemId = player?.Item3 ?? 0 },
                new() { ItemId = player?.Item4 ?? 0 },
                new() { ItemId = player?.Item5 ?? 0 }
            },
            PlayerTrinket = new ItemSlotViewModel { ItemId = player?.Item6 ?? 0 }
        };

        // Find max damage for relative damage bar width
        var maxDamage = Math.Max(1, match.Participants.Count > 0 ? match.Participants.Max(p => p.TotalDamageDealtToChampions) : 1);

        // Calculate MVP (highest damage on winning team) and ACE (highest damage on losing team)
        var winners = match.Participants.Where(p => p.Win).ToList();
        if (winners.Count == 0)
        {
            winners = match.Participants.Where(p => p.TeamSide == match.WinningTeam).ToList();
        }
        var losers = match.Participants.Where(p => !winners.Contains(p)).ToList();

        var mvpParticipant = winners.OrderByDescending(p => p.TotalDamageDealtToChampions).FirstOrDefault();
        var aceParticipant = losers.OrderByDescending(p => p.TotalDamageDealtToChampions).FirstOrDefault();

        var playerIsMvp = false;
        var playerIsAce = false;

        if (!isRemake && match.Participants.Count >= 6)
        {
            if (player != null && mvpParticipant == player && player.TotalDamageDealtToChampions > 0)
            {
                playerIsMvp = true;
                vm.PerformanceBadgeText = "MVP";
                vm.PerformanceBadgeBg = "#C8AA6E";
                vm.PerformanceBadgeFg = "#010A13";
            }
            else if (player != null && aceParticipant == player && player.TotalDamageDealtToChampions > 0)
            {
                playerIsAce = true;
                vm.PerformanceBadgeText = "ACE";
                vm.PerformanceBadgeBg = "#E84057";
                vm.PerformanceBadgeFg = "#FFFFFF";
            }
        }

        // Calculate LP Delta (Ranked games)
        var (lpDelta, lpText, lpBg, lpFg) = GlobalCoachingAnalyzer.CalculateLpDelta(match, isVictory, isRemake, playerIsMvp, playerIsAce, overrideLpDelta, playerWinRate);
        vm.LpDelta = lpDelta;
        vm.LpChangeText = lpText;
        vm.LpChangeBg = lpBg;
        vm.LpChangeFg = lpFg;

        // Evaluate Single Match Grade, Coaching Tag, and Tactical Minute-by-Minute Breakdown
        if (player != null)
        {
            var (grade, gradeColor, tag) = GlobalCoachingAnalyzer.EvaluateSingleMatch(match, player, isVictory, isRemake);
            vm.MatchGrade = grade;
            vm.MatchGradeBg = gradeColor;
            vm.CoachingBadgeText = tag;

            var rune = player.PrimaryRuneId;
            var runeStyle = player.SecondaryRuneStyleId;
            var sp1 = player.Summoner1Id;
            var sp2 = player.Summoner2Id;

            if ((rune <= 0 || (rune == 8010 && runeStyle == 8100 && !SpellRuneHelper.IsConquerorChampion(player.ChampionName)))
                && !string.IsNullOrWhiteSpace(player.ChampionName))
            {
                var rec = SpellRuneHelper.GetRecommendedRunesAndSpells(player.ChampionName, player.Position);
                rune = rec.PrimaryRune;
                runeStyle = rec.SecondaryStyle;
                if (sp1 <= 0 || (sp1 == 4 && sp2 == 14))
                {
                    sp1 = rec.Spell1;
                    sp2 = rec.Spell2;
                }
            }

            vm.Summoner1Id = sp1 > 0 ? sp1 : 4;
            vm.Summoner2Id = sp2 > 0 ? sp2 : 14;
            vm.PrimaryRuneId = rune > 0 ? rune : 8010;
            vm.SecondaryRuneStyleId = runeStyle > 0 ? runeStyle : 8100;
            vm.VisionScore = player.VisionScore;
            vm.WardsPlaced = player.WardsPlaced;
            vm.ControlWardsBought = player.ControlWardsBought;
            vm.SoloKills = player.SoloKills;
            vm.TurretPlatesTaken = player.TurretPlatesTaken;

            if (player.PentaKills > 0)
            {
                vm.MultiKillText = "PENTAKILL";
                vm.MultiKillBg = "#8A1C2A";
            }
            else if (player.QuadraKills > 0)
            {
                vm.MultiKillText = "QUADRA KILL";
                vm.MultiKillBg = "#6B1D38";
            }
            else if (player.TripleKills > 0)
            {
                vm.MultiKillText = "TRIPLE KILL";
                vm.MultiKillBg = "#55234A";
            }
            else if (player.DoubleKills > 0)
            {
                vm.MultiKillText = "DOUBLE KILL";
                vm.MultiKillBg = "#2E2540";
            }

            vm._underlyingMatch = match;
            vm._underlyingPlayer = player;
            vm._playerTier = playerTier;
            vm._loadTimelineFunc = loadTimelineFunc;

            // Generate tactical play-by-play & gap analysis with tier scaling
            vm.TacticalReport = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, player, null, playerTier);
        }

        // Load main champion, spells, runes and item icons
        vm.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.ChampionIconUrl, bmp => vm.ChampionIcon = bmp);
        vm.Summoner1Icon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.Summoner1IconUrl, bmp => vm.Summoner1Icon = bmp);
        vm.Summoner2Icon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.Summoner2IconUrl, bmp => vm.Summoner2Icon = bmp);
        vm.PrimaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.PrimaryRuneIconUrl, bmp => vm.PrimaryRuneIcon = bmp);
        vm.SecondaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.SecondaryRuneIconUrl, bmp => vm.SecondaryRuneIcon = bmp);

        foreach (var itm in vm.PlayerItems)
        {
            if (itm.HasItem)
            {
                itm.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(itm.IconUrl, bmp => itm.ItemIcon = bmp);
            }
        }
        if (vm.PlayerTrinket.HasItem)
        {
            vm.PlayerTrinket.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(vm.PlayerTrinket.IconUrl, bmp => vm.PlayerTrinket.ItemIcon = bmp);
        }

        // Populate Blue and Red participants (both compact and detailed)
        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Blue))
        {
            var isCurrent = p == player;
            var shortName = ExtractShortName(p.SummonerName);
            var fullRiotId = string.IsNullOrWhiteSpace(p.SummonerName) ? shortName : p.SummonerName;

            var mini = new MiniParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = ChampionNameHelper.ToDisplayName(p.ChampionName),
                IsCurrentPlayer = isCurrent,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}",
                SelectPlayerCommand = selectPlayerCommand
            };
            mini.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(mini.ChampionIconUrl, bmp => mini.ChampionIcon = bmp);
            vm.BlueTeam.Add(mini);

            var (pRoleName, pRoleIcon) = isAram
                ? ("ARAM", "🎲")
                : p.Position switch
                {
                    Position.Top => ("TOP", "🛡️"),
                    Position.Jungle => ("JGL", "🌲"),
                    Position.Middle => ("MID", "⚡"),
                    Position.Bottom => ("BOT", "🏹"),
                    Position.Utility => ("SUP", "✨"),
                    _ => ("MID", "⚔️")
                };

            var isMvp = !isRemake && match.Participants.Count >= 6 && p == mvpParticipant && p.TotalDamageDealtToChampions > 0;
            var isAce = !isRemake && match.Participants.Count >= 6 && p == aceParticipant && p.TotalDamageDealtToChampions > 0;
            var badgeText = isMvp ? "MVP" : (isAce ? "ACE" : string.Empty);
            var badgeBg = isMvp ? "#C8AA6E" : (isAce ? "#E84057" : "Transparent");

            var (pGrade, pGradeColor, _) = GlobalCoachingAnalyzer.EvaluateSingleMatch(match, p, p.Win, isRemake);

            var detailed = new DetailedParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = ChampionNameHelper.ToDisplayName(p.ChampionName),
                ChampLevel = p.ChampLevel > 0 ? p.ChampLevel : 1,
                IsCurrentPlayer = isCurrent,
                BadgeText = badgeText,
                BadgeBg = badgeBg,
                MatchGrade = pGrade,
                MatchGradeColor = pGradeColor,
                PositionName = pRoleName,
                PositionIcon = pRoleIcon,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                DamageDealt = p.TotalDamageDealtToChampions,
                DamagePercentOfMax = Math.Round((double)p.TotalDamageDealtToChampions / maxDamage * 100, 1),
                GoldEarned = p.GoldEarned,
                MinionsKilled = p.TotalMinionsKilled,
                Summoner1Id = (p.Summoner1Id > 0 && p.Summoner1Id != 4) ? p.Summoner1Id : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).Spell1,
                Summoner2Id = (p.Summoner2Id > 0 && p.Summoner2Id != 14) ? p.Summoner2Id : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).Spell2,
                PrimaryRuneId = (p.PrimaryRuneId > 0 && (p.PrimaryRuneId != 8010 || SpellRuneHelper.IsConquerorChampion(p.ChampionName))) ? p.PrimaryRuneId : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).PrimaryRune,
                SecondaryRuneStyleId = (p.SecondaryRuneStyleId > 0 && (p.SecondaryRuneStyleId != 8100 || SpellRuneHelper.IsConquerorChampion(p.ChampionName))) ? p.SecondaryRuneStyleId : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).SecondaryStyle,
                VisionScore = p.VisionScore,
                WardsPlaced = p.WardsPlaced,
                ControlWardsBought = p.ControlWardsBought,
                TurretPlatesTaken = p.TurretPlatesTaken,
                SoloKills = p.SoloKills,
                Items = new List<ItemSlotViewModel>
                {
                    new() { ItemId = p.Item0 },
                    new() { ItemId = p.Item1 },
                    new() { ItemId = p.Item2 },
                    new() { ItemId = p.Item3 },
                    new() { ItemId = p.Item4 },
                    new() { ItemId = p.Item5 }
                },
                Trinket = new ItemSlotViewModel { ItemId = p.Item6 },
                SelectPlayerCommand = selectPlayerCommand
            };
            detailed.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.ChampionIconUrl, bmp => detailed.ChampionIcon = bmp);
            detailed.Summoner1Icon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Summoner1IconUrl, bmp => detailed.Summoner1Icon = bmp);
            detailed.Summoner2Icon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Summoner2IconUrl, bmp => detailed.Summoner2Icon = bmp);
            detailed.PrimaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.PrimaryRuneIconUrl, bmp => detailed.PrimaryRuneIcon = bmp);
            detailed.SecondaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.SecondaryRuneIconUrl, bmp => detailed.SecondaryRuneIcon = bmp);
            foreach (var itm in detailed.Items)
            {
                if (itm.HasItem) itm.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(itm.IconUrl, bmp => itm.ItemIcon = bmp);
            }
            if (detailed.Trinket.HasItem) detailed.Trinket.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Trinket.IconUrl, bmp => detailed.Trinket.ItemIcon = bmp);
            vm.BlueTeamDetailed.Add(detailed);
        }

        foreach (var p in match.Participants.Where(x => x.TeamSide == TeamSide.Red))
        {
            var isCurrent = p == player;
            var shortName = ExtractShortName(p.SummonerName);
            var fullRiotId = string.IsNullOrWhiteSpace(p.SummonerName) ? shortName : p.SummonerName;

            var mini = new MiniParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = ChampionNameHelper.ToDisplayName(p.ChampionName),
                IsCurrentPlayer = isCurrent,
                FormattedKda = $"{p.Kills}/{p.Deaths}/{p.Assists}",
                SelectPlayerCommand = selectPlayerCommand
            };
            mini.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(mini.ChampionIconUrl, bmp => mini.ChampionIcon = bmp);
            vm.RedTeam.Add(mini);

            var (pRoleName, pRoleIcon) = isAram
                ? ("ARAM", "🎲")
                : p.Position switch
                {
                    Position.Top => ("TOP", "🛡️"),
                    Position.Jungle => ("JGL", "🌲"),
                    Position.Middle => ("MID", "⚡"),
                    Position.Bottom => ("BOT", "🏹"),
                    Position.Utility => ("SUP", "✨"),
                    _ => ("MID", "⚔️")
                };

            var isMvp = !isRemake && match.Participants.Count >= 6 && p == mvpParticipant && p.TotalDamageDealtToChampions > 0;
            var isAce = !isRemake && match.Participants.Count >= 6 && p == aceParticipant && p.TotalDamageDealtToChampions > 0;
            var badgeText = isMvp ? "MVP" : (isAce ? "ACE" : string.Empty);
            var badgeBg = isMvp ? "#C8AA6E" : (isAce ? "#E84057" : "Transparent");

            var (pGrade, pGradeColor, _) = GlobalCoachingAnalyzer.EvaluateSingleMatch(match, p, p.Win, isRemake);

            var detailed = new DetailedParticipantViewModel
            {
                SummonerName = shortName,
                FullRiotId = fullRiotId,
                ChampionName = ChampionNameHelper.ToDisplayName(p.ChampionName),
                ChampLevel = p.ChampLevel > 0 ? p.ChampLevel : 1,
                IsCurrentPlayer = isCurrent,
                BadgeText = badgeText,
                BadgeBg = badgeBg,
                MatchGrade = pGrade,
                MatchGradeColor = pGradeColor,
                PositionName = pRoleName,
                PositionIcon = pRoleIcon,
                Kills = p.Kills,
                Deaths = p.Deaths,
                Assists = p.Assists,
                DamageDealt = p.TotalDamageDealtToChampions,
                DamagePercentOfMax = Math.Round((double)p.TotalDamageDealtToChampions / maxDamage * 100, 1),
                GoldEarned = p.GoldEarned,
                MinionsKilled = p.TotalMinionsKilled,
                Summoner1Id = (p.Summoner1Id > 0 && p.Summoner1Id != 4) ? p.Summoner1Id : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).Spell1,
                Summoner2Id = (p.Summoner2Id > 0 && p.Summoner2Id != 14) ? p.Summoner2Id : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).Spell2,
                PrimaryRuneId = (p.PrimaryRuneId > 0 && (p.PrimaryRuneId != 8010 || SpellRuneHelper.IsConquerorChampion(p.ChampionName))) ? p.PrimaryRuneId : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).PrimaryRune,
                SecondaryRuneStyleId = (p.SecondaryRuneStyleId > 0 && (p.SecondaryRuneStyleId != 8100 || SpellRuneHelper.IsConquerorChampion(p.ChampionName))) ? p.SecondaryRuneStyleId : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, p.Position).SecondaryStyle,
                VisionScore = p.VisionScore,
                WardsPlaced = p.WardsPlaced,
                ControlWardsBought = p.ControlWardsBought,
                TurretPlatesTaken = p.TurretPlatesTaken,
                SoloKills = p.SoloKills,
                Items = new List<ItemSlotViewModel>
                {
                    new() { ItemId = p.Item0 },
                    new() { ItemId = p.Item1 },
                    new() { ItemId = p.Item2 },
                    new() { ItemId = p.Item3 },
                    new() { ItemId = p.Item4 },
                    new() { ItemId = p.Item5 }
                },
                Trinket = new ItemSlotViewModel { ItemId = p.Item6 },
                SelectPlayerCommand = selectPlayerCommand
            };
            detailed.ChampionIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.ChampionIconUrl, bmp => detailed.ChampionIcon = bmp);
            detailed.Summoner1Icon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Summoner1IconUrl, bmp => detailed.Summoner1Icon = bmp);
            detailed.Summoner2Icon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Summoner2IconUrl, bmp => detailed.Summoner2Icon = bmp);
            detailed.PrimaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.PrimaryRuneIconUrl, bmp => detailed.PrimaryRuneIcon = bmp);
            detailed.SecondaryRuneIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.SecondaryRuneIconUrl, bmp => detailed.SecondaryRuneIcon = bmp);
            foreach (var itm in detailed.Items)
            {
                if (itm.HasItem) itm.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(itm.IconUrl, bmp => itm.ItemIcon = bmp);
            }
            if (detailed.Trinket.HasItem) detailed.Trinket.ItemIcon = BitmapAssetValueConverter.GetOrLoadBitmap(detailed.Trinket.IconUrl, bmp => detailed.Trinket.ItemIcon = bmp);
            vm.RedTeamDetailed.Add(detailed);
        }

        return vm;
    }

    public static string NormalizeChampionName(string? name)
    {
        return ChampionNameHelper.ToDDragonImageKey(name);
    }

    private static string ExtractShortName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "Player";
        var idx = fullName.IndexOf('#');
        return idx > 0 ? fullName[..idx] : fullName;
    }
}
