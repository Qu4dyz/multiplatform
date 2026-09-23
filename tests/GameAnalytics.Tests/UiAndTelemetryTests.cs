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

        // Unknown → blank (do not impersonate Flash)
        var fallbackSpell = SpellRuneHelper.GetSpellIconUrl(9999, "14.18.1");
        Assert.Equal(string.Empty, fallbackSpell);

        var fallbackRune = SpellRuneHelper.GetRuneIconUrl(9999);
        Assert.Contains("Conqueror.png", fallbackRune);
    }

    [Fact]
    public void SpellRuneHelper_ResolveSpellIds_KeepsFlashAndIgnite()
    {
        var (s1, s2) = SpellRuneHelper.ResolveSpellIds(4, 14, "Ahri", Position.Middle);
        Assert.Equal(4, s1);
        Assert.Equal(14, s2);

        var (flashSmite1, flashSmite2) = SpellRuneHelper.ResolveSpellIds(4, 11, "Ambessa", Position.Jungle);
        Assert.Equal(4, flashSmite1);
        Assert.Equal(11, flashSmite2);

        var (missing1, missing2) = SpellRuneHelper.ResolveSpellIds(0, 0, "Ambessa", Position.Jungle);
        Assert.Equal(4, missing1);
        Assert.Equal(11, missing2);
    }

    [Fact]
    public void PlayerMatchItemViewModel_FromMatch_PreservesRealSummonerSpells()
    {
        var match = new Match
        {
            MatchId = "EUW1_SPELLS",
            QueueId = 420,
            GameDurationSeconds = 1800,
            GameCreation = DateTime.UtcNow.AddHours(-1),
            Participants =
            {
                new Participant
                {
                    Puuid = "hero",
                    SummonerName = "Qu4dyz#qu4",
                    ChampionName = "Ambessa",
                    Position = Position.Jungle,
                    TeamSide = TeamSide.Blue,
                    Win = true,
                    Kills = 8,
                    Deaths = 2,
                    Assists = 10,
                    ChampLevel = 16,
                    Summoner1Id = 4,
                    Summoner2Id = 11,
                    PrimaryRuneId = 8010,
                    SecondaryRuneStyleId = 8300,
                    TotalDamageDealtToChampions = 22000,
                    Item0 = 3078
                },
                new Participant
                {
                    Puuid = "enemy",
                    SummonerName = "Other#euw",
                    ChampionName = "Rengar",
                    Position = Position.Jungle,
                    TeamSide = TeamSide.Red,
                    Win = false,
                    Kills = 3,
                    Deaths = 7,
                    Assists = 2,
                    ChampLevel = 14,
                    Summoner1Id = 4,
                    Summoner2Id = 14,
                    PrimaryRuneId = 9923,
                    SecondaryRuneStyleId = 8000,
                    TotalDamageDealtToChampions = 12000,
                    Item0 = 3074
                }
            }
        };

        var vm = GameAnalytics.Desktop.ViewModels.PlayerMatchItemViewModel.FromMatch(match, "hero");

        Assert.Equal(4, vm.Summoner1Id);
        Assert.Equal(11, vm.Summoner2Id);
        Assert.Contains("SummonerFlash", vm.Summoner1IconUrl);
        Assert.Contains("SummonerSmite", vm.Summoner2IconUrl);

        var rengar = vm.RedTeamDetailed.Single(p => p.ChampionName.Contains("Rengar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, rengar.Summoner1Id);
        Assert.Equal(14, rengar.Summoner2Id);
        Assert.Contains("SummonerDot", rengar.Summoner2IconUrl);
    }

    [Fact]
    public void MatchAnalyticsService_LooksLikeMigrationDefaultSpells_DetectsUniformFlashIgnite()
    {
        var stub = new Match
        {
            MatchId = "X",
            Participants =
            {
                new Participant { Summoner1Id = 4, Summoner2Id = 14 },
                new Participant { Summoner1Id = 4, Summoner2Id = 14 }
            }
        };
        Assert.True(GameAnalytics.Infrastructure.Services.MatchAnalyticsService.LooksLikeMigrationDefaultSpells(stub));

        stub.Participants[1].Summoner2Id = 11;
        Assert.False(GameAnalytics.Infrastructure.Services.MatchAnalyticsService.LooksLikeMigrationDefaultSpells(stub));
    }

    [Fact]
    public void PlayerAnalyticsViewModel_AutoRefresh_DefaultsAndToggle()
    {
        var vm = new GameAnalytics.Desktop.ViewModels.PlayerAnalyticsViewModel();
        Assert.True(vm.IsAutoRefreshEnabled);
        Assert.Equal(TimeSpan.FromSeconds(100), GameAnalytics.Desktop.ViewModels.PlayerAnalyticsViewModel.DefaultAutoRefreshInterval);
        Assert.Contains("авто", vm.AutoRefreshToggleText, StringComparison.OrdinalIgnoreCase);

        vm.ToggleAutoRefreshCommand.Execute(null);
        Assert.False(vm.IsAutoRefreshEnabled);
        Assert.Contains("Увімкнути", vm.AutoRefreshToggleText);
        Assert.Contains("пауза", vm.AutoRefreshStatusText, StringComparison.OrdinalIgnoreCase);
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
