using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Interfaces;

public interface IRiotApiClient
{
    Task<SummonerProfile?> GetSummonerByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRecentMatchIdsByPuuidAsync(string puuid, int count = 10, int? queue = null, CancellationToken ct = default);
    Task<Match?> GetMatchDetailsAsync(string matchId, CancellationToken ct = default);
    Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default);
}

public interface IMatchRepository
{
    Task<IReadOnlyList<Match>> GetAllMatchesAsync(CancellationToken ct = default);
    Task<Match?> GetMatchByMatchIdAsync(string matchId, CancellationToken ct = default);
    Task SaveMatchAsync(Match match, CancellationToken ct = default);
    Task UpsertMatchAsync(Match match, CancellationToken ct = default);
    Task<int> GetTotalMatchesCountAsync(CancellationToken ct = default);
    Task ClearAllMatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctParticipantPuuidsAsync(int limit = 50, CancellationToken ct = default);
}

public interface IPredictionEngine
{
    PredictionResult PredictMatchOutcome(MatchInputFeatures features);
    Task TrainModelAsync(IEnumerable<Match> historicalMatches, CancellationToken ct = default);
    ModelBenchmarkReport RunAlgorithmsBenchmark(int sampleCount = 2000);
    void SetActiveAlgorithm(GameAnalytics.Core.Enums.MLAlgorithmType algorithm);
    bool ReloadModel();
    GameAnalytics.Core.Enums.MLAlgorithmType ActiveAlgorithm { get; }
    ModelMetrics? CurrentModelMetrics { get; }
    bool IsTrainedOnRealData { get; }
    int TrainingDatasetSize { get; }
    DateTime? LastTrainedAt { get; }
}

public interface IMatchAnalyticsService
{
    Task<SummonerProfile?> FetchAndCacheSummonerAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<IReadOnlyList<Match>> FetchAndSaveRecentMatchesAsync(string puuid, int count = 10, CancellationToken ct = default);
    Task<IReadOnlyList<Match>> GetSavedMatchHistoryAsync(CancellationToken ct = default);
    Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default);
    Task<bool> RetrainModelOnSavedMatchesAsync(CancellationToken ct = default);
    PredictionResult PredictOutcome(MatchInputFeatures features);
    ModelBenchmarkReport RunBenchmark(int sampleCount = 2000);
    void SetActiveMLAlgorithm(GameAnalytics.Core.Enums.MLAlgorithmType algorithm);
    bool ReloadModel();
    GameAnalytics.Core.Enums.MLAlgorithmType ActiveMLAlgorithm { get; }
    ModelMetrics? CurrentModelMetrics { get; }
    bool IsTrainedOnRealData { get; }
    int TrainingDatasetSize { get; }
    DateTime? LastTrainedAt { get; }
}

