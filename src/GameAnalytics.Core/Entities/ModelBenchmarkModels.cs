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

    /// <summary>
    /// Accuracy only on confident predictions (|p−0.5| ≥ ConfidentMargin).
    /// Strong lab metric: abstain on coin-flip games, measure when the model commits.
    /// </summary>
    public double AccuracyWhenConfident { get; set; }

    /// <summary>Fraction of the temporal test set that was confident enough to score.</summary>
    public double ConfidentCoverage { get; set; }

    /// <summary>Mean squared error of probabilities (lower is better calibration).</summary>
    public double BrierScore { get; set; }

    /// <summary>True when a Diamond+ specialist model was blended into evaluation/prediction.</summary>
    public bool UsedHighEloSpecialist { get; set; }

    /// <summary>True when a Gold–Emerald mid-elo specialist was blended.</summary>
    public bool UsedMidEloSpecialist { get; set; }

    /// <summary>FastTree weight in the soft ensemble (FastForest = 1 − TreeWeight).</summary>
    public float EnsembleTreeWeight { get; set; } = 0.5f;

    /// <summary>
    /// Leave-one-group-out ablation: accuracy drop when a feature group is zeroed and FastTree is retrained.
    /// Key = group name, Value = baselineAcc − ablatedAcc (positive ⇒ group helps).
    /// </summary>
    public Dictionary<string, double> AblationAccuracyDrop { get; set; } = new();

    /// <summary>Feature groups soft-pruned (scaled down) after negative ablation.</summary>
    public List<string> SoftPrunedGroups { get; set; } = new();

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

