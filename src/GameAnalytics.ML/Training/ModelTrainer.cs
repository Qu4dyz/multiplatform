using GameAnalytics.Core.Helpers;
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
        var benchmark = new ModelBenchmarkService();
        var pipeline = benchmark.BuildPipeline(Core.Enums.MLAlgorithmType.FastTree);
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
            var towerDiff = (float)rand.Next(-3, 4);
            var dragonDiff = (float)rand.Next(-2, 3);
            var csDiff = (float)rand.Next(-40, 41);
            var xpDiff = goldDiff * 0.65f + (float)rand.Next(-300, 301);
            var voidgrubDiff = (float)rand.Next(-4, 5);
            var heraldDiff = (float)rand.Next(-1, 2);
            var earlyDiff = (float)rand.Next(-20, 21);
            var lateDiff = (float)rand.Next(-20, 21);
            var blueWr = 45f + (float)rand.NextDouble() * 10f;
            var redWr = 45f + (float)rand.NextDouble() * 10f;
            var rankScore = 1f + (float)rand.NextDouble() * 9f;
            var goldPace = 45f + (float)rand.NextDouble() * 12f;
            var topGold = (float)rand.Next(-1200, 1201);
            var jglGold = (float)rand.Next(-1200, 1201);
            var midGold = (float)rand.Next(-1500, 1501);
            var botGold = (float)rand.Next(-1800, 1801);
            var levelDiff = (float)rand.Next(-8, 9);
            var objScore = towerDiff * 1.5f + dragonDiff * 1.2f + voidgrubDiff * 0.35f + heraldDiff;
            var rankAdjGold = goldDiff * (rankScore / RankScoreHelper.DefaultMixedLobby);

            double z = 0.0;
            z += goldDiff * 0.00055;
            z += rankAdjGold * 0.00025;
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
            z += earlyDiff * 0.012;
            z += lateDiff * 0.008;
            z += (blueWr - redWr) * 0.04;
            z += midGold * 0.0002;
            z += botGold * 0.00015;
            z += levelDiff * 0.04;
            z += objScore * 0.08;
            // At higher ranks the same lead is more decisive
            z += Math.Abs(goldDiff) * (rankScore - 5.5) * 0.00002;

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
                BlueAvgWinRate = blueWr,
                RedAvgWinRate = redWr,
                TowerDiff = towerDiff,
                DragonDiff = dragonDiff,
                EarlyPowerDiff = earlyDiff,
                LatePowerDiff = lateDiff,
                AvgRankScore = rankScore,
                GoldPace15 = goldPace,
                TopGoldDiff15 = topGold,
                JungleGoldDiff15 = jglGold,
                MidGoldDiff15 = midGold,
                BotDuoGoldDiff15 = botGold,
                LevelDiff15 = levelDiff,
                ObjectiveScoreDiff = objScore,
                RankAdjustedGoldDiff = rankAdjGold,
                Label = win
            });
        }

        return list;
    }
}
