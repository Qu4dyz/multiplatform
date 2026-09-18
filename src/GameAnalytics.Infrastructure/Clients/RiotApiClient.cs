using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Infrastructure.Configuration;
using GameAnalytics.Infrastructure.Services;

namespace GameAnalytics.Infrastructure.Clients;

public class RiotApiClient : IRiotApiClient
{
    private readonly HttpClient _httpClient;
    private readonly RiotApiOptions _options;
    private readonly RiotRateLimiter _rateLimiter;

    public RiotApiClient(HttpClient httpClient, RiotApiOptions options, RiotRateLimiter? rateLimiter = null)
    {
        _httpClient = httpClient;
        _options = options;
        _rateLimiter = rateLimiter ?? new RiotRateLimiter();
    }

    private HttpRequestMessage CreateAuthenticatedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Riot-Token", _options.ApiKey);
        request.Headers.Add("User-Agent", "GameAnalytics/1.0 (Windows NT 10.0; Win64; x64)");
        return request;
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(string url, CancellationToken ct = default)
    {
        var region = ExtractRegionFromUrl(url);

        const int maxRetries = 3;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Wait for rate-limit slot specifically for this routing region (20/1s, 100/120s)
            await _rateLimiter.WaitForSlotAsync(region, ct);

            var request = CreateAuthenticatedRequest(url);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                await Task.Delay(500 * (attempt + 1), ct);
                continue;
            }

            // Handle HTTP 429 Too Many Requests
            if (response.StatusCode == (HttpStatusCode)429)
            {
                var retryAfter = TimeSpan.FromSeconds(2);
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retryAfter = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        if (diff > TimeSpan.Zero) retryAfter = diff;
                    }
                }
                else if (response.Headers.TryGetValues("Retry-After", out var values))
                {
                    var raw = values.FirstOrDefault();
                    if (int.TryParse(raw, out var sec))
                    {
                        retryAfter = TimeSpan.FromSeconds(sec);
                    }
                }

                // Notify the per-region rate limiter so other calls pause
                _rateLimiter.NotifyRateLimitHit(region, retryAfter);
                response.Dispose();

                if (attempt < maxRetries)
                {
                    await Task.Delay(retryAfter + TimeSpan.FromMilliseconds(250), ct);
                    continue;
                }

                return null;
            }

            // Retry on transient server errors (500, 502, 503, 504)
            if ((int)response.StatusCode >= 500 && attempt < maxRetries)
            {
                response.Dispose();
                await Task.Delay(1000 * (attempt + 1), ct);
                continue;
            }

            return response;
        }

        return null;
    }

    private static string ExtractRegionFromUrl(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            var dotIndex = host.IndexOf('.');
            if (dotIndex > 0)
            {
                return host[..dotIndex].ToLowerInvariant();
            }
        }
        catch { }
        return "global";
    }

    public async Task<SummonerProfile?> GetSummonerByRiotIdAsync(string gameName, string tagLine, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GetMockProfile(gameName, tagLine);
        }

        try
        {
            // Step 1: Query Account-v1 by Riot ID (GameName + TagLine)
            var accountUrl = $"https://{_options.RoutingRegion}.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";
            using var accountResp = await SendWithRetryAsync(accountUrl, ct);

            if (accountResp == null || accountResp.StatusCode == HttpStatusCode.NotFound)
            {
                // Account not found on Riot servers
                return null;
            }

            if (!accountResp.IsSuccessStatusCode)
            {
                if (_options.UseMockFallback) return GetMockProfile(gameName, tagLine);
                return null;
            }

            var account = await accountResp.Content.ReadFromJsonAsync<RiotAccountDto>(cancellationToken: ct);
            if (account == null) return _options.UseMockFallback ? GetMockProfile(gameName, tagLine) : null;

            // Step 2: Query Summoner-v4 by PUUID
            var summonerUrl = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/{account.Puuid}";
            using var summonerResp = await SendWithRetryAsync(summonerUrl, ct);

            var summoner = (summonerResp != null && summonerResp.IsSuccessStatusCode)
                ? await summonerResp.Content.ReadFromJsonAsync<RiotSummonerDto>(cancellationToken: ct)
                : null;

            var profile = new SummonerProfile
            {
                Puuid = account.Puuid,
                GameName = account.GameName,
                TagLine = account.TagLine,
                SummonerLevel = summoner?.SummonerLevel ?? 100,
                ProfileIconId = summoner?.ProfileIconId ?? 1,
                Tier = GameTier.Unranked,
                Rank = "",
                LeaguePoints = 0,
                Wins = 0,
                Losses = 0
            };

            // Step 3: Query League-v4 Ranked Entries (Try modern by-puuid, fallback to legacy by-summoner)
            var leaguePuuidUrl = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/league/v4/entries/by-puuid/{account.Puuid}";
            using var leaguePuuidResp = await SendWithRetryAsync(leaguePuuidUrl, ct);

            List<RiotLeagueEntryDto>? entries = null;
            if (leaguePuuidResp != null && leaguePuuidResp.IsSuccessStatusCode)
            {
                entries = await leaguePuuidResp.Content.ReadFromJsonAsync<List<RiotLeagueEntryDto>>(cancellationToken: ct);
            }
            else if (summoner != null && !string.IsNullOrWhiteSpace(summoner.Id))
            {
                var legacyUrl = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/league/v4/entries/by-summoner/{summoner.Id}";
                using var legacyResp = await SendWithRetryAsync(legacyUrl, ct);
                if (legacyResp != null && legacyResp.IsSuccessStatusCode)
                {
                    entries = await legacyResp.Content.ReadFromJsonAsync<List<RiotLeagueEntryDto>>(cancellationToken: ct);
                }
            }

            if (entries != null && entries.Count > 0)
            {
                var soloEntry = entries.FirstOrDefault(e => e.QueueType == "RANKED_SOLO_5x5") ??
                                entries.FirstOrDefault(e => e.QueueType == "RANKED_FLEX_SR");

                if (soloEntry != null)
                {
                    profile.Tier = ParseTier(soloEntry.Tier);
                    profile.Rank = soloEntry.Rank ?? "IV";
                    profile.LeaguePoints = soloEntry.LeaguePoints;
                    profile.Wins = soloEntry.Wins;
                    profile.Losses = soloEntry.Losses;
                }
            }

            return profile;
        }
        catch
        {
            return _options.UseMockFallback ? GetMockProfile(gameName, tagLine) : null;
        }
    }

    public async Task<IReadOnlyList<string>> GetRecentMatchIdsByPuuidAsync(string puuid, int count = 10, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GenerateMockMatchIds(puuid, count);
        }

        try
        {
            var url = $"https://{_options.RoutingRegion}.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?count={count}";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return _options.UseMockFallback ? GenerateMockMatchIds(puuid, count) : Array.Empty<string>();
            }

            var matchIds = await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken: ct);
            return matchIds ?? (_options.UseMockFallback ? GenerateMockMatchIds(puuid, count) : Array.Empty<string>());
        }
        catch
        {
            return _options.UseMockFallback ? GenerateMockMatchIds(puuid, count) : Array.Empty<string>();
        }
    }

    public async Task<Match?> GetMatchDetailsAsync(string matchId, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GenerateMockMatch(matchId);
        }

        try
        {
            var url = $"https://{_options.RoutingRegion}.api.riotgames.com/lol/match/v5/matches/{matchId}";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return _options.UseMockFallback ? GenerateMockMatch(matchId) : null;
            }

            var matchDto = await response.Content.ReadFromJsonAsync<RiotMatchDto>(cancellationToken: ct);
            if (matchDto?.Info == null)
            {
                return _options.UseMockFallback ? GenerateMockMatch(matchId) : null;
            }

            return MapMatchDtoToDomain(matchId, matchDto.Info);
        }
        catch
        {
            return _options.UseMockFallback ? GenerateMockMatch(matchId) : null;
        }
    }

    public async Task<MatchTimelineData?> GetMatchTimelineAsync(string matchId, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GenerateMockTimeline(matchId);
        }

        try
        {
            var url = $"https://{_options.RoutingRegion}.api.riotgames.com/lol/match/v5/matches/{matchId}/timeline";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return _options.UseMockFallback ? GenerateMockTimeline(matchId) : null;
            }

            var timelineDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct);
            if (timelineDoc == null) return _options.UseMockFallback ? GenerateMockTimeline(matchId) : null;

            return ParseRealTimeline(matchId, timelineDoc.RootElement);
        }
        catch
        {
            return _options.UseMockFallback ? GenerateMockTimeline(matchId) : null;
        }
    }

    private Match MapMatchDtoToDomain(string matchId, RiotMatchInfoDto info)
    {
        var blueTeamDto = info.Teams?.FirstOrDefault(t => t.TeamId == 100);
        var redTeamDto = info.Teams?.FirstOrDefault(t => t.TeamId == 200);
        var blueWin = blueTeamDto?.Win ?? false;

        var isRemake = (info.GameDuration > 0 && info.GameDuration < 300) ||
                       (info.Participants?.Any(p => p.GameEndedInEarlySurrender || p.TeamEarlySurrendered) ?? false);

        var match = new Match
        {
            MatchId = matchId,
            QueueId = info.QueueId,
            IsRemake = isRemake,
            GameCreation = DateTimeOffset.FromUnixTimeMilliseconds(info.GameCreation).UtcDateTime,
            GameDurationSeconds = info.GameDuration,
            GameVersion = info.GameVersion ?? "14.18.1",
            WinningTeam = blueWin ? TeamSide.Blue : TeamSide.Red
        };

        // Teams
        match.Teams.Add(new TeamStats
        {
            TeamSide = TeamSide.Blue,
            Win = blueWin,
            FirstBlood = blueTeamDto?.Objectives?.Champion?.First ?? false,
            FirstTower = blueTeamDto?.Objectives?.Tower?.First ?? false,
            FirstDragon = blueTeamDto?.Objectives?.Dragon?.First ?? false,
            FirstBaron = blueTeamDto?.Objectives?.Baron?.First ?? false,
            TowerKills = blueTeamDto?.Objectives?.Tower?.Kills ?? 0,
            DragonKills = blueTeamDto?.Objectives?.Dragon?.Kills ?? 0,
            BaronKills = blueTeamDto?.Objectives?.Baron?.Kills ?? 0,
            GoldAt15 = 25000,
            KillsAt15 = blueTeamDto?.Objectives?.Champion?.Kills ?? 10
        });

        match.Teams.Add(new TeamStats
        {
            TeamSide = TeamSide.Red,
            Win = !blueWin,
            FirstBlood = redTeamDto?.Objectives?.Champion?.First ?? false,
            FirstTower = redTeamDto?.Objectives?.Tower?.First ?? false,
            FirstDragon = redTeamDto?.Objectives?.Dragon?.First ?? false,
            FirstBaron = redTeamDto?.Objectives?.Baron?.First ?? false,
            TowerKills = redTeamDto?.Objectives?.Tower?.Kills ?? 0,
            DragonKills = redTeamDto?.Objectives?.Dragon?.Kills ?? 0,
            BaronKills = redTeamDto?.Objectives?.Baron?.Kills ?? 0,
            GoldAt15 = 25000,
            KillsAt15 = redTeamDto?.Objectives?.Champion?.Kills ?? 10
        });

        // Participants
        if (info.Participants != null)
        {
            foreach (var p in info.Participants)
            {
                var side = p.TeamId == 100 ? TeamSide.Blue : TeamSide.Red;
                match.Participants.Add(new Participant
                {
                    Puuid = p.Puuid ?? string.Empty,
                    SummonerName = !string.IsNullOrWhiteSpace(p.RiotIdGameName)
                        ? $"{p.RiotIdGameName}#{p.RiotIdTagline}"
                        : (p.SummonerName ?? "Player"),
                    ChampionName = p.ChampionName ?? "Champion",
                    ChampionId = p.ChampionId,
                    ParticipantId = p.ParticipantId > 0 ? p.ParticipantId : (info.Participants.IndexOf(p) + 1),
                    ChampLevel = p.ChampLevel > 0 ? p.ChampLevel : 1,
                    Position = ParsePosition(p.TeamPosition),
                    TeamSide = side,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    TotalDamageDealtToChampions = p.TotalDamageDealtToChampions,
                    GoldEarned = p.GoldEarned,
                    TotalMinionsKilled = p.TotalMinionsKilled + p.NeutralMinionsKilled,
                    Win = p.Win,
                    Item0 = p.Item0,
                    Item1 = p.Item1,
                    Item2 = p.Item2,
                    Item3 = p.Item3,
                    Item4 = p.Item4,
                    Item5 = p.Item5,
                    Item6 = p.Item6
                });
            }
        }

        return match;
    }

    private MatchTimelineData ParseRealTimeline(string matchId, JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("info", out var infoElem) && infoElem.TryGetProperty("frames", out var framesElem))
            {
                var result = new MatchTimelineData { MatchId = matchId };
                var frameCount = framesElem.GetArrayLength();

                // Frame 15 snapshot for gold
                var targetIndex = Math.Min(15, frameCount - 1);
                var frame15 = framesElem[targetIndex];

                int goldBlue = 0;
                int goldRed = 0;

                if (frame15.TryGetProperty("participantFrames", out var pFrames))
                {
                    for (int i = 1; i <= 10; i++)
                    {
                        if (pFrames.TryGetProperty(i.ToString(), out var pf))
                        {
                            var totalGold = pf.TryGetProperty("totalGold", out var g) ? g.GetInt32() : 0;
                            if (i <= 5) goldBlue += totalGold;
                            else goldRed += totalGold;
                        }
                    }
                }

                result.GoldAt15Blue = goldBlue > 0 ? goldBlue : 24500;
                result.GoldAt15Red = goldRed > 0 ? goldRed : 23800;

                bool firstBloodSet = false;
                bool firstTowerSet = false;
                bool firstDragonSet = false;

                const int fifteenMinMs = 15 * 60 * 1000;

                // Process all frames to collect real events
                for (int fIdx = 0; fIdx < frameCount; fIdx++)
                {
                    var curFrame = framesElem[fIdx];
                    if (!curFrame.TryGetProperty("events", out var eventsElem)) continue;

                    foreach (var ev in eventsElem.EnumerateArray())
                    {
                        if (!ev.TryGetProperty("type", out var typeProp)) continue;
                        var evType = typeProp.GetString() ?? string.Empty;
                        var ts = ev.TryGetProperty("timestamp", out var tsProp) ? tsProp.GetInt32() : 0;

                        if (evType == "CHAMPION_KILL")
                        {
                            var killerId = ev.TryGetProperty("killerId", out var kProp) ? kProp.GetInt32() : 0;
                            var victimId = ev.TryGetProperty("victimId", out var vProp) ? vProp.GetInt32() : 0;
                            var bounty = ev.TryGetProperty("bounty", out var bProp) ? bProp.GetInt32() : 0;

                            var assists = new List<int>();
                            if (ev.TryGetProperty("assistingParticipantIds", out var assistsElem))
                            {
                                foreach (var a in assistsElem.EnumerateArray())
                                {
                                    assists.Add(a.GetInt32());
                                }
                            }

                            result.RealEvents.Add(new TimelineEventRecord
                            {
                                TimestampMs = ts,
                                EventType = evType,
                                KillerId = killerId,
                                VictimId = victimId,
                                AssistingParticipantIds = assists,
                                Bounty = bounty
                            });

                            if (!firstBloodSet && killerId > 0)
                            {
                                result.BlueFirstBlood = killerId <= 5;
                                firstBloodSet = true;
                            }

                            if (ts <= fifteenMinMs)
                            {
                                if (killerId <= 5) result.KillsAt15Blue++;
                                else result.KillsAt15Red++;
                            }
                        }
                        else if (evType == "ELITE_MONSTER_KILL")
                        {
                            var monsterType = ev.TryGetProperty("monsterType", out var mTypeProp) ? mTypeProp.GetString() ?? string.Empty : string.Empty;
                            var monsterSubType = ev.TryGetProperty("monsterSubType", out var mSubProp) ? mSubProp.GetString() ?? string.Empty : string.Empty;
                            var killerId = ev.TryGetProperty("killerId", out var kProp) ? kProp.GetInt32() : 0;
                            var killerTeamId = ev.TryGetProperty("killerTeamId", out var ktProp) ? ktProp.GetInt32() : 0;

                            result.RealEvents.Add(new TimelineEventRecord
                            {
                                TimestampMs = ts,
                                EventType = evType,
                                KillerId = killerId,
                                KillerTeamId = killerTeamId,
                                MonsterType = monsterType,
                                MonsterSubType = monsterSubType
                            });

                            if (monsterType.Equals("DRAGON", StringComparison.OrdinalIgnoreCase))
                            {
                                var isBlueDragon = killerTeamId == 100 || killerId <= 5;
                                if (!firstDragonSet)
                                {
                                    result.BlueFirstDragon = isBlueDragon;
                                    firstDragonSet = true;
                                }

                                if (ts <= fifteenMinMs)
                                {
                                    if (isBlueDragon) result.DragonsAt15Blue++;
                                    else result.DragonsAt15Red++;
                                }
                            }
                        }
                        else if (evType == "BUILDING_KILL")
                        {
                            var buildingType = ev.TryGetProperty("buildingType", out var bTypeProp) ? bTypeProp.GetString() ?? string.Empty : string.Empty;
                            var laneType = ev.TryGetProperty("laneType", out var laneProp) ? laneProp.GetString() ?? string.Empty : string.Empty;
                            var killerId = ev.TryGetProperty("killerId", out var kProp) ? kProp.GetInt32() : 0;
                            var teamId = ev.TryGetProperty("teamId", out var tProp) ? tProp.GetInt32() : 0;

                            result.RealEvents.Add(new TimelineEventRecord
                            {
                                TimestampMs = ts,
                                EventType = evType,
                                KillerId = killerId,
                                BuildingType = buildingType,
                                LaneType = laneType
                            });

                            if (buildingType.Contains("TOWER", StringComparison.OrdinalIgnoreCase))
                            {
                                var blueDestroyed = teamId == 200 || killerId <= 5;
                                if (!firstTowerSet)
                                {
                                    result.BlueFirstTower = blueDestroyed;
                                    firstTowerSet = true;
                                }

                                if (ts <= fifteenMinMs)
                                {
                                    if (blueDestroyed) result.TowersAt15Blue++;
                                    else result.TowersAt15Red++;
                                }
                            }
                        }
                    }
                }

                return result;
            }
        }
        catch
        {
            // Fallback
        }

        return GenerateMockTimeline(matchId);
    }

    private static GameTier ParseTier(string? tierStr)
    {
        if (string.IsNullOrWhiteSpace(tierStr)) return GameTier.Unranked;
        return tierStr.ToUpperInvariant() switch
        {
            "IRON" => GameTier.Iron,
            "BRONZE" => GameTier.Bronze,
            "SILVER" => GameTier.Silver,
            "GOLD" => GameTier.Gold,
            "PLATINUM" => GameTier.Platinum,
            "EMERALD" => GameTier.Emerald,
            "DIAMOND" => GameTier.Diamond,
            "MASTER" => GameTier.Master,
            "GRANDMASTER" => GameTier.Grandmaster,
            "CHALLENGER" => GameTier.Challenger,
            _ => GameTier.Silver
        };
    }

    private static Position ParsePosition(string? posStr)
    {
        if (string.IsNullOrWhiteSpace(posStr)) return Position.Middle;
        return posStr.ToUpperInvariant() switch
        {
            "TOP" => Position.Top,
            "JUNGLE" => Position.Jungle,
            "MIDDLE" or "MID" => Position.Middle,
            "BOTTOM" or "BOT" => Position.Bottom,
            "UTILITY" or "SUPPORT" => Position.Utility,
            _ => Position.Middle
        };
    }

    private SummonerProfile GetMockProfile(string gameName, string tagLine)
    {
        var cleanName = string.IsNullOrWhiteSpace(gameName) ? "Qu4dyz" : gameName.Trim();
        var cleanTag = string.IsNullOrWhiteSpace(tagLine) ? "EUW" : tagLine.Trim();

        var tier = _options.DemoTier;
        var rank = string.IsNullOrWhiteSpace(_options.DemoRank) ? "II" : _options.DemoRank;
        var lp = _options.DemoLp > 0 ? _options.DemoLp : 48;

        return new SummonerProfile
        {
            Puuid = $"puuid-{cleanName.ToLowerInvariant()}-{cleanTag.ToLowerInvariant()}-001",
            GameName = cleanName,
            TagLine = cleanTag,
            SummonerLevel = 184,
            ProfileIconId = 538,
            Tier = tier,
            Rank = rank,
            LeaguePoints = lp,
            Wins = 58,
            Losses = 49
        };
    }

    private IReadOnlyList<string> GenerateMockMatchIds(string puuid, int count)
    {
        var list = new List<string>();
        for (int i = 1; i <= count; i++)
        {
            list.Add($"EUW1_{6890000000 + i * 137}");
        }
        return list;
    }

    public Match GenerateMockMatch(string matchId)
    {
        var random = new Random(matchId.GetHashCode());
        var duration = random.Next(1350, 2200);
        var blueWin = random.Next(0, 2) == 1;

        var champions = new[]
        {
            ("Aatrox", Position.Top),
            ("Lee Sin", Position.Jungle),
            ("Ahri", Position.Middle),
            ("Jinx", Position.Bottom),
            ("Thresh", Position.Utility),
            ("Ornn", Position.Top),
            ("Sejuani", Position.Jungle),
            ("Syndra", Position.Middle),
            ("Kai'Sa", Position.Bottom),
            ("Nautilus", Position.Utility)
        };

        var match = new Match
        {
            MatchId = matchId,
            GameCreation = DateTime.UtcNow.AddMinutes(-random.Next(100, 10000)),
            GameDurationSeconds = duration,
            GameVersion = GameConstants.DDragonVersion,
            WinningTeam = blueWin ? TeamSide.Blue : TeamSide.Red
        };

        var blueStats = new TeamStats
        {
            TeamSide = TeamSide.Blue,
            Win = blueWin,
            FirstBlood = blueWin || random.Next(0, 2) == 1,
            FirstTower = blueWin,
            FirstDragon = random.Next(0, 2) == 1,
            FirstBaron = blueWin && duration > 1500,
            DragonKills = blueWin ? random.Next(2, 4) : random.Next(0, 2),
            BaronKills = blueWin ? 1 : 0,
            TowerKills = blueWin ? random.Next(7, 11) : random.Next(1, 4),
            GoldAt15 = 25000 + (blueWin ? random.Next(1000, 3500) : -random.Next(500, 2000)),
            KillsAt15 = blueWin ? random.Next(8, 15) : random.Next(3, 8)
        };

        var redStats = new TeamStats
        {
            TeamSide = TeamSide.Red,
            Win = !blueWin,
            FirstBlood = !blueStats.FirstBlood,
            FirstTower = !blueStats.FirstTower,
            FirstDragon = !blueStats.FirstDragon,
            FirstBaron = !blueWin && duration > 1500,
            DragonKills = !blueWin ? random.Next(2, 4) : random.Next(0, 2),
            BaronKills = !blueWin ? 1 : 0,
            TowerKills = !blueWin ? random.Next(7, 11) : random.Next(1, 4),
            GoldAt15 = 50000 - blueStats.GoldAt15,
            KillsAt15 = blueWin ? random.Next(3, 8) : random.Next(8, 15)
        };

        match.Teams.Add(blueStats);
        match.Teams.Add(redStats);

        for (int i = 0; i < 10; i++)
        {
            var isBlue = i < 5;
            var champ = champions[i];
            var kills = random.Next(1, 12);
            var deaths = random.Next(1, 9);
            var assists = random.Next(3, 16);

            match.Participants.Add(new Participant
            {
                Puuid = $"puuid-participant-{i}",
                SummonerName = i == 3 ? "Qu4dyz#EUW" : $"Player_{i + 1}",
                ChampionName = champ.Item1,
                ChampionId = 100 + i,
                ParticipantId = i + 1,
                ChampLevel = random.Next(11, 18),
                Position = champ.Item2,
                TeamSide = isBlue ? TeamSide.Blue : TeamSide.Red,
                Kills = kills,
                Deaths = deaths,
                Assists = assists,
                TotalDamageDealtToChampions = random.Next(12000, 38000),
                GoldEarned = random.Next(8000, 17000),
                TotalMinionsKilled = random.Next(120, 290),
                Win = isBlue ? blueWin : !blueWin,
                Item0 = 3078,
                Item1 = 3006,
                Item2 = 3031,
                Item3 = 3072,
                Item4 = 3026,
                Item5 = 3153,
                Item6 = 3340
            });
        }

        return match;
    }

    private MatchTimelineData GenerateMockTimeline(string matchId)
    {
        var random = new Random(matchId.GetHashCode());
        var blueFavored = random.Next(0, 2) == 1;

        var goldDiff = blueFavored ? random.Next(1200, 4200) : -random.Next(1000, 3800);
        var killDiff = blueFavored ? random.Next(2, 7) : -random.Next(2, 6);

        var goldBlue = 24000 + (goldDiff > 0 ? goldDiff : 0);
        var goldRed = 24000 + (goldDiff < 0 ? -goldDiff : 0);

        var killsBlue = 8 + (killDiff > 0 ? killDiff : 0);
        var killsRed = 8 + (killDiff < 0 ? -killDiff : 0);

        return new MatchTimelineData
        {
            MatchId = matchId,
            GoldAt15Blue = goldBlue,
            GoldAt15Red = goldRed,
            KillsAt15Blue = killsBlue,
            KillsAt15Red = killsRed,
            TowersAt15Blue = blueFavored ? random.Next(1, 3) : 0,
            TowersAt15Red = !blueFavored ? random.Next(1, 3) : 0,
            DragonsAt15Blue = blueFavored ? random.Next(1, 3) : 0,
            DragonsAt15Red = !blueFavored ? random.Next(1, 2) : 0,
            BlueFirstBlood = blueFavored,
            BlueFirstTower = blueFavored,
            BlueFirstDragon = blueFavored || random.Next(0, 2) == 1
        };
    }

    private record RiotAccountDto(
        [property: JsonPropertyName("puuid")] string Puuid,
        [property: JsonPropertyName("gameName")] string GameName,
        [property: JsonPropertyName("tagLine")] string TagLine
    );

    private record RiotSummonerDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("accountId")] string? AccountId,
        [property: JsonPropertyName("puuid")] string Puuid,
        [property: JsonPropertyName("profileIconId")] int ProfileIconId,
        [property: JsonPropertyName("summonerLevel")] int SummonerLevel
    );

    private record RiotLeagueEntryDto(
        [property: JsonPropertyName("queueType")] string QueueType,
        [property: JsonPropertyName("tier")] string Tier,
        [property: JsonPropertyName("rank")] string Rank,
        [property: JsonPropertyName("leaguePoints")] int LeaguePoints,
        [property: JsonPropertyName("wins")] int Wins,
        [property: JsonPropertyName("losses")] int Losses
    );

    private class RiotMatchDto
    {
        [JsonPropertyName("info")]
        public RiotMatchInfoDto? Info { get; set; }
    }

    private class RiotMatchInfoDto
    {
        [JsonPropertyName("gameCreation")]
        public long GameCreation { get; set; }

        [JsonPropertyName("gameDuration")]
        public int GameDuration { get; set; }

        [JsonPropertyName("gameVersion")]
        public string? GameVersion { get; set; }

        [JsonPropertyName("queueId")]
        public int QueueId { get; set; }

        [JsonPropertyName("teams")]
        public List<RiotTeamDto>? Teams { get; set; }

        [JsonPropertyName("participants")]
        public List<RiotParticipantDto>? Participants { get; set; }
    }

    private class RiotTeamDto
    {
        [JsonPropertyName("teamId")]
        public int TeamId { get; set; }

        [JsonPropertyName("win")]
        public bool Win { get; set; }

        [JsonPropertyName("objectives")]
        public RiotObjectivesDto? Objectives { get; set; }
    }

    private class RiotObjectivesDto
    {
        [JsonPropertyName("baron")]
        public RiotObjectiveDetailDto? Baron { get; set; }

        [JsonPropertyName("champion")]
        public RiotObjectiveDetailDto? Champion { get; set; }

        [JsonPropertyName("dragon")]
        public RiotObjectiveDetailDto? Dragon { get; set; }

        [JsonPropertyName("tower")]
        public RiotObjectiveDetailDto? Tower { get; set; }
    }

    private class RiotObjectiveDetailDto
    {
        [JsonPropertyName("first")]
        public bool First { get; set; }

        [JsonPropertyName("kills")]
        public int Kills { get; set; }
    }

    private class RiotParticipantDto
    {
        [JsonPropertyName("puuid")]
        public string? Puuid { get; set; }

        [JsonPropertyName("summonerName")]
        public string? SummonerName { get; set; }

        [JsonPropertyName("riotIdGameName")]
        public string? RiotIdGameName { get; set; }

        [JsonPropertyName("riotIdTagline")]
        public string? RiotIdTagline { get; set; }

        [JsonPropertyName("championId")]
        public int ChampionId { get; set; }

        [JsonPropertyName("participantId")]
        public int ParticipantId { get; set; }

        [JsonPropertyName("championName")]
        public string? ChampionName { get; set; }

        [JsonPropertyName("champLevel")]
        public int ChampLevel { get; set; }

        [JsonPropertyName("teamId")]
        public int TeamId { get; set; }

        [JsonPropertyName("teamPosition")]
        public string? TeamPosition { get; set; }

        [JsonPropertyName("kills")]
        public int Kills { get; set; }

        [JsonPropertyName("deaths")]
        public int Deaths { get; set; }

        [JsonPropertyName("assists")]
        public int Assists { get; set; }

        [JsonPropertyName("totalDamageDealtToChampions")]
        public int TotalDamageDealtToChampions { get; set; }

        [JsonPropertyName("goldEarned")]
        public int GoldEarned { get; set; }

        [JsonPropertyName("totalMinionsKilled")]
        public int TotalMinionsKilled { get; set; }

        [JsonPropertyName("neutralMinionsKilled")]
        public int NeutralMinionsKilled { get; set; }

        [JsonPropertyName("win")]
        public bool Win { get; set; }

        [JsonPropertyName("gameEndedInEarlySurrender")]
        public bool GameEndedInEarlySurrender { get; set; }

        [JsonPropertyName("teamEarlySurrendered")]
        public bool TeamEarlySurrendered { get; set; }

        [JsonPropertyName("item0")]
        public int Item0 { get; set; }

        [JsonPropertyName("item1")]
        public int Item1 { get; set; }

        [JsonPropertyName("item2")]
        public int Item2 { get; set; }

        [JsonPropertyName("item3")]
        public int Item3 { get; set; }

        [JsonPropertyName("item4")]
        public int Item4 { get; set; }

        [JsonPropertyName("item5")]
        public int Item5 { get; set; }

        [JsonPropertyName("item6")]
        public int Item6 { get; set; }
    }
}
