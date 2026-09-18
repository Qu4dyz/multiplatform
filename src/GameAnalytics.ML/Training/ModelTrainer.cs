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
                nameof(MatchInputData.BlueAvgWinRate),
                nameof(MatchInputData.RedAvgWinRate),
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
            var blueWinRate = (float)rand.Next(44, 58);
            var redWinRate = (float)rand.Next(44, 58);
            var towerDiff = (float)rand.Next(-5, 6);
            var dragonDiff = (float)rand.Next(-3, 4);

            // Log-odds simulation for realistic LoL Solo/Duo Ranked match outcome
            double z = 0.0;
            z += goldDiff * 0.00075;      // 2000 gold diff gives substantial boost
            z += killDiff * 0.12;         // each kill provides edge
            z += firstTower * 0.45;       // first turret gives map pressure
            z += firstDragon * 0.30;
            z += firstBlood * 0.20;
            z += (blueWinRate - redWinRate) * 0.08;
            z += towerDiff * 0.35;
            z += dragonDiff * 0.25;

            var probability = 1.0 / (1.0 + Math.Exp(-z));
            var win = rand.NextDouble() < probability;

            list.Add(new MatchInputData
            {
                FirstBlood = firstBlood,
                FirstTower = firstTower,
                FirstDragon = firstDragon,
                GoldDiff15 = goldDiff,
                KillDiff15 = killDiff,
                BlueAvgWinRate = blueWinRate,
                RedAvgWinRate = redWinRate,
                TowerDiff = towerDiff,
                DragonDiff = dragonDiff,
                Label = win
            });
        }

        return list;
    }
}
