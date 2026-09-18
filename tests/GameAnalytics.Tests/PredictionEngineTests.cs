using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class PredictionEngineTests
{
    [Fact]
    public void PredictMatchOutcome_WithBlueAdvantage_ReturnsHigherBlueProbability()
    {
        // Arrange
        var engine = new MatchPredictionEngine();
        var blueAdvantageFeatures = new MatchInputFeatures
        {
            BlueFirstBlood = true,
            BlueFirstTower = true,
            BlueFirstDragon = true,
            GoldDiffAt15 = 3500,
            KillDiffAt15 = 6,
            BlueTeamAvgWinRate = 54.0f,
            RedTeamAvgWinRate = 48.0f,
            BlueTowerCount = 3,
            RedTowerCount = 0,
            BlueDragonCount = 2,
            RedDragonCount = 0
        };

        // Act
        var result = engine.PredictMatchOutcome(blueAdvantageFeatures);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.BlueWinProbability > 0.5);
        Assert.Equal(TeamSide.Blue, result.PredictedWinner);
        Assert.NotEmpty(result.KeyFactors);
    }

    [Fact]
    public void PredictMatchOutcome_WithRedAdvantage_ReturnsHigherRedProbability()
    {
        // Arrange
        var engine = new MatchPredictionEngine();
        var redAdvantageFeatures = new MatchInputFeatures
        {
            BlueFirstBlood = false,
            BlueFirstTower = false,
            BlueFirstDragon = false,
            GoldDiffAt15 = -3500,
            KillDiffAt15 = -6,
            BlueTeamAvgWinRate = 47.0f,
            RedTeamAvgWinRate = 54.0f,
            BlueTowerCount = 0,
            RedTowerCount = 3,
            BlueDragonCount = 0,
            RedDragonCount = 2
        };

        // Act
        var result = engine.PredictMatchOutcome(redAdvantageFeatures);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.RedWinProbability > 0.5);
        Assert.Equal(TeamSide.Red, result.PredictedWinner);
    }

    [Fact]
    public void PredictMatchOutcome_Probabilities_SumToOne()
    {
        // Arrange
        var engine = new MatchPredictionEngine();
        var features = new MatchInputFeatures
        {
            GoldDiffAt15 = 1000,
            KillDiffAt15 = 2
        };

        // Act
        var result = engine.PredictMatchOutcome(features);

        // Assert
        Assert.InRange(result.BlueWinProbability + result.RedWinProbability, 0.999, 1.001);
    }
}

