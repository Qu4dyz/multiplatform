using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class UiAndTelemetryTests
{
    [Fact]
    public void SpellRuneHelper_MapsCommonSpellsAndRunesCorrectly()
    {
        // Spells
        var flashUrl = SpellRuneHelper.GetSpellIconUrl(4, "14.18.1");
        var igniteUrl = SpellRuneHelper.GetSpellIconUrl(14, "14.18.1");
        var tpUrl = SpellRuneHelper.GetSpellIconUrl(12, "14.18.1");

        Assert.Contains("SummonerFlash.png", flashUrl);
        Assert.Contains("SummonerDot.png", igniteUrl);
        Assert.Contains("SummonerTeleport.png", tpUrl);

        // Runes
        var conquerorUrl = SpellRuneHelper.GetRuneIconUrl(8010);
        var electrocuteUrl = SpellRuneHelper.GetRuneIconUrl(8112);
        var precisionStyleUrl = SpellRuneHelper.GetRuneStyleIconUrl(8000);

        Assert.Contains("Conqueror.png", conquerorUrl);
        Assert.Contains("Electrocute.png", electrocuteUrl);
        Assert.Contains("7201_Precision.png", precisionStyleUrl);

        // Unknown fallback
        var fallbackSpell = SpellRuneHelper.GetSpellIconUrl(9999, "14.18.1");
        Assert.Contains("SummonerFlash.png", fallbackSpell);

        var fallbackRune = SpellRuneHelper.GetRuneIconUrl(9999);
        Assert.Contains("Conqueror.png", fallbackRune);
    }

    [Fact]
    public void TacticalIconHelper_ReturnsValidSvgGeometryPaths()
    {
        var swordPath = TacticalIconHelper.GetPathForKind("sword");
        var dragonPath = TacticalIconHelper.GetPathForKind("dragon");
        var skullPath = TacticalIconHelper.GetPathForKind("skull");
        var warningPath = TacticalIconHelper.GetPathForKind("warning");
        var towerPath = TacticalIconHelper.GetPathForKind("tower");

        Assert.False(string.IsNullOrWhiteSpace(swordPath));
        Assert.False(string.IsNullOrWhiteSpace(dragonPath));
        Assert.False(string.IsNullOrWhiteSpace(skullPath));
        Assert.False(string.IsNullOrWhiteSpace(warningPath));
        Assert.False(string.IsNullOrWhiteSpace(towerPath));

        Assert.StartsWith("M", swordPath);
        Assert.StartsWith("M", dragonPath);
    }

    [Fact]
    public void MatchTacticalAnalyzer_GeneratesTelemetryWithCategoryAndSeverityTags()
    {
        var match = new Match
        {
            MatchId = "EUW1_TEST1",
            GameDurationSeconds = 1800,
            QueueId = 420
        };

        var player = new Participant
        {
            ParticipantId = 1,
            ChampionName = "Ahri",
            Position = Position.Middle,
            TeamSide = TeamSide.Blue,
            Kills = 7,
            Deaths = 2,
            Assists = 6,
            TotalMinionsKilled = 210,
            TotalDamageDealtToChampions = 22000,
            Win = true,
            Summoner1Id = 4,
            Summoner2Id = 14,
            PrimaryRuneId = 8112,
            SecondaryRuneStyleId = 8200,
            VisionScore = 28
        };
        match.Participants.Add(player);

        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, player, null, GameTier.Diamond);

        Assert.NotNull(report);
        Assert.NotEmpty(report.TimelineEvents);

        foreach (var ev in report.TimelineEvents)
        {
            Assert.False(string.IsNullOrWhiteSpace(ev.CategoryTag), "CategoryTag should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(ev.SeverityTag), "SeverityTag should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(ev.IconPath), "IconPath should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(ev.CardBackground), "CardBackground should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(ev.IconBadgeBg), "IconBadgeBg should not be empty");
        }
    }
}
