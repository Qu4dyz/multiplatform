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
        var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count, queue: null, ct: ct);
        var fetchedMatches = new List<Match>();

        foreach (var id in matchIds)
        {
            var existing = await _matchRepository.GetMatchByMatchIdAsync(id, ct);
            var hasCompleteData = existing != null 
                && existing.Participants.Any(p => p.Item0 > 0 || p.Item1 > 0 || p.Item2 > 0 || p.Item6 > 0)
                && existing.Participants.Any(p => p.ParticipantId > 0);

            if (existing != null && hasCompleteData)
            {
                fetchedMatches.Add(existing);
                continue;
            }

            var match = await _apiClient.GetMatchDetailsAsync(id, ct);
            if (match != null)
            {
                await _matchRepository.UpsertMatchAsync(match, ct);
                fetchedMatches.Add(match);
            }
            else if (existing != null)
            {
                fetchedMatches.Add(existing);
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

    public ModelBenchmarkReport RunBenchmark(int sampleCount = 2000)
    {
        return _predictionEngine.RunAlgorithmsBenchmark(sampleCount);
    }

    public void SetActiveMLAlgorithm(GameAnalytics.Core.Enums.MLAlgorithmType algorithm)
    {
        _predictionEngine.SetActiveAlgorithm(algorithm);
    }

    public async Task<bool> RetrainModelOnSavedMatchesAsync(CancellationToken ct = default)
    {
        var matches = await _matchRepository.GetAllMatchesAsync(ct);
        if (matches.Count < 5) return false;

        await _predictionEngine.TrainModelAsync(matches, ct);
        return _predictionEngine.IsTrainedOnRealData;
    }

    public GameAnalytics.Core.Enums.MLAlgorithmType ActiveMLAlgorithm => _predictionEngine.ActiveAlgorithm;
    public ModelMetrics? CurrentModelMetrics => _predictionEngine.CurrentModelMetrics;
    public bool IsTrainedOnRealData => _predictionEngine.IsTrainedOnRealData;
    public int TrainingDatasetSize => _predictionEngine.TrainingDatasetSize;
    public DateTime? LastTrainedAt => _predictionEngine.LastTrainedAt;
}

