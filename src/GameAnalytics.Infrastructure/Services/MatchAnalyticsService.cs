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

    public async Task<IReadOnlyDictionary<string, int>> GetPlayerMatchLpMapAsync(string puuid, CancellationToken ct = default)
    {
        return await _matchRepository.GetPlayerMatchLpMapAsync(puuid, ct);
    }

    public async Task TrackAndReconstructLpAsync(SummonerProfile profile, IReadOnlyList<Match> matches, CancellationToken ct = default)
    {
        if (profile == null || string.IsNullOrWhiteSpace(profile.Puuid) || matches.Count == 0)
            return;

        var rankedMatches = matches
            .Where(m => m.QueueId == 420 || m.QueueId == 440 || m.QueueName.Contains("Ranked", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.GameCreation)
            .ToList();

        if (rankedMatches.Count == 0)
            return;

        var existingMap = await _matchRepository.GetPlayerMatchLpMapAsync(profile.Puuid, ct);
        var snapshot = await _matchRepository.GetLpSnapshotAsync(profile.Puuid, 420, ct);

        var newestRanked = rankedMatches[0];
        var currentTotalLp = GameAnalytics.Core.Helpers.LpCalculationHelper.CalculateTotalLp(profile.Tier, profile.Rank, profile.LeaguePoints);

        // Case A: A snapshot exists and new ranked matches were played since last snapshot
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.LastMatchId) && !snapshot.LastMatchId.Equals(newestRanked.MatchId, StringComparison.OrdinalIgnoreCase))
        {
            var newMatches = rankedMatches
                .TakeWhile(m => !m.MatchId.Equals(snapshot.LastMatchId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (newMatches.Count == 1)
            {
                var match = newMatches[0];
                var prevTotalLp = GameAnalytics.Core.Helpers.LpCalculationHelper.CalculateTotalLp(snapshot.Tier, snapshot.Rank, snapshot.LeaguePoints);
                var exactDelta = currentTotalLp - prevTotalLp;

                var p = match.Participants.FirstOrDefault(x => x.Puuid == profile.Puuid || x.SummonerName.Equals(profile.GameName, StringComparison.OrdinalIgnoreCase));
                var isWin = p?.Win ?? (match.WinningTeam == GameAnalytics.Core.Enums.TeamSide.Blue);
                if (exactDelta == 0 && !isWin && !match.IsRemake)
                {
                    exactDelta = -20;
                }
                else if (exactDelta == 0 && isWin && !match.IsRemake)
                {
                    exactDelta = 20;
                }

                await _matchRepository.SaveMatchLpRecordAsync(new PlayerMatchLpRecord
                {
                    Puuid = profile.Puuid,
                    MatchId = match.MatchId,
                    QueueId = match.QueueId,
                    LpDelta = exactDelta,
                    LeaguePointsAfter = profile.LeaguePoints
                }, ct);
            }
            else if (newMatches.Count > 1)
            {
                var currentRunningLp = profile.LeaguePoints;
                foreach (var match in newMatches)
                {
                    var p = match.Participants.FirstOrDefault(x => x.Puuid == profile.Puuid || x.SummonerName.Equals(profile.GameName, StringComparison.OrdinalIgnoreCase));
                    var isWin = p?.Win ?? (match.WinningTeam == GameAnalytics.Core.Enums.TeamSide.Blue);
                    var delta = match.IsRemake ? 0 : (isWin ? (profile.WinRate >= 54 ? 21 : 20) : (profile.WinRate >= 54 ? -19 : -20));

                    await _matchRepository.SaveMatchLpRecordAsync(new PlayerMatchLpRecord
                    {
                        Puuid = profile.Puuid,
                        MatchId = match.MatchId,
                        QueueId = match.QueueId,
                        LpDelta = delta,
                        LeaguePointsAfter = currentRunningLp
                    }, ct);

                    currentRunningLp = Math.Max(0, currentRunningLp - delta);
                }
            }

            snapshot.LeaguePoints = profile.LeaguePoints;
            snapshot.Tier = profile.Tier;
            snapshot.Rank = profile.Rank;
            snapshot.Wins = profile.Wins;
            snapshot.Losses = profile.Losses;
            snapshot.LastMatchId = newestRanked.MatchId;
            await _matchRepository.SaveLpSnapshotAsync(snapshot, ct);
        }
        else if (snapshot == null)
        {
            var currentRunningLp = profile.LeaguePoints;
            foreach (var match in rankedMatches)
            {
                if (!existingMap.ContainsKey(match.MatchId))
                {
                    var p = match.Participants.FirstOrDefault(x => x.Puuid == profile.Puuid || x.SummonerName.Equals(profile.GameName, StringComparison.OrdinalIgnoreCase));
                    var isWin = p?.Win ?? (match.WinningTeam == GameAnalytics.Core.Enums.TeamSide.Blue);
                    var delta = match.IsRemake ? 0 : (isWin ? (profile.WinRate >= 54 ? 21 : 20) : (profile.WinRate >= 54 ? -19 : -20));

                    await _matchRepository.SaveMatchLpRecordAsync(new PlayerMatchLpRecord
                    {
                        Puuid = profile.Puuid,
                        MatchId = match.MatchId,
                        QueueId = match.QueueId,
                        LpDelta = delta,
                        LeaguePointsAfter = currentRunningLp
                    }, ct);

                    currentRunningLp = Math.Max(0, currentRunningLp - delta);
                }
            }

            await _matchRepository.SaveLpSnapshotAsync(new PlayerLpSnapshot
            {
                Puuid = profile.Puuid,
                QueueId = 420,
                LeaguePoints = profile.LeaguePoints,
                Tier = profile.Tier,
                Rank = profile.Rank,
                Wins = profile.Wins,
                Losses = profile.Losses,
                LastMatchId = newestRanked.MatchId
            }, ct);
        }
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

    public bool ReloadModel()
    {
        return _predictionEngine.ReloadModel();
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

