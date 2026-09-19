using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
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
        Assert.Equal("#181D26", vmFlag.ResultBgColor);

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
        Assert.Equal("#7C3AED", vm.Scale100Bg);
        Assert.Equal("Transparent", vm.Scale115Bg);

        // Switch to 115%
        vm.SetScale115Command.Execute(null);
        Assert.Equal(1.15, vm.UiScale);
        Assert.False(vm.IsScale100);
        Assert.True(vm.IsScale115);
        Assert.False(vm.IsScale125);
        Assert.Equal("#7C3AED", vm.Scale115Bg);
        Assert.Equal("Transparent", vm.Scale100Bg);

        // Switch to 125%
        vm.SetScale125Command.Execute(null);
        Assert.Equal(1.25, vm.UiScale);
        Assert.False(vm.IsScale100);
        Assert.False(vm.IsScale115);
        Assert.True(vm.IsScale125);
        Assert.Equal("#7C3AED", vm.Scale125Bg);

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
        Assert.Equal(20, winDelta);
        Assert.Equal("▲ 20 LP", winText);
        Assert.Equal("#5383E8", winFg);

        // Ranked Loss
        var (lossDelta, lossText, lossBg, lossFg) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: false);
        Assert.Equal(-20, lossDelta);
        Assert.Equal("▼ 20 LP", lossText);
        Assert.Equal("#E84057", lossFg);

        // Exact override LP delta (-20 LP as in user request)
        var (overrideDelta, overrideText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: false, overrideLpDelta: -20);
        Assert.Equal(-20, overrideDelta);
        Assert.Equal("▼ 20 LP", overrideText);

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
        Assert.StartsWith("▲", vm.LpChangeText);
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
    public void LpTracking_TotalLpAndDeltaCalculation_AccuratelyTracksRankedProgression()
    {
        // 1. Total LP calculation
        var emerald4_48 = LpCalculationHelper.CalculateTotalLp(GameTier.Emerald, "IV", 48);
        var emerald4_68 = LpCalculationHelper.CalculateTotalLp(GameTier.Emerald, "IV", 68);
        Assert.Equal(-20, emerald4_48 - emerald4_68);

        // Cross-division promotion: Plat I 90 LP -> Emerald IV 10 LP
        var plat1_90 = LpCalculationHelper.CalculateTotalLp(GameTier.Platinum, "I", 90);
        var emerald4_10 = LpCalculationHelper.CalculateTotalLp(GameTier.Emerald, "IV", 10);
        Assert.Equal(20, emerald4_10 - plat1_90);

        // 2. Baseline Ranked Loss must be -20 LP (matching user request)
        var rankedMatch = new Match { QueueId = 420 };
        var (lossDelta, lossText, lossBg, lossFg) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: false);
        Assert.Equal(-20, lossDelta);
        Assert.Equal("▼ 20 LP", lossText);
        Assert.Equal("#E84057", lossFg);

        // 3. Baseline Ranked Win must be +20 LP
        var (winDelta, winText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: true, isRemake: false, isMvp: false, isAce: false);
        Assert.Equal(20, winDelta);
        Assert.Equal("▲ 20 LP", winText);

        // 4. Exact override LP delta (-20 LP as in user request)
        var (ovDelta, ovText, _, _) = GlobalCoachingAnalyzer.CalculateLpDelta(rankedMatch, isVictory: false, isRemake: false, isMvp: false, isAce: false, overrideLpDelta: -20);
        Assert.Equal(-20, ovDelta);
        Assert.Equal("▼ 20 LP", ovText);
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
        Assert.Equal("ЗАВАНТАЖИТИ ЩЕ 10 МАТЧІВ", vm.LoadMoreButtonText);

        vm.IsLoadingMore = true;
        Assert.Equal("ЗАВАНТАЖЕННЯ...", vm.LoadMoreButtonText);

        vm.IsLoadingMore = false;
        Assert.Equal("ЗАВАНТАЖИТИ ЩЕ 10 МАТЧІВ", vm.LoadMoreButtonText);
    }

    [Fact]
    public void ChampionNameHelper_ToDisplayName_NormalizesSpecialNames()
    {
        Assert.Equal("Wukong", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("MonkeyKing"));
        Assert.Equal("Wukong", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("monkeyking"));
        Assert.Equal("Nunu & Willump", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("Nunu"));
        Assert.Equal("Renata Glasc", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("Renata"));
        Assert.Equal("Dr. Mundo", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("DrMundo"));
        Assert.Equal("Ambessa", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName("Ambessa"));

        Assert.Equal("MonkeyKing", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDDragonImageKey("Wukong"));
        Assert.Equal("MonkeyKing", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDDragonImageKey("MonkeyKing"));
        Assert.Equal("Nunu", GameAnalytics.Core.Helpers.ChampionNameHelper.ToDDragonImageKey("Nunu & Willump"));
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

    [Fact]
    public void RoleBenchmark_TierScaling_AdjustsTargetsAccurately()
    {
        var master = RoleBenchmark.GetBenchmark(Position.Jungle, GameTier.Master);
        var silver = RoleBenchmark.GetBenchmark(Position.Jungle, GameTier.Silver);

        Assert.True(master.TargetCsPerMin > silver.TargetCsPerMin);
        Assert.True(master.TargetDpm > silver.TargetDpm);
        Assert.True(master.MaxTargetDeaths < silver.MaxTargetDeaths);
    }

    [Fact]
    public void MatchTacticalAnalyzer_WithRealTimeline_BuildsExactEvents()
    {
        var match = new Match
        {
            MatchId = "TEST_MATCH",
            GameDurationSeconds = 1800,
            Participants = new List<Participant>
            {
                new() { ParticipantId = 1, ChampionName = "Ambessa", TeamSide = TeamSide.Blue, Kills = 5, Deaths = 1, Assists = 3, TotalMinionsKilled = 180 },
                new() { ParticipantId = 6, ChampionName = "MonkeyKing", TeamSide = TeamSide.Red, Kills = 1, Deaths = 4, Assists = 2, TotalMinionsKilled = 140 }
            }
        };

        var player = match.Participants[0];

        var timeline = new MatchTimelineData
        {
            MatchId = "TEST_MATCH",
            RealEvents = new List<TimelineEventRecord>
            {
                new() { TimestampMs = 330000, EventType = "CHAMPION_KILL", KillerId = 1, VictimId = 6, Bounty = 300 },
                new() { TimestampMs = 720000, EventType = "ELITE_MONSTER_KILL", KillerId = 1, KillerTeamId = 100, MonsterType = "DRAGON", MonsterSubType = "FIRE_DRAGON" },
                new() { TimestampMs = 1200000, EventType = "BUILDING_KILL", KillerId = 1, BuildingType = "TOWER_BUILDING", LaneType = "MID_LANE" }
            }
        };

        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, player, timeline, GameTier.Diamond);

        Assert.NotNull(report);
        Assert.Equal(3, report.TimelineEvents.Count);
        Assert.Contains("Вбивство Wukong", report.TimelineEvents[0].Title);
        Assert.Equal("05:30", report.TimelineEvents[0].TimestampText);
        Assert.Contains("Вогняний Дракон", report.TimelineEvents[1].Title);
        Assert.Equal("12:00", report.TimelineEvents[1].TimestampText);
        Assert.Contains("Знищення", report.TimelineEvents[2].Title);
    }

    [Fact]
    public void MatchTacticalAnalyzer_VoidgrubsConsecutiveKills_ConsolidatesIntoSingleEvent()
    {
        var match = new Match
        {
            MatchId = "GRUB_TEST",
            GameDurationSeconds = 1800,
            Participants = new List<Participant>
            {
                new() { ParticipantId = 1, ChampionName = "Ambessa", TeamSide = TeamSide.Blue, Kills = 2 }
            }
        };

        var timeline = new MatchTimelineData
        {
            MatchId = "GRUB_TEST",
            RealEvents = new List<TimelineEventRecord>
            {
                new() { TimestampMs = 360000, EventType = "ELITE_MONSTER_KILL", KillerId = 1, KillerTeamId = 100, MonsterType = "HORDE" },
                new() { TimestampMs = 364000, EventType = "ELITE_MONSTER_KILL", KillerId = 1, KillerTeamId = 100, MonsterType = "HORDE" },
                new() { TimestampMs = 368000, EventType = "ELITE_MONSTER_KILL", KillerId = 1, KillerTeamId = 100, MonsterType = "HORDE" }
            }
        };

        var report = MatchTacticalAnalyzer.AnalyzeMatchTactics(match, match.Participants[0], timeline, GameTier.Emerald);

        Assert.NotNull(report);
        Assert.Single(report.TimelineEvents);
        Assert.Contains("Личинки Безодні (x3)", report.TimelineEvents[0].Title);
    }

    [Fact]
    public void RealMatchDatasetCollector_ExtractFeaturesFromMatches_ComputesFeaturesCorrectly()
    {
        var matches = new List<Match>
        {
            new Match
            {
                MatchId = "EUW1_1",
                GameDurationSeconds = 1500,
                WinningTeam = TeamSide.Blue,
                Teams = new List<TeamStats>
                {
                    new TeamStats { TeamSide = TeamSide.Blue, GoldAt15 = 26000, KillsAt15 = 10, CsAt15 = 240, XpAt15 = 18000, VoidgrubKills = 5, RiftHeraldKills = 1, FirstBlood = true, FirstTower = true, FirstDragon = true, TowersAt15 = 2, DragonsAt15 = 1, TowerKills = 7, DragonKills = 3 },
                    new TeamStats { TeamSide = TeamSide.Red, GoldAt15 = 23000, KillsAt15 = 4, CsAt15 = 200, XpAt15 = 16000, VoidgrubKills = 1, RiftHeraldKills = 0, FirstBlood = false, FirstTower = false, FirstDragon = false, TowersAt15 = 0, DragonsAt15 = 0, TowerKills = 2, DragonKills = 1 }
                },
                Participants = new List<Participant>()
            }
        };

        var features = GameAnalytics.ML.Training.RealMatchDatasetCollector.ExtractFeaturesFromMatches(matches);

        Assert.Single(features);
        Assert.True(features[0].Label);
        Assert.Equal(3000f, features[0].GoldDiff15);
        Assert.Equal(6f, features[0].KillDiff15);
        Assert.Equal(40f, features[0].CsDiff15);
        Assert.Equal(2000f, features[0].XpDiff15);
        Assert.Equal(4f, features[0].VoidgrubDiff);
        Assert.Equal(1f, features[0].HeraldDiff);
        Assert.Equal(1f, features[0].FirstBlood);
        Assert.Equal(1f, features[0].FirstTower);
        Assert.Equal(1f, features[0].FirstDragon);
        // Must use at-15 towers/dragons, never end-of-game 7-2 / 3-1.
        Assert.Equal(2f, features[0].TowerDiff);
        Assert.Equal(1f, features[0].DragonDiff);
        Assert.True(features[0].AvgRankScore > 0);
        Assert.Equal(features[0].GoldDiff15 * (features[0].AvgRankScore / 5.5f), features[0].RankAdjustedGoldDiff, 2);
    }

    [Fact]
    public void RealMatchDatasetCollector_ExtractFeatures_UsesChampionWinRatesAndDraftScaling()
    {
        var matches = new List<Match>();
        for (int i = 0; i < 12; i++)
        {
            matches.Add(new Match
            {
                MatchId = $"EUW1_WR_{i}",
                GameDurationSeconds = 1600,
                WinningTeam = TeamSide.Blue,
                ApproxRankScore = 8f,
                Teams =
                {
                    new TeamStats { TeamSide = TeamSide.Blue, GoldAt15 = 27000, KillsAt15 = 9, CsAt15 = 230, XpAt15 = 17500, TowersAt15 = 1, DragonsAt15 = 1 },
                    new TeamStats { TeamSide = TeamSide.Red, GoldAt15 = 25000, KillsAt15 = 6, CsAt15 = 210, XpAt15 = 16500, TowersAt15 = 0, DragonsAt15 = 0 }
                },
                Participants =
                {
                    new Participant { TeamSide = TeamSide.Blue, ChampionName = "Renekton", Win = true, Position = Position.Top },
                    new Participant { TeamSide = TeamSide.Blue, ChampionName = "Lee Sin", Win = true, Position = Position.Jungle },
                    new Participant { TeamSide = TeamSide.Blue, ChampionName = "Ahri", Win = true, Position = Position.Middle },
                    new Participant { TeamSide = TeamSide.Blue, ChampionName = "Jinx", Win = true, Position = Position.Bottom },
                    new Participant { TeamSide = TeamSide.Blue, ChampionName = "Thresh", Win = true, Position = Position.Utility },
                    new Participant { TeamSide = TeamSide.Red, ChampionName = "Kayle", Win = false, Position = Position.Top },
                    new Participant { TeamSide = TeamSide.Red, ChampionName = "Nidalee", Win = false, Position = Position.Jungle },
                    new Participant { TeamSide = TeamSide.Red, ChampionName = "Kassadin", Win = false, Position = Position.Middle },
                    new Participant { TeamSide = TeamSide.Red, ChampionName = "Smolder", Win = false, Position = Position.Bottom },
                    new Participant { TeamSide = TeamSide.Red, ChampionName = "Yuumi", Win = false, Position = Position.Utility }
                }
            });
        }

        var features = GameAnalytics.ML.Training.RealMatchDatasetCollector.ExtractFeaturesFromMatches(matches);
        Assert.Equal(12, features.Count);
        Assert.Equal(8f, features[0].AvgRankScore);
        // First match has no past history → neutral WR; later matches see rolling blue-favoring WR.
        Assert.Equal(50f, features[0].BlueAvgWinRate);
        Assert.Equal(50f, features[0].RedAvgWinRate);
        Assert.True(features[^1].BlueAvgWinRate > features[^1].RedAvgWinRate);
        Assert.True(features[^1].WinRateDiff > 0);
        // Renekton/Lee early vs Kayle/Kassadin late → blue early lead, red late lead
        Assert.True(features[0].EarlyPowerDiff > 0);
        Assert.True(features[0].LatePowerDiff < 0);
        Assert.True(features[0].EngageDiff != 0 || features[0].TankDiff != 0);
    }

    [Fact]
    public void RealMatchDatasetCollector_ExtractFeatures_SkipsMatchesWithoutGoldAt15()
    {
        var matches = new List<Match>
        {
            new Match
            {
                MatchId = "EUW1_NO15",
                GameDurationSeconds = 1800,
                WinningTeam = TeamSide.Blue,
                Teams = new List<TeamStats>
                {
                    new TeamStats { TeamSide = TeamSide.Blue, TowerKills = 9, DragonKills = 4 },
                    new TeamStats { TeamSide = TeamSide.Red, TowerKills = 1, DragonKills = 0 }
                }
            }
        };

        var features = GameAnalytics.ML.Training.RealMatchDatasetCollector.ExtractFeaturesFromMatches(matches);
        Assert.Empty(features);
    }

    [Fact]
    public void RealMatchDatasetCollector_ExtractFeatures_IgnoresEndGameTowerDragonLeakage()
    {
        var matches = new List<Match>
        {
            new Match
            {
                MatchId = "EUW1_LEAK",
                GameDurationSeconds = 2000,
                WinningTeam = TeamSide.Red,
                Teams = new List<TeamStats>
                {
                    // Blue slightly ahead at 15', but crushed end-game scoreboard — must not leak.
                    new TeamStats { TeamSide = TeamSide.Blue, GoldAt15 = 25500, KillsAt15 = 7, CsAt15 = 220, XpAt15 = 17000, TowersAt15 = 1, DragonsAt15 = 1, TowerKills = 1, DragonKills = 0 },
                    new TeamStats { TeamSide = TeamSide.Red, GoldAt15 = 24800, KillsAt15 = 6, CsAt15 = 210, XpAt15 = 16800, TowersAt15 = 1, DragonsAt15 = 0, TowerKills = 9, DragonKills = 4 }
                }
            }
        };

        var features = GameAnalytics.ML.Training.RealMatchDatasetCollector.ExtractFeaturesFromMatches(matches);
        Assert.Single(features);
        Assert.Equal(0f, features[0].TowerDiff);
        Assert.Equal(1f, features[0].DragonDiff);
        Assert.False(features[0].Label);
    }

    [Fact]
    public void MatchTimelineData_ToInputFeatures_MapsCsXpAndObjectives()
    {
        var timeline = new MatchTimelineData
        {
            MatchId = "TL_TEST",
            GoldAt15Blue = 27000,
            GoldAt15Red = 24000,
            KillsAt15Blue = 8,
            KillsAt15Red = 5,
            CsAt15Blue = 260,
            CsAt15Red = 220,
            XpAt15Blue = 20000,
            XpAt15Red = 18000,
            VoidgrubsAt15Blue = 6,
            VoidgrubsAt15Red = 0,
            HeraldsAt15Blue = 1,
            HeraldsAt15Red = 0,
            BlueFirstBlood = true,
            BlueFirstTower = true,
            BlueFirstDragon = true
        };

        var features = timeline.ToInputFeatures(blueAvgWinRate: 52f, redAvgWinRate: 48f);

        Assert.Equal(3000, features.GoldDiffAt15);
        Assert.Equal(3, features.KillDiffAt15);
        Assert.Equal(40, features.CsDiffAt15);
        Assert.Equal(2000, features.XpDiffAt15);
        Assert.Equal(6, features.VoidgrubDiff);
        Assert.Equal(1, features.HeraldDiff);
        Assert.True(features.BlueFirstBlood);
        Assert.True(features.BlueFirstTower);
        Assert.True(features.BlueFirstDragon);
    }
}


