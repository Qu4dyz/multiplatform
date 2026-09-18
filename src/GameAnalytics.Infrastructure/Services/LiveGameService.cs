using System.Net.Http.Json;
using System.Text.Json;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;

namespace GameAnalytics.Infrastructure.Services;

public class LiveGameService : ILiveGameService
{
    private readonly HttpClient _httpClient;

    public LiveGameService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static HttpClient CreateLiveClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public async Task<bool> IsLiveGameRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            var response = await _httpClient.GetAsync("https://127.0.0.1:2999/liveclientdata/gamestats", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LiveGameStats> GetLiveGameStatsAsync(CancellationToken ct = default)
    {
        var stats = new LiveGameStats();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var response = await _httpClient.GetAsync("https://127.0.0.1:2999/liveclientdata/allgamedata", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                stats.StatusMessage = "Клієнт League of Legends не в активній грі (код відповіді відсутній).";
                return stats;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            stats.IsActiveGame = true;

            // 1. Game Data
            if (root.TryGetProperty("gameData", out var gameData))
            {
                if (gameData.TryGetProperty("gameTime", out var gt)) stats.GameTimeSeconds = gt.GetDouble();
                if (gameData.TryGetProperty("gameMode", out var gm)) stats.GameMode = gm.GetString() ?? string.Empty;
            }

            // 2. All Players
            int blueGold = 0;
            int redGold = 0;

            if (root.TryGetProperty("allPlayers", out var allPlayers) && allPlayers.ValueKind == JsonValueKind.Array)
            {
                foreach (var player in allPlayers.EnumerateArray())
                {
                    var team = player.TryGetProperty("team", out var t) ? t.GetString() : "ORDER";
                    var isBlue = team == "ORDER";

                    if (player.TryGetProperty("championName", out var cn))
                    {
                        var champ = cn.GetString() ?? string.Empty;
                        if (isBlue) stats.BlueChampions.Add(champ);
                        else stats.RedChampions.Add(champ);
                    }

                    if (player.TryGetProperty("scores", out var scores))
                    {
                        var kills = scores.TryGetProperty("kills", out var k) ? k.GetInt32() : 0;
                        var cs = scores.TryGetProperty("creepScore", out var c) ? c.GetInt32() : 0;

                        if (isBlue)
                        {
                            stats.BlueKills += kills;
                            stats.BlueCs += cs;
                        }
                        else
                        {
                            stats.RedKills += kills;
                            stats.RedCs += cs;
                        }
                    }

                    // Estimate items gold
                    if (player.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        int pGold = 0;
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("price", out var price))
                            {
                                pGold += price.GetInt32();
                            }
                        }

                        if (isBlue) blueGold += pGold;
                        else redGold += pGold;
                    }
                }
            }

            // 3. Events
            if (root.TryGetProperty("events", out var eventsWrapper) &&
                eventsWrapper.TryGetProperty("Events", out var events) &&
                events.ValueKind == JsonValueKind.Array)
            {
                bool firstBloodFound = false;
                bool firstTowerFound = false;
                bool firstDragonFound = false;

                foreach (var ev in events.EnumerateArray())
                {
                    var eventName = ev.TryGetProperty("EventName", out var en) ? en.GetString() : string.Empty;

                    if (eventName == "FirstBlood" && !firstBloodFound)
                    {
                        firstBloodFound = true;
                        // Determine recipient team if possible
                        var killer = ev.TryGetProperty("KillerName", out var kn) ? kn.GetString() : string.Empty;
                        stats.BlueFirstBlood = stats.BlueChampions.Contains(killer ?? string.Empty);
                    }
                    else if (eventName == "TurretKilled")
                    {
                        var killer = ev.TryGetProperty("KillerName", out var kn) ? kn.GetString() : string.Empty;
                        var isBlue = stats.BlueChampions.Contains(killer ?? string.Empty);
                        if (isBlue) stats.BlueTowers++;
                        else stats.RedTowers++;

                        if (!firstTowerFound)
                        {
                            firstTowerFound = true;
                            stats.BlueFirstTower = isBlue;
                        }
                    }
                    else if (eventName == "DragonKill")
                    {
                        var killer = ev.TryGetProperty("KillerName", out var kn) ? kn.GetString() : string.Empty;
                        var isBlue = stats.BlueChampions.Contains(killer ?? string.Empty);
                        if (isBlue) stats.BlueDragons++;
                        else stats.RedDragons++;

                        if (!firstDragonFound)
                        {
                            firstDragonFound = true;
                            stats.BlueFirstDragon = isBlue;
                        }
                    }
                    else if (eventName == "HeraldKill")
                    {
                        var killer = ev.TryGetProperty("KillerName", out var kn) ? kn.GetString() : string.Empty;
                        if (stats.BlueChampions.Contains(killer ?? string.Empty)) stats.BlueHeralds++;
                        else stats.RedHeralds++;
                    }
                    else if (eventName == "HordeKill") // Voidgrub
                    {
                        var killer = ev.TryGetProperty("KillerName", out var kn) ? kn.GetString() : string.Empty;
                        if (stats.BlueChampions.Contains(killer ?? string.Empty)) stats.BlueVoidgrubs++;
                        else stats.RedVoidgrubs++;
                    }
                }
            }

            // Refine gold diff estimation: include kills and CS
            var killDiffGold = stats.KillDiff * 300;
            var csDiffGold = stats.CsDiff * 21;
            stats.EstimatedGoldDiff = (blueGold - redGold) + killDiffGold + csDiffGold;
            stats.EstimatedXpDiff = stats.CsDiff * 60 + stats.KillDiff * 400;

            stats.StatusMessage = $"Активний матч виявлено! Час: {stats.FormattedGameTime}, Режим: {stats.GameMode}";
            return stats;
        }
        catch (Exception ex)
        {
            stats.StatusMessage = $"Не вдалося з'єднатися з клієнтом LoL (гра не запущена): {ex.Message}";
            return stats;
        }
    }
}
