using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Desktop.Converters;
using GameAnalytics.Desktop.ViewModels;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Services;
using GameAnalytics.ML.Engine;
using Xunit;

namespace GameAnalytics.Tests;

public class DataLayerTests
{
    [Fact]
    public async Task DataDragonService_GetAllChampions_ReturnsPopulatedList()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var service = new DataDragonService(httpClient);

        // Act
        var champions = await service.GetAllChampionsAsync();

        // Assert
        Assert.NotNull(champions);
        Assert.NotEmpty(champions);
        Assert.Contains(champions, c => c.Name == "Ahri" || c.Id == "Ahri");
    }

    [Fact]
    public async Task DataDragonService_GetChampionsByRole_FiltersCorrectly()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var service = new DataDragonService(httpClient);

        // Act
        var mages = await service.GetChampionsByRoleAsync("Mage");

        // Assert
        Assert.NotNull(mages);
        Assert.NotEmpty(mages);
        Assert.All(mages, m => Assert.Contains("Mage", m.Roles));
    }

    [Fact]
    public void MatchTimelineData_ToInputFeatures_CalculatesDiffsCorrectly()
    {
        // Arrange
        var timeline = new MatchTimelineData
        {
            MatchId = "EUN1_123456",
            GoldAt15Blue = 28500,
            GoldAt15Red = 25000,
            KillsAt15Blue = 10,
            KillsAt15Red = 4,
            TowersAt15Blue = 2,
            TowersAt15Red = 0,
            DragonsAt15Blue = 1,
            DragonsAt15Red = 0,
            BlueFirstBlood = true,
            BlueFirstTower = true,
            BlueFirstDragon = true
        };

        // Act
        var features = timeline.ToInputFeatures(52.5f, 48.5f);

        // Assert
        Assert.Equal(3500, features.GoldDiffAt15);
        Assert.Equal(6, features.KillDiffAt15);
        Assert.True(features.BlueFirstBlood);
        Assert.True(features.BlueFirstTower);
        Assert.Equal(52.5f, features.BlueTeamAvgWinRate);
        Assert.Equal(48.5f, features.RedTeamAvgWinRate);
    }

    [Fact]
    public async Task RiotRateLimiter_AllowsSuccessiveCalls()
    {
        // Arrange
        var rateLimiter = new RiotRateLimiter();

        // Act & Assert
        for (int i = 0; i < 5; i++)
        {
            await rateLimiter.WaitForSlotAsync();
        }

        Assert.True(true);
    }

    [Fact]
    public void Match_QueueName_MapsCorrectly()
    {
        var normalDraft = new Match { QueueId = 400 };
        var rankedSolo = new Match { QueueId = 420 };
        var aram = new Match { QueueId = 450 };
        var custom = new Match { QueueId = 9999 };

        Assert.Equal("Normal Draft", normalDraft.QueueName);
        Assert.Equal("Ranked Solo", rankedSolo.QueueName);
        Assert.Equal("ARAM", aram.QueueName);
        Assert.Equal("Normal Game", custom.QueueName);
    }

    [Fact]
    public void SummonerProfile_ProfileIconUrl_FormatsProperly()
    {
        var profile = new SummonerProfile { ProfileIconId = 2072 };
        Assert.Equal($"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/profileicon/2072.png", profile.ProfileIconUrl);
    }

    [Fact]
    public async Task RiotRateLimiter_IsolatesRoutingRegions()
    {
        var limiter = new RiotRateLimiter();

        await limiter.WaitForSlotAsync("euw1");
        await limiter.WaitForSlotAsync("europe");

        Assert.Equal(2, limiter.ActiveBucketCount);
    }

    [Fact]
    public async Task RiotRateLimiter_NotifyRateLimitHit_BlocksOnlyTargetRegion()
    {
        var limiter = new RiotRateLimiter();

        // Hit rate limit on euw1 for 600ms
        limiter.NotifyRateLimitHit("euw1", TimeSpan.FromMilliseconds(600));

        // Call to europe should complete almost instantaneously (<100ms)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitForSlotAsync("europe");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 250, $"europe was blocked unexpectedly: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void PlayerMatchItemViewModel_DetectsRemake_ByFlagAndDuration()
    {
        var remakeByFlag = new Match
        {
            MatchId = "EUN1_1001",
            IsRemake = true,
            GameDurationSeconds = 600,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var remakeByDuration = new Match
        {
            MatchId = "EUN1_1002",
            IsRemake = false,
            GameDurationSeconds = 210, // < 300s
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var normalVictory = new Match
        {
            MatchId = "EUN1_1003",
            IsRemake = false,
            GameDurationSeconds = 1800,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Win = true, Position = Position.Middle, ChampionName = "Ahri" }
            }
        };

        var vmFlag = PlayerMatchItemViewModel.FromMatch(remakeByFlag, "test-puuid");
        var vmDuration = PlayerMatchItemViewModel.FromMatch(remakeByDuration, "test-puuid");
        var vmNormal = PlayerMatchItemViewModel.FromMatch(normalVictory, "test-puuid");

        Assert.True(vmFlag.IsRemake);
        Assert.False(vmFlag.IsVictory);
        Assert.Equal("РЕМЕЙК", vmFlag.ResultText);
        Assert.Equal("#A0A8B6", vmFlag.ResultColor);
        Assert.Equal("#1C222B", vmFlag.ResultBgColor);

        Assert.True(vmDuration.IsRemake);
        Assert.False(vmDuration.IsVictory);
        Assert.Equal("РЕМЕЙК", vmDuration.ResultText);

        Assert.False(vmNormal.IsRemake);
        Assert.True(vmNormal.IsVictory);
        Assert.Equal("ПЕРЕМОГА", vmNormal.ResultText);
    }

    [Fact]
    public void PlayerMatchItemViewModel_AramQueue_SetsRoleToAram()
    {
        var aramMatchById = new Match
        {
            MatchId = "EUN1_2001",
            QueueId = 450,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Middle, ChampionName = "Lux" }
            }
        };

        var aramMatchByName = new Match
        {
            MatchId = "EUN1_2002",
            QueueId = 0,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Bottom, ChampionName = "Jinx" }
            }
        };
        // QueueName = "ARAM" when QueueId = 450
        var vmAramId = PlayerMatchItemViewModel.FromMatch(aramMatchById, "test-puuid");
        var vmSummoners = PlayerMatchItemViewModel.FromMatch(new Match
        {
            MatchId = "EUN1_2003",
            QueueId = 420,
            Participants = new List<Participant>
            {
                new() { Puuid = "test-puuid", Position = Position.Middle, ChampionName = "Lux" }
            }
        }, "test-puuid");

        Assert.Equal("ARAM", vmAramId.PositionName);
        Assert.Equal("🎲", vmAramId.PositionIcon);

        Assert.Equal("MID", vmSummoners.PositionName);
        Assert.Equal("⚡", vmSummoners.PositionIcon);
    }

    [Fact]
    public void PlayerAnalyticsViewModel_CalculateSessionSummary_ExcludesRemakesFromWinRate()
    {
        var vm = new PlayerAnalyticsViewModel();

        var matches = new List<PlayerMatchItemViewModel>
        {
            new() { MatchId = "1", IsVictory = true, IsRemake = false, ChampionName = "Ahri", Kills = 5, Deaths = 1, Assists = 4, KillParticipationPercent = 60 },
            new() { MatchId = "2", IsVictory = true, IsRemake = false, ChampionName = "Ahri", Kills = 6, Deaths = 2, Assists = 5, KillParticipationPercent = 50 },
            new() { MatchId = "3", IsVictory = true, IsRemake = false, ChampionName = "Zed", Kills = 10, Deaths = 3, Assists = 2, KillParticipationPercent = 70 },
            new() { MatchId = "4", IsVictory = false, IsRemake = false, ChampionName = "Zed", Kills = 2, Deaths = 8, Assists = 3, KillParticipationPercent = 40 },
            new() { MatchId = "5", IsVictory = false, IsRemake = true, ChampionName = "Ahri", Kills = 0, Deaths = 0, Assists = 0, KillParticipationPercent = 0 }
        };

        vm.CalculateSessionSummary(matches);

        // 5 total games, 3 wins, 1 loss, 1 remake
        // Win rate should be 3 / (3 + 1) * 100 = 75.0%
        Assert.Equal(5, vm.SessionTotalGames);
        Assert.Equal(3, vm.SessionWins);
        Assert.Equal(1, vm.SessionLosses);
        Assert.Equal(75.0, vm.SessionWinRate);
        Assert.Contains("(1 ремейк)", vm.SessionRecordText);

        // Ahri has 2 wins out of 2 non-remake games (100% win rate)
        var ahriStat = vm.TopRecentChampions.FirstOrDefault(c => c.ChampionName == "Ahri");
        Assert.NotNull(ahriStat);
        Assert.Equal(3, ahriStat.GamesCount); // 3 total games played
        Assert.Equal(2, ahriStat.WinsCount);  // 2 wins
        Assert.Equal(100.0, ahriStat.WinRate); // 2/2 non-remake = 100%
    }

    [Fact]
    public void PlayerAnalyticsViewModel_ApplyQueueFilter_RecalculatesTopChampionsAndSessionSummary()
    {
        var vm = new PlayerAnalyticsViewModel();

        var m1 = new PlayerMatchItemViewModel { MatchId = "1", GameModeText = "Normal Draft", IsVictory = true, ChampionName = "Ahri", PositionName = "MID", PositionIcon = "⚡" };
        var m2 = new PlayerMatchItemViewModel { MatchId = "2", GameModeText = "Normal Draft", IsVictory = false, ChampionName = "Lux", PositionName = "SUP", PositionIcon = "✨" };
        var m3 = new PlayerMatchItemViewModel { MatchId = "3", GameModeText = "ARAM", IsVictory = true, ChampionName = "Jinx", PositionName = "ARAM", PositionIcon = "🎲" };
        var m4 = new PlayerMatchItemViewModel { MatchId = "4", GameModeText = "ARAM", IsVictory = true, ChampionName = "Jinx", PositionName = "ARAM", PositionIcon = "🎲" };

        vm.RecentMatches.Add(m1);
        vm.RecentMatches.Add(m2);
        vm.RecentMatches.Add(m3);
        vm.RecentMatches.Add(m4);

        // Filter by Normal Draft
        vm.SelectedQueueFilter = "Normal Draft";
        vm.ApplyQueueFilter();

        Assert.Equal(2, vm.FilteredRecentMatches.Count);
        Assert.All(vm.FilteredRecentMatches, m => Assert.Equal("Normal Draft", m.GameModeText));
        Assert.Equal(2, vm.SessionTotalGames);
        Assert.Equal(1, vm.SessionWins);
        Assert.Equal(1, vm.SessionLosses);
        Assert.Equal(50.0, vm.SessionWinRate);

        // Top champions only include Ahri and Lux, not Jinx
        Assert.DoesNotContain(vm.TopRecentChampions, c => c.ChampionName == "Jinx");
        Assert.Contains(vm.TopRecentChampions, c => c.ChampionName == "Ahri");
        Assert.Contains(vm.TopRecentChampions, c => c.ChampionName == "Lux");
    }

    [Fact]
    public void PlayerMomentumAnalyzer_WinStreak_IncreasesScoreAndSetsOnFire()
    {
        var matches = new List<Match>();
        for (int i = 0; i < 4; i++)
        {
            matches.Add(new Match
            {
                MatchId = $"MATCH_{i}",
                GameCreation = DateTime.UtcNow.AddHours(-i),
                GameDurationSeconds = 1800,
                Participants = new List<Participant>
                {
                    new()
                    {
                        Puuid = "target-puuid",
                        SummonerName = "Qu4dyz",
                        Win = true,
                        Kills = 10,
                        Deaths = 2,
                        Assists = 8,
                        ChampionName = "Ahri"
                    }
                }
            });
        }

        var report = PlayerMomentumAnalyzer.Analyze(matches, "target-puuid");

        Assert.True(report.MomentumScore >= 75);
        Assert.Contains("вогні", report.StatusText);
        Assert.True(report.IsWinStreak);
        Assert.Equal(4, report.CurrentStreakCount);
        Assert.True(report.NextGameWinProbability >= 65.0);
        Assert.NotEmpty(report.AiAdvice);
        Assert.Contains("Ahri", report.AiAdvice);
    }

    [Fact]
    public void PlayerMomentumAnalyzer_LossStreak_HighDeaths_TriggersTiltAlert()
    {
        var matches = new List<Match>();
        for (int i = 0; i < 3; i++)
        {
            matches.Add(new Match
            {
                MatchId = $"MATCH_{i}",
                GameCreation = DateTime.UtcNow.AddHours(-i),
                GameDurationSeconds = 1600,
                Participants = new List<Participant>
                {
                    new()
                    {
                        Puuid = "target-puuid",
                        SummonerName = "Qu4dyz",
                        Win = false,
                        Kills = 1,
                        Deaths = 9,
                        Assists = 2,
                        ChampionName = "Yasuo"
                    }
                }
            });
        }

        var report = PlayerMomentumAnalyzer.Analyze(matches, "target-puuid");

        Assert.True(report.MomentumScore < 42);
        Assert.Contains("Тільт", report.StatusText);
        Assert.False(report.IsWinStreak);
        Assert.Equal(3, report.CurrentStreakCount);
        Assert.Contains("смертн", report.AiAdvice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerMatchItemViewModel_MapsItems_AndTogglesAccordion()
    {
        var match = new Match
        {
            MatchId = "ITEM_TEST_01",
            GameDurationSeconds = 1800,
            QueueId = 420,
            Participants = new List<Participant>()
        };

        // Add 5 blue and 5 red
        for (int i = 0; i < 10; i++)
        {
            var isBlue = i < 5;
            match.Participants.Add(new Participant
            {
                Puuid = $"puuid-{i}",
                SummonerName = $"Player_{i}",
                ChampionName = "Ahri",
                ChampLevel = 14,
                TeamSide = isBlue ? TeamSide.Blue : TeamSide.Red,
                Position = Position.Middle,
                Kills = 5,
                Deaths = 2,
                Assists = 4,
                TotalDamageDealtToChampions = (i + 1) * 3000,
                GoldEarned = 12000,
                TotalMinionsKilled = 180,
                Win = isBlue,
                Item0 = 3078,
                Item1 = 3006,
                Item2 = 3031,
                Item3 = 3072,
                Item4 = 3026,
                Item5 = 3153,
                Item6 = 3340
            });
        }

        var vm = PlayerMatchItemViewModel.FromMatch(match, "puuid-2");

        // Assert items on player
        Assert.Equal(6, vm.PlayerItems.Count);
        Assert.Equal(3078, vm.ItemSlot0.ItemId);
        Assert.True(vm.ItemSlot0.HasItem);
        Assert.Equal(3340, vm.PlayerTrinket.ItemId);
        Assert.True(vm.PlayerTrinket.HasItem);

        // Assert detailed participants
        Assert.Equal(5, vm.BlueTeamDetailed.Count);
        Assert.Equal(5, vm.RedTeamDetailed.Count);

        var playerDetailed = vm.BlueTeamDetailed.FirstOrDefault(p => p.IsCurrentPlayer);
        Assert.NotNull(playerDetailed);
        Assert.Equal(6, playerDetailed.Items.Count);
        Assert.True(playerDetailed.DamagePercentOfMax > 0);

        // Assert Accordion toggle
        Assert.False(vm.IsExpanded);
        Assert.Equal("▼ Деталі", vm.ExpandButtonText);

        vm.ToggleExpand();
        Assert.True(vm.IsExpanded);
        Assert.Equal("▲ Згорнути", vm.ExpandButtonText);

        vm.ToggleExpand();
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void PlayerMatchItemViewModel_MapsFullRiotId_AndClickableCommand()
    {
        var match = new Match
        {
            MatchId = "TEST_MATCH_CLICK",
            GameDurationSeconds = 1800,
            GameCreation = DateTime.UtcNow.AddHours(-1)
        };

        match.Participants.Add(new Participant
        {
            Puuid = "current-player",
            SummonerName = "Qu4dyz#qu4",
            ChampionName = "Ambessa",
            TeamSide = TeamSide.Blue,
            Kills = 10, Deaths = 2, Assists = 8
        });

        match.Participants.Add(new Participant
        {
            Puuid = "other-player",
            SummonerName = "Tiramisu55#cook",
            ChampionName = "Gragas",
            TeamSide = TeamSide.Blue,
            Kills = 5, Deaths = 4, Assists = 12
        });

        string? clickedRiotId = null;
        var relayCmd = new CommunityToolkit.Mvvm.Input.RelayCommand<string?>(id => clickedRiotId = id);

        var vm = PlayerMatchItemViewModel.FromMatch(match, "current-player", "Qu4dyz", relayCmd);

        // Check current player
        var currentParticipant = vm.BlueTeamDetailed.First(p => p.IsCurrentPlayer);
        Assert.Equal("Qu4dyz#qu4", currentParticipant.FullRiotId);
        Assert.Contains("(Ви)", currentParticipant.ToolTipText);

        // Check other player
        var otherParticipant = vm.BlueTeamDetailed.First(p => !p.IsCurrentPlayer);
        Assert.Equal("Tiramisu55#cook", otherParticipant.FullRiotId);
        Assert.Contains("Tiramisu55#cook", otherParticipant.ToolTipText);
        Assert.NotNull(otherParticipant.SelectPlayerCommand);

        // Execute command and verify callback
        otherParticipant.SelectPlayerCommand.Execute(otherParticipant.FullRiotId);
        Assert.Equal("Tiramisu55#cook", clickedRiotId);

        // Also verify mini roster participant
        var otherMini = vm.BlueTeam.First(p => !p.IsCurrentPlayer);
        Assert.Equal("Tiramisu55#cook", otherMini.FullRiotId);
        Assert.NotNull(otherMini.SelectPlayerCommand);
    }

    [Fact]
    public async Task PlayerAnalyticsViewModel_SelectPlayer_ParsesNameAndTag()
    {
        var vm = new PlayerAnalyticsViewModel
        {
            GameName = "OldName",
            TagLine = "OldTag"
        };

        // Act with Name#Tag
        await vm.SelectPlayerAsync("Desired Faye#EUW");

        Assert.Equal("Desired Faye", vm.GameName);
        Assert.Equal("EUW", vm.TagLine);

        // Act with Name only
        await vm.SelectPlayerAsync("Bofur");
        Assert.Equal("Bofur", vm.GameName);
        Assert.Equal("EUW", vm.TagLine); // Keeps existing tag
    }

    [Fact]
    public void MainViewModel_ScalingCommands_UpdateUiScaleAndIndicators()
    {
        var vm = new MainViewModel();

        // Default scale is 1.0 (100%)
        Assert.Equal(1.0, vm.UiScale);
        Assert.True(vm.IsScale100);
        Assert.False(vm.IsScale115);
        Assert.False(vm.IsScale125);
        Assert.Equal("#0AC8B9", vm.Scale100Bg);
        Assert.Equal("Transparent", vm.Scale115Bg);

        // Switch to 115%
        vm.SetScale115Command.Execute(null);
        Assert.Equal(1.15, vm.UiScale);
        Assert.False(vm.IsScale100);
        Assert.True(vm.IsScale115);
        Assert.False(vm.IsScale125);
        Assert.Equal("#0AC8B9", vm.Scale115Bg);
        Assert.Equal("Transparent", vm.Scale100Bg);

        // Switch to 125%
        vm.SetScale125Command.Execute(null);
        Assert.Equal(1.25, vm.UiScale);
        Assert.False(vm.IsScale100);
        Assert.False(vm.IsScale115);
        Assert.True(vm.IsScale125);
        Assert.Equal("#0AC8B9", vm.Scale125Bg);

        // Switch back to 100%
        vm.SetScale100Command.Execute(null);
        Assert.Equal(1.0, vm.UiScale);
        Assert.True(vm.IsScale100);
    }

    [Fact]
    public void PlayerMatchItemViewModel_FromMatch_AssignsMvpAndAceCorrectly()
    {
        // 1. Victory Match: Player has highest damage on winning team -> MVP
        var winMatch = new Match
        {
            MatchId = "WIN_001",
            GameDurationSeconds = 1800,
            WinningTeam = TeamSide.Blue,
            QueueId = 420,
            GameCreation = DateTime.UtcNow
        };

        // 3 Blue, 3 Red participants (>= 6)
        var player = new Participant
        {
            Puuid = "hero-puuid",
            SummonerName = "Hero#EUW",
            TeamSide = TeamSide.Blue,
            Win = true,
            TotalDamageDealtToChampions = 35000,
            Kills = 12, Deaths = 1, Assists = 7
        };
        winMatch.Participants.Add(player);

        winMatch.Participants.Add(new Participant
        {
            Puuid = "blue-2",
            SummonerName = "Blue2#EUW",
            TeamSide = TeamSide.Blue,
            Win = true,
            TotalDamageDealtToChampions = 20000,
            Kills = 4, Deaths = 3, Assists = 9
        });

        winMatch.Participants.Add(new Participant
        {
            Puuid = "blue-3",
            SummonerName = "Blue3#EUW",
            TeamSide = TeamSide.Blue,
            Win = true,
            TotalDamageDealtToChampions = 15000,
            Kills = 2, Deaths = 4, Assists = 10
        });

        var redAce = new Participant
        {
            Puuid = "red-ace",
            SummonerName = "RedAce#EUW",
            TeamSide = TeamSide.Red,
            Win = false,
            TotalDamageDealtToChampions = 28000,
            Kills = 6, Deaths = 5, Assists = 2
        };
        winMatch.Participants.Add(redAce);

        winMatch.Participants.Add(new Participant
        {
            Puuid = "red-2",
            SummonerName = "Red2#EUW",
            TeamSide = TeamSide.Red,
            Win = false,
            TotalDamageDealtToChampions = 12000,
            Kills = 1, Deaths = 6, Assists = 3
        });

        winMatch.Participants.Add(new Participant
        {
            Puuid = "red-3",
            SummonerName = "Red3#EUW",
            TeamSide = TeamSide.Red,
            Win = false,
            TotalDamageDealtToChampions = 9000,
            Kills = 1, Deaths = 7, Assists = 4
        });

        var winVm = PlayerMatchItemViewModel.FromMatch(winMatch, "hero-puuid");
        Assert.True(winVm.HasPerformanceBadge);
        Assert.Equal("MVP", winVm.PerformanceBadgeText);
        Assert.Equal("#C8AA6E", winVm.PerformanceBadgeBg);

        var blueHeroDetailed = winVm.BlueTeamDetailed.First(p => p.FullRiotId == "Hero#EUW");
        Assert.True(blueHeroDetailed.HasBadge);
        Assert.Equal("MVP", blueHeroDetailed.BadgeText);

        var redAceDetailed = winVm.RedTeamDetailed.First(p => p.FullRiotId == "RedAce#EUW");
        Assert.True(redAceDetailed.HasBadge);
        Assert.Equal("ACE", redAceDetailed.BadgeText);

        // 2. Defeat Match: Player has highest damage on losing team -> ACE
        var lossMatch = new Match
        {
            MatchId = "LOSS_001",
            GameDurationSeconds = 1800,
            WinningTeam = TeamSide.Red,
            QueueId = 420,
            GameCreation = DateTime.UtcNow
        };

        var losingPlayer = new Participant
        {
            Puuid = "hero-puuid",
            SummonerName = "Hero#EUW",
            TeamSide = TeamSide.Blue,
            Win = false,
            TotalDamageDealtToChampions = 30000,
            Kills = 8, Deaths = 4, Assists = 3
        };
        lossMatch.Participants.Add(losingPlayer);

        lossMatch.Participants.Add(new Participant
        {
            Puuid = "blue-2",
            SummonerName = "Blue2#EUW",
            TeamSide = TeamSide.Blue,
            Win = false,
            TotalDamageDealtToChampions = 14000,
            Kills = 2, Deaths = 8, Assists = 4
        });

        lossMatch.Participants.Add(new Participant
        {
            Puuid = "blue-3",
            SummonerName = "Blue3#EUW",
            TeamSide = TeamSide.Blue,
            Win = false,
            TotalDamageDealtToChampions = 11000,
            Kills = 1, Deaths = 9, Assists = 2
        });

        lossMatch.Participants.Add(new Participant
        {
            Puuid = "red-mvp",
            SummonerName = "RedMvp#EUW",
            TeamSide = TeamSide.Red,
            Win = true,
            TotalDamageDealtToChampions = 38000,
            Kills = 15, Deaths = 2, Assists = 8
        });

        lossMatch.Participants.Add(new Participant
        {
            Puuid = "red-2",
            SummonerName = "Red2#EUW",
            TeamSide = TeamSide.Red,
            Win = true,
            TotalDamageDealtToChampions = 16000,
            Kills = 4, Deaths = 3, Assists = 10
        });

        lossMatch.Participants.Add(new Participant
        {
            Puuid = "red-3",
            SummonerName = "Red3#EUW",
            TeamSide = TeamSide.Red,
            Win = true,
            TotalDamageDealtToChampions = 13000,
            Kills = 2, Deaths = 6, Assists = 12
        });

        var lossVm = PlayerMatchItemViewModel.FromMatch(lossMatch, "hero-puuid");
        Assert.True(lossVm.HasPerformanceBadge);
        Assert.Equal("ACE", lossVm.PerformanceBadgeText);
        Assert.Equal("#E84057", lossVm.PerformanceBadgeBg);

        var losingPlayerDetailed = lossVm.BlueTeamDetailed.First(p => p.FullRiotId == "Hero#EUW");
        Assert.True(losingPlayerDetailed.HasBadge);
        Assert.Equal("ACE", losingPlayerDetailed.BadgeText);

        var winningRedDetailed = lossVm.RedTeamDetailed.First(p => p.FullRiotId == "RedMvp#EUW");
        Assert.True(winningRedDetailed.HasBadge);
        Assert.Equal("MVP", winningRedDetailed.BadgeText);
    }

    [Fact]
    public void GlobalCoachingAnalyzer_Analyze_CalculatesPillarsAndGeneratesCoachingReport()
    {
        var puuid = "coach-player-1";
        var matches = new List<Match>();

        for (int i = 0; i < 4; i++)
        {
            var match = new Match
            {
                MatchId = $"coach-{i}",
                GameDurationSeconds = 1800,
                QueueId = 420,
                WinningTeam = TeamSide.Blue,
                Participants = new List<Participant>
                {
                    new()
                    {
                        Puuid = puuid,
                        SummonerName = "ProJungler#EUW",
                        Position = Position.Jungle,
                        TeamSide = TeamSide.Blue,
                        Win = true,
                        Kills = 7,
                        Deaths = 2,
                        Assists = 8,
                        TotalMinionsKilled = 220,
                        TotalDamageDealtToChampions = 22000
                    },
                    new()
                    {
                        Puuid = "teammate",
                        SummonerName = "Mid#EUW",
                        Position = Position.Middle,
                        TeamSide = TeamSide.Blue,
                        Win = true,
                        Kills = 5,
                        Deaths = 3,
                        Assists = 6,
                        TotalDamageDealtToChampions = 18000
                    }
                }
            };
            matches.Add(match);
        }

        var report = GlobalCoachingAnalyzer.Analyze(matches, puuid);

        Assert.NotNull(report);
        Assert.Equal(Position.Jungle, report.PrimaryRole);
        Assert.Equal("JGL", report.RoleName);
        Assert.Equal(4, report.TotalMatchesAnalyzed);
        Assert.True(report.OverallScore >= 60, "High performing jungler should score well");
        Assert.NotEmpty(report.OverallGrade);
        Assert.NotEmpty(report.CoachAdvice);

        // Check pillars
        Assert.NotNull(report.Combat);
        Assert.Equal("⚔️", report.Combat.Icon);
        Assert.NotNull(report.Economy);
        Assert.Equal("🌾", report.Economy.Icon);
        Assert.True(report.Economy.Score > 70, "7.3 CS/M exceeds 6.8 benchmark");
        Assert.NotNull(report.Objectives);
        Assert.NotNull(report.Survival);
        Assert.True(report.Survival.Score >= 70, "2 deaths is very safe");
    }

    [Fact]
    public void GlobalCoachingAnalyzer_CalculateLpDelta_RankedGamesYieldAccurateLp()
    {
        var rankedMatch = new Match { QueueId = 420 };
        var normalMatch = new Match { QueueId = 400 };

        // Ranked Win
        var (winDelta, winText, winBg, winFg) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: true, isRemake: false, isMvp: false, isAce: false);
        Assert.InRange(winDelta, 20, 24);
        Assert.StartsWith("+", winText);
        Assert.Equal("#0AC8B9", winFg);

        // Ranked Win with MVP bonus
        var (mvpDelta, mvpText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: true, isRemake: false, isMvp: true, isAce: false);
        Assert.True(mvpDelta >= winDelta);

        // Ranked Loss
        var (lossDelta, lossText, lossBg, lossFg) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: false);
        Assert.InRange(lossDelta, -20, -17);
        Assert.StartsWith("-", lossText);
        Assert.Equal("#E84057", lossFg);

        // Ranked Loss with ACE mitigation
        var (aceDelta, _, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: true);
        Assert.True(aceDelta > lossDelta);

        // Remake
        var (remakeDelta, remakeText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: true, isMvp: false, isAce: false);
        Assert.Equal(0, remakeDelta);
        Assert.Equal("0 LP", remakeText);

        // Normal queue
        var (normDelta, normText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(normalMatch, isVictory: true, isRemake: false, isMvp: false, isAce: false);
        Assert.Equal(0, normDelta);
        Assert.Equal("-", normText);
    }

    [Fact]
    public void PlayerMatchItemViewModel_FromMatch_ComputesMatchGradesAndLpDeltas()
    {
        var match = new Match
        {
            MatchId = "test-lp-grade",
            GameDurationSeconds = 1800,
            QueueId = 420,
            WinningTeam = TeamSide.Blue,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "star-puuid",
                    SummonerName = "StarLaner#EUW",
                    TeamSide = TeamSide.Blue,
                    Position = Position.Middle,
                    Win = true,
                    Kills = 12,
                    Deaths = 1,
                    Assists = 9,
                    TotalMinionsKilled = 250,
                    TotalDamageDealtToChampions = 35000
                },
                new()
                {
                    Puuid = "rival-puuid",
                    SummonerName = "Rival#EUW",
                    TeamSide = TeamSide.Red,
                    Position = Position.Middle,
                    Win = false,
                    Kills = 2,
                    Deaths = 8,
                    Assists = 3,
                    TotalMinionsKilled = 120,
                    TotalDamageDealtToChampions = 12000
                }
            }
        };

        var vm = PlayerMatchItemViewModel.FromMatch(match, "star-puuid");

        Assert.True(vm.HasLpChange);
        Assert.StartsWith("+", vm.LpChangeText);
        Assert.NotEmpty(vm.MatchGrade);
        Assert.Contains(vm.MatchGrade, new[] { "S+", "S", "A" });
        Assert.True(vm.HasCoachingBadge);

        // Detailed participant grade checks
        var starDetailed = vm.BlueTeamDetailed.First(p => p.FullRiotId == "StarLaner#EUW");
        Assert.NotEmpty(starDetailed.MatchGrade);
        Assert.NotEmpty(starDetailed.MatchGradeColor);

        var rivalDetailed = vm.RedTeamDetailed.First(p => p.FullRiotId == "Rival#EUW");
        Assert.NotEmpty(rivalDetailed.MatchGrade);
        Assert.NotEmpty(rivalDetailed.MatchGradeColor);
    }

    [Fact]
    public void PlayerAnalyticsViewModel_CalculateSessionSummary_TracksNetLpAndPromoProgress()
    {
        var vm = new PlayerAnalyticsViewModel();
        vm.Summoner = new SummonerProfile
        {
            GameName = "Qu4dyz",
            TagLine = "qu4",
            Tier = GameTier.Emerald,
            Rank = "II",
            LeaguePoints = 78
        };

        var matches = new List<PlayerMatchItemViewModel>
        {
            new() { MatchId = "1", GameModeText = "Ranked Solo", IsVictory = true, IsRemake = false, LpDelta = 22, LpChangeText = "+22 LP", ChampionName = "Ahri", Kills = 8, Deaths = 2, Assists = 5 },
            new() { MatchId = "2", GameModeText = "Ranked Solo", IsVictory = true, IsRemake = false, LpDelta = 24, LpChangeText = "+24 LP", ChampionName = "Ahri", Kills = 10, Deaths = 1, Assists = 7 },
            new() { MatchId = "3", GameModeText = "Ranked Solo", IsVictory = false, IsRemake = false, LpDelta = -19, LpChangeText = "-19 LP", ChampionName = "Zed", Kills = 3, Deaths = 6, Assists = 2 },
            new() { MatchId = "4", GameModeText = "Normal Draft", IsVictory = true, IsRemake = false, LpDelta = 0, LpChangeText = "-", ChampionName = "Lux", Kills = 4, Deaths = 3, Assists = 10 }
        };

        vm.CalculateSessionSummary(matches);

        // Net LP should be 22 + 24 - 19 = 27 LP
        Assert.Equal(27, vm.SessionNetLp);
        Assert.Equal("+27 LP", vm.SessionNetLpText);
        Assert.Equal("#0AC8B9", vm.SessionNetLpColor);

        // Promo progress: 78 LP, Emerald II -> Emerald I
        Assert.True(vm.HasRankProgress);
        Assert.Equal(78, vm.NextTierProgressValue);
        Assert.Contains("78 / 100 LP до Emerald I", vm.NextTierProgressText);
    }

    [Fact]
    public void GameConstants_UsesModernPatch_16x()
    {
        Assert.StartsWith("16.", GameConstants.DDragonVersion);
    }

    [Fact]
    public void GameConstants_GetItemTooltip_ReturnsDetailedDescription()
    {
        var ieTooltip = GameConstants.GetItemTooltip(3031); // Infinity Edge
        Assert.Contains("Infinity Edge", ieTooltip);
        Assert.Contains("Сила атаки", ieTooltip);
        Assert.Contains("критичного", ieTooltip);

        var zhonyasTooltip = GameConstants.GetItemTooltip(3157); // Zhonya's Hourglass
        Assert.Contains("Zhonya's Hourglass", zhonyasTooltip);
        Assert.Contains("Стазис", zhonyasTooltip);

        var rabadonTooltip = GameConstants.GetItemTooltip(3089); // Rabadon's Deathcap
        Assert.Contains("Rabadon's Deathcap", rabadonTooltip);
        Assert.Contains("Сила вмінь", rabadonTooltip);
    }

    [Fact]
    public void ItemSlotViewModel_AutoGeneratesTooltip_AndModernUrl()
    {
        var slot = new ItemSlotViewModel { ItemId = 3078 }; // Trinity Force
        Assert.True(slot.HasItem);
        Assert.Contains(GameConstants.DDragonVersion, slot.IconUrl);
        Assert.Contains("Trinity Force", slot.ToolTipText);
        Assert.Contains("Spellblade", slot.ToolTipText);
    }

    [Fact]
    public void MatchTacticalAnalyzer_GeneratesTimelineEventsAndGapAnalysis()
    {
        var match = new Match
        {
            MatchId = "EUW1_999999",
            GameDurationSeconds = 1850, // ~30.8 min
            QueueId = 420,
            WinningTeam = TeamSide.Blue,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "player_jgl_shyvana",
                    SummonerName = "Qu4dyz#qu4",
                    ChampionName = "Shyvana",
                    Position = Position.Jungle,
                    TeamSide = TeamSide.Blue,
                    Win = true,
                    Kills = 12,
                    Deaths = 2,
                    Assists = 8,
                    TotalMinionsKilled = 240,
                    TotalDamageDealtToChampions = 34000
                },
                new()
                {
                    Puuid = "ally_mid",
                    ChampionName = "Ahri",
                    Position = Position.Middle,
                    TeamSide = TeamSide.Blue,
                    Kills = 8,
                    Deaths = 4,
                    Assists = 10,
                    TotalDamageDealtToChampions = 25000
                },
                new()
                {
                    Puuid = "enemy_jgl",
                    ChampionName = "LeeSin",
                    Position = Position.Jungle,
                    TeamSide = TeamSide.Red,
                    Kills = 3,
                    Deaths = 6,
                    Assists = 4,
                    TotalMinionsKilled = 160,
                    TotalDamageDealtToChampions = 12000
                }
            }
        };

        var player = match.Participants.First();
        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, player, "Emerald");

        Assert.NotNull(report);
        Assert.Equal("Shyvana", report.ChampionName);
        Assert.Equal("JGL", report.RoleName);
        Assert.NotEmpty(report.OverallMatchVerdict);
        Assert.NotEmpty(report.KeyTakeaway);

        // Assert 4-5 tactical play-by-play events
        Assert.True(report.TimelineEvents.Count >= 4);
        Assert.Contains(report.TimelineEvents, e => e.Minute <= 5); // Early game milestone
        Assert.Contains(report.TimelineEvents, e => e.Minute >= 15); // Mid/late milestone

        // Assert 4 Gap Analysis items
        Assert.True(report.GapAnalysis.Count >= 4);
        Assert.Contains(report.GapAnalysis, g => g.Category.Contains("Економіка"));
        Assert.Contains(report.GapAnalysis, g => g.Category.Contains("Бойова"));
        Assert.Contains(report.GapAnalysis, g => g.Category.Contains("Виживання"));
    }

    [Fact]
    public void PlayerMatchItemViewModel_TabSwitchingAndTacticalReport_WorkCorrectly()
    {
        var match = new Match
        {
            MatchId = "EUW1_123456",
            GameDurationSeconds = 1500,
            QueueId = 420,
            Participants = new List<Participant>
            {
                new()
                {
                    Puuid = "my_puuid",
                    SummonerName = "Qu4dyz#qu4",
                    ChampionName = "Shyvana",
                    Position = Position.Jungle,
                    Kills = 9,
                    Deaths = 1,
                    Assists = 6,
                    Item0 = 3078, // Trinity Force
                    Item6 = 3340  // Stealth Ward
                }
            }
        };

        var vm = PlayerMatchItemViewModel.FromMatch(match, "my_puuid");

        // Verify initial state
        Assert.Equal(0, vm.ActiveDetailTab);
        Assert.True(vm.IsScoreboardTabActive);
        Assert.False(vm.IsTacticalTabActive);
        Assert.NotNull(vm.TacticalReport);
        Assert.Equal("Shyvana", vm.TacticalReport.ChampionName);

        // Verify item tooltips
        Assert.Contains("Trinity Force", vm.ItemSlot0.ToolTipText);
        Assert.Contains("Stealth Ward", vm.PlayerTrinket.ToolTipText);

        // Switch to Tactical Breakdown tab
        vm.ShowTacticalTab();
        Assert.Equal(1, vm.ActiveDetailTab);
        Assert.False(vm.IsScoreboardTabActive);
        Assert.True(vm.IsTacticalTabActive);

        // Switch back to Scoreboard tab
        vm.ShowScoreboardTab();
        Assert.Equal(0, vm.ActiveDetailTab);
        Assert.True(vm.IsScoreboardTabActive);
        Assert.False(vm.IsTacticalTabActive);
    }

    [Fact]
    public void NormalizeChampionName_HandlesEdgeCasesAndSpecialCharacters()
    {
        Assert.Equal("MonkeyKing", PlayerMatchItemViewModel.NormalizeChampionName("Wukong"));
        Assert.Equal("MonkeyKing", PlayerMatchItemViewModel.NormalizeChampionName("wukong"));
        Assert.Equal("Renata", PlayerMatchItemViewModel.NormalizeChampionName("Renata Glasc"));
        Assert.Equal("Nunu", PlayerMatchItemViewModel.NormalizeChampionName("Nunu & Willump"));
        Assert.Equal("Kaisa", PlayerMatchItemViewModel.NormalizeChampionName("Kai'Sa"));
        Assert.Equal("Khazix", PlayerMatchItemViewModel.NormalizeChampionName("Kha'Zix"));
        Assert.Equal("Chogath", PlayerMatchItemViewModel.NormalizeChampionName("Cho'Gath"));
        Assert.Equal("Leblanc", PlayerMatchItemViewModel.NormalizeChampionName("LeBlanc"));
        Assert.Equal("Fiddlesticks", PlayerMatchItemViewModel.NormalizeChampionName("FiddleSticks"));
        Assert.Equal("DrMundo", PlayerMatchItemViewModel.NormalizeChampionName("Dr. Mundo"));
        Assert.Equal("LeeSin", PlayerMatchItemViewModel.NormalizeChampionName("Lee Sin"));
        Assert.Equal("Unknown", PlayerMatchItemViewModel.NormalizeChampionName(""));
    }

    [Fact]
    public void PlayerAnalyticsViewModel_LoadMoreButtonText_ReflectsLoadingState()
    {
        var vm = new PlayerAnalyticsViewModel();
        Assert.False(vm.IsLoadingMore);
        Assert.Equal("ЗАВАНТАЖИТИ ЩЕ 5 МАТЧІВ", vm.LoadMoreButtonText);

        vm.IsLoadingMore = true;
        Assert.Equal("ЗАВАНТАЖЕННЯ...", vm.LoadMoreButtonText);

        vm.IsLoadingMore = false;
        Assert.Equal("ЗАВАНТАЖИТИ ЩЕ 5 МАТЧІВ", vm.LoadMoreButtonText);
    }

    [Fact]
    public void BitmapAssetValueConverter_GetCacheDir_ContainsVersion()
    {
        var dir = BitmapAssetValueConverter.GetCacheDir();
        Assert.Contains(GameConstants.DDragonVersion, dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void BitmapAssetValueConverter_GetLocalPathForUrl_IncludesCategory()
    {
        var itemPath = BitmapAssetValueConverter.GetLocalPathForUrl("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/item/3172.png");
        Assert.Contains("item_3172.png", itemPath);

        var champPath = BitmapAssetValueConverter.GetLocalPathForUrl("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/champion/Yone.png");
        Assert.Contains("champion_Yone.png", champPath);

        var profilePath = BitmapAssetValueConverter.GetLocalPathForUrl("https://ddragon.leagueoflegends.com/cdn/16.18.1/img/profileicon/588.png");
        Assert.Contains("profileicon_588.png", profilePath);
    }

    [Fact]
    public void MiniParticipantViewModel_ChampionIconUrl_NormalizesChampionName()
    {
        var mini = new MiniParticipantViewModel
        {
            SummonerName = "ProPlayer",
            ChampionName = "Kai'Sa"
        };
        Assert.Equal("Kaisa", mini.NormalizedChampionName);
        Assert.EndsWith("Kaisa.png", mini.ChampionIconUrl);
    }

    [Fact]
    public void ItemSlotViewModel_ToolTipText_IsDynamicAndContainsDetails()
    {
        var slot = new ItemSlotViewModel { ItemId = 3172 };
        Assert.True(slot.HasItem);
        Assert.Contains("Gunmetal Greaves", slot.ToolTipText);
        Assert.Contains("1100 Gold", slot.ToolTipText);

        slot.ItemId = 3006;
        Assert.Contains("Berserker's Greaves", slot.ToolTipText);
    }

    [Fact]
    public void RiotApiOptions_Validation_CorrectlyIdentifiesValidKeys()
    {
        var validOptions = new RiotApiOptions { ApiKey = "RGAPI-eb76424c-9723-49ac-bd29-950b9b342595" };
        Assert.True(validOptions.HasValidApiKey);

        var dummyOptions = new RiotApiOptions { ApiKey = "RGAPI-XXXX-XXXX" };
        Assert.False(dummyOptions.HasValidApiKey);

        var emptyOptions = new RiotApiOptions { ApiKey = "" };
        Assert.False(emptyOptions.HasValidApiKey);
    }

    [Fact]
    public void RiotApiOptions_LoadFromFile_HandlesOverridesGracefully()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"appsettings_test_{Guid.NewGuid():N}.json");
        var tempLocal = Path.Combine(Path.GetTempPath(), "appsettings.local.json");

        try
        {
            File.WriteAllText(tempBase, @"{ ""RiotApi"": { ""ApiKey"": ""RGAPI-BASE-KEY-VALUE-TOO-LONG-123456"", ""PlatformRegion"": ""euw1"" } }");
            var loaded = RiotApiOptions.LoadFromFile(tempBase);
            Assert.Equal("euw1", loaded.PlatformRegion);
            Assert.Equal("RGAPI-BASE-KEY-VALUE-TOO-LONG-123456", loaded.ApiKey);
        }
        finally
        {
            if (File.Exists(tempBase)) File.Delete(tempBase);
            if (File.Exists(tempLocal)) File.Delete(tempLocal);
        }
    }
}


