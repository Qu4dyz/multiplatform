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
        int batchSize = 20, 
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
                log($"[VPS ML Trainer] === Епоха #{epoch} розпочата: збір реальних матчів ===");
                var newMatches = await collector.CollectRankedMatchesAsync(seedPuuid, batchSize, log, ct);

                var allMatches = await _matchRepo.GetAllMatchesAsync(ct);
                log($"[VPS ML Trainer] Всього матчів у базі даних: {allMatches.Count} (нових у цій епосі: {newMatches})");

                if (allMatches.Count >= 5)
                {
                    log($"[VPS ML Trainer] Навчання моделі {_predictionEngine.ActiveAlgorithm} на реальних даних...");
                    await _predictionEngine.TrainModelAsync(allMatches, ct);

                    if (_predictionEngine.CurrentModelMetrics != null)
                    {
                        var m = _predictionEngine.CurrentModelMetrics;
                        log($"[VPS ML Trainer] Результати навчання: Точність (Accuracy) = {m.Accuracy:P1} | AUC = {m.AreaUnderRocCurve:F3} | F1-Score = {m.F1Score:F3}");
                        if (m.FeatureImportance.Count > 0)
                        {
                            var topFeatures = string.Join(", ", m.FeatureImportance.Take(4).Select(f => $"{f.Key}: {f.Value:P0}"));
                            log($"[VPS ML Trainer] Топ фактори перемоги (Feature Importance): {topFeatures}");
                        }
                    }
                    log($"[VPS ML Trainer] Модель успішно експортована в директорію models/{_predictionEngine.ActiveAlgorithm.ToString().ToLower()}_model.zip.");
                }

                epoch++;
                CurrentEpoch = epoch;
                log($"[VPS ML Trainer] Очікування {delay.TotalMinutes} хв до наступного циклу навчання...");
                await Task.Delay(delay, ct);
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

