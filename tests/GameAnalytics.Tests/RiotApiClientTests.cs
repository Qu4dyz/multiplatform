using System.Net;
using System.Text;
using System.Text.Json;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Exceptions;
using GameAnalytics.Infrastructure.Clients;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Services;
using Xunit;

namespace GameAnalytics.Tests;

/// <summary>RiotApiClient against canned Riot-shaped JSON (no network).</summary>
public class RiotApiClientTests
{
    private const string Puuid = "puuid-test-0001";

    internal static RiotApiClient CreateClient(Func<string, HttpResponseMessage> respond, bool mockFallback = false)
    {
        var http = new HttpClient(new RouteHandler(respond)) { Timeout = TimeSpan.FromSeconds(10) };
        var options = new RiotApiOptions
        {
            ApiKey = "RGAPI-test-key-not-a-real-key-xxxxxx",
            PlatformRegion = "euw1",
            RoutingRegion = "europe",
            UseMockFallback = mockFallback,
            MaxRateLimitBlockSeconds = 5
        };
        return new RiotApiClient(http, options, new RiotRateLimiter());
    }

    internal static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    internal static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("") };

    [Fact]
    public async Task GetSummonerByRiotId_MapsAccountSummonerRankAndMasteries()
    {
        var client = CreateClient(url => url switch
        {
            _ when url.Contains("/riot/account/v1/accounts/by-riot-id/") =>
                Json(new { puuid = Puuid, gameName = "Qu4dyz", tagLine = "qu4" }),
            _ when url.Contains("/lol/summoner/v4/summoners/by-puuid/") =>
                Json(new { id = "sum-1", accountId = "acc-1", puuid = Puuid, profileIconId = 4567, summonerLevel = 321 }),
            _ when url.Contains("/lol/league/v4/entries/by-puuid/") => Json(new[]
            {
                new { queueType = "RANKED_FLEX_SR", tier = "SILVER", rank = "I", leaguePoints = 10, wins = 3, losses = 4 },
                new { queueType = "RANKED_SOLO_5x5", tier = "EMERALD", rank = "II", leaguePoints = 57, wins = 40, losses = 35 }
            }),
            _ when url.Contains("/champion-mastery/v4/") => Json(new[]
            {
                new { puuid = Puuid, championId = 222L, championLevel = 7, championPoints = 250000, lastPlayTime = 1700000000000L },
                new { puuid = Puuid, championId = 103L, championLevel = 5, championPoints = 90000, lastPlayTime = 1700000000000L }
            }),
            _ => Status(HttpStatusCode.NotFound)
        });

        var profile = await client.GetSummonerByRiotIdAsync("Qu4dyz", "qu4");

        Assert.NotNull(profile);
        Assert.Equal(Puuid, profile!.Puuid);
        Assert.Equal(321, profile.SummonerLevel);
        Assert.Equal(4567, profile.ProfileIconId);
        Assert.Equal(GameTier.Emerald, profile.Tier);
        Assert.Equal("II", profile.Rank);
        Assert.Equal(57, profile.LeaguePoints);
        Assert.Equal(2, profile.TopMasteries.Count);
    }

    [Fact]
    public async Task GetSummonerByRiotId_UnknownAccount_ReturnsNull()
    {
        var client = CreateClient(_ => Status(HttpStatusCode.NotFound));
        Assert.Null(await client.GetSummonerByRiotIdAsync("Nobody", "none"));
    }

    [Fact]
    public async Task GetSummonerByRiotId_RejectedKey_ThrowsWithStatus()
    {
        var client = CreateClient(_ => Status(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<RiotApiException>(() => client.GetSummonerByRiotIdAsync("Qu4dyz", "qu4"));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task GetRecentMatchIds_ReturnsIdsFromApi()
    {
        var client = CreateClient(url => url.Contains("/ids")
            ? Json(new[] { "EUW1_1", "EUW1_2", "EUW1_3" })
            : Status(HttpStatusCode.NotFound));

        var ids = await client.GetRecentMatchIdsByPuuidAsync(Puuid, count: 3, queue: 420);

        Assert.Equal(new[] { "EUW1_1", "EUW1_2", "EUW1_3" }, ids);
    }

    [Fact]
    public async Task GetMatchDetails_MapsTeamsParticipantsAndChallenges()
    {
        var client = CreateClient(url => url.EndsWith("/matches/EUW1_42")
            ? Json(MatchJson())
            : Status(HttpStatusCode.NotFound));

        var match = await client.GetMatchDetailsAsync("EUW1_42");

        Assert.NotNull(match);
        Assert.Equal("EUW1_42", match!.MatchId);
        Assert.Equal(420, match.QueueId);
        Assert.Equal(1800, match.GameDurationSeconds);
        Assert.False(match.IsRemake);
        Assert.Equal(TeamSide.Blue, match.WinningTeam);
        Assert.Equal(10, match.Participants.Count);
        Assert.Equal(2, match.Teams.Count);

        var mid = match.Participants.Single(p => p.ParticipantId == 3);
        Assert.Equal(Position.Middle, mid.Position);
        Assert.Equal("Ahri", mid.ChampionName);
        Assert.Equal(3, mid.SoloKills);
        Assert.True(mid.KillParticipation > 0.5f);
        Assert.Equal(3031, mid.Item0);
    }

    [Fact]
    public async Task GetMatchDetails_ShortGame_IsRemake()
    {
        var json = MatchJson(durationSeconds: 200);
        var client = CreateClient(_ => Json(json));

        var match = await client.GetMatchDetailsAsync("EUW1_42");

        Assert.True(match!.IsRemake);
    }

    [Fact]
    public async Task GetMatchDetails_ServerError_WithoutFallback_ReturnsNull()
    {
        var client = CreateClient(_ => Status(HttpStatusCode.InternalServerError));
        Assert.Null(await client.GetMatchDetailsAsync("EUW1_42"));
    }

    [Fact]
    public async Task GetMatchDetails_ServerError_WithFallback_ReturnsMockMatch()
    {
        var client = CreateClient(_ => Status(HttpStatusCode.InternalServerError), mockFallback: true);

        var match = await client.GetMatchDetailsAsync("EUW1_42");

        Assert.NotNull(match);
        Assert.Equal(10, match!.Participants.Count);
    }

    [Fact]
    public async Task GetMatchTimeline_ParsesFramesGoldAndEvents()
    {
        var client = CreateClient(url => url.EndsWith("/timeline")
            ? Json(TimelineJson())
            : Status(HttpStatusCode.NotFound));

        var timeline = await client.GetMatchTimelineAsync("EUW1_42");

        Assert.NotNull(timeline);
        Assert.Equal("EUW1_42", timeline!.MatchId);
        // 5 players x (1000 + 400 * minute) gold at minute 15, blue 10% ahead.
        Assert.Equal(5 * (1100 + 440 * 15), timeline.GoldAt15Blue);
        Assert.Equal(5 * (1000 + 400 * 15), timeline.GoldAt15Red);
        Assert.True(timeline.Frames.Count >= 15);
        Assert.True(timeline.HasRealTimelineEvents);
    }

    [Fact]
    public async Task GetMatchTimeline_BrokenJson_WithoutFallback_ReturnsNull()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json")
        });

        Assert.Null(await client.GetMatchTimelineAsync("EUW1_42"));
    }

    [Fact]
    public async Task HighEloLadder_ReturnsPlayersFromLeagueList()
    {
        var client = CreateClient(url => url.Contains("challengerleagues")
            ? Json(new
            {
                tier = "CHALLENGER",
                queue = "RANKED_SOLO_5x5",
                entries = new[]
                {
                    new { summonerId = "s1", puuid = "c-1", leaguePoints = 1500, rank = "I", wins = 300, losses = 200 },
                    new { summonerId = "s2", puuid = "c-2", leaguePoints = 1400, rank = "I", wins = 250, losses = 210 }
                }
            })
            : Json(new { tier = "GRANDMASTER", queue = "RANKED_SOLO_5x5", entries = Array.Empty<object>() }));

        var puuids = await client.GetChallengerPlayerPuuidsAsync(maxCount: 5);

        Assert.Contains("c-1", puuids);
        Assert.Contains("c-2", puuids);
    }

    [Fact]
    public async Task SoloRankScore_IsHigherForHigherTier()
    {
        var tier = "IRON";
        var client = CreateClient(url => url.Contains("/entries/by-puuid/")
            ? Json(new[] { new { queueType = "RANKED_SOLO_5x5", tier, rank = "II", leaguePoints = 50, wins = 10, losses = 10 } })
            : Status(HttpStatusCode.NotFound));

        var iron = await client.GetSoloRankScoreByPuuidAsync(Puuid);
        tier = "DIAMOND";
        var diamond = await client.GetSoloRankScoreByPuuidAsync(Puuid);

        Assert.True(diamond > iron, $"diamond {diamond} should beat iron {iron}");
    }

    [Fact]
    public async Task WithoutValidKey_ClientServesOfflineMocks()
    {
        var http = new HttpClient(new RouteHandler(_ => throw new InvalidOperationException("network must not be used")));
        var client = new RiotApiClient(http, new RiotApiOptions { ApiKey = "" });

        var profile = await client.GetSummonerByRiotIdAsync("Qu4dyz", "qu4");
        var ids = await client.GetRecentMatchIdsByPuuidAsync(profile!.Puuid, count: 4);
        var match = await client.GetMatchDetailsAsync(ids[0]);
        var timeline = await client.GetMatchTimelineAsync(ids[0]);

        Assert.Equal(4, ids.Count);
        Assert.Equal(10, match!.Participants.Count);
        Assert.NotNull(timeline);
    }

    private static object MatchJson(int durationSeconds = 1800)
    {
        string[] positions = { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };
        string[] champs = { "Garen", "LeeSin", "Ahri", "Jinx", "Thresh", "Darius", "Vi", "Zed", "Caitlyn", "Lulu" };
        var participants = Enumerable.Range(1, 10).Select(i => new Dictionary<string, object?>
        {
            ["puuid"] = $"p-{i}",
            ["riotIdGameName"] = $"Player{i}",
            ["riotIdTagline"] = "EUW",
            ["participantId"] = i,
            ["teamId"] = i <= 5 ? 100 : 200,
            ["teamPosition"] = positions[(i - 1) % 5],
            ["championId"] = 100 + i,
            ["championName"] = champs[i - 1],
            ["champLevel"] = 16,
            ["kills"] = i <= 5 ? 6 : 3,
            ["deaths"] = i <= 5 ? 3 : 6,
            ["assists"] = 7,
            ["win"] = i <= 5,
            ["goldEarned"] = 12000,
            ["totalMinionsKilled"] = 180,
            ["totalDamageDealtToChampions"] = 21000,
            ["visionScore"] = 25,
            ["item0"] = 3031,
            ["item6"] = 3340,
            ["summoner1Id"] = 4,
            ["summoner2Id"] = 14,
            ["perks"] = new
            {
                styles = new object[]
                {
                    new { style = 8100, description = "primaryStyle", selections = new[] { new { perk = 8112 } } },
                    new { style = 8300, description = "subStyle", selections = new[] { new { perk = 8345 } } }
                }
            },
            ["challenges"] = new Dictionary<string, object>
            {
                ["soloKills"] = 3,
                ["killParticipation"] = 0.62,
                ["goldPerMinute"] = 400.5,
                ["visionScorePerMinute"] = 0.8,
                ["turretPlatesTaken"] = 2
            }
        }).ToList();

        object Team(int id, bool win) => new
        {
            teamId = id,
            win,
            objectives = new
            {
                baron = new { first = win, kills = win ? 1 : 0 },
                champion = new { first = win, kills = win ? 30 : 15 },
                dragon = new { first = win, kills = win ? 3 : 1 },
                tower = new { first = win, kills = win ? 8 : 3 },
                horde = new { first = win, kills = win ? 4 : 2 },
                riftHerald = new { first = win, kills = win ? 1 : 0 }
            }
        };

        return new
        {
            info = new
            {
                gameCreation = 1_760_000_000_000L,
                gameDuration = durationSeconds,
                gameVersion = "15.20.1",
                queueId = 420,
                teams = new[] { Team(100, true), Team(200, false) },
                participants
            }
        };
    }

    internal static object TimelineJson(int minutes = 20)
    {
        var frames = Enumerable.Range(0, minutes + 1).Select(minute =>
        {
            var participantFrames = Enumerable.Range(1, 10).ToDictionary(
                i => i.ToString(),
                i => (object)new
                {
                    totalGold = i <= 5 ? 1100 + 440 * minute : 1000 + 400 * minute,
                    minionsKilled = 7 * minute,
                    jungleMinionsKilled = i == 2 || i == 7 ? 5 * minute : 0,
                    xp = 500 * minute,
                    level = Math.Min(18, 1 + minute),
                    position = new { x = 1000 * i, y = 500 * i }
                });

            var events = new List<object>();
            if (minute == 5)
            {
                events.Add(new { type = "CHAMPION_KILL", timestamp = 300_500, killerId = 3, victimId = 8, bounty = 400, assistingParticipantIds = new[] { 2 }, position = new { x = 7000, y = 7000 } });
                events.Add(new { type = "WARD_PLACED", timestamp = 301_000, creatorId = 5, wardType = "CONTROL_WARD" });
            }
            if (minute == 8)
            {
                events.Add(new { type = "ELITE_MONSTER_KILL", timestamp = 480_000, killerId = 2, killerTeamId = 100, monsterType = "DRAGON", monsterSubType = "FIRE_DRAGON" });
                events.Add(new { type = "TURRET_PLATE_DESTROYED", timestamp = 490_000, killerId = 1, teamId = 200, laneType = "TOP_LANE" });
            }
            if (minute == 12)
                events.Add(new { type = "BUILDING_KILL", timestamp = 720_000, killerId = 4, teamId = 200, buildingType = "TOWER_BUILDING", laneType = "BOT_LANE" });

            return new { timestamp = minute * 60_000, participantFrames, events };
        }).ToList();

        return new { info = new { frames } };
    }

    private sealed class RouteHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request.RequestUri!.ToString()));
    }
}
