using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Interfaces;

public interface IRiotApiClient
{
    Task<SummonerProfile?> GetSummonerByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRecentMatchIdsByPuuidAsync(string puuid, int count = 10, int? queue = null, CancellationToken ct = default);
    Task<Match?> GetMatchDetailsAsync(string matchId, CancellationToken ct = default);
    Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default);
    Task<IReadOnlyList<ChampionMasteryInfo>> GetTopChampionMasteriesAsync(string puuid, int count = 10, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetChallengerPlayerPuuidsAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default);
    /// <summary>High-elo ladder entries with tier tags for rank-aware ML labeling.</summary>
    Task<IReadOnlyList<LadderPlayerEntry>> GetHighEloLadderPlayersAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default);
    /// <summary>Iron–Diamond division pages for mid/low-elo crawl + rank hints.</summary>
    Task<IReadOnlyList<LadderPlayerEntry>> GetDivisionLadderPlayersAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 40, CancellationToken ct = default);
    /// <summary>Current solo/duo rank score (1–10) for a player, or 0 if unranked/unknown.</summary>
    Task<float> GetSoloRankScoreByPuuidAsync(string puuid, CancellationToken ct = default);
}

public readonly record struct LadderPlayerEntry(string Puuid, GameAnalytics.Core.Enums.GameTier Tier);

public interface IMatchRepository
{
    Task<IReadOnlyList<Match>> GetAllMatchesAsync(CancellationToken ct = default);
    Task<Match?> GetMatchByMatchIdAsync(string matchId, CancellationToken ct = default);
    Task SaveMatchAsync(Match match, CancellationToken ct = default);
    Task UpsertMatchAsync(Match match, CancellationToken ct = default);
    Task<int> GetTotalMatchesCountAsync(CancellationToken ct = default);
    Task ClearAllMatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctParticipantPuuidsAsync(int limit = 50, CancellationToken ct = default);
    /// <summary>Match IDs that have minute-15 objectives but still miss vision/death or CS/XP@10 features.</summary>
    Task<IReadOnlyList<string>> GetMatchIdsNeedingTimelineBackfillAsync(int limit = 50, CancellationToken ct = default);
    /// <summary>Ranked matches still missing ApproxRankScore (lobby skill tag).</summary>
    Task<IReadOnlyList<string>> GetMatchIdsNeedingRankBackfillAsync(int limit = 80, CancellationToken ct = default);
    /// <summary>Ranked matches still missing match-v5 challenges payload.</summary>
    Task<IReadOnlyList<string>> GetMatchIdsNeedingChallengesBackfillAsync(int limit = 40, CancellationToken ct = default);
    /// <summary>Matches with 15' features but no MatchTimelineCaches row (gold curve / replay).</summary>
    Task<IReadOnlyList<string>> GetMatchIdsMissingTimelineCacheAsync(int limit = 12, CancellationToken ct = default);
    /// <summary>Cached PUUID → solo rank score (1–10) for lobby tagging across epochs.</summary>
    Task<IReadOnlyDictionary<string, float>> GetPlayerRankHintsAsync(CancellationToken ct = default);
    Task UpsertPlayerRankHintsAsync(IReadOnlyDictionary<string, float> hints, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, int>> GetPlayerMatchLpMapAsync(string puuid, CancellationToken ct = default);
    Task SaveMatchLpRecordAsync(PlayerMatchLpRecord record, CancellationToken ct = default);
    Task<PlayerLpSnapshot?> GetLpSnapshotAsync(string puuid, int queueId = 420, CancellationToken ct = default);
    Task SaveLpSnapshotAsync(PlayerLpSnapshot snapshot, CancellationToken ct = default);
    Task<MatchTimelineData?> GetCachedTimelineAsync(string matchId, CancellationToken ct = default);
    Task SaveTimelineCacheAsync(string matchId, MatchTimelineData data, CancellationToken ct = default);
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
    Task<IReadOnlyDictionary<string, int>> GetPlayerMatchLpMapAsync(string puuid, CancellationToken ct = default);
    Task TrackAndReconstructLpAsync(SummonerProfile profile, IReadOnlyList<Match> matches, CancellationToken ct = default);
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

