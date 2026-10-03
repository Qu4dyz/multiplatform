using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Engine;

namespace GameAnalytics.ML.Training;

public class VpsTrainingWorker
{
    /// <summary>Full early FastTree/ensemble retrain cadence on 2GB VPS (crawl still every epoch).</summary>
    public const int RetrainIntervalEpochs = 4;
    /// <summary>Lab mid/late (m25/m35) cadence — Tree-only, kept rarer than early retrain.</summary>
    public const int LabHorizonIntervalEpochs = 10;
    /// <summary>Newest-N cap for early train rows on 2GB hosts (same style as horizon maxRows).</summary>
    public const int EarlyTrainMaxRows = 20_000;

    private readonly IRiotApiClient _apiClient;
    private readonly IMatchRepository _matchRepo;
    private readonly IPredictionEngine _predictionEngine;

    public IPredictionEngine PredictionEngine => _predictionEngine;
    public int CurrentEpoch { get; private set; } = 1;

    public VpsTrainingWorker(
        IRiotApiClient apiClient,
        IMatchRepository matchRepo,
        IPredictionEngine predictionEngine)
    {
        _apiClient = apiClient;
        _matchRepo = matchRepo;
        _predictionEngine = predictionEngine;
    }

    /// <summary>
    /// Autonomous background training daemon designed for 24/7 VPS execution.
    /// Periodically discovers and crawls real Riot Ranked matches, saves early-game metrics into SQLite,
    /// retrains ML.NET models, evaluates test accuracy/AUC, and exports updated model weights.
    /// </summary>
    public async Task RunContinuousTrainingAsync(
        string seedPuuid, 
        int batchSize = 30, 
        TimeSpan? epochDelay = null,
        Action<string>? logger = null, 
        CancellationToken ct = default)
    {
        var log = logger ?? Console.WriteLine;
        var delay = epochDelay ?? TimeSpan.FromMinutes(2);
        var collector = new RealMatchDatasetCollector(_apiClient, _matchRepo);

        int epoch = 1;
        CurrentEpoch = epoch;
        log($"[VPS ML Trainer] Запуск автономного тренувального демона на VPS...");
        log($"[VPS ML Trainer] Початковий гравець (seed PUUID): {seedPuuid}");
        log($"[VPS ML Trainer] 2GB cadence: crawl кожен цикл; early retrain epoch 1 / %{RetrainIntervalEpochs}; lab m25/m35 %{LabHorizonIntervalEpochs}.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var epochStarted = DateTime.UtcNow;
                log($"[VPS ML Trainer] === Епоха #{epoch} розпочата: збір реальних матчів ===");
                var newMatches = await collector.CollectRankedMatchesAsync(seedPuuid, batchSize, log, ct);

                // Retrain schedule is independent of +N crawl hits — otherwise +50/epoch forces full RAM train every cycle.
                var shouldRetrain = epoch == 1 || epoch % RetrainIntervalEpochs == 0;
                var shouldLabHorizons = epoch % LabHorizonIntervalEpochs == 0;
                var needMatchGraph = shouldRetrain || shouldLabHorizons;

                int totalMatches;
                IReadOnlyList<Match>? allMatches = null;
                if (needMatchGraph)
                {
                    allMatches = await _matchRepo.GetAllMatchesAsync(ct);
                    totalMatches = allMatches.Count;
                }
                else
                {
                    // Count-only path: avoid materializing ~47k matches + participants into RAM.
                    totalMatches = await _matchRepo.GetTotalMatchesCountAsync(ct);
                }

                log($"[VPS ML Trainer] Всього матчів у базі даних: {totalMatches} (нових у цій епосі: {newMatches})");

                if (totalMatches >= 5 && shouldRetrain && allMatches != null)
                {
                    log($"[VPS ML Trainer] Навчання моделі {_predictionEngine.ActiveAlgorithm} на реальних даних (cap {EarlyTrainMaxRows} newest)...");
                    if (_predictionEngine is MatchPredictionEngine mpe)
                        mpe.MaxTrainRows = EarlyTrainMaxRows;

                    await _predictionEngine.TrainModelAsync(allMatches, ct);

                    if (_predictionEngine.CurrentModelMetrics != null)
                    {
                        var m = _predictionEngine.CurrentModelMetrics;
                        log($"[VPS ML Trainer] Результати навчання: Точність (Accuracy) = {m.Accuracy:P1} | AUC = {m.AreaUnderRocCurve:F3} | F1-Score = {m.F1Score:F3}");
                        if (m.AccuracyWhenConfident > 0 && m.ConfidentCoverage > 0)
                        {
                            var specialists = new List<string>();
                            if (m.UsedHighEloSpecialist) specialists.Add("HighElo");
                            if (m.UsedMidEloSpecialist) specialists.Add("MidElo");
                            if (m.UsedLowEloSpecialist) specialists.Add("LowElo");
                            var specTxt = specialists.Count > 0 ? $" | Specialists: {string.Join("+", specialists)}" : "";
                            log($"[VPS ML Trainer] Впевнені предикти (|p-0.5|≥0.15): Accuracy = {m.AccuracyWhenConfident:P1} на {m.ConfidentCoverage:P0} тест-вибірки | Brier = {m.BrierScore:F3} | TreeW={m.EnsembleTreeWeight:F2} | SpecW={m.SpecialistBlendWeight:F2} | Temp={m.EnsembleTemperature:F2}{specTxt}");
                        }
                        if (m.SoftPrunedGroups.Count > 0)
                        {
                            log($"[VPS ML Trainer] Soft-prune EMA (шумні групи ×0.35): {string.Join(", ", m.SoftPrunedGroups)}");
                        }
                        if (m.AccuracyByRankBucket.Count > 0)
                        {
                            var byRank = string.Join(" | ", m.AccuracyByRankBucket.Select(kv => $"{kv.Key}: {kv.Value:P0}"));
                            log($"[VPS ML Trainer] Accuracy по рангах (для лаб): {byRank}");
                        }
                        if (m.AblationAccuracyDrop.Count > 0)
                        {
                            var abl = string.Join(" | ", m.AblationAccuracyDrop.Select(kv => $"{kv.Key}: Δ{kv.Value:+0.0%;-0.0%;0%}"));
                            log($"[VPS ML Trainer] Ablation (drop якщо прибрати групу): {abl}");
                        }
                        if (m.FeatureImportance.Count > 0)
                        {
                            var topFeatures = string.Join(", ", m.FeatureImportance.Take(4).Select(f => $"{f.Key}: {f.Value:P0}"));
                            log($"[VPS ML Trainer] Топ фактори перемоги (Feature Importance): {topFeatures}");
                        }
                    }
                    log($"[VPS ML Trainer] Модель успішно експортована в директорію models/{_predictionEngine.ActiveAlgorithm.ToString().ToLower()}_model.zip.");

                    // Release training graphs ASAP on 1–2GB VPS hosts.
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }
                else if (totalMatches >= 5 && !shouldRetrain)
                {
                    log($"[VPS ML Trainer] Пропуск early retrain (епоха #{epoch}; наступний на 1/%{RetrainIntervalEpochs}) — crawl продовжується, RAM збережено.");
                }

                // Lab mid/late: every 10th epoch only, Tree-only, subsampled — independent of early retrain schedule.
                if (totalMatches >= 5 && shouldLabHorizons && allMatches != null)
                {
                    Func<string, MatchTimelineData?> timelineLookup = id =>
                        _matchRepo.GetCachedTimelineAsync(id).GetAwaiter().GetResult();

                    foreach (var minute in new[] { PredictionHorizons.Mid, PredictionHorizons.Late })
                    {
                        log($"[VPS ML Trainer] Lab horizon train @{minute}' (FastTree only, max 12k rows)...");
                        await _predictionEngine.TrainHorizonModelAsync(
                            allMatches, minute, timelineLookup, maxRows: 12_000, ct);
                        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        log(_predictionEngine.HasHorizonModel(minute)
                            ? $"[VPS ML Trainer] Lab model ready: {PredictionHorizons.ModelFileName(minute)}"
                            : $"[VPS ML Trainer] Lab @{minute}' skipped (insufficient horizon rows / timelines).");
                    }
                }

                epoch++;
                CurrentEpoch = epoch;

                // If crawl already burned most of the 2-minute Riot window, don't stack a full idle delay on top.
                var elapsed = DateTime.UtcNow - epochStarted;
                var remaining = delay - elapsed;
                if (remaining < TimeSpan.FromSeconds(15))
                    remaining = TimeSpan.FromSeconds(15);
                log($"[VPS ML Trainer] Очікування {remaining.TotalMinutes:F1} хв до наступного циклу (епоха тривала {elapsed.TotalMinutes:F1} хв)...");
                await Task.Delay(remaining, ct);
            }
            catch (OperationCanceledException)
            {
                log("[VPS ML Trainer] Зупинка демона за сигналом скасування.");
                break;
            }
            catch (Exception ex)
            {
                log($"[VPS ML Trainer] Помилка в циклі навчання: {ex.Message}. Повторна спроба через 30 сек...");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch { break; }
            }
        }
    }
}
