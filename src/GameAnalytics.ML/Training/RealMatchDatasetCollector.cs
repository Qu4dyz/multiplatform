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
    /// populates early-game timeline metrics (15-min gold diff, kills, objectives),
    /// and saves them into the repository.
    /// </summary>
    public async Task<int> CollectRankedMatchesAsync(
        string seedPuuid, 
        int targetNewMatches = 30, 
        Action<string>? logger = null, 
        CancellationToken ct = default)
    {
        var visitedPuuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { seedPuuid };
        var candidatePuuids = new Queue<string>();
        candidatePuuids.Enqueue(seedPuuid);

        int newlySavedMatches = 0;

        logger?.Invoke($"[Collector] Початок збору реальних матчів для навчання ML (Ціль: {targetNewMatches})...");

        while (candidatePuuids.Count > 0 && newlySavedMatches < targetNewMatches && !ct.IsCancellationRequested)
        {
            var puuid = candidatePuuids.Dequeue();
            var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 15, ct);

            foreach (var matchId in matchIds)
            {
                if (newlySavedMatches >= targetNewMatches || ct.IsCancellationRequested) break;

                var existing = await _matchRepo.GetMatchByMatchIdAsync(matchId, ct);
                if (existing != null && existing.Teams.Count >= 2 && existing.Teams.Any(t => t.GoldAt15 > 0))
                {
                    continue; // Already collected with full timeline
                }

                var match = await _apiClient.GetMatchDetailsAsync(matchId, ct);
                if (match == null || match.IsRemake || match.GameDurationSeconds < 300)
                {
                    continue;
                }

                // Fetch real timeline to get exact 15-minute game state
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
                        blue.FirstBlood = timeline.BlueFirstBlood;
                        blue.FirstTower = timeline.BlueFirstTower;
                        blue.FirstDragon = timeline.BlueFirstDragon;
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
    /// Converts stored historical matches into ML training features.
    /// </summary>
    public static List<MatchInputData> ExtractFeaturesFromMatches(IEnumerable<Match> matches)
    {
        var result = new List<MatchInputData>();

        foreach (var match in matches)
        {
            if (match.IsRemake || match.GameDurationSeconds < 300) continue;

            var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
            var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
            if (blue == null || red == null) continue;

            // If 15-min stats not in timeline, approximate from match totals
            var goldDiff15 = (blue.GoldAt15 > 0 || red.GoldAt15 > 0)
                ? (blue.GoldAt15 - red.GoldAt15)
                : (float)Math.Round((blue.TowerKills - red.TowerKills) * 650.0 + (blue.DragonKills - red.DragonKills) * 400.0);

            var killDiff15 = (blue.KillsAt15 > 0 || red.KillsAt15 > 0)
                ? (blue.KillsAt15 - red.KillsAt15)
                : (match.Participants.Where(p => p.TeamSide == TeamSide.Blue).Sum(p => p.Kills) -
                   match.Participants.Where(p => p.TeamSide == TeamSide.Red).Sum(p => p.Kills));

            result.Add(new MatchInputData
            {
                FirstBlood = blue.FirstBlood ? 1f : 0f,
                FirstTower = blue.FirstTower ? 1f : 0f,
                FirstDragon = blue.FirstDragon ? 1f : 0f,
                GoldDiff15 = goldDiff15,
                KillDiff15 = killDiff15,
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
