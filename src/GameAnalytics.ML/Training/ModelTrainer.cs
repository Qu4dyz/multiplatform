using GameAnalytics.Core.Entities;
using GameAnalytics.ML.Models;
using Microsoft.ML;

namespace GameAnalytics.ML.Training;

public class ModelTrainer
{
    private readonly MLContext _mlContext;

    public ModelTrainer(int? seed = 42)
    {
        _mlContext = new MLContext(seed);
    }

    public ITransformer TrainPipeline(IEnumerable<MatchInputData> trainingData)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = _mlContext.Transforms.Concatenate(
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
                nameof(MatchInputData.DragonDiff))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: "Label",
                featureColumnName: "Features",
                numberOfLeaves: 20,
                numberOfTrees: 50,
                minimumExampleCountPerLeaf: 10));

        return pipeline.Fit(dataView);
    }

    public void SaveModel(ITransformer model, DataViewSchema schema, string modelPath)
    {
        var directory = Path.GetDirectoryName(modelPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _mlContext.Model.Save(model, schema, modelPath);
    }

    public static List<MatchInputData> GenerateSyntheticRankedDataset(int sampleCount = 1000)
    {
        var list = new List<MatchInputData>();
        var rand = new Random(42);

        for (int i = 0; i < sampleCount; i++)
        {
            var firstBlood = rand.Next(0, 2) == 1 ? 1f : 0f;
            var firstTower = rand.Next(0, 2) == 1 ? 1f : 0f;
            var firstDragon = rand.Next(0, 2) == 1 ? 1f : 0f;

            var goldDiff = (float)rand.Next(-5000, 5000);
            var killDiff = (float)rand.Next(-15, 15);
            // At minute 15 tower/dragon spreads are small (not end-game scoreboards).
            var towerDiff = (float)rand.Next(-3, 4);
            var dragonDiff = (float)rand.Next(-2, 3);
            var csDiff = (float)rand.Next(-40, 41);
            var xpDiff = goldDiff * 0.65f + (float)rand.Next(-300, 301);
            var voidgrubDiff = (float)rand.Next(-4, 5);
            var heraldDiff = (float)rand.Next(-1, 2);

            // Log-odds simulation for realistic LoL Solo/Duo Ranked match outcome from minute-15 state
            double z = 0.0;
            z += goldDiff * 0.00055;
            z += killDiff * 0.10;
            z += firstTower * 0.35;
            z += firstDragon * 0.25;
            z += firstBlood * 0.15;
            z += voidgrubDiff * 0.12;
            z += heraldDiff * 0.20;
            z += csDiff * 0.012;
            z += xpDiff * 0.00025;
            z += towerDiff * 0.22;
            z += dragonDiff * 0.18;

            var probability = 1.0 / (1.0 + Math.Exp(-z));
            var win = rand.NextDouble() < probability;

            list.Add(new MatchInputData
            {
                FirstBlood = firstBlood,
                FirstTower = firstTower,
                FirstDragon = firstDragon,
                GoldDiff15 = goldDiff,
                KillDiff15 = killDiff,
                CsDiff15 = csDiff,
                XpDiff15 = xpDiff,
                VoidgrubDiff = voidgrubDiff,
                HeraldDiff = heraldDiff,
                BlueAvgWinRate = 50f,
                RedAvgWinRate = 50f,
                TowerDiff = towerDiff,
                DragonDiff = dragonDiff,
                Label = win
            });
        }

        return list;
    }
}

