using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class ModelMetrics
{
    public MLAlgorithmType AlgorithmType { get; set; }
    public string AlgorithmName => AlgorithmType switch
    {
        MLAlgorithmType.FastTree => "FastTree (Gradient Boosted Trees)",
        MLAlgorithmType.FastForest => "FastForest (Random Forest Ensemble)",
        MLAlgorithmType.SdcaLogisticRegression => "SDCA Logistic Regression",
        _ => AlgorithmType.ToString()
    };

    public double Accuracy { get; set; }
    public double AreaUnderRocCurve { get; set; }
    public double F1Score { get; set; }
    public double PositivePrecision { get; set; }
    public double PositiveRecall { get; set; }
    public double LogLoss { get; set; }
    public long TrainingDurationMs { get; set; }

    public Dictionary<string, double> FeatureImportance { get; set; } = new();

    /// <summary>Temporal-holdout accuracy broken down by lobby rank bucket (lab reporting).</summary>
    public Dictionary<string, double> AccuracyByRankBucket { get; set; } = new();

    public string FormattedAccuracy => $"{Accuracy * 100:F1}%";
    public string FormattedAuc => $"{AreaUnderRocCurve:F3}";
    public string FormattedF1 => $"{F1Score:F3}";
}

public class ModelBenchmarkReport
{
    public List<ModelMetrics> Models { get; set; } = new();
    public string BestModelByAuc { get; set; } = string.Empty;
    public string BestModelByAccuracy { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int TotalSamplesUsed { get; set; }
}

