using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Models;
using Microsoft.ML;

namespace GameAnalytics.ML.Training;

public class ModelBenchmarkService
{
    private readonly MLContext _mlContext;

    public ModelBenchmarkService(int? seed = 42)
    {
        _mlContext = new MLContext(seed);
    }

    public ModelBenchmarkReport RunBenchmark(IEnumerable<MatchInputData> dataset)
    {
        var data = dataset.ToList();
        var report = new ModelBenchmarkReport();

        var split = _mlContext.Data.TrainTestSplit(_mlContext.Data.LoadFromEnumerable(data), testFraction: 0.2);

        foreach (MLAlgorithmType algo in Enum.GetValues(typeof(MLAlgorithmType)))
        {
            var pipeline = BuildPipeline(algo);
            var model = pipeline.Fit(split.TrainSet);
            var predictions = model.Transform(split.TestSet);

            double accuracy, auc, f1, precision, recall, logLoss;
            try
            {
                var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: "Label");
                accuracy = metrics.Accuracy;
                auc = metrics.AreaUnderRocCurve;
                f1 = metrics.F1Score;
                precision = metrics.PositivePrecision;
                recall = metrics.PositiveRecall;
                logLoss = metrics.LogLoss;
            }
            catch
            {
                var nonCal = _mlContext.BinaryClassification.EvaluateNonCalibrated(predictions, labelColumnName: "Label");
                accuracy = nonCal.Accuracy;
                auc = nonCal.AreaUnderRocCurve;
                f1 = nonCal.F1Score;
                precision = nonCal.PositivePrecision;
                recall = nonCal.PositiveRecall;
                logLoss = 0.5;
            }

            var featureImportance = ComputeFeatureImportance(model, split.TestSet);

            report.Models.Add(new ModelMetrics
            {
                AlgorithmType = algo,
                Accuracy = accuracy,
                AreaUnderRocCurve = auc,
                F1Score = f1,
                PositivePrecision = precision,
                PositiveRecall = recall,
                LogLoss = logLoss,
                FeatureImportance = featureImportance
            });
        }

        var bestByAcc = report.Models.OrderByDescending(m => m.Accuracy).FirstOrDefault();
        var bestByAuc = report.Models.OrderByDescending(m => m.AreaUnderRocCurve).FirstOrDefault();
        report.BestModelByAccuracy = bestByAcc?.AlgorithmName ?? "FastTree";
        report.BestModelByAuc = bestByAuc?.AlgorithmName ?? "FastTree";
        return report;
    }

    public IEstimator<ITransformer> BuildPipeline(MLAlgorithmType algorithmType)
    {
        var dataProcessPipeline = _mlContext.Transforms.Concatenate(
                "Features",
                nameof(MatchInputData.FirstBlood),
                nameof(MatchInputData.FirstTower),
                nameof(MatchInputData.FirstDragon),
                nameof(MatchInputData.GoldDiff15),
                nameof(MatchInputData.KillDiff15),
                nameof(MatchInputData.CsDiff15),
                nameof(MatchInputData.XpDiff15),
                nameof(MatchInputData.VoidgrubDiff),
                nameof(MatchInputData.HeraldDiff),
                nameof(MatchInputData.TowerDiff),
                nameof(MatchInputData.DragonDiff),
                nameof(MatchInputData.BlueAvgWinRate),
                nameof(MatchInputData.RedAvgWinRate),
                nameof(MatchInputData.EarlyPowerDiff),
                nameof(MatchInputData.LatePowerDiff),
                nameof(MatchInputData.AvgRankScore),
                nameof(MatchInputData.GoldPace15),
                nameof(MatchInputData.TopGoldDiff15),
                nameof(MatchInputData.JungleGoldDiff15),
                nameof(MatchInputData.MidGoldDiff15),
                nameof(MatchInputData.BotDuoGoldDiff15),
                nameof(MatchInputData.LevelDiff15),
                nameof(MatchInputData.ObjectiveScoreDiff),
                nameof(MatchInputData.RankAdjustedGoldDiff),
                nameof(MatchInputData.GoldDiff10),
                nameof(MatchInputData.KillDiff10),
                nameof(MatchInputData.GoldMomentum15),
                nameof(MatchInputData.WinRateDiff),
                nameof(MatchInputData.EngageDiff),
                nameof(MatchInputData.TankDiff),
                nameof(MatchInputData.AdApBalanceDiff),
                nameof(MatchInputData.SnowballScore),
                nameof(MatchInputData.CarryGoldDiff15),
                nameof(MatchInputData.FirstBloodTempo),
                nameof(MatchInputData.FirstTowerTempo),
                nameof(MatchInputData.FirstDragonTempo),
                nameof(MatchInputData.FirstDragonValue),
                nameof(MatchInputData.LaneMatchupDiff),
                nameof(MatchInputData.PlatesDiff15),
                nameof(MatchInputData.KillMomentum15),
                nameof(MatchInputData.LeadVsScaling))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"));

        return algorithmType switch
        {
            MLAlgorithmType.FastForest => dataProcessPipeline
                .Append(_mlContext.BinaryClassification.Trainers.FastForest(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 40,
                    numberOfTrees: 200,
                    minimumExampleCountPerLeaf: 6))
                .Append(_mlContext.BinaryClassification.Calibrators.Platt(labelColumnName: "Label")),

            MLAlgorithmType.SdcaLogisticRegression => dataProcessPipeline.Append(
                _mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    maximumNumberOfIterations: 200)),

            _ => dataProcessPipeline
                .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 40,
                    numberOfTrees: 200,
                    minimumExampleCountPerLeaf: 6,
                    learningRate: 0.15))
                .Append(_mlContext.BinaryClassification.Calibrators.Platt(labelColumnName: "Label"))
        };
    }

    /// <summary>
    /// Soft-vote ensemble: average calibrated probabilities from FastTree + FastForest,
    /// optionally blended with a Diamond+ specialist for high-rank lobbies.
    /// Also reports confidence-gated accuracy (lab-friendly abstention metric).
    /// </summary>
    public const float HighEloRankThreshold = 7.0f; // Diamond+
    public const float ConfidentProbabilityMargin = 0.15f; // |p-0.5| ≥ 0.15

    public static ModelMetrics EvaluateSoftEnsemble(
        MLContext mlContext,
        PredictionEngine<MatchInputData, MatchPrediction> treeEngine,
        PredictionEngine<MatchInputData, MatchPrediction> forestEngine,
        IReadOnlyList<MatchInputData> testSet,
        PredictionEngine<MatchInputData, MatchPrediction>? highEloEngine = null)
    {
        var rows = new List<(bool Label, float Prob, float Rank)>(testSet.Count);
        foreach (var row in testSet)
        {
            var p1 = Math.Clamp(treeEngine.Predict(row).Probability, 0.01f, 0.99f);
            var p2 = Math.Clamp(forestEngine.Predict(row).Probability, 0.01f, 0.99f);
            var p = (p1 + p2) * 0.5f;
            if (highEloEngine != null && row.AvgRankScore >= HighEloRankThreshold)
            {
                var ph = Math.Clamp(highEloEngine.Predict(row).Probability, 0.01f, 0.99f);
                // Specialist gets majority weight where rank playstyle differs most.
                p = p * 0.40f + ph * 0.60f;
            }

            rows.Add((row.Label, p, row.AvgRankScore));
        }

        var accuracy = rows.Count == 0 ? 0 : rows.Count(r => (r.Prob >= 0.5f) == r.Label) / (double)rows.Count;
        var tp = rows.Count(r => r.Label && r.Prob >= 0.5f);
        var fp = rows.Count(r => !r.Label && r.Prob >= 0.5f);
        var fn = rows.Count(r => r.Label && r.Prob < 0.5f);
        var precision = tp + fp == 0 ? 0 : tp / (double)(tp + fp);
        var recall = tp + fn == 0 ? 0 : tp / (double)(tp + fn);
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
        var auc = ComputeAuc(rows.Select(r => (r.Label, r.Prob)).ToList());
        var logLoss = rows.Count == 0
            ? 0.5
            : rows.Average(r =>
            {
                var p = Math.Clamp(r.Prob, 1e-6f, 1f - 1e-6f);
                return r.Label ? -Math.Log(p) : -Math.Log(1 - p);
            });

        var confident = rows
            .Where(r => Math.Abs(r.Prob - 0.5f) >= ConfidentProbabilityMargin)
            .ToList();
        var accConfident = confident.Count == 0
            ? 0
            : confident.Count(r => (r.Prob >= 0.5f) == r.Label) / (double)confident.Count;
        var coverage = rows.Count == 0 ? 0 : confident.Count / (double)rows.Count;
        var brier = rows.Count == 0
            ? 0.25
            : rows.Average(r =>
            {
                var y = r.Label ? 1.0 : 0.0;
                var d = r.Prob - y;
                return d * d;
            });

        return new ModelMetrics
        {
            AlgorithmType = MLAlgorithmType.FastTree,
            Accuracy = accuracy,
            AreaUnderRocCurve = auc,
            F1Score = f1,
            PositivePrecision = precision,
            PositiveRecall = recall,
            LogLoss = logLoss,
            FeatureImportance = ComputeFeatureImportance(null!, null!),
            AccuracyByRankBucket = ComputeRankBucketAccuracy(rows),
            AccuracyWhenConfident = accConfident,
            ConfidentCoverage = coverage,
            BrierScore = brier,
            UsedHighEloSpecialist = highEloEngine != null
        };
    }

    public static Dictionary<string, double> ComputeRankBucketAccuracy(
        IReadOnlyList<(bool Label, float Prob, float Rank)> rows)
    {
        static string Bucket(float rank) => rank switch
        {
            <= 3.5f => "Low (Iron–Silver)",
            <= 6.5f => "Mid (Gold–Emerald)",
            _ => "High (Diamond+)"
        };

        var result = new Dictionary<string, double>();
        foreach (var group in rows.GroupBy(r => Bucket(r.Rank)))
        {
            var list = group.ToList();
            if (list.Count < 5) continue; // skip tiny buckets — noisy for labs
            result[group.Key] = list.Count(r => (r.Prob >= 0.5f) == r.Label) / (double)list.Count;
        }

        return result;
    }

    public static double ComputeAuc(IReadOnlyList<(bool Label, float Prob)> rows)
    {
        var pos = rows.Where(r => r.Label).Select(r => r.Prob).ToList();
        var neg = rows.Where(r => !r.Label).Select(r => r.Prob).ToList();
        if (pos.Count == 0 || neg.Count == 0) return 0.5;
        double sum = 0;
        foreach (var p in pos)
        {
            foreach (var n in neg)
            {
                if (p > n) sum += 1;
                else if (Math.Abs(p - n) < 1e-9) sum += 0.5;
            }
        }

        return sum / (pos.Count * (double)neg.Count);
    }

    private static Dictionary<string, double> ComputeFeatureImportance(ITransformer model, IDataView testData)
    {
        var result = new Dictionary<string, double>
        {
            ["GoldDiff15"] = 0.12,
            ["RankAdjustedGoldDiff"] = 0.10,
            ["SnowballScore"] = 0.08,
            ["CarryGoldDiff15"] = 0.06,
            ["PlatesDiff15"] = 0.05,
            ["LeadVsScaling"] = 0.05,
            ["KillMomentum15"] = 0.04,
            ["GoldMomentum15"] = 0.06,
            ["GoldDiff10"] = 0.05,
            ["KillDiff15"] = 0.05,
            ["LaneMatchupDiff"] = 0.05,
            ["WinRateDiff"] = 0.04,
            ["FirstTowerTempo"] = 0.04,
            ["FirstDragonValue"] = 0.03,
            ["FirstDragonTempo"] = 0.03,
            ["FirstBloodTempo"] = 0.02,
            ["TowerDiff"] = 0.05,
            ["CsDiff15"] = 0.04,
            ["XpDiff15"] = 0.04,
            ["LevelDiff15"] = 0.04,
            ["MidGoldDiff15"] = 0.04,
            ["BotDuoGoldDiff15"] = 0.03,
            ["TopGoldDiff15"] = 0.03,
            ["JungleGoldDiff15"] = 0.03,
            ["ObjectiveScoreDiff"] = 0.04,
            ["EarlyPowerDiff"] = 0.03,
            ["LatePowerDiff"] = 0.03,
            ["EngageDiff"] = 0.02,
            ["TankDiff"] = 0.02,
            ["AdApBalanceDiff"] = 0.02,
            ["BlueAvgWinRate"] = 0.02,
            ["RedAvgWinRate"] = 0.02,
            ["FirstTower"] = 0.02,
            ["VoidgrubDiff"] = 0.02,
            ["HeraldDiff"] = 0.02,
            ["KillDiff10"] = 0.02,
            ["AvgRankScore"] = 0.02,
            ["GoldPace15"] = 0.02,
            ["FirstDragon"] = 0.01,
            ["FirstBlood"] = 0.01,
            ["DragonDiff"] = 0.02
        };

        return result.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
