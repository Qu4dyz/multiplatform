using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Models;

namespace GameAnalytics.ML.Training;

public class RealMatchDatasetCollector
{
    private readonly IRiotApiClient _apiClient;
    private readonly IMatchRepository _matchRepo;

    public RealMatchDatasetCollector(IRiotApiClient apiClient, IMatchRepository matchRepo)
    {
        _apiClient = apiClient;
        _matchRepo = matchRepo;
    }

    /// <summary>
    /// Crawls real Ranked Solo/Duo matches from Riot API starting from seed PUUIDs,
    /// populates early-game timeline metrics (15-min gold/CS/XP diff, kills, Voidgrubs, Herald, objectives),
    /// and saves them into the repository.
    /// </summary>
    public async Task<int> CollectRankedMatchesAsync(
        string seedPuuid,
        int targetNewMatches = 30,
        Action<string>? logger = null,
        CancellationToken ct = default)
    {
        var visitedPuuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidatePuuids = new Queue<string>();
        var refillAttempts = 0;
        const int maxRefillAttempts = 6;

        void EnqueueCandidate(string? puuid)
        {
            if (string.IsNullOrWhiteSpace(puuid)) return;
            if (puuid.StartsWith("puuid-", StringComparison.OrdinalIgnoreCase)) return;
            if (!visitedPuuids.Add(puuid)) return;
            candidatePuuids.Enqueue(puuid);
        }

        void EnqueueParticipants(IEnumerable<Participant> participants)
        {
            foreach (var p in participants)
            {
                EnqueueCandidate(p.Puuid);
            }
        }

        EnqueueCandidate(seedPuuid);

        int newlySavedMatches = 0;

        logger?.Invoke($"[Collector] Початок збору реальних Ranked Solo матчів для навчання ML (Ціль: {targetNewMatches})...");

        while (newlySavedMatches < targetNewMatches && !ct.IsCancellationRequested)
        {
            // Self-healing: refill crawl queue from DB / high-elo ladders when empty
            if (candidatePuuids.Count == 0)
            {
                refillAttempts++;
                if (refillAttempts > maxRefillAttempts)
                {
                    logger?.Invoke($"[Collector] Досягнуто ліміт refill ({maxRefillAttempts}). Зупинка ітерації зі {newlySavedMatches} новими матчами.");
                    break;
                }

                // Forget visitation for DB/ladder refill so we can re-walk teammates of stored games
                // and discover matches beyond the previously exhausted frontier.
                visitedPuuids.Clear();
                if (!string.IsNullOrWhiteSpace(seedPuuid))
                {
                    visitedPuuids.Add(seedPuuid);
                }

                var storedPuuids = await _matchRepo.GetDistinctParticipantPuuidsAsync(limit: 80, ct);
                foreach (var p in storedPuuids)
                {
                    EnqueueCandidate(p);
                }

                logger?.Invoke($"[Collector] Refill #{refillAttempts}: додано {candidatePuuids.Count} кандидатів з локальної БД.");

                if (candidatePuuids.Count < 15)
                {
                    try
                    {
                        var ladderPuuids = await _apiClient.GetChallengerPlayerPuuidsAsync("RANKED_SOLO_5x5", maxCount: 40, ct: ct);
                        var before = candidatePuuids.Count;
                        foreach (var cPuuid in ladderPuuids)
                        {
                            EnqueueCandidate(cPuuid);
                        }
                        logger?.Invoke($"[Collector] Ladder refill: +{candidatePuuids.Count - before} гравців з Challenger/GM/Master.");
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"[Collector] Ladder refill failed: {ex.Message}");
                    }
                }

                if (candidatePuuids.Count == 0)
                {
                    logger?.Invoke("[Collector] Черга кандидатів порожня після refill. Завершення поточної ітерації.");
                    break;
                }
            }

            var puuid = candidatePuuids.Dequeue();

            // Deeper lookback: previously collected players still have older ranked games worth mining
            var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 30, queue: 420, ct: ct);
            if (matchIds.Count == 0)
            {
                matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 20, queue: null, ct: ct);
            }

            foreach (var matchId in matchIds)
            {
                if (newlySavedMatches >= targetNewMatches || ct.IsCancellationRequested) break;

                var existing = await _matchRepo.GetMatchByMatchIdAsync(matchId, ct);
                if (existing != null && existing.Teams.Count >= 2 && existing.Teams.Any(t => t.GoldAt15 > 0))
                {
                    // Critical: expand crawl graph from already-stored matches, otherwise the frontier dies
                    // once the seed player's recent games are fully collected.
                    // Cap queue growth to avoid burning the Riot rate limit on endless teammate BFS.
                    if (candidatePuuids.Count < 150)
                    {
                        EnqueueParticipants(existing.Participants);
                    }
                    continue;
                }

                var match = await _apiClient.GetMatchDetailsAsync(matchId, ct);
                // Strict filter: must not be remake, duration >= 5 mins, and must be Summoner's Rift
                if (match == null || match.IsRemake || match.GameDurationSeconds < 300)
                {
                    continue;
                }
                if (match.QueueId != 420 && match.QueueId != 440 && match.QueueId != 400)
                {
                    continue; // Skip ARAM, Arena, Fun modes
                }

                // Fetch real timeline to get exact 15-minute game state (Gold, CS, XP, Grubs, Herald)
                var timeline = await _apiClient.GetMatchTimelineAsync(matchId, ct);
                if (timeline != null)
                {
                    var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
                    var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
                    if (blue != null && red != null)
                    {
                        blue.GoldAt15 = timeline.GoldAt15Blue;
                        red.GoldAt15 = timeline.GoldAt15Red;
                        blue.KillsAt15 = timeline.KillsAt15Blue;
                        red.KillsAt15 = timeline.KillsAt15Red;
                        blue.CsAt15 = timeline.CsAt15Blue;
                        red.CsAt15 = timeline.CsAt15Red;
                        blue.XpAt15 = timeline.XpAt15Blue;
                        red.XpAt15 = timeline.XpAt15Red;
                        blue.VoidgrubKills = timeline.VoidgrubsAt15Blue;
                        red.VoidgrubKills = timeline.VoidgrubsAt15Red;
                        blue.RiftHeraldKills = timeline.HeraldsAt15Blue;
                        red.RiftHeraldKills = timeline.HeraldsAt15Red;
                        blue.FirstBlood = timeline.BlueFirstBlood;
                        blue.FirstTower = timeline.BlueFirstTower;
                        blue.FirstDragon = timeline.BlueFirstDragon;
                        blue.FirstVoidgrub = timeline.VoidgrubsAt15Blue > 0;
                        blue.FirstRiftHerald = timeline.HeraldsAt15Blue > 0;
                    }
                }

                await _matchRepo.UpsertMatchAsync(match, ct);
                newlySavedMatches++;
                logger?.Invoke($"[Collector] Збережено реальний матч #{newlySavedMatches}/{targetNewMatches}: {match.MatchId} ({match.FormattedDuration}, {match.QueueName})");

                EnqueueParticipants(match.Participants);
            }
        }

        logger?.Invoke($"[Collector] Збір завершено. Додано {newlySavedMatches} нових матчів до тренувальної вибірки.");
        return newlySavedMatches;
    }

    /// <summary>
    /// Converts stored historical matches into rich ML training features.
    /// </summary>
    public static List<MatchInputData> ExtractFeaturesFromMatches(IEnumerable<Match> matches)
    {
        var result = new List<MatchInputData>();

        foreach (var match in matches)
        {
            if (match.IsRemake || match.GameDurationSeconds < 300) continue;
            if (match.QueueId != 420 && match.QueueId != 440 && match.QueueId != 400 && match.QueueId != 0) continue;

            var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
            var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
            if (blue == null || red == null) continue;

            // 15-min gold diff
            var goldDiff15 = (blue.GoldAt15 > 0 || red.GoldAt15 > 0)
                ? (blue.GoldAt15 - red.GoldAt15)
                : (float)Math.Round((blue.TowerKills - red.TowerKills) * 650.0 + (blue.DragonKills - red.DragonKills) * 400.0);

            // 15-min kill diff
            var killDiff15 = (blue.KillsAt15 > 0 || red.KillsAt15 > 0)
                ? (blue.KillsAt15 - red.KillsAt15)
                : (match.Participants.Where(p => p.TeamSide == TeamSide.Blue).Sum(p => p.Kills) -
                   match.Participants.Where(p => p.TeamSide == TeamSide.Red).Sum(p => p.Kills));

            // 15-min CS diff
            var csDiff15 = (blue.CsAt15 > 0 || red.CsAt15 > 0)
                ? (float)(blue.CsAt15 - red.CsAt15)
                : (float)(match.Participants.Where(p => p.TeamSide == TeamSide.Blue).Sum(p => p.TotalMinionsKilled) -
                          match.Participants.Where(p => p.TeamSide == TeamSide.Red).Sum(p => p.TotalMinionsKilled)) * 0.6f;

            // 15-min XP diff
            var xpDiff15 = (blue.XpAt15 > 0 || red.XpAt15 > 0)
                ? (float)(blue.XpAt15 - red.XpAt15)
                : goldDiff15 * 0.65f;

            // Voidgrubs & Herald diff
            var voidgrubDiff = (float)(blue.VoidgrubKills - red.VoidgrubKills);
            var heraldDiff = (float)(blue.RiftHeraldKills - red.RiftHeraldKills);

            result.Add(new MatchInputData
            {
                FirstBlood = blue.FirstBlood ? 1f : 0f,
                FirstTower = blue.FirstTower ? 1f : 0f,
                FirstDragon = blue.FirstDragon ? 1f : 0f,
                GoldDiff15 = goldDiff15,
                KillDiff15 = killDiff15,
                CsDiff15 = csDiff15,
                XpDiff15 = xpDiff15,
                VoidgrubDiff = voidgrubDiff,
                HeraldDiff = heraldDiff,
                BlueAvgWinRate = 50.0f,
                RedAvgWinRate = 50.0f,
                TowerDiff = blue.TowerKills - red.TowerKills,
                DragonDiff = blue.DragonKills - red.DragonKills,
                Label = match.WinningTeam == TeamSide.Blue
            });
        }

        return result;
    }
}
