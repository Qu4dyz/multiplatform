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
            var gold10 = goldDiff * (0.55f + (float)rand.NextDouble() * 0.25f);
            var kill10 = killDiff * (0.5f + (float)rand.NextDouble() * 0.3f);
            var momentum = goldDiff - gold10;
            var wrDiff = blueWr - redWr;
            var engage = (float)rand.Next(-3, 4);
            var tank = (float)rand.Next(-3, 4);
            var adAp = (float)rand.Next(-3, 4);
            var snowball = (killDiff * 400f + goldDiff) / 1000f;
            var carryGold = goldDiff * (0.25f + (float)rand.NextDouble() * 0.3f);
            var fbTempo = firstBlood * (2f + (float)rand.NextDouble() * 8f) * (rand.Next(0, 2) == 0 ? 1f : -1f);
            var ftTempo = firstTower * (1f + (float)rand.NextDouble() * 7f) * (rand.Next(0, 2) == 0 ? 1f : -1f);
            var fdTempo = firstDragon * (1f + (float)rand.NextDouble() * 6f) * (rand.Next(0, 2) == 0 ? 1f : -1f);
            var dragonVal = firstDragon * (0.9f + (float)rand.NextDouble() * 0.4f) * (rand.Next(0, 2) == 0 ? 1f : -1f);
            var laneMu = (float)(rand.NextDouble() * 10 - 5);

            double z = 0.0;
            z += goldDiff * 0.00055;
            z += rankAdjGold * 0.00025;
            z += gold10 * 0.0003;
            z += momentum * 0.0004;
            z += killDiff * 0.10;
            z += kill10 * 0.05;
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
            z += wrDiff * 0.04;
            z += midGold * 0.0002;
            z += botGold * 0.00015;
            z += levelDiff * 0.04;
            z += objScore * 0.08;
            z += snowball * 0.08;
            z += engage * 0.05;
            z += tank * 0.03;
            z += adAp * 0.02;
            z += carryGold * 0.00035;
            z += fbTempo * 0.03;
            z += ftTempo * 0.04;
            z += fdTempo * 0.035;
            z += dragonVal * 0.12;
            z += laneMu * 0.035;
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
                GoldDiff10 = gold10,
                KillDiff10 = kill10,
                GoldMomentum15 = momentum,
                WinRateDiff = wrDiff,
                EngageDiff = engage,
                TankDiff = tank,
                AdApBalanceDiff = adAp,
                SnowballScore = snowball,
                CarryGoldDiff15 = carryGold,
                FirstBloodTempo = fbTempo,
                FirstTowerTempo = ftTempo,
                FirstDragonTempo = fdTempo,
                FirstDragonValue = dragonVal,
                LaneMatchupDiff = laneMu,
                Label = win
            });
        }

        return list;
    }
}
