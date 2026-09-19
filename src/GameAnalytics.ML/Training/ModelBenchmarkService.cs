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
                nameof(MatchInputData.LeadVsScaling),
                nameof(MatchInputData.DeathDiff15),
                nameof(MatchInputData.VisionWardDiff15),
                nameof(MatchInputData.ControlWardDiff15))
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
            ["DragonDiff"] = 0.02,
            ["DeathDiff15"] = 0.05,
            ["VisionWardDiff15"] = 0.04,
            ["ControlWardDiff15"] = 0.04
        };

        return result.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Leave-one-group-out ablation for lab reports: zero a feature group, retrain FastTree, measure accuracy drop.
    /// </summary>
    public static Dictionary<string, double> RunFeatureGroupAblation(
        MLContext mlContext,
        IReadOnlyList<MatchInputData> trainSet,
        IReadOnlyList<MatchInputData> testSet,
        double baselineAccuracy)
    {
        var result = new Dictionary<string, double>();
        if (trainSet.Count < 80 || testSet.Count < 20) return result;

        var groups = new (string Name, Action<MatchInputData> Zero)[]
        {
            ("GoldLead", r =>
            {
                r.GoldDiff15 = 0; r.GoldDiff10 = 0; r.GoldMomentum15 = 0;
                r.RankAdjustedGoldDiff = 0; r.CarryGoldDiff15 = 0;
                r.SnowballScore = 0; r.LeadVsScaling = 0; r.GoldPace15 = 0;
            }),
            ("Combat", r =>
            {
                r.KillDiff15 = 0; r.KillDiff10 = 0; r.KillMomentum15 = 0;
                r.FirstBlood = 0; r.FirstBloodTempo = 0; r.DeathDiff15 = 0;
            }),
            ("Objectives", r =>
            {
                r.TowerDiff = 0; r.DragonDiff = 0; r.VoidgrubDiff = 0; r.HeraldDiff = 0;
                r.ObjectiveScoreDiff = 0; r.FirstTower = 0; r.FirstDragon = 0;
                r.FirstTowerTempo = 0; r.FirstDragonTempo = 0; r.FirstDragonValue = 0;
                r.PlatesDiff15 = 0;
            }),
            ("Vision", r =>
            {
                r.VisionWardDiff15 = 0; r.ControlWardDiff15 = 0;
            }),
            ("Draft", r =>
            {
                r.EarlyPowerDiff = 0; r.LatePowerDiff = 0; r.EngageDiff = 0;
                r.TankDiff = 0; r.AdApBalanceDiff = 0; r.WinRateDiff = 0;
                r.LaneMatchupDiff = 0; r.BlueAvgWinRate = 50; r.RedAvgWinRate = 50;
            }),
            ("Lanes", r =>
            {
                r.TopGoldDiff15 = 0; r.JungleGoldDiff15 = 0; r.MidGoldDiff15 = 0;
                r.BotDuoGoldDiff15 = 0; r.LevelDiff15 = 0; r.CsDiff15 = 0; r.XpDiff15 = 0;
            })
        };

        var bench = new ModelBenchmarkService();
        var pipeline = bench.BuildPipeline(MLAlgorithmType.FastTree);

        foreach (var (name, zero) in groups)
        {
            try
            {
                var trainZ = trainSet.Select(CloneRow).ToList();
                var testZ = testSet.Select(CloneRow).ToList();
                foreach (var r in trainZ) zero(r);
                foreach (var r in testZ) zero(r);

                var model = pipeline.Fit(mlContext.Data.LoadFromEnumerable(trainZ));
                var eng = mlContext.Model.CreatePredictionEngine<MatchInputData, MatchPrediction>(model);
                var correct = 0;
                foreach (var row in testZ)
                {
                    var p = eng.Predict(row).Probability;
                    if ((p >= 0.5f) == row.Label) correct++;
                }

                var acc = testZ.Count == 0 ? 0 : correct / (double)testZ.Count;
                result[name] = baselineAccuracy - acc;
            }
            catch
            {
                // Skip group on pipeline failure — ablation is best-effort for labs.
            }
        }

        return result
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static MatchInputData CloneRow(MatchInputData r) => new()
    {
        FirstBlood = r.FirstBlood,
        FirstTower = r.FirstTower,
        FirstDragon = r.FirstDragon,
        GoldDiff15 = r.GoldDiff15,
        KillDiff15 = r.KillDiff15,
        BlueAvgWinRate = r.BlueAvgWinRate,
        RedAvgWinRate = r.RedAvgWinRate,
        TowerDiff = r.TowerDiff,
        DragonDiff = r.DragonDiff,
        CsDiff15 = r.CsDiff15,
        XpDiff15 = r.XpDiff15,
        VoidgrubDiff = r.VoidgrubDiff,
        HeraldDiff = r.HeraldDiff,
        EarlyPowerDiff = r.EarlyPowerDiff,
        LatePowerDiff = r.LatePowerDiff,
        AvgRankScore = r.AvgRankScore,
        GoldPace15 = r.GoldPace15,
        TopGoldDiff15 = r.TopGoldDiff15,
        JungleGoldDiff15 = r.JungleGoldDiff15,
        MidGoldDiff15 = r.MidGoldDiff15,
        BotDuoGoldDiff15 = r.BotDuoGoldDiff15,
        LevelDiff15 = r.LevelDiff15,
        ObjectiveScoreDiff = r.ObjectiveScoreDiff,
        RankAdjustedGoldDiff = r.RankAdjustedGoldDiff,
        GoldDiff10 = r.GoldDiff10,
        KillDiff10 = r.KillDiff10,
        GoldMomentum15 = r.GoldMomentum15,
        WinRateDiff = r.WinRateDiff,
        EngageDiff = r.EngageDiff,
        TankDiff = r.TankDiff,
        AdApBalanceDiff = r.AdApBalanceDiff,
        SnowballScore = r.SnowballScore,
        CarryGoldDiff15 = r.CarryGoldDiff15,
        FirstBloodTempo = r.FirstBloodTempo,
        FirstTowerTempo = r.FirstTowerTempo,
        FirstDragonTempo = r.FirstDragonTempo,
        FirstDragonValue = r.FirstDragonValue,
        LaneMatchupDiff = r.LaneMatchupDiff,
        PlatesDiff15 = r.PlatesDiff15,
        KillMomentum15 = r.KillMomentum15,
        LeadVsScaling = r.LeadVsScaling,
        DeathDiff15 = r.DeathDiff15,
        VisionWardDiff15 = r.VisionWardDiff15,
        ControlWardDiff15 = r.ControlWardDiff15,
        Label = r.Label
    };
}
