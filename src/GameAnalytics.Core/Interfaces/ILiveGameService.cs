using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Interfaces;

public interface ILiveGameService
{
    Task<LiveGameStats> GetLiveGameStatsAsync(CancellationToken ct = default);
    Task<bool> IsLiveGameRunningAsync(CancellationToken ct = default);
}
