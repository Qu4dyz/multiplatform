using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Training;

namespace GameAnalytics.ML.Training;

public class VpsTrainingWorker
{
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

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var epochStarted = DateTime.UtcNow;
                log($"[VPS ML Trainer] === Епоха #{epoch} розпочата: збір реальних матчів ===");
                var newMatches = await collector.CollectRankedMatchesAsync(seedPuuid, batchSize, log, ct);

                var allMatches = await _matchRepo.GetAllMatchesAsync(ct);
                log($"[VPS ML Trainer] Всього матчів у базі даних: {allMatches.Count} (нових у цій епосі: {newMatches})");

                // On tiny VPS: skip full retrain when crawl added nothing (still refresh every 4th epoch).
                var shouldTrain = newMatches > 0 || epoch == 1 || epoch % 4 == 0;
                if (allMatches.Count >= 5 && shouldTrain)
                {
                    log($"[VPS ML Trainer] Навчання моделі {_predictionEngine.ActiveAlgorithm} на реальних даних...");
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
                            log($"[VPS ML Trainer] Впевнені предикти (|p-0.5|≥0.15): Accuracy = {m.AccuracyWhenConfident:P1} на {m.ConfidentCoverage:P0} тест-вибірки | Brier = {m.BrierScore:F3} | TreeW={m.EnsembleTreeWeight:F2}{specTxt}");
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

                    // Release training graphs ASAP on 1GB VPS hosts.
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }
                else if (allMatches.Count >= 5)
                {
                    log($"[VPS ML Trainer] Пропуск retrain (нових матчів немає) — економія CPU/RAM на VPS.");
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

