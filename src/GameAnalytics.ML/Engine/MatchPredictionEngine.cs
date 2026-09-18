using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
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
    private PredictionEngine<MatchInputData, MatchPrediction>? _predictionEngine;
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
                DragonDiff = features.BlueDragonCount - features.RedDragonCount
            };

            var prediction = _predictionEngine!.Predict(input);

            // Clamp probability between 0.05 and 0.95 for realistic LoL predictions
            var blueProb = Math.Clamp(prediction.Probability, 0.05f, 0.95f);
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
            var matchDataList = RealMatchDatasetCollector.ExtractFeaturesFromMatches(historicalMatches);

            if (matchDataList.Count >= 10)
            {
                lock (_lock)
                {
                    var dataView = _mlContext.Data.LoadFromEnumerable(matchDataList);
                    var benchmark = new ModelBenchmarkService();
                    var pipeline = benchmark.BuildPipeline(ActiveAlgorithm);

                    if (matchDataList.Count >= 20)
                    {
                        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);
                        _model = pipeline.Fit(split.TrainSet);
                        var predictions = _model.Transform(split.TestSet);
                        try
                        {
                            var eval = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");
                            CurrentModelMetrics = new ModelMetrics
                            {
                                AlgorithmType = ActiveAlgorithm,
                                Accuracy = eval.Accuracy,
                                AreaUnderRocCurve = eval.AreaUnderRocCurve,
                                F1Score = eval.F1Score,
                                PositivePrecision = eval.PositivePrecision,
                                PositiveRecall = eval.PositiveRecall,
                                LogLoss = eval.LogLoss
                            };
                        }
                        catch
                        {
                            var nonCal = _mlContext.BinaryClassification.EvaluateNonCalibrated(predictions, labelColumnName: "Label");
                            CurrentModelMetrics = new ModelMetrics
                            {
                                AlgorithmType = ActiveAlgorithm,
                                Accuracy = nonCal.Accuracy,
                                AreaUnderRocCurve = nonCal.AreaUnderRocCurve,
                                F1Score = nonCal.F1Score,
                                PositivePrecision = nonCal.PositivePrecision,
                                PositiveRecall = nonCal.PositiveRecall,
                                LogLoss = 0.5
                            };
                        }
                    }
                    else
                    {
                        _model = pipeline.Fit(dataView);
                    }

                    var trainer = new ModelTrainer();
                    var modelFile = Path.Combine(_modelDirectory, $"{ActiveAlgorithm.ToString().ToLower()}_model.zip");
                    trainer.SaveModel(_model, dataView.Schema, modelFile);
                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);

                    IsTrainedOnRealData = true;
                    TrainingDatasetSize = matchDataList.Count;
                    LastTrainedAt = DateTime.UtcNow;
                }
            }
        }, ct);
    }
}
