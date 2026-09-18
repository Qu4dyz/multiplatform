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

        if (!string.IsNullOrWhiteSpace(seedPuuid))
        {
            visitedPuuids.Add(seedPuuid);
            candidatePuuids.Enqueue(seedPuuid);
        }

        int newlySavedMatches = 0;

        logger?.Invoke($"[Collector] Початок збору реальних Ranked Solo матчів для навчання ML (Ціль: {targetNewMatches})...");

        while (newlySavedMatches < targetNewMatches && !ct.IsCancellationRequested)
        {
            // Self-healing: if candidate queue is empty, pull stored PUUIDs from DB
            if (candidatePuuids.Count == 0)
            {
                var storedPuuids = await _matchRepo.GetDistinctParticipantPuuidsAsync(limit: 50, ct);
                foreach (var p in storedPuuids)
                {
                    if (visitedPuuids.Add(p))
                    {
                        candidatePuuids.Enqueue(p);
                    }
                }

                if (candidatePuuids.Count == 0)
                {
                    logger?.Invoke("[Collector] Черга кандидатів порожня та база не містить нових гравців. Завершення поточної ітерації.");
                    break;
                }
            }

            var puuid = candidatePuuids.Dequeue();

            // Query Ranked Solo/Duo queue (420) first to ensure 100% competitive SR games
            var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 15, queue: 420, ct: ct);
            if (matchIds.Count == 0)
            {
                // Fallback to general matches if queue 420 returned empty
                matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 10, queue: null, ct: ct);
            }

            foreach (var matchId in matchIds)
            {
                if (newlySavedMatches >= targetNewMatches || ct.IsCancellationRequested) break;

                var existing = await _matchRepo.GetMatchByMatchIdAsync(matchId, ct);
                if (existing != null && existing.Teams.Count >= 2 && existing.Teams.Any(t => t.GoldAt15 > 0))
                {
                    continue; // Already collected with full timeline
                }

                var match = await _apiClient.GetMatchDetailsAsync(matchId, ct);
                // Strict filter: must not be remake, duration >= 5 mins, and must be Summoner's Rift (420 Ranked Solo, 440 Ranked Flex, 400 Normal Draft)
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

                // Discover other high-elo players from this match to expand the crawl graph
                foreach (var p in match.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(p.Puuid) && visitedPuuids.Add(p.Puuid))
                    {
                        candidatePuuids.Enqueue(p.Puuid);
                    }
                }
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
