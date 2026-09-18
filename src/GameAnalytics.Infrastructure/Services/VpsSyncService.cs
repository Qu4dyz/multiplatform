using System.Net.Http.Json;
using System.Text.Json;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Infrastructure.Services;

public class VpsSyncService : IVpsSyncService
{
    private readonly HttpClient _httpClient;

    public VpsSyncService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<VpsServerStatus?> GetServerStatusAsync(string vpsBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vpsBaseUrl)) return null;

        var url = $"{vpsBaseUrl.TrimEnd('/')}/api/status";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            return JsonSerializer.Deserialize<VpsServerStatus>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DownloadModelAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vpsBaseUrl)) return false;

        var url = $"{vpsBaseUrl.TrimEnd('/')}/api/model";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return false;

            var targetDir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var tempFile = $"{targetFilePath}.tmp_{Guid.NewGuid():N}";
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(cts.Token))
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await remoteStream.CopyToAsync(fileStream, cts.Token);
            }

            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }
            File.Move(tempFile, targetFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DownloadDatabaseAsync(string vpsBaseUrl, string targetFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vpsBaseUrl)) return false;

        var url = $"{vpsBaseUrl.TrimEnd('/')}/api/database";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(45));

            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) return false;

            var targetDir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var tempFile = $"{targetFilePath}.tmp_{Guid.NewGuid():N}";
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(cts.Token))
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await remoteStream.CopyToAsync(fileStream, cts.Token);
            }

            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }
            File.Move(tempFile, targetFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
