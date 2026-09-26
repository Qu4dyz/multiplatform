using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
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

            // Step 4: Query Champion Mastery V4 Top Masteries
            try
            {
                var masteries = await GetTopChampionMasteriesAsync(account.Puuid, count: 5, ct: ct);
                if (masteries.Count > 0)
                {
                    profile.TopMasteries = masteries.ToList();
                }
            }
            catch { /* non-critical */ }

            return profile;
        }
        catch
        {
            return _options.UseMockFallback ? GetMockProfile(gameName, tagLine) : null;
        }
    }

    public async Task<IReadOnlyList<ChampionMasteryInfo>> GetTopChampionMasteriesAsync(string puuid, int count = 10, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GenerateMockMasteries();
        }

        try
        {
            var url = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}/top?count={count}";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return _options.UseMockFallback ? GenerateMockMasteries() : Array.Empty<ChampionMasteryInfo>();
            }

            var dtos = await response.Content.ReadFromJsonAsync<List<RiotChampionMasteryDto>>(cancellationToken: ct);
            if (dtos == null) return Array.Empty<ChampionMasteryInfo>();

            var list = new List<ChampionMasteryInfo>();
            foreach (var d in dtos)
            {
                var champName = GameAnalytics.Core.Helpers.ChampionNameHelper.GetChampionNameById((int)d.ChampionId);
                list.Add(new ChampionMasteryInfo
                {
                    ChampionId = d.ChampionId,
                    ChampionName = champName,
                    ChampionLevel = d.ChampionLevel,
                    ChampionPoints = d.ChampionPoints,
                    LastPlayTime = d.LastPlayTime
                });
            }
            return list;
        }
        catch
        {
            return _options.UseMockFallback ? GenerateMockMasteries() : Array.Empty<ChampionMasteryInfo>();
        }
    }

    public async Task<IReadOnlyList<string>> GetChallengerPlayerPuuidsAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default)
    {
        var entries = await GetHighEloLadderPlayersAsync(queue, maxCount, ct);
        return entries.Select(e => e.Puuid).ToList();
    }

    public async Task<IReadOnlyList<LadderPlayerEntry>> GetHighEloLadderPlayersAsync(string queue = "RANKED_SOLO_5x5", int maxCount = 20, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return Array.Empty<LadderPlayerEntry>();
        }

        var result = new List<LadderPlayerEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ladders = new (string Path, GameTier Tier)[]
        {
            ($"challengerleagues/by-queue/{queue}", GameTier.Challenger),
            ($"grandmasterleagues/by-queue/{queue}", GameTier.Grandmaster),
            ($"masterleagues/by-queue/{queue}", GameTier.Master)
        };

        try
        {
            foreach (var (ladderPath, tier) in ladders)
            {
                if (result.Count >= maxCount) break;

                var url = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/league/v4/{ladderPath}";
                using var response = await SendWithRetryAsync(url, ct);
                if (response == null || !response.IsSuccessStatusCode) continue;

                var league = await response.Content.ReadFromJsonAsync<RiotLeagueListDto>(cancellationToken: ct);
                if (league?.Entries == null || league.Entries.Count == 0) continue;

                foreach (var entry in league.Entries.OrderByDescending(e => e.LeaguePoints))
                {
                    if (result.Count >= maxCount) break;

                    string? puuid = null;
                    if (!string.IsNullOrWhiteSpace(entry.Puuid))
                    {
                        puuid = entry.Puuid;
                    }
                    else if (!string.IsNullOrWhiteSpace(entry.SummonerId))
                    {
                        puuid = await ResolvePuuidFromSummonerIdAsync(entry.SummonerId, ct);
                    }

                    if (string.IsNullOrWhiteSpace(puuid)) continue;
                    if (!seen.Add(puuid)) continue;
                    result.Add(new LadderPlayerEntry(puuid, tier));
                }
            }

            return result;
        }
        catch
        {
            return result;
        }
    }

    public async Task<IReadOnlyList<LadderPlayerEntry>> GetDivisionLadderPlayersAsync(
        string queue = "RANKED_SOLO_5x5", int maxCount = 40, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey || maxCount <= 0)
            return Array.Empty<LadderPlayerEntry>();

        // Sample one division page per band so crawl covers Iron→Diamond, not only Master+.
        var targets = new (GameTier Tier, string TierName, string Division)[]
        {
            (GameTier.Iron, "IRON", "IV"),
            (GameTier.Bronze, "BRONZE", "II"),
            (GameTier.Silver, "SILVER", "I"),
            (GameTier.Gold, "GOLD", "II"),
            (GameTier.Platinum, "PLATINUM", "II"),
            (GameTier.Emerald, "EMERALD", "I"),
            (GameTier.Diamond, "DIAMOND", "II"),
        };

        var result = new List<LadderPlayerEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var perTier = Math.Max(3, maxCount / targets.Length);

        try
        {
            foreach (var (tier, tierName, division) in targets)
            {
                if (result.Count >= maxCount) break;

                var url =
                    $"https://{_options.PlatformRegion}.api.riotgames.com/lol/league/v4/entries/{queue}/{tierName}/{division}?page=1";
                using var response = await SendWithRetryAsync(url, ct);
                if (response == null || !response.IsSuccessStatusCode) continue;

                var entries = await response.Content.ReadFromJsonAsync<List<RiotLeagueItemDto>>(cancellationToken: ct);
                if (entries == null || entries.Count == 0) continue;

                var taken = 0;
                foreach (var entry in entries)
                {
                    if (result.Count >= maxCount || taken >= perTier) break;

                    string? puuid = null;
                    if (!string.IsNullOrWhiteSpace(entry.Puuid))
                        puuid = entry.Puuid;
                    else if (!string.IsNullOrWhiteSpace(entry.SummonerId))
                        puuid = await ResolvePuuidFromSummonerIdAsync(entry.SummonerId, ct);

                    if (string.IsNullOrWhiteSpace(puuid) || !seen.Add(puuid)) continue;
                    result.Add(new LadderPlayerEntry(puuid, tier));
                    taken++;
                }
            }

            return result;
        }
        catch
        {
            return result;
        }
    }

    public async Task<float> GetSoloRankScoreByPuuidAsync(string puuid, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey || string.IsNullOrWhiteSpace(puuid))
            return 0f;

        try
        {
            var url = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/league/v4/entries/by-puuid/{puuid}";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode) return 0f;

            var entries = await response.Content.ReadFromJsonAsync<List<RiotLeagueEntryDto>>(cancellationToken: ct);
            var solo = entries?.FirstOrDefault(e =>
                string.Equals(e.QueueType, "RANKED_SOLO_5x5", StringComparison.OrdinalIgnoreCase));
            if (solo == null || string.IsNullOrWhiteSpace(solo.Tier)) return 0f;

            var tier = ParseTier(solo.Tier);
            return tier == GameTier.Unranked ? 0f : RankScoreHelper.FromTier(tier);
        }
        catch
        {
            return 0f;
        }
    }

    private async Task<string?> ResolvePuuidFromSummonerIdAsync(string summonerId, CancellationToken ct)
    {
        try
        {
            var url = $"https://{_options.PlatformRegion}.api.riotgames.com/lol/summoner/v4/summoners/{summonerId}";
            using var response = await SendWithRetryAsync(url, ct);
            if (response == null || !response.IsSuccessStatusCode) return null;

            var summoner = await response.Content.ReadFromJsonAsync<RiotSummonerDto>(cancellationToken: ct);
            return string.IsNullOrWhiteSpace(summoner?.Puuid) ? null : summoner.Puuid;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<ChampionMasteryInfo> GenerateMockMasteries() => new List<ChampionMasteryInfo>
    {
        new() { ChampionId = 103, ChampionName = "Ahri", ChampionLevel = 7, ChampionPoints = 145000 },
        new() { ChampionId = 84, ChampionName = "Akali", ChampionLevel = 6, ChampionPoints = 88000 },
        new() { ChampionId = 157, ChampionName = "Yasuo", ChampionLevel = 7, ChampionPoints = 220000 },
        new() { ChampionId = 238, ChampionName = "Zed", ChampionLevel = 5, ChampionPoints = 48000 },
        new() { ChampionId = 99, ChampionName = "Lux", ChampionLevel = 5, ChampionPoints = 39000 }
    };

    public async Task<IReadOnlyList<string>> GetRecentMatchIdsByPuuidAsync(string puuid, int count = 10, int? queue = null, CancellationToken ct = default)
    {
        if (!_options.HasValidApiKey)
        {
            return GenerateMockMatchIds(puuid, count);
        }

        try
        {
            var url = queue.HasValue
                ? $"https://{_options.RoutingRegion}.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?queue={queue.Value}&count={count}"
                : $"https://{_options.RoutingRegion}.api.riotgames.com/lol/match/v5/matches/by-puuid/{puuid}/ids?count={count}";
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

        // Teams — end-game objective totals stay on TowerKills/DragonKills/etc.
        // Minute-15 fields stay 0 until MATCH-V5 timeline is applied (avoids placeholder leakage into ML).
        match.Teams.Add(new TeamStats
        {
            TeamSide = TeamSide.Blue,
            Win = blueWin,
            FirstBaron = blueTeamDto?.Objectives?.Baron?.First ?? false,
            TowerKills = blueTeamDto?.Objectives?.Tower?.Kills ?? 0,
            DragonKills = blueTeamDto?.Objectives?.Dragon?.Kills ?? 0,
            BaronKills = blueTeamDto?.Objectives?.Baron?.Kills ?? 0,
            VoidgrubKills = blueTeamDto?.Objectives?.Horde?.Kills ?? 0,
            RiftHeraldKills = blueTeamDto?.Objectives?.RiftHerald?.Kills ?? 0,
            GoldAt15 = 0,
            KillsAt15 = 0
        });

        match.Teams.Add(new TeamStats
        {
            TeamSide = TeamSide.Red,
            Win = !blueWin,
            FirstBaron = redTeamDto?.Objectives?.Baron?.First ?? false,
            TowerKills = redTeamDto?.Objectives?.Tower?.Kills ?? 0,
            DragonKills = redTeamDto?.Objectives?.Dragon?.Kills ?? 0,
            BaronKills = redTeamDto?.Objectives?.Baron?.Kills ?? 0,
            VoidgrubKills = redTeamDto?.Objectives?.Horde?.Kills ?? 0,
            RiftHeraldKills = redTeamDto?.Objectives?.RiftHerald?.Kills ?? 0,
            GoldAt15 = 0,
            KillsAt15 = 0
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
                    ChampionName = GameAnalytics.Core.Helpers.ChampionNameHelper.ToDisplayName(p.ChampionName ?? "Champion"),
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
                    Item6 = p.Item6,
                    Summoner1Id = p.Summoner1Id > 0 ? p.Summoner1Id : 4,
                    Summoner2Id = p.Summoner2Id > 0 ? p.Summoner2Id : 14,
                    PrimaryRuneId = (p.Perks?.Styles?.FirstOrDefault(s => string.Equals(s.Description, "primaryStyle", StringComparison.OrdinalIgnoreCase)) ?? p.Perks?.Styles?.FirstOrDefault())?.Selections?.FirstOrDefault()?.Perk is int rId && rId > 0
                        ? rId
                        : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, ParsePosition(p.TeamPosition)).PrimaryRune,
                    SecondaryRuneStyleId = (p.Perks?.Styles?.FirstOrDefault(s => string.Equals(s.Description, "subStyle", StringComparison.OrdinalIgnoreCase)) ?? p.Perks?.Styles?.ElementAtOrDefault(1))?.Style is int sId && sId > 0
                        ? sId
                        : SpellRuneHelper.GetRecommendedRunesAndSpells(p.ChampionName, ParsePosition(p.TeamPosition)).SecondaryStyle,
                    VisionScore = p.VisionScore,
                    WardsPlaced = p.WardsPlaced,
                    WardsKilled = p.WardsKilled,
                    ControlWardsBought = p.VisionWardsBoughtInGame,
                    PhysicalDamageDealtToChampions = p.PhysicalDamageDealtToChampions,
                    MagicDamageDealtToChampions = p.MagicDamageDealtToChampions,
                    TrueDamageDealtToChampions = p.TrueDamageDealtToChampions,
                    TotalDamageTaken = p.TotalDamageTaken,
                    DamageSelfMitigated = p.DamageSelfMitigated,
                    DamageDealtToObjectives = p.DamageDealtToObjectives,
                    DamageDealtToTurrets = p.DamageDealtToTurrets,
                    DoubleKills = p.DoubleKills,
                    TripleKills = p.TripleKills,
                    QuadraKills = p.QuadraKills,
                    PentaKills = p.PentaKills,
                    SoloKills = p.Challenges?.SoloKills ?? 0,
                    TurretPlatesTaken = p.Challenges?.TurretPlatesTaken ?? 0,
                    KillParticipation = (float)(p.Challenges?.KillParticipation ?? 0),
                    VisionScorePerMinute = (float)(p.Challenges?.VisionScorePerMinute ?? 0),
                    GoldPerMinute = (float)(p.Challenges?.GoldPerMinute ?? 0),
                    DamagePerMinute = (float)(p.Challenges?.DamagePerMinute ?? 0),
                    TeamDamagePercentage = (float)(p.Challenges?.TeamDamagePercentage ?? 0),
                    ControlWardsPlaced = p.Challenges?.ControlWardsPlaced ?? 0,
                    EffectiveHealAndShielding = (int)(p.Challenges?.EffectiveHealAndShielding ?? 0),
                    DamageDealtToObjectivesChallenge = (int)(p.Challenges?.DamageDealtToObjectives ?? 0),
                    ChallengesJson = SerializeChallenges(p.Challenges),
                    JungleCsBefore10Minutes = (float)(p.Challenges?.JungleCsBefore10Minutes ?? 0),
                    LaneMinionsFirst10Minutes = (float)(p.Challenges?.LaneMinionsFirst10Minutes ?? 0),
                    SkillshotsDodged = p.Challenges?.SkillshotsDodged ?? 0,
                    SkillshotsHit = p.Challenges?.SkillshotsHit ?? 0,
                    TakedownsFirstXMinutes = p.Challenges?.TakedownsFirstXMinutes ?? 0,
                    EpicMonsterSteals = p.Challenges?.EpicMonsterSteals ?? 0,
                    SoloBaronKills = p.Challenges?.SoloBaronKills ?? 0,
                    KdaChallenge = (float)(p.Challenges?.Kda ?? 0)
                });
            }
        }

        ParticipantChallengeHelper.RefreshHasChallengesFlag(match);
        return match;
    }

    private static string SerializeChallenges(RiotChallengesDto? challenges)
    {
        if (challenges == null) return string.Empty;
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(challenges);
        }
        catch
        {
            return string.Empty;
        }
    }

    private MatchTimelineData ParseRealTimeline(string matchId, JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("info", out var infoElem) && infoElem.TryGetProperty("frames", out var framesElem))
            {
                var result = new MatchTimelineData { MatchId = matchId };
                var frameCount = framesElem.GetArrayLength();

                // Frame 10 + 15 snapshots for gold / CS / XP momentum features
                ExtractTeamGoldFrame(framesElem, frameCount, 10,
                    out var gold10Blue, out var gold10Red, out var cs10Blue, out var cs10Red, out var xp10Blue, out var xp10Red);
                ExtractTeamGoldFrame(framesElem, frameCount, 15, out var goldBlue, out var goldRed, out var csBlue, out var csRed, out var xpBlue, out var xpRed);

                result.GoldAt10Blue = gold10Blue;
                result.GoldAt10Red = gold10Red;
                result.CsAt10Blue = cs10Blue;
                result.CsAt10Red = cs10Red;
                result.XpAt10Blue = xp10Blue;
                result.XpAt10Red = xp10Red;
                result.GoldAt15Blue = goldBlue > 0 ? goldBlue : 24500;
                result.GoldAt15Red = goldRed > 0 ? goldRed : 23800;
                result.CsAt15Blue = csBlue;
                result.CsAt15Red = csRed;
                result.XpAt15Blue = xpBlue;
                result.XpAt15Red = xpRed;

                bool firstBloodSet = false;
                bool firstTowerSet = false;
                bool firstDragonSet = false;

                const int tenMinMs = 10 * 60 * 1000;
                const int fifteenMinMs = 15 * 60 * 1000;

                // Process all frames to collect real events + participant positions for moment replay
                for (int fIdx = 0; fIdx < frameCount; fIdx++)
                {
                    var curFrame = framesElem[fIdx];
                    var frameTs = curFrame.TryGetProperty("timestamp", out var frameTsProp)
                        ? frameTsProp.GetInt32()
                        : fIdx * 60_000;

                    if (curFrame.TryGetProperty("participantFrames", out var frameParticipants))
                    {
                        var snapshot = new TimelineFrameSnapshot { TimestampMs = frameTs };
                        for (int i = 1; i <= 10; i++)
                        {
                            if (!frameParticipants.TryGetProperty(i.ToString(), out var pf)) continue;
                            var x = 0;
                            var y = 0;
                            if (pf.TryGetProperty("position", out var posElem))
                            {
                                x = posElem.TryGetProperty("x", out var xProp) ? xProp.GetInt32() : 0;
                                y = posElem.TryGetProperty("y", out var yProp) ? yProp.GetInt32() : 0;
                            }

                            snapshot.Participants.Add(new TimelineParticipantPos
                            {
                                ParticipantId = i,
                                X = x,
                                Y = y,
                                TotalGold = pf.TryGetProperty("totalGold", out var gProp) ? gProp.GetInt32() : 0,
                                Level = pf.TryGetProperty("level", out var lvlProp) ? lvlProp.GetInt32() : 1
                            });
                        }

                        if (snapshot.Participants.Count > 0)
                        {
                            result.Frames.Add(snapshot);
                        }
                    }

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

                            int? posX = null;
                            int? posY = null;
                            if (ev.TryGetProperty("position", out var killPos))
                            {
                                posX = killPos.TryGetProperty("x", out var kx) ? kx.GetInt32() : null;
                                posY = killPos.TryGetProperty("y", out var ky) ? ky.GetInt32() : null;
                            }

                            result.RealEvents.Add(new TimelineEventRecord
                            {
                                TimestampMs = ts,
                                EventType = evType,
                                KillerId = killerId,
                                VictimId = victimId,
                                AssistingParticipantIds = assists,
                                Bounty = bounty,
                                PositionX = posX,
                                PositionY = posY
                            });

                            if (!firstBloodSet && killerId > 0)
                            {
                                result.BlueFirstBlood = killerId <= 5;
                                result.FirstBloodTimeMs = ts;
                                firstBloodSet = true;
                            }

                            if (ts <= fifteenMinMs)
                            {
                                if (killerId <= 5) result.KillsAt15Blue++;
                                else result.KillsAt15Red++;

                                // Deaths by victim side (executions still count as deaths).
                                if (victimId > 0)
                                {
                                    if (victimId <= 5) result.DeathsAt15Blue++;
                                    else result.DeathsAt15Red++;
                                }
                            }

                            if (ts <= tenMinMs)
                            {
                                if (killerId <= 5) result.KillsAt10Blue++;
                                else result.KillsAt10Red++;
                            }
                        }
                        else if (evType == "WARD_PLACED")
                        {
                            if (ts > fifteenMinMs) continue;
                            var creatorId = ev.TryGetProperty("creatorId", out var cProp) ? cProp.GetInt32() : 0;
                            if (creatorId <= 0) continue;
                            var wardType = ev.TryGetProperty("wardType", out var wProp)
                                ? wProp.GetString() ?? string.Empty
                                : string.Empty;
                            var isBlue = creatorId <= 5;
                            var isControl = wardType.Contains("CONTROL", StringComparison.OrdinalIgnoreCase);
                            if (isControl)
                            {
                                if (isBlue) result.ControlWardsAt15Blue++;
                                else result.ControlWardsAt15Red++;
                            }
                            else
                            {
                                // Yellow/blue trinket, sight ward, etc. — skip undefined noise.
                                if (string.IsNullOrWhiteSpace(wardType) ||
                                    wardType.Equals("UNDEFINED", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                if (isBlue) result.VisionWardsAt15Blue++;
                                else result.VisionWardsAt15Red++;
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

                            var isBlueMonster = killerTeamId == 100 || killerId <= 5;

                            if (monsterType.Equals("DRAGON", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!firstDragonSet)
                                {
                                    result.BlueFirstDragon = isBlueMonster;
                                    result.FirstDragonTimeMs = ts;
                                    result.FirstDragonSubType = monsterSubType;
                                    firstDragonSet = true;
                                }

                                if (ts <= fifteenMinMs)
                                {
                                    if (isBlueMonster) result.DragonsAt15Blue++;
                                    else result.DragonsAt15Red++;
                                }
                            }
                            else if (monsterType.Equals("HORDE", StringComparison.OrdinalIgnoreCase))
                            {
                                if (ts <= fifteenMinMs)
                                {
                                    if (isBlueMonster) result.VoidgrubsAt15Blue++;
                                    else result.VoidgrubsAt15Red++;
                                }
                            }
                            else if (monsterType.Equals("RIFTHERALD", StringComparison.OrdinalIgnoreCase))
                            {
                                if (ts <= fifteenMinMs)
                                {
                                    if (isBlueMonster) result.HeraldsAt15Blue++;
                                    else result.HeraldsAt15Red++;
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
                                    result.FirstTowerTimeMs = ts;
                                    firstTowerSet = true;
                                }

                                if (ts <= fifteenMinMs)
                                {
                                    if (blueDestroyed) result.TowersAt15Blue++;
                                    else result.TowersAt15Red++;
                                }
                            }
                        }
                        else if (evType == "TURRET_PLATE_DESTROYED")
                        {
                            // teamId = team that OWNED the turret (lost the plate). Killer takes it.
                            var killerId = ev.TryGetProperty("killerId", out var kProp) ? kProp.GetInt32() : 0;
                            var teamId = ev.TryGetProperty("teamId", out var tProp) ? tProp.GetInt32() : 0;
                            if (ts > fifteenMinMs) continue;

                            var blueTookPlate = killerId > 0
                                ? killerId <= 5
                                : teamId == 200; // destroyed red's plate ⇒ blue gold

                            if (blueTookPlate) result.PlatesAt15Blue++;
                            else result.PlatesAt15Red++;
                        }
                    }
                }

                // Carry gold concentration at ~15' from the nearest frame
                result.CarryGoldDiff15 = ExtractCarryGoldDiff(framesElem, frameCount, 15);

                return result;
            }
        }
        catch
        {
            // Fallback
        }

        return GenerateMockTimeline(matchId);
    }

    private static float ExtractCarryGoldDiff(JsonElement framesElem, int frameCount, int minute)
    {
        if (frameCount <= 0) return 0f;
        var targetMs = minute * 60_000;
        var bestIdx = Math.Min(minute, frameCount - 1);
        var bestDelta = int.MaxValue;
        for (var i = 0; i < frameCount; i++)
        {
            var f = framesElem[i];
            var ts = f.TryGetProperty("timestamp", out var tProp) ? tProp.GetInt32() : i * 60_000;
            var delta = Math.Abs(ts - targetMs);
            if (delta < bestDelta && ts <= targetMs + 30_000)
            {
                bestDelta = delta;
                bestIdx = i;
            }
        }

        var frame = framesElem[bestIdx];
        if (!frame.TryGetProperty("participantFrames", out var pFrames)) return 0f;

        var blueMax = 0;
        var redMax = 0;
        for (var i = 1; i <= 10; i++)
        {
            if (!pFrames.TryGetProperty(i.ToString(), out var pf)) continue;
            var gold = pf.TryGetProperty("totalGold", out var g) ? g.GetInt32() : 0;
            if (i <= 5) blueMax = Math.Max(blueMax, gold);
            else redMax = Math.Max(redMax, gold);
        }

        return blueMax - redMax;
    }

    private static void ExtractTeamGoldFrame(
        JsonElement framesElem,
        int frameCount,
        int minute,
        out int goldBlue,
        out int goldRed,
        out int csBlue,
        out int csRed,
        out int xpBlue,
        out int xpRed)
    {
        goldBlue = goldRed = csBlue = csRed = xpBlue = xpRed = 0;
        if (frameCount <= 0) return;

        var targetIndex = Math.Min(minute, frameCount - 1);
        // Prefer a frame whose timestamp is near the requested minute when available
        var bestIdx = targetIndex;
        var targetMs = minute * 60_000;
        var bestDelta = int.MaxValue;
        for (var i = 0; i < frameCount; i++)
        {
            var f = framesElem[i];
            var ts = f.TryGetProperty("timestamp", out var tProp) ? tProp.GetInt32() : i * 60_000;
            var delta = Math.Abs(ts - targetMs);
            if (delta < bestDelta && ts <= targetMs + 30_000)
            {
                bestDelta = delta;
                bestIdx = i;
            }
        }

        var frame = framesElem[bestIdx];
        if (!frame.TryGetProperty("participantFrames", out var pFrames)) return;

        for (int i = 1; i <= 10; i++)
        {
            if (!pFrames.TryGetProperty(i.ToString(), out var pf)) continue;
            var totalGold = pf.TryGetProperty("totalGold", out var g) ? g.GetInt32() : 0;
            var minions = pf.TryGetProperty("minionsKilled", out var m) ? m.GetInt32() : 0;
            var jgMinions = pf.TryGetProperty("jungleMinionsKilled", out var jm) ? jm.GetInt32() : 0;
            var xp = pf.TryGetProperty("xp", out var x) ? x.GetInt32() : 0;
            var totalCs = minions + jgMinions;

            if (i <= 5)
            {
                goldBlue += totalGold;
                csBlue += totalCs;
                xpBlue += xp;
            }
            else
            {
                goldRed += totalGold;
                csRed += totalCs;
                xpRed += xp;
            }
        }
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
                Item6 = 3340,
                Summoner1Id = SpellRuneHelper.GetRecommendedRunesAndSpells(champ.Item1, champ.Item2).Spell1,
                Summoner2Id = SpellRuneHelper.GetRecommendedRunesAndSpells(champ.Item1, champ.Item2).Spell2,
                PrimaryRuneId = SpellRuneHelper.GetRecommendedRunesAndSpells(champ.Item1, champ.Item2).PrimaryRune,
                SecondaryRuneStyleId = SpellRuneHelper.GetRecommendedRunesAndSpells(champ.Item1, champ.Item2).SecondaryStyle,
                VisionScore = random.Next(15, 65),
                WardsPlaced = random.Next(8, 25),
                WardsKilled = random.Next(2, 10),
                ControlWardsBought = random.Next(1, 5),
                PhysicalDamageDealtToChampions = random.Next(6000, 20000),
                MagicDamageDealtToChampions = random.Next(3000, 15000),
                TrueDamageDealtToChampions = random.Next(500, 3000),
                TotalDamageTaken = random.Next(15000, 35000),
                DamageSelfMitigated = random.Next(10000, 30000),
                DamageDealtToObjectives = random.Next(2000, 18000),
                DamageDealtToTurrets = random.Next(1000, 8000),
                DoubleKills = random.Next(0, 3),
                SoloKills = random.Next(0, 4),
                TurretPlatesTaken = random.Next(0, 5),
                KillParticipation = (float)(random.NextDouble() * 0.4 + 0.35),
                VisionScorePerMinute = (float)(random.NextDouble() * 0.8 + 0.3),
                GoldPerMinute = (float)(random.Next(300, 450)),
                DamagePerMinute = (float)(random.Next(400, 800)),
                TeamDamagePercentage = (float)(random.NextDouble() * 0.2 + 0.15),
                ControlWardsPlaced = random.Next(1, 8),
                EffectiveHealAndShielding = random.Next(500, 8000),
                DamageDealtToObjectivesChallenge = random.Next(2000, 18000),
                JungleCsBefore10Minutes = champ.Item2 == Position.Jungle ? random.Next(40, 80) : 0,
                LaneMinionsFirst10Minutes = champ.Item2 == Position.Jungle ? 0 : random.Next(50, 90),
                SkillshotsDodged = random.Next(5, 40),
                SkillshotsHit = random.Next(10, 60),
                TakedownsFirstXMinutes = random.Next(0, 6),
                ChallengesJson = "{\"mock\":true}"
            });
        }

        ParticipantChallengeHelper.RefreshHasChallengesFlag(match);
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

        [JsonPropertyName("horde")]
        public RiotObjectiveDetailDto? Horde { get; set; }

        [JsonPropertyName("riftHerald")]
        public RiotObjectiveDetailDto? RiftHerald { get; set; }
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

        [JsonPropertyName("summoner1Id")]
        public int Summoner1Id { get; set; }

        [JsonPropertyName("summoner2Id")]
        public int Summoner2Id { get; set; }

        [JsonPropertyName("perks")]
        public RiotPerksDto? Perks { get; set; }

        [JsonPropertyName("visionScore")]
        public int VisionScore { get; set; }

        [JsonPropertyName("wardsPlaced")]
        public int WardsPlaced { get; set; }

        [JsonPropertyName("wardsKilled")]
        public int WardsKilled { get; set; }

        [JsonPropertyName("visionWardsBoughtInGame")]
        public int VisionWardsBoughtInGame { get; set; }

        [JsonPropertyName("physicalDamageDealtToChampions")]
        public int PhysicalDamageDealtToChampions { get; set; }

        [JsonPropertyName("magicDamageDealtToChampions")]
        public int MagicDamageDealtToChampions { get; set; }

        [JsonPropertyName("trueDamageDealtToChampions")]
        public int TrueDamageDealtToChampions { get; set; }

        [JsonPropertyName("totalDamageTaken")]
        public int TotalDamageTaken { get; set; }

        [JsonPropertyName("damageSelfMitigated")]
        public int DamageSelfMitigated { get; set; }

        [JsonPropertyName("damageDealtToObjectives")]
        public int DamageDealtToObjectives { get; set; }

        [JsonPropertyName("damageDealtToTurrets")]
        public int DamageDealtToTurrets { get; set; }

        [JsonPropertyName("doubleKills")]
        public int DoubleKills { get; set; }

        [JsonPropertyName("tripleKills")]
        public int TripleKills { get; set; }

        [JsonPropertyName("quadraKills")]
        public int QuadraKills { get; set; }

        [JsonPropertyName("pentaKills")]
        public int PentaKills { get; set; }

        [JsonPropertyName("challenges")]
        public RiotChallengesDto? Challenges { get; set; }
    }

    public class RiotPerksDto
    {
        [JsonPropertyName("styles")]
        public List<RiotPerkStyleDto>? Styles { get; set; }
    }

    public class RiotPerkStyleDto
    {
        [JsonPropertyName("style")]
        public int Style { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("selections")]
        public List<RiotPerkSelectionDto>? Selections { get; set; }
    }

    public class RiotPerkSelectionDto
    {
        [JsonPropertyName("perk")]
        public int Perk { get; set; }
    }

    public class RiotChallengesDto
    {
        [JsonPropertyName("soloKills")]
        public int? SoloKills { get; set; }

        [JsonPropertyName("turretPlatesTaken")]
        public int? TurretPlatesTaken { get; set; }

        [JsonPropertyName("killParticipation")]
        public double? KillParticipation { get; set; }

        [JsonPropertyName("visionScorePerMinute")]
        public double? VisionScorePerMinute { get; set; }

        [JsonPropertyName("goldPerMinute")]
        public double? GoldPerMinute { get; set; }

        [JsonPropertyName("damagePerMinute")]
        public double? DamagePerMinute { get; set; }

        [JsonPropertyName("teamDamagePercentage")]
        public double? TeamDamagePercentage { get; set; }

        [JsonPropertyName("controlWardsPlaced")]
        public int? ControlWardsPlaced { get; set; }

        [JsonPropertyName("effectiveHealAndShielding")]
        public double? EffectiveHealAndShielding { get; set; }

        [JsonPropertyName("damageDealtToObjectives")]
        public double? DamageDealtToObjectives { get; set; }

        [JsonPropertyName("jungleCsBefore10Minutes")]
        public double? JungleCsBefore10Minutes { get; set; }

        [JsonPropertyName("laneMinionsFirst10Minutes")]
        public double? LaneMinionsFirst10Minutes { get; set; }

        [JsonPropertyName("skillshotsDodged")]
        public int? SkillshotsDodged { get; set; }

        [JsonPropertyName("skillshotsHit")]
        public int? SkillshotsHit { get; set; }

        [JsonPropertyName("takedownsFirstXMinutes")]
        public int? TakedownsFirstXMinutes { get; set; }

        [JsonPropertyName("epicMonsterSteals")]
        public int? EpicMonsterSteals { get; set; }

        [JsonPropertyName("soloBaronKills")]
        public int? SoloBaronKills { get; set; }

        [JsonPropertyName("kda")]
        public double? Kda { get; set; }

        /// <summary>Captures every other challenge key Riot returns (full fidelity → ChallengesJson).</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    public class RiotChampionMasteryDto
    {
        [JsonPropertyName("puuid")]
        public string? Puuid { get; set; }

        [JsonPropertyName("championId")]
        public long ChampionId { get; set; }

        [JsonPropertyName("championLevel")]
        public int ChampionLevel { get; set; }

        [JsonPropertyName("championPoints")]
        public int ChampionPoints { get; set; }

        [JsonPropertyName("lastPlayTime")]
        public long LastPlayTime { get; set; }
    }

    public class RiotLeagueListDto
    {
        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("queue")]
        public string? Queue { get; set; }

        [JsonPropertyName("entries")]
        public List<RiotLeagueItemDto>? Entries { get; set; }
    }

    public class RiotLeagueItemDto
    {
        [JsonPropertyName("summonerId")]
        public string? SummonerId { get; set; }

        [JsonPropertyName("puuid")]
        public string? Puuid { get; set; }

        [JsonPropertyName("leaguePoints")]
        public int LeaguePoints { get; set; }

        [JsonPropertyName("rank")]
        public string? Rank { get; set; }

        [JsonPropertyName("wins")]
        public int Wins { get; set; }

        [JsonPropertyName("losses")]
        public int Losses { get; set; }
    }
}
