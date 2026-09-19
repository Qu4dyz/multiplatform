using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Models;
using GameAnalytics.ML.Training;
using Microsoft.ML;

namespace GameAnalytics.ML.Engine;

public class MatchPredictionEngine : IPredictionEngine
{
    private readonly MLContext _mlContext;
    private readonly string _modelDirectory;
    private ITransformer? _model;
    private ITransformer? _ensembleForestModel;
    private ITransformer? _highEloModel;
    private PredictionEngine<MatchInputData, MatchPrediction>? _predictionEngine;
    private PredictionEngine<MatchInputData, MatchPrediction>? _ensembleForestEngine;
    private PredictionEngine<MatchInputData, MatchPrediction>? _highEloEngine;
    private readonly object _lock = new();

    public MLAlgorithmType ActiveAlgorithm { get; private set; } = MLAlgorithmType.FastTree;
    public ModelMetrics? CurrentModelMetrics { get; private set; }
    public bool IsTrainedOnRealData { get; private set; }
    public int TrainingDatasetSize { get; private set; }
    public DateTime? LastTrainedAt { get; private set; }

    public MatchPredictionEngine()
    {
        _mlContext = new MLContext(seed: 42);
        
        var baseDir = AppContext.BaseDirectory;
        var candidateDir = Path.Combine(baseDir, "models");
        if (!Directory.Exists(candidateDir))
        {
            var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "models");
            if (Directory.Exists(projectDir))
            {
                candidateDir = projectDir;
            }
            else
            {
                var rootDir = Path.Combine(Directory.GetCurrentDirectory(), "models");
                if (Directory.Exists(rootDir)) candidateDir = rootDir;
            }
        }
        _modelDirectory = candidateDir;
        InitializeModel();
    }

    private void InitializeModel()
    {
        lock (_lock)
        {
            var modelFile = Path.Combine(_modelDirectory, $"{ActiveAlgorithm.ToString().ToLower()}_model.zip");

            if (File.Exists(modelFile))
            {
                try
                {
                    _model = _mlContext.Model.Load(modelFile, out _);
                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);

                    var forestFile = Path.Combine(_modelDirectory, "fastforest_model.zip");
                    if (File.Exists(forestFile))
                    {
                        _ensembleForestModel = _mlContext.Model.Load(forestFile, out _);
                        _ensembleForestEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_ensembleForestModel);
                    }

                    var highFile = Path.Combine(_modelDirectory, "fasttree_highelo_model.zip");
                    if (File.Exists(highFile))
                    {
                        _highEloModel = _mlContext.Model.Load(highFile, out _);
                        _highEloEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_highEloModel);
                    }

                    IsTrainedOnRealData = true;
                    return;
                }
                catch
                {
                    // Fallback to retrain if file corrupted
                }
            }

            // Train default model on initial synthetic dataset
            var benchmarkService = new ModelBenchmarkService();
            var syntheticData = ModelTrainer.GenerateSyntheticRankedDataset(1500);
            var pipeline = benchmarkService.BuildPipeline(ActiveAlgorithm);
            var dataView = _mlContext.Data.LoadFromEnumerable(syntheticData);

            _model = pipeline.Fit(dataView);

            try
            {
                var trainer = new ModelTrainer();
                trainer.SaveModel(_model, dataView.Schema, modelFile);
            }
            catch
            {
                // In-memory model is ready; ignore concurrent file lock
            }

            _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);
        }
    }

    public bool ReloadModel()
    {
        lock (_lock)
        {
            InitializeModel();
            return IsTrainedOnRealData;
        }
    }

    public void SetActiveAlgorithm(MLAlgorithmType algorithm)
    {
        if (ActiveAlgorithm == algorithm && _predictionEngine != null)
        {
            return;
        }

        lock (_lock)
        {
            ActiveAlgorithm = algorithm;
            var benchmarkService = new ModelBenchmarkService();
            var syntheticData = ModelTrainer.GenerateSyntheticRankedDataset(1500);
            var pipeline = benchmarkService.BuildPipeline(algorithm);
            var dataView = _mlContext.Data.LoadFromEnumerable(syntheticData);

            _model = pipeline.Fit(dataView);
            var modelFile = Path.Combine(_modelDirectory, $"{ActiveAlgorithm.ToString().ToLower()}_model.zip");
            try
            {
                var trainer = new ModelTrainer();
                trainer.SaveModel(_model, dataView.Schema, modelFile);
            }
            catch
            {
                // In-memory model is ready; ignore concurrent file lock
            }

            _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);
        }
    }

    public ModelBenchmarkReport RunAlgorithmsBenchmark(int sampleCount = 2000)
    {
        var dataset = ModelTrainer.GenerateSyntheticRankedDataset(sampleCount);
        var benchmark = new ModelBenchmarkService();
        var report = benchmark.RunBenchmark(dataset);

        var current = report.Models.FirstOrDefault(m => m.AlgorithmType == ActiveAlgorithm);
        if (current != null)
        {
            CurrentModelMetrics = current;
        }

        return report;
    }

    public PredictionResult PredictMatchOutcome(MatchInputFeatures features)
    {
        lock (_lock)
        {
            if (_predictionEngine == null)
            {
                InitializeModel();
            }

            var input = new MatchInputData
            {
                FirstBlood = features.BlueFirstBlood ? 1f : 0f,
                FirstTower = features.BlueFirstTower ? 1f : 0f,
                FirstDragon = features.BlueFirstDragon ? 1f : 0f,
                GoldDiff15 = features.GoldDiffAt15,
                KillDiff15 = features.KillDiffAt15,
                CsDiff15 = features.CsDiffAt15,
                XpDiff15 = features.XpDiffAt15,
                VoidgrubDiff = features.VoidgrubDiff,
                HeraldDiff = features.HeraldDiff,
                BlueAvgWinRate = features.BlueTeamAvgWinRate,
                RedAvgWinRate = features.RedTeamAvgWinRate,
                TowerDiff = features.BlueTowerCount - features.RedTowerCount,
                DragonDiff = features.BlueDragonCount - features.RedDragonCount,
                EarlyPowerDiff = features.EarlyPowerDiff != 0
                    ? features.EarlyPowerDiff
                    : (float)(-features.BlueScalingAdvantage * 0.4),
                LatePowerDiff = features.LatePowerDiff != 0
                    ? features.LatePowerDiff
                    : (float)features.BlueScalingAdvantage,
                AvgRankScore = features.AvgRankScore > 0 ? features.AvgRankScore : 5.5f,
                GoldPace15 = (Math.Abs(features.GoldDiffAt15) + 48000) / 1000f,
                TopGoldDiff15 = features.TopGoldDiff15,
                JungleGoldDiff15 = features.JungleGoldDiff15,
                MidGoldDiff15 = features.MidGoldDiff15,
                BotDuoGoldDiff15 = features.BotDuoGoldDiff15,
                LevelDiff15 = features.LevelDiff15 != 0
                    ? features.LevelDiff15
                    : features.XpDiffAt15 / 180f,
                ObjectiveScoreDiff =
                    (features.BlueTowerCount - features.RedTowerCount) * 1.5f +
                    (features.BlueDragonCount - features.RedDragonCount) * 1.2f +
                    features.VoidgrubDiff * 0.35f +
                    features.HeraldDiff,
                RankAdjustedGoldDiff = features.GoldDiffAt15 *
                    ((features.AvgRankScore > 0 ? features.AvgRankScore : 5.5f) / 5.5f),
                GoldDiff10 = features.GoldDiff10 != 0
                    ? features.GoldDiff10
                    : features.GoldDiffAt15 * 0.65f,
                KillDiff10 = features.KillDiff10,
                GoldMomentum15 = features.GoldMomentum15 != 0
                    ? features.GoldMomentum15
                    : features.GoldDiffAt15 - (features.GoldDiff10 != 0 ? features.GoldDiff10 : features.GoldDiffAt15 * 0.65f),
                WinRateDiff = features.BlueTeamAvgWinRate - features.RedTeamAvgWinRate,
                EngageDiff = features.EngageDiff,
                TankDiff = features.TankDiff,
                AdApBalanceDiff = features.AdApBalanceDiff,
                SnowballScore = (features.KillDiffAt15 * 400f + features.GoldDiffAt15) / 1000f,
                CarryGoldDiff15 = features.CarryGoldDiff15 != 0
                    ? features.CarryGoldDiff15
                    : features.GoldDiffAt15 * 0.35f,
                FirstBloodTempo = features.FirstBloodTempo,
                FirstTowerTempo = features.FirstTowerTempo,
                FirstDragonTempo = features.FirstDragonTempo,
                FirstDragonValue = features.FirstDragonValue,
                LaneMatchupDiff = features.LaneMatchupDiff,
                PlatesDiff15 = features.PlatesDiff15,
                KillMomentum15 = features.KillMomentum15 != 0
                    ? features.KillMomentum15
                    : features.KillDiffAt15 - features.KillDiff10,
                LeadVsScaling = features.LeadVsScaling != 0
                    ? features.LeadVsScaling
                    : (features.GoldDiffAt15 / 2000f) * (features.LatePowerDiff / 10f),
                DeathDiff15 = features.DeathDiff15,
                VisionWardDiff15 = features.VisionWardDiff15,
                ControlWardDiff15 = features.ControlWardDiff15
            };

            var prediction = _predictionEngine!.Predict(input);
            var blueProb = Math.Clamp(prediction.Probability, 0.05f, 0.95f);

            // Soft-vote with FastForest when the ensemble partner is available.
            if (_ensembleForestEngine != null)
            {
                var forestProb = Math.Clamp(_ensembleForestEngine.Predict(input).Probability, 0.05f, 0.95f);
                blueProb = Math.Clamp((blueProb + forestProb) * 0.5f, 0.05f, 0.95f);
            }

            // Diamond+ lobbies: blend in the high-elo specialist (ranks convert leads differently).
            if (_highEloEngine != null && input.AvgRankScore >= ModelBenchmarkService.HighEloRankThreshold)
            {
                var highProb = Math.Clamp(_highEloEngine.Predict(input).Probability, 0.05f, 0.95f);
                blueProb = Math.Clamp(blueProb * 0.40f + highProb * 0.60f, 0.05f, 0.95f);
            }

            var redProb = 1.0f - blueProb;
            var predictedWinner = blueProb >= 0.5f ? TeamSide.Blue : TeamSide.Red;
            var confidence = Math.Abs(blueProb - 0.5f) * 2.0;

            var factors = new List<string>();
            if (Math.Abs(features.GoldDiffAt15) >= 1500)
            {
                factors.Add(features.GoldDiffAt15 > 0
                    ? $"Перевага за золотом на 15 хв: +{features.GoldDiffAt15:N0} на користь Синіх"
                    : $"Відставання за золотом на 15 хв: {features.GoldDiffAt15:N0} на користь Червоних");
            }

            if (features.VoidgrubDiff != 0)
            {
                factors.Add(features.VoidgrubDiff > 0
                    ? $"Контроль личинок безодні: +{features.VoidgrubDiff} на користь Синіх (прискорене знесення веж)"
                    : $"Контроль личинок безодні: +{-features.VoidgrubDiff} на користь Червоних (прискорене знесення веж)");
            }

            if (features.HeraldDiff != 0)
            {
                factors.Add(features.HeraldDiff > 0
                    ? "Герольд безодні під контролем Синьої команди (високий потенціал пушу)"
                    : "Герольд безодні під контролем Червоної команди (високий потенціал пушу)");
            }

            if (Math.Abs(features.CsDiffAt15) >= 20)
            {
                factors.Add(features.CsDiffAt15 > 0
                    ? $"Перевага за фармом (CS) на 15 хв: +{features.CsDiffAt15} на користь Синіх"
                    : $"Відставання за фармом (CS) на 15 хв: {features.CsDiffAt15} на користь Червоних");
            }

            if (Math.Abs(features.XpDiffAt15) >= 700)
            {
                factors.Add(features.XpDiffAt15 > 0
                    ? $"Перевага за досвідом (XP) на 15 хв: +{features.XpDiffAt15:N0} на користь Синіх (перевага за рівнями)"
                    : $"Відставання за досвідом (XP) на 15 хв: {features.XpDiffAt15:N0} на користь Червоних (перевага за рівнями)");
            }

            if (features.BlueFirstTower)
            {
                factors.Add("Перша вежа знищена Синьою командою (+темп і контроль карти)");
            }

            if (features.BlueFirstDragon)
            {
                factors.Add("Перший дракон взятий Синьою командою (бонус до драконячої душі)");
            }

            if (Math.Abs(features.BlueTeamAvgWinRate - features.RedTeamAvgWinRate) >= 2.0f)
            {
                factors.Add(features.BlueTeamAvgWinRate > features.RedTeamAvgWinRate
                    ? $"Середній вінрейт піків Синіх вищий ({features.BlueTeamAvgWinRate:F1}% проти {features.RedTeamAvgWinRate:F1}%)"
                    : $"Середній вінрейт піків Червоних вищий ({features.RedTeamAvgWinRate:F1}% проти {features.BlueTeamAvgWinRate:F1}%)");
            }

            // Dragon Soul Factor & Probability Adjustment
            if (features.HasDragonSoul)
            {
                var isBlueSoul = features.SoulOwner == TeamSide.Blue;
                var soulName = features.SoulType switch
                {
                    DragonSoulType.Infernal => "Вогняна (Infernal)",
                    DragonSoulType.Mountain => "Гірська (Mountain)",
                    DragonSoulType.Ocean => "Морська (Ocean)",
                    DragonSoulType.Cloud => "Хмарна (Cloud)",
                    DragonSoulType.Hextech => "Гекстекова (Hextech)",
                    DragonSoulType.Chemtech => "Хімічна (Chemtech)",
                    _ => "Драконяча"
                };

                var soulImpact = features.SoulType switch
                {
                    DragonSoulType.Infernal => "+вибухова додаткова шкода по площі",
                    DragonSoulType.Mountain => "+масові щити та блокування шкоди",
                    DragonSoulType.Ocean => "+постійне відновлення здоров'я та мани у бою",
                    DragonSoulType.Cloud => "+критична швидкість пересування та мобільність",
                    DragonSoulType.Hextech => "+ланцюгові блискавки та сповільнення ворогів",
                    DragonSoulType.Chemtech => "+висока стійкість та сплеск шкоди при низькому HP",
                    _ => "+колосальне підсилення характеристик"
                };

                var teamName = isBlueSoul ? "Синьої" : "Червоної";
                factors.Add($"🐉 Драконяча душа: {soulName} під контролем {teamName} команди ({soulImpact})");

                // Competitive LoL soul modifier (~15% win probability swing)
                blueProb = Math.Clamp(blueProb + (isBlueSoul ? 0.15f : -0.15f), 0.05f, 0.95f);
            }

            // Late-game scaling is now a trained feature (LatePowerDiff) — only surface as a factor text.
            if (Math.Abs(features.BlueScalingAdvantage) >= 5.0 || Math.Abs(features.LatePowerDiff) >= 5.0)
            {
                var late = features.LatePowerDiff != 0 ? features.LatePowerDiff : (float)features.BlueScalingAdvantage;
                var isBlueScaling = late > 0;
                var diff = Math.Abs(late);

                factors.Add(isBlueScaling
                    ? $"Скейлінг композиції: Сині мають кращий лейт-гейм (+{diff:F0}% Late Power). Затягування гри вигідне Синім."
                    : $"Скейлінг композиції: Червоні мають кращий лейт-гейм (+{diff:F0}% Late Power). Синім необхідно реалізувати ранній темп.");
            }

            if (features.AvgRankScore >= 8.0f)
            {
                factors.Add($"Лобі високого рангу (~{RankScoreHelper.FromScore(features.AvgRankScore)}): переваги реалізуються чистіше, помилки дорожчі.");
            }
            else if (features.AvgRankScore > 0 && features.AvgRankScore <= 3.5f)
            {
                factors.Add($"Лобі низького рангу (~{RankScoreHelper.FromScore(features.AvgRankScore)}): великий розкид виконання — ліди менш гарантовані.");
            }

            redProb = 1.0f - blueProb;
            predictedWinner = blueProb >= 0.5f ? TeamSide.Blue : TeamSide.Red;
            confidence = Math.Abs(blueProb - 0.5f) * 2.0;

            if (factors.Count == 0)
            {
                factors.Add("Рівна гра на ранній стадії (відсутня значна перевага за ключовими об'єктами)");
            }

            return new PredictionResult
            {
                BlueWinProbability = blueProb,
                RedWinProbability = redProb,
                PredictedWinner = predictedWinner,
                ConfidenceScore = confidence,
                KeyFactors = factors,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public Task TrainModelAsync(IEnumerable<Match> historicalMatches, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            // Chronological order first so the held-out slice is "future" games, not a random leaky mix.
            var orderedMatches = historicalMatches
                .OrderBy(m => m.GameCreation)
                .ThenBy(m => m.MatchId, StringComparer.Ordinal)
                .ToList();

            var matchDataList = RealMatchDatasetCollector.ExtractFeaturesFromMatches(orderedMatches);

            if (matchDataList.Count >= 10)
            {
                lock (_lock)
                {
                    var dataView = _mlContext.Data.LoadFromEnumerable(matchDataList);
                    var benchmark = new ModelBenchmarkService();
                    var treePipeline = benchmark.BuildPipeline(MLAlgorithmType.FastTree);
                    var forestPipeline = benchmark.BuildPipeline(MLAlgorithmType.FastForest);

                    if (matchDataList.Count >= 20)
                    {
                        // Temporal holdout: train on older ~80%, evaluate on newest ~20%.
                        var testCount = Math.Max(1, (int)Math.Round(matchDataList.Count * 0.2));
                        var trainCount = matchDataList.Count - testCount;
                        var trainSet = matchDataList.Take(trainCount).ToList();
                        var testSet = matchDataList.Skip(trainCount).ToList();

                        var trainView = _mlContext.Data.LoadFromEnumerable(trainSet);
                        var treeModel = treePipeline.Fit(trainView);
                        var forestModel = forestPipeline.Fit(trainView);

                        var treeEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(treeModel);
                        var forestEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(forestModel);

                        PredictionEngine<MatchInputData, MatchPrediction>? highEloEngine = null;
                        var highTrain = trainSet
                            .Where(r => r.AvgRankScore >= ModelBenchmarkService.HighEloRankThreshold)
                            .ToList();
                        if (highTrain.Count >= 180)
                        {
                            var highView = _mlContext.Data.LoadFromEnumerable(highTrain);
                            var highModel = treePipeline.Fit(highView);
                            highEloEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(highModel);
                        }

                        CurrentModelMetrics = ModelBenchmarkService.EvaluateSoftEnsemble(
                            _mlContext, treeEngine, forestEngine, testSet, highEloEngine);

                        // Lab ablation: which feature groups actually move temporal accuracy.
                        if (trainSet.Count >= 120 && testSet.Count >= 30)
                        {
                            CurrentModelMetrics.AblationAccuracyDrop =
                                ModelBenchmarkService.RunFeatureGroupAblation(
                                    _mlContext, trainSet, testSet, CurrentModelMetrics.Accuracy);
                        }

                        // Retrain on the full chronological corpus for the shipped model weights.
                        _model = treePipeline.Fit(dataView);
                        _ensembleForestModel = forestPipeline.Fit(dataView);

                        var highFull = matchDataList
                            .Where(r => r.AvgRankScore >= ModelBenchmarkService.HighEloRankThreshold)
                            .ToList();
                        _highEloModel = highFull.Count >= 180
                            ? treePipeline.Fit(_mlContext.Data.LoadFromEnumerable(highFull))
                            : null;
                    }
                    else
                    {
                        _model = treePipeline.Fit(dataView);
                        _ensembleForestModel = forestPipeline.Fit(dataView);
                        _highEloModel = null;
                    }

                    var trainer = new ModelTrainer();
                    var modelFile = Path.Combine(_modelDirectory, $"{ActiveAlgorithm.ToString().ToLower()}_model.zip");
                    var forestFile = Path.Combine(_modelDirectory, "fastforest_model.zip");
                    var highFile = Path.Combine(_modelDirectory, "fasttree_highelo_model.zip");
                    trainer.SaveModel(_model, dataView.Schema, modelFile);
                    if (_ensembleForestModel != null)
                        trainer.SaveModel(_ensembleForestModel, dataView.Schema, forestFile);
                    if (_highEloModel != null)
                        trainer.SaveModel(_highEloModel, dataView.Schema, highFile);
                    else if (File.Exists(highFile))
                        File.Delete(highFile);

                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);
                    _ensembleForestEngine = _ensembleForestModel != null
                        ? _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_ensembleForestModel)
                        : null;
                    _highEloEngine = _highEloModel != null
                        ? _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_highEloModel)
                        : null;

                    IsTrainedOnRealData = true;
                    TrainingDatasetSize = matchDataList.Count;
                    LastTrainedAt = DateTime.UtcNow;
                }
            }
        }, ct);
    }
}
