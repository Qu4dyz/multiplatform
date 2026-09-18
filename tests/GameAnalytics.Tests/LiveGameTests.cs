using System.Net;
using System.Text.Json;
using GameAnalytics.Core.Entities;
using GameAnalytics.Infrastructure.Services;
using Xunit;

namespace GameAnalytics.Tests;

public class LiveGameTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetLiveGameStatsAsync_WhenGameRunning_ParsesStatsCorrectly()
    {
        var sampleJson = """
        {
            "gameData": {
                "gameTime": 920.5,
                "gameMode": "CLASSIC"
            },
            "allPlayers": [
                {
                    "championName": "Aatrox",
                    "team": "ORDER",
                    "scores": { "kills": 3, "creepScore": 120 },
                    "items": [ { "price": 1100 }, { "price": 3300 } ]
                },
                {
                    "championName": "Ornn",
                    "team": "CHAOS",
                    "scores": { "kills": 1, "creepScore": 95 },
                    "items": [ { "price": 1000 }, { "price": 2800 } ]
                }
            ],
            "events": {
                "Events": [
                    { "EventName": "FirstBlood", "KillerName": "Aatrox" },
                    { "EventName": "TurretKilled", "KillerName": "Aatrox" },
                    { "EventName": "DragonKill", "KillerName": "Aatrox" },
                    { "EventName": "HordeKill", "KillerName": "Aatrox" }
                ]
            }
        }
        """;

        var handler = new MockHttpMessageHandler(sampleJson);
        var httpClient = new HttpClient(handler);
        var service = new LiveGameService(httpClient);

        var result = await service.GetLiveGameStatsAsync();

        Assert.True(result.IsActiveGame);
        Assert.Equal(920.5, result.GameTimeSeconds);
        Assert.Equal("15:20", result.FormattedGameTime);
        Assert.Equal(3, result.BlueKills);
        Assert.Equal(1, result.RedKills);
        Assert.Equal(2, result.KillDiff);
        Assert.Equal(120, result.BlueCs);
        Assert.Equal(95, result.RedCs);
        Assert.Equal(25, result.CsDiff);
        Assert.True(result.BlueFirstBlood);
        Assert.True(result.BlueFirstTower);
        Assert.True(result.BlueFirstDragon);
        Assert.Equal(1, result.BlueVoidgrubs);
        Assert.Contains("Aatrox", result.BlueChampions);
        Assert.Contains("Ornn", result.RedChampions);
    }

    [Fact]
    public async Task GetLiveGameStatsAsync_WhenClientOffline_ReturnsInactiveGameGracefully()
    {
        var handler = new MockHttpMessageHandler("", HttpStatusCode.NotFound);
        var httpClient = new HttpClient(handler);
        var service = new LiveGameService(httpClient);

        var result = await service.GetLiveGameStatsAsync();

        Assert.False(result.IsActiveGame);
        Assert.NotEmpty(result.StatusMessage);
    }
}
