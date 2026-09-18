using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Infrastructure.Services;

public class MatchAnalyticsService : IMatchAnalyticsService
{
    private readonly IRiotApiClient _apiClient;
    private readonly IMatchRepository _matchRepository;
    private readonly IPredictionEngine _predictionEngine;

    public MatchAnalyticsService(
        IRiotApiClient apiClient,
        IMatchRepository matchRepository,
        IPredictionEngine predictionEngine)
    {
        _apiClient = apiClient;
        _matchRepository = matchRepository;
        _predictionEngine = predictionEngine;
    }

    public async Task<SummonerProfile?> FetchAndCacheSummonerAsync(string gameName, string tagLine, CancellationToken ct = default)
    {
        return await _apiClient.GetSummonerByRiotIdAsync(gameName, tagLine, ct);
    }

    public async Task<IReadOnlyList<Match>> FetchAndSaveRecentMatchesAsync(string puuid, int count = 10, CancellationToken ct = default)
    {
        var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count, ct);
        var fetchedMatches = new List<Match>();

        foreach (var id in matchIds)
        {
            var existing = await _matchRepository.GetMatchByMatchIdAsync(id, ct);
            if (existing != null)
            {
                fetchedMatches.Add(existing);
                continue;
            }

            var match = await _apiClient.GetMatchDetailsAsync(id, ct);
            if (match != null)
            {
                await _matchRepository.SaveMatchAsync(match, ct);
                fetchedMatches.Add(match);
            }
        }

        return fetchedMatches;
    }

    public async Task<IReadOnlyList<Match>> GetSavedMatchHistoryAsync(CancellationToken ct = default)
    {
        return await _matchRepository.GetAllMatchesAsync(ct);
    }

    public async Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default)
    {
        return await _apiClient.GetMatchTimelineAsync(matchId, ct);
    }

    public PredictionResult PredictOutcome(MatchInputFeatures features)
    {
        return _predictionEngine.PredictMatchOutcome(features);
    }
}

