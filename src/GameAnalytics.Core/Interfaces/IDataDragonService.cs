using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Interfaces;

public interface IDataDragonService
{
    Task<string> GetLatestGameVersionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ChampionInfo>> GetAllChampionsAsync(CancellationToken ct = default);
    Task<ChampionInfo?> GetChampionByKeyAsync(int key, CancellationToken ct = default);
    Task<ChampionInfo?> GetChampionByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ChampionInfo>> GetChampionsByRoleAsync(string role, CancellationToken ct = default);
    Task PreloadItemDataAsync(CancellationToken ct = default);
    Task<ItemDefinition?> GetItemByIdAsync(int itemId, CancellationToken ct = default);
}

