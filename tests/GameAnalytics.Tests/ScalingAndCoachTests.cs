using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class ScalingAndCoachTests
{
    [Fact]
    public void ChampionScalingAnalyzer_IdentifiesHyperScalersAndEarlyAggroCorrectly()
    {
        var kayle = ChampionScalingAnalyzer.AnalyzeChampion("Kayle");
        Assert.True(kayle.LateGameScore >= 9);
        Assert.True(kayle.EarlyGameScore <= 4);

        var renekton = ChampionScalingAnalyzer.AnalyzeChampion("Renekton");
        Assert.True(renekton.EarlyGameScore >= 8);
        Assert.True(renekton.LateGameScore <= 5);

        var ahri = ChampionScalingAnalyzer.AnalyzeChampion("Ahri");
        Assert.True(ahri.MidGameScore >= 7);
    }

    [Fact]
    public void ChampionScalingAnalyzer_CompareDraftCompositions_DetectsScalingAdvantage()
    {
        var earlyTeam = new[] { "Renekton", "Lee Sin", "LeBlanc", "Draven", "Blitzcrank" };
        var lateTeam = new[] { "Kayle", "Kassadin", "Smolder", "Vayne", "Sona" };

        var comparison = ChampionScalingAnalyzer.CompareDraftCompositions(earlyTeam, lateTeam);

        Assert.NotNull(comparison);
        Assert.True(comparison.BlueTeam.EarlyGamePower > comparison.RedTeam.EarlyGamePower);
        Assert.True(comparison.RedTeam.LateGamePower > comparison.BlueTeam.LateGamePower);
        Assert.NotEmpty(comparison.ComparativeVerdict);
    }

    [Fact]
    public void PredictMatchOutcome_WithDragonSoul_AdjustsProbabilityAndAddsFactor()
    {
        var engine = new MatchPredictionEngine();
        var featuresWithSoul = new MatchInputFeatures
        {
            BlueDragonCount = 4,
            RedDragonCount = 1,
            SoulType = DragonSoulType.Hextech,
            SoulOwner = TeamSide.Blue,
            GoldDiffAt15 = 500
        };

        var result = engine.PredictMatchOutcome(featuresWithSoul);

        Assert.NotNull(result);
        Assert.True(result.BlueWinProbability > 0.60);
        Assert.Contains(result.KeyFactors, f => f.Contains("Драконяча душа") && f.Contains("Hextech"));
    }

    [Fact]
    public void CoachReportExporter_GeneratesValidMarkdownWithGapAndTimeline()
    {
        var match = new Match
        {
            MatchId = "EUW1_TEST_12345",
            GameDurationSeconds = 1800,
            WinningTeam = TeamSide.Blue
        };

        var player = new Participant
        {
            SummonerName = "Quady",
            ChampionName = "Ahri",
            Position = Position.Middle,
            Win = true,
            Kills = 7,
            Deaths = 2,
            Assists = 9,
            TotalMinionsKilled = 230,
            TotalDamageDealtToChampions = 26500,
            GoldEarned = 14500
        };

        var tacticalReport = new MatchTacticalReport
        {
            MatchId = match.MatchId,
            ChampionName = "Ahri",
            RoleName = "MID",
            IsVictory = true,
            MatchGrade = "MVP",
            OverallMatchVerdict = "Виняткове виконання мікро-моментів на лінії та ефективні роумінги.",
            KeyTakeaway = "Збереження високого CS/хв дозволило завершити ключовий міфічний слот на 3 хвилини раніше стандарту.",
            GapAnalysis = new List<MatchGapItem>
            {
                new MatchGapItem
                {
                    Category = "Економіка",
                    MetricName = "Фарм (CS/хв)",
                    ActualValueText = "7.7 CS/хв",
                    BenchmarkValueText = "7.2 CS/хв",
                    EvaluationText = "+0.5 CS/хв",
                    IsPositive = true,
                    Advice = "Відмінний контроль хвиль."
                }
            },
            TimelineEvents = new List<TacticalTimelineEvent>
            {
                new TacticalTimelineEvent
                {
                    Minute = 3,
                    TimestampText = "03:15",
                    PhaseName = "Early Game",
                    Icon = "🩸",
                    Title = "Перша кров на міді",
                    Description = "Соло-кіл ворожого мідлейнера з використанням спалаху.",
                    ImpactText = "+400 золота та перевага за темпом"
                }
            }
        };

        var md = CoachReportExporter.GenerateMarkdownReport(match, player, tacticalReport);

        Assert.NotNull(md);
        Assert.Contains("Тренерський звіт матчу", md);
        Assert.Contains("EUW1_TEST_12345", md);
        Assert.Contains("Ahri", md);
        Assert.Contains("MVP", md);
        Assert.Contains("Перша кров на міді", md);
        Assert.Contains("Рекомендації тренера", md);
    }
}
