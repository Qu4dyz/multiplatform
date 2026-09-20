using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Training;
using Xunit;

namespace GameAnalytics.Tests;

public class MatchCollectorTests
{
    private sealed class FakeRiotApi : IRiotApiClient
    {
        public Dictionary<string, List<string>> MatchIdsByPuuid { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Match> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> LadderPuuids { get; } = new();

        public Task<SummonerProfile?> GetSummonerByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default)
            => Task.FromResult<SummonerProfile?>(null);

        public Task<IReadOnlyList<string>> GetRecentMatchIdsByPuuidAsync(string puuid, int count = 10, int? queue = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(MatchIdsByPuuid.TryGetValue(puuid, out var ids) ? ids.Take(count).ToList() : Array.Empty<string>());

        public Task<Match?> GetMatchDetailsAsync(string matchId, CancellationToken ct = default)
            => Task.FromResult(Matches.TryGetValue(matchId, out var m) ? m : null);

        public Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default)
            => Task.FromResult<MatchTimelineData?>(new MatchTimelineData
            {
                MatchId = matchId,
                GoldAt15Blue = 25000,
                GoldAt15Red = 23000,
                KillsAt15Blue = 8,
                KillsAt15Red = 5,
                CsAt15Blue = 200,
                CsAt15Red = 180,
                XpAt15Blue = 16000,
                XpAt15Red = 15000,
                VoidgrubsAt15Blue = 3,
                VoidgrubsAt15Red = 0,
                HeraldsAt15Blue = 1,
                HeraldsAt15Red = 0,
                BlueFirstBlood = true,
                BlueFirstTower = true,
                BlueFirstDragon = false
            });

        public Task<IReadOnlyList<ChampionMasteryInfo>> GetTopChampionMasteriesAsync(string puuid, int count = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChampionMasteryInfo>>(Array.Empty<ChampionMasteryInfo>());

        public Task<IReadOnlyList<string>> GetChallengerPlayerPuuidsAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(LadderPuuids.Take(maxCount).ToList());

        public Task<IReadOnlyList<LadderPlayerEntry>> GetHighEloLadderPlayersAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LadderPlayerEntry>>(
                LadderPuuids.Take(maxCount).Select(p => new LadderPlayerEntry(p, GameTier.Challenger)).ToList());
    }

    private sealed class FakeMatchRepo : IMatchRepository
    {
        public Dictionary<string, Match> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<Match>> GetAllMatchesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Match>>(Stored.Values.ToList());

        public Task<Match?> GetMatchByMatchIdAsync(string matchId, CancellationToken ct = default)
            => Task.FromResult(Stored.TryGetValue(matchId, out var m) ? m : null);

        public Task SaveMatchAsync(Match match, CancellationToken ct = default)
        {
            Stored[match.MatchId] = match;
            return Task.CompletedTask;
        }

        public Task UpsertMatchAsync(Match match, CancellationToken ct = default)
        {
            Stored[match.MatchId] = match;
            return Task.CompletedTask;
        }

        public Task<int> GetTotalMatchesCountAsync(CancellationToken ct = default)
            => Task.FromResult(Stored.Count);

        public Task ClearAllMatchesAsync(CancellationToken ct = default)
        {
            Stored.Clear();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetDistinctParticipantPuuidsAsync(int limit = 50, CancellationToken ct = default)
        {
            var puuids = Stored.Values
                .SelectMany(m => m.Participants)
                .Select(p => p.Puuid)
                .Where(p => !string.IsNullOrWhiteSpace(p) && !p.StartsWith("puuid-", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(puuids);
        }

        public Task<IReadOnlyList<string>> GetMatchIdsNeedingTimelineBackfillAsync(int limit = 50, CancellationToken ct = default)
        {
            var ids = Stored.Values
                .Where(m => m.HasMinute15Objectives && (!m.HasCsXp10Features || !m.HasVisionDeathFeatures))
                .Select(m => m.MatchId)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(ids);
        }

        public Task<IReadOnlyDictionary<string, int>> GetPlayerMatchLpMapAsync(string puuid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        public Task SaveMatchLpRecordAsync(PlayerMatchLpRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<PlayerLpSnapshot?> GetLpSnapshotAsync(string puuid, int queueId = 420, CancellationToken ct = default)
            => Task.FromResult<PlayerLpSnapshot?>(null);

        public Task SaveLpSnapshotAsync(PlayerLpSnapshot snapshot, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static Match MakeStoredMatch(string matchId, params string[] puuids)
    {
        var participants = puuids.Select((p, i) => new Participant
        {
            Puuid = p,
            SummonerName = $"Player{i}",
            TeamSide = i < 5 ? TeamSide.Blue : TeamSide.Red,
            ChampionName = "Ahri"
        }).ToList();

        return new Match
        {
            MatchId = matchId,
            QueueId = 420,
            GameDurationSeconds = 1800,
            WinningTeam = TeamSide.Blue,
            Participants = participants,
            Teams = new List<TeamStats>
            {
                new() { TeamSide = TeamSide.Blue, GoldAt15 = 25000 },
                new() { TeamSide = TeamSide.Red, GoldAt15 = 23000 }
            }
        };
    }

    private static Match MakeFreshMatch(string matchId, params string[] puuids)
    {
        var m = MakeStoredMatch(matchId, puuids);
        // Fresh API match has no 15-min gold yet — collector fills from timeline
        foreach (var t in m.Teams) t.GoldAt15 = 0;
        return m;
    }

    [Fact]
    public async Task CollectRankedMatches_ExpandsGraphFromAlreadyStoredMatches()
    {
        var api = new FakeRiotApi();
        var repo = new FakeMatchRepo();

        // Seed already fully collected — previously this killed the frontier
        var stored = MakeStoredMatch("EUW1_OLD", "seed-puuid", "mate-a", "mate-b", "mate-c", "enemy-1", "enemy-2", "enemy-3", "enemy-4", "enemy-5", "enemy-6");
        repo.Stored[stored.MatchId] = stored;
        api.MatchIdsByPuuid["seed-puuid"] = new List<string> { "EUW1_OLD" };

        // Teammate from stored match has a NEW ranked game
        api.MatchIdsByPuuid["mate-a"] = new List<string> { "EUW1_NEW" };
        api.Matches["EUW1_NEW"] = MakeFreshMatch("EUW1_NEW", "mate-a", "fresh-1", "fresh-2", "fresh-3", "fresh-4", "fresh-5", "fresh-6", "fresh-7", "fresh-8", "fresh-9");

        var collector = new RealMatchDatasetCollector(api, repo);
        var added = await collector.CollectRankedMatchesAsync("seed-puuid", targetNewMatches: 5);

        Assert.True(added >= 1);
        Assert.Contains("EUW1_NEW", (IDictionary<string, Match>)repo.Stored);
        Assert.True(repo.Stored["EUW1_NEW"].Teams.Any(t => t.GoldAt15 > 0));
    }

    [Fact]
    public async Task CollectRankedMatches_UsesLadderWhenDbFrontierIsEmpty()
    {
        var api = new FakeRiotApi();
        var repo = new FakeMatchRepo();
        api.LadderPuuids.Add("challenger-1");
        api.MatchIdsByPuuid["challenger-1"] = new List<string> { "EUW1_CHAL" };
        api.Matches["EUW1_CHAL"] = MakeFreshMatch("EUW1_CHAL", "challenger-1", "c2", "c3", "c4", "c5", "c6", "c7", "c8", "c9", "c10");

        // Seed has no match history → forces refill → ladder
        var collector = new RealMatchDatasetCollector(api, repo);
        var added = await collector.CollectRankedMatchesAsync("lonely-seed", targetNewMatches: 3);

        Assert.Equal(1, added);
        Assert.Contains("EUW1_CHAL", (IDictionary<string, Match>)repo.Stored);
    }
}
