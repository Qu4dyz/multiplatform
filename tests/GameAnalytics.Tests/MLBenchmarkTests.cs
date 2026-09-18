using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using GameAnalytics.ML.Training;
using Xunit;

namespace GameAnalytics.Tests;

public class MLBenchmarkTests
{
    [Fact]
    public void ModelBenchmarkService_RunBenchmark_EvaluatesAllAlgorithms()
    {
        // Arrange
        var service = new ModelBenchmarkService();
        var dataset = ModelTrainer.GenerateSyntheticRankedDataset(800);

        // Act
        var report = service.RunBenchmark(dataset);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(3, report.Models.Count);

        foreach (var model in report.Models)
        {
            Assert.InRange(model.Accuracy, 0.60, 1.0);
            Assert.InRange(model.AreaUnderRocCurve, 0.60, 1.0);
            Assert.NotEmpty(model.FeatureImportance);
        }

        Assert.NotEmpty(report.BestModelByAuc);
        Assert.NotEmpty(report.BestModelByAccuracy);
    }

    [Fact]
    public void ModelBenchmarkService_FeatureImportance_IdentifiesKeyObjectives()
    {
        // Arrange
        var service = new ModelBenchmarkService();
        var dataset = ModelTrainer.GenerateSyntheticRankedDataset(800);

        // Act
        var report = service.RunBenchmark(dataset);
        var fastTreeModel = report.Models.FirstOrDefault(m => m.AlgorithmType == MLAlgorithmType.FastTree);

        // Assert
        Assert.NotNull(fastTreeModel);
        Assert.True(fastTreeModel.FeatureImportance.ContainsKey("GoldDiff15"));
    }

    [Fact]
    public void MatchPredictionEngine_SetActiveAlgorithm_SwitchesAndPredicts()
    {
        // Arrange
        var engine = new MatchPredictionEngine();
        var features = new MatchInputFeatures
        {
            BlueFirstBlood = true,
            BlueFirstTower = true,
            GoldDiffAt15 = 2500,
            KillDiffAt15 = 4
        };

        // Act & Assert FastForest
        engine.SetActiveAlgorithm(MLAlgorithmType.FastForest);
        Assert.Equal(MLAlgorithmType.FastForest, engine.ActiveAlgorithm);
        var forestPrediction = engine.PredictMatchOutcome(features);
        Assert.NotNull(forestPrediction);
        Assert.True(forestPrediction.BlueWinProbability > 0.5);

        // Act & Assert SdcaLogisticRegression
        engine.SetActiveAlgorithm(MLAlgorithmType.SdcaLogisticRegression);
        Assert.Equal(MLAlgorithmType.SdcaLogisticRegression, engine.ActiveAlgorithm);
        var sdcaPrediction = engine.PredictMatchOutcome(features);
        Assert.NotNull(sdcaPrediction);
        Assert.True(sdcaPrediction.BlueWinProbability > 0.5);
    }
}

