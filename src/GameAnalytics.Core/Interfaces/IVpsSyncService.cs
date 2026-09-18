using GameAnalytics.Core.Entities;

namespace GameAnalytics.Core.Interfaces;

public interface IVpsSyncService
{
    Task<VpsServerStatus?> GetServerStatusAsync(string vpsBaseUrl, CancellationToken ct = default);
    Task<bool> DownloadModelAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default);
    Task<bool> DownloadDatabaseAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default);
}
