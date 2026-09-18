using System.Net;
using System.Text.Json;
using GameAnalytics.Core.Entities;
using GameAnalytics.Infrastructure.Services;
using GameAnalytics.ML.Training;
using Xunit;

namespace GameAnalytics.Tests;

public class VpsSyncTests
{
    [Fact]
    public void VpsServerStatus_SerializationRoundtrip_WorksCorrectly()
    {
        var status = new VpsServerStatus
        {
            Status = "online",
            ActiveAlgorithm = "FastTree",
            Epoch = 25,
            TotalMatches = 500,
            TotalTeamStats = 1000,
            Accuracy = 0.942,
            AreaUnderRocCurve = 0.991,
            F1Score = 0.945,
            ModelFileExists = true,
            ModelFileSize = 40960
        };

        var json = JsonSerializer.Serialize(status);
        var deserialized = JsonSerializer.Deserialize<VpsServerStatus>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("online", deserialized.Status);
        Assert.Equal(25, deserialized.Epoch);
        Assert.Equal(500, deserialized.TotalMatches);
        Assert.Equal(0.942, deserialized.Accuracy);
    }

    [Fact]
    public async Task VpsHttpSyncServer_ServesStatusAndModelOverHttp()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"vps_test_{Guid.NewGuid():N}");
        var modelsDir = Path.Combine(testDir, "models");
        Directory.CreateDirectory(modelsDir);

        var modelFile = Path.Combine(modelsDir, "fasttree_model.zip");
        await File.WriteAllBytesAsync(modelFile, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14 });

        var dbFile = Path.Combine(testDir, "game_analytics.db");
        await File.WriteAllBytesAsync(dbFile, new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 });

        // Choose a free port
        var port = 5060;
        using var cts = new CancellationTokenSource();

        var server = new VpsHttpSyncServer(
            port: port,
            modelDirectory: modelsDir,
            databasePath: dbFile,
            statusProvider: () => new VpsServerStatus
            {
                Status = "online",
                Epoch = 10,
                TotalMatches = 200,
                Accuracy = 0.935
            });

        try
        {
            server.Start(null, cts.Token);
            await Task.Delay(200);

            using var httpClient = new HttpClient();
            var syncService = new VpsSyncService(httpClient);
            var baseUrl = $"http://127.0.0.1:{port}";

            // 1. Test status endpoint
            var status = await syncService.GetServerStatusAsync(baseUrl, cts.Token);
            Assert.NotNull(status);
            Assert.Equal("online", status.Status);
            Assert.Equal(10, status.Epoch);
            Assert.Equal(200, status.TotalMatches);

            // 2. Test download model
            var downloadedModel = Path.Combine(testDir, "downloaded_model.zip");
            var modelOk = await syncService.DownloadModelAsync(baseUrl, downloadedModel, cts.Token);
            Assert.True(modelOk);
            Assert.True(File.Exists(downloadedModel));
            Assert.Equal(5, new FileInfo(downloadedModel).Length);

            // 3. Test download database
            var downloadedDb = Path.Combine(testDir, "downloaded.db");
            var dbOk = await syncService.DownloadDatabaseAsync(baseUrl, downloadedDb, cts.Token);
            Assert.True(dbOk);
            Assert.True(File.Exists(downloadedDb));
            Assert.Equal(6, new FileInfo(downloadedDb).Length);
        }
        finally
        {
            server.Stop();
            cts.Cancel();
            try { Directory.Delete(testDir, true); } catch { }
        }
    }
}
