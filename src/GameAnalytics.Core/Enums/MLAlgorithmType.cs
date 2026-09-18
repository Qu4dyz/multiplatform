namespace GameAnalytics.Core.Enums;

public enum MLAlgorithmType
{
    FastTree,                  // Gradient Boosted Trees (best non-linear accuracy)
    FastForest,                // Random Forest ensemble
    SdcaLogisticRegression     // Stochastic Dual Coordinate Ascent Logistic Regression
}
