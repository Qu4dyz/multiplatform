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

    public MatchPredictionEngine()
    {
        _mlContext = new MLContext(seed: 42);
        _modelDirectory = Path.Combine(AppContext.BaseDirectory, "models");
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
            var matchDataList = new List<MatchInputData>();
            foreach (var match in historicalMatches)
            {
                var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
                var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
                if (blue == null || red == null) continue;

                matchDataList.Add(new MatchInputData
                {
                    FirstBlood = blue.FirstBlood ? 1f : 0f,
                    FirstTower = blue.FirstTower ? 1f : 0f,
                    FirstDragon = blue.FirstDragon ? 1f : 0f,
                    GoldDiff15 = blue.GoldAt15 - red.GoldAt15,
                    KillDiff15 = blue.KillsAt15 - red.KillsAt15,
                    BlueAvgWinRate = 50.0f,
                    RedAvgWinRate = 50.0f,
                    TowerDiff = blue.TowerKills - red.TowerKills,
                    DragonDiff = blue.DragonKills - red.DragonKills,
                    Label = match.WinningTeam == TeamSide.Blue
                });
            }

            if (matchDataList.Count >= 20)
            {
                lock (_lock)
                {
                    var trainer = new ModelTrainer();
                    _model = trainer.TrainPipeline(matchDataList);
                    var dataView = _mlContext.Data.LoadFromEnumerable(matchDataList);
                    var modelFile = Path.Combine(_modelDirectory, $"{ActiveAlgorithm.ToString().ToLower()}_model.zip");
                    trainer.SaveModel(_model, dataView.Schema, modelFile);
                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(_model);
                }
            }
        }, ct);
    }
}
