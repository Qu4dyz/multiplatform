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
}
