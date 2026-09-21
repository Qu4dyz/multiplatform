using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Helpers;
using GameAnalytics.ML.Training;

namespace GameAnalytics.Tests;

public class MlFeatureV2Tests
{
    [Theory]
    [InlineData("FIRE_DRAGON", 1.25f)]
    [InlineData("HEXTECH_DRAGON", 1.15f)]
    [InlineData("AIR_DRAGON", 0.90f)]
    public void DragonValueHelper_MapsKnownSubTypes(string subType, float expected)
    {
        Assert.Equal(expected, DragonValueHelper.ValueForSubType(subType));
    }

    [Fact]
    public void DragonValueHelper_SignedTempo_EarlierIsStronger()
    {
        var early = DragonValueHelper.SignedTempo(true, 3 * 60_000);  // 3'
        var late = DragonValueHelper.SignedTempo(true, 12 * 60_000); // 12'
        Assert.True(early > late);
        Assert.True(early > 0);
        Assert.Equal(-early, DragonValueHelper.SignedTempo(false, 3 * 60_000));
    }

    [Fact]
    public void ExtractFeatures_IncludesTempoCarryAndLaneMatchupFields()
    {
        // Explicit priors: 6 blue wins for Garen vs Darius top → later games get positive lane matchup.
        var priors = Enumerable.Range(0, 6)
            .Select(i => MakeMatch($"PRIOR{i}", TeamSide.Blue, "Garen", "Darius",
                DateTime.UtcNow.AddDays(-20 + i), goldDiff: 50))
            .ToList();

        var m1 = MakeMatch("A", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow.AddDays(-2), goldDiff: 800);
        m1.FirstTowerTempo = 6f;
        m1.FirstDragonValue = 1.25f;
        m1.CarryGoldDiff15 = 400;
        m1.ApproxRankScore = 7f;

        var m2 = MakeMatch("B", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow.AddDays(-1), goldDiff: 200);

        var features = RealMatchDatasetCollector.ExtractFeaturesFromMatches(priors.Concat(new[] { m1, m2 }));
        Assert.True(features.Count >= 2);

        var withTempo = features.First(f => Math.Abs(f.FirstTowerTempo - 6f) < 0.01f);
        Assert.Equal(1.25f, withTempo.FirstDragonValue);
        Assert.Equal(400f, withTempo.CarryGoldDiff15);

        var last = features[^1];
        Assert.True(last.LaneMatchupDiff > 0);
    }

    [Fact]
    public void ExtractFeatures_ComputesKillMomentumAndLeadVsScaling()
    {
        var match = MakeMatch("LS1", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow, goldDiff: 2000);
        match.GoldDiff10 = 800;
        match.KillDiff10 = 2;
        match.Teams.First(t => t.TeamSide == TeamSide.Blue).KillsAt15 = 10;
        match.Teams.First(t => t.TeamSide == TeamSide.Red).KillsAt15 = 5;
        match.PlatesDiff15 = 3;

        var features = RealMatchDatasetCollector.ExtractFeaturesFromMatches(new[] { match });
        Assert.Single(features);
        Assert.Equal(3f, features[0].PlatesDiff15);
        Assert.Equal(3f, features[0].KillMomentum15); // (10-5) - 2
        Assert.True(float.IsFinite(features[0].LeadVsScaling));
    }

    [Fact]
    public void ExtractFeatures_IncludesVisionAndDeathDiff()
    {
        var match = MakeMatch("VIS1", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow, goldDiff: 1000);
        match.DeathDiff15 = -3f;       // blue died less
        match.VisionWardDiff15 = 8f;
        match.ControlWardDiff15 = 2f;

        var features = RealMatchDatasetCollector.ExtractFeaturesFromMatches(new[] { match });
        Assert.Single(features);
        Assert.Equal(-3f, features[0].DeathDiff15);
        Assert.Equal(8f, features[0].VisionWardDiff15);
        Assert.Equal(2f, features[0].ControlWardDiff15);
    }

    [Fact]
    public void ExtractFeatures_IncludesCsXpAt10()
    {
        var match = MakeMatch("CS10", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow, goldDiff: 1200);
        match.CsDiff10 = 25f;
        match.XpDiff10 = 900f;
        match.HasCsXp10Features = true;

        var features = RealMatchDatasetCollector.ExtractFeaturesFromMatches(new[] { match });
        Assert.Single(features);
        Assert.Equal(25f, features[0].CsDiff10);
        Assert.Equal(900f, features[0].XpDiff10);
    }

    [Fact]
    public void OptimizeEnsembleTreeWeight_ReturnsWeightInRange()
    {
        var data = ModelTrainer.GenerateSyntheticRankedDataset(300);
        var ml = new Microsoft.ML.MLContext(seed: 3);
        var bench = new ModelBenchmarkService();
        var train = data.Take(240).ToList();
        var cal = data.Skip(240).ToList();
        var tree = bench.BuildPipeline(GameAnalytics.Core.Enums.MLAlgorithmType.FastTree)
            .Fit(ml.Data.LoadFromEnumerable(train));
        var forest = bench.BuildPipeline(GameAnalytics.Core.Enums.MLAlgorithmType.FastForest)
            .Fit(ml.Data.LoadFromEnumerable(train));
        var treeEng = ml.Model.CreatePredictionEngine<GameAnalytics.ML.Models.MatchInputData, GameAnalytics.ML.Models.MatchPrediction>(tree);
        var forestEng = ml.Model.CreatePredictionEngine<GameAnalytics.ML.Models.MatchInputData, GameAnalytics.ML.Models.MatchPrediction>(forest);

        var w = ModelBenchmarkService.OptimizeEnsembleTreeWeight(treeEng, forestEng, cal, previousWeight: 0.55f);
        Assert.InRange(w, 0.25f, 0.80f);
    }

    [Fact]
    public void SoftPrune_ScalesNegativeAblationGroups()
    {
        var row = ModelTrainer.GenerateSyntheticRankedDataset(1)[0];
        row.TopGoldDiff15 = 100f;
        row.CsDiff10 = 40f;
        var ablation = new Dictionary<string, double> { ["Lanes"] = -0.02, ["GoldLead"] = 0.05 };
        var pruned = ModelBenchmarkService.ApplySoftPruneFromAblation(new[] { row }, ablation);
        Assert.Contains("Lanes", pruned);
        Assert.DoesNotContain("GoldLead", pruned);
        Assert.True(Math.Abs(row.TopGoldDiff15 - 100f * ModelBenchmarkService.SoftPruneScale) < 0.01f);
        Assert.True(Math.Abs(row.CsDiff10 - 40f * ModelBenchmarkService.SoftPruneScale) < 0.01f);
    }

    [Fact]
    public void UpdateAblationEma_SmoothsNoisyEpoch()
    {
        var ema = new Dictionary<string, double>();
        ModelBenchmarkService.UpdateAblationEma(ema, new Dictionary<string, double> { ["Draft"] = -0.02 });
        ModelBenchmarkService.UpdateAblationEma(ema, new Dictionary<string, double> { ["Draft"] = 0.03 });
        Assert.True(ema["Draft"] > ModelBenchmarkService.SoftPruneNegativeDrop);
    }

    [Fact]
    public void RunFeatureGroupAblation_ReportsGroupDrops()
    {
        var data = ModelTrainer.GenerateSyntheticRankedDataset(500);
        var ml = new Microsoft.ML.MLContext(seed: 7);
        var train = data.Take(400).ToList();
        var test = data.Skip(400).ToList();

        var bench = new ModelBenchmarkService();
        var pipe = bench.BuildPipeline(GameAnalytics.Core.Enums.MLAlgorithmType.FastTree);
        var model = pipe.Fit(ml.Data.LoadFromEnumerable(train));
        var eng = ml.Model.CreatePredictionEngine<GameAnalytics.ML.Models.MatchInputData, GameAnalytics.ML.Models.MatchPrediction>(model);
        var baseline = test.Count(r => (eng.Predict(r).Probability >= 0.5f) == r.Label) / (double)test.Count;

        var ablation = ModelBenchmarkService.RunFeatureGroupAblation(ml, train, test, baseline);
        Assert.True(ablation.Count >= 4);
        Assert.Contains("GoldLead", ablation.Keys);
        Assert.Contains("Vision", ablation.Keys);
        // Gold lead should matter on synthetic data with gold-driven labels.
        Assert.True(ablation["GoldLead"] > 0.01);
    }

    [Fact]
    public void EvaluateSoftEnsemble_ReportsConfidenceGatedAccuracy()
    {
        var data = ModelTrainer.GenerateSyntheticRankedDataset(400);
        var ml = new Microsoft.ML.MLContext(seed: 1);
        var bench = new ModelBenchmarkService();
        var pipe = bench.BuildPipeline(GameAnalytics.Core.Enums.MLAlgorithmType.FastTree);
        var forest = bench.BuildPipeline(GameAnalytics.Core.Enums.MLAlgorithmType.FastForest);
        var train = data.Take(320).ToList();
        var test = data.Skip(320).ToList();
        var treeModel = pipe.Fit(ml.Data.LoadFromEnumerable(train));
        var forestModel = forest.Fit(ml.Data.LoadFromEnumerable(train));
        var treeEng = ml.Model.CreatePredictionEngine<GameAnalytics.ML.Models.MatchInputData, GameAnalytics.ML.Models.MatchPrediction>(treeModel);
        var forestEng = ml.Model.CreatePredictionEngine<GameAnalytics.ML.Models.MatchInputData, GameAnalytics.ML.Models.MatchPrediction>(forestModel);

        var metrics = ModelBenchmarkService.EvaluateSoftEnsemble(ml, treeEng, forestEng, test, treeWeight: 0.60f);
        Assert.InRange(metrics.Accuracy, 0.55, 1.0);
        Assert.Equal(0.60f, metrics.EnsembleTreeWeight);
        Assert.True(metrics.ConfidentCoverage > 0);
        Assert.True(metrics.AccuracyWhenConfident >= metrics.Accuracy - 0.05);
        Assert.InRange(metrics.BrierScore, 0, 0.5);
    }

    private static Match MakeMatch(
        string id,
        TeamSide winner,
        string blueTop,
        string redTop,
        DateTime created,
        int goldDiff)
    {
        var blueGold = 24000 + goldDiff / 2;
        var redGold = 24000 - goldDiff / 2;
        return new Match
        {
            MatchId = id,
            QueueId = 420,
            GameCreation = created,
            GameDurationSeconds = 1800,
            WinningTeam = winner,
            HasMinute15Objectives = true,
            Participants =
            {
                new Participant { ChampionName = blueTop, TeamSide = TeamSide.Blue, Position = Position.Top, Win = winner == TeamSide.Blue, ParticipantId = 1 },
                new Participant { ChampionName = "LeeSin", TeamSide = TeamSide.Blue, Position = Position.Jungle, Win = winner == TeamSide.Blue, ParticipantId = 2 },
                new Participant { ChampionName = "Ahri", TeamSide = TeamSide.Blue, Position = Position.Middle, Win = winner == TeamSide.Blue, ParticipantId = 3 },
                new Participant { ChampionName = "Jinx", TeamSide = TeamSide.Blue, Position = Position.Bottom, Win = winner == TeamSide.Blue, ParticipantId = 4 },
                new Participant { ChampionName = "Thresh", TeamSide = TeamSide.Blue, Position = Position.Utility, Win = winner == TeamSide.Blue, ParticipantId = 5 },
                new Participant { ChampionName = redTop, TeamSide = TeamSide.Red, Position = Position.Top, Win = winner == TeamSide.Red, ParticipantId = 6 },
                new Participant { ChampionName = "JarvanIV", TeamSide = TeamSide.Red, Position = Position.Jungle, Win = winner == TeamSide.Red, ParticipantId = 7 },
                new Participant { ChampionName = "Syndra", TeamSide = TeamSide.Red, Position = Position.Middle, Win = winner == TeamSide.Red, ParticipantId = 8 },
                new Participant { ChampionName = "KaiSa", TeamSide = TeamSide.Red, Position = Position.Bottom, Win = winner == TeamSide.Red, ParticipantId = 9 },
                new Participant { ChampionName = "Nautilus", TeamSide = TeamSide.Red, Position = Position.Utility, Win = winner == TeamSide.Red, ParticipantId = 10 },
            },
            Teams =
            {
                new TeamStats { TeamSide = TeamSide.Blue, GoldAt15 = blueGold, KillsAt15 = 10, CsAt15 = 400, XpAt15 = 9000, TowersAt15 = 1, DragonsAt15 = 1, Win = winner == TeamSide.Blue },
                new TeamStats { TeamSide = TeamSide.Red, GoldAt15 = redGold, KillsAt15 = 8, CsAt15 = 380, XpAt15 = 8500, TowersAt15 = 0, DragonsAt15 = 0, Win = winner == TeamSide.Red }
            }
        };
    }

    [Fact]
    public void TryTagMatchRankScore_AveragesKnownParticipantHints()
    {
        var match = MakeMatch("RANK1", TeamSide.Blue, "Garen", "Darius", DateTime.UtcNow, goldDiff: 100);
        match.Participants[0].Puuid = "p-challenger";
        match.Participants[5].Puuid = "p-silver";
        var hints = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["p-challenger"] = RankScoreHelper.FromTier(GameTier.Challenger),
            ["p-silver"] = RankScoreHelper.FromTier(GameTier.Silver)
        };

        Assert.True(RealMatchDatasetCollector.TryTagMatchRankScore(match, hints));
        Assert.Equal(6.5f, match.ApproxRankScore, 1); // (10+3)/2
    }

    [Fact]
    public void RankBuckets_LowMidHigh_AreContiguous()
    {
        Assert.True(ModelBenchmarkService.LowEloRankMax == ModelBenchmarkService.MidEloRankMin);
        Assert.True(ModelBenchmarkService.MidEloRankMax == ModelBenchmarkService.HighEloRankThreshold);
        Assert.True(2.0f < ModelBenchmarkService.LowEloRankMax);
        Assert.True(5.0f >= ModelBenchmarkService.MidEloRankMin
                    && 5.0f < ModelBenchmarkService.MidEloRankMax);
    }
}
