using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.ML.Engine;
using GameAnalytics.ML.Models;

namespace GameAnalytics.ML.Training;

public class RealMatchDatasetCollector
{
    private readonly IRiotApiClient _apiClient;
    private readonly IMatchRepository _matchRepo;

    public RealMatchDatasetCollector(IRiotApiClient apiClient, IMatchRepository matchRepo)
    {
        _apiClient = apiClient;
        _matchRepo = matchRepo;
    }

    /// <summary>
    /// Crawls real Ranked Solo/Duo matches from Riot API starting from seed PUUIDs,
    /// populates early-game timeline metrics (15-min gold/CS/XP diff, kills, Voidgrubs, Herald, objectives),
    /// and saves them into the repository.
    /// </summary>
    public async Task<int> CollectRankedMatchesAsync(
        string seedPuuid,
        int targetNewMatches = 30,
        Action<string>? logger = null,
        CancellationToken ct = default)
    {
        var visitedPuuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidatePuuids = new Queue<string>();
        // PUUID → known ladder skill (1–10). Used to tag matches with ApproxRankScore.
        var puuidRankHints = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var refillAttempts = 0;
        const int maxRefillAttempts = 6;
        var objectiveBackfills = 0;
        var ladderSeeded = false;

        void RememberRankHint(string? puuid, float rankScore)
        {
            if (string.IsNullOrWhiteSpace(puuid) || rankScore <= 0) return;
            if (!puuidRankHints.TryGetValue(puuid, out var existing) || rankScore > existing)
                puuidRankHints[puuid] = rankScore;
        }

        void EnqueueCandidate(string? puuid, float? rankHint = null)
        {
            if (string.IsNullOrWhiteSpace(puuid)) return;
            if (puuid.StartsWith("puuid-", StringComparison.OrdinalIgnoreCase)) return;
            if (rankHint.HasValue) RememberRankHint(puuid, rankHint.Value);
            if (!visitedPuuids.Add(puuid)) return;
            candidatePuuids.Enqueue(puuid);
        }

        void EnqueueParticipants(IEnumerable<Participant> participants)
        {
            foreach (var p in participants)
            {
                EnqueueCandidate(p.Puuid);
            }
        }

        async Task SeedLadderAsync()
        {
            if (ladderSeeded) return;
            ladderSeeded = true;
            try
            {
                var ladder = await _apiClient.GetHighEloLadderPlayersAsync("RANKED_SOLO_5x5", maxCount: 50, ct: ct);
                var before = candidatePuuids.Count;
                foreach (var entry in ladder)
                {
                    EnqueueCandidate(entry.Puuid, RankScoreHelper.FromTier(entry.Tier));
                }
                logger?.Invoke($"[Collector] Ladder seed: +{candidatePuuids.Count - before} high-elo гравців (rank hints для лобі).");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[Collector] Ladder seed failed: {ex.Message}");
            }
        }

        EnqueueCandidate(seedPuuid);
        await SeedLadderAsync();

        int newlySavedMatches = 0;

        logger?.Invoke($"[Collector] Початок збору реальних Ranked Solo матчів для навчання ML (Ціль: {targetNewMatches})...");

        while (newlySavedMatches < targetNewMatches && !ct.IsCancellationRequested)
        {
            // Self-healing: refill crawl queue from DB / high-elo ladders when empty
            if (candidatePuuids.Count == 0)
            {
                refillAttempts++;
                if (refillAttempts > maxRefillAttempts)
                {
                    logger?.Invoke($"[Collector] Досягнуто ліміт refill ({maxRefillAttempts}). Зупинка ітерації зі {newlySavedMatches} новими матчами.");
                    break;
                }

                // Forget visitation for DB/ladder refill so we can re-walk teammates of stored games
                // and discover matches beyond the previously exhausted frontier.
                visitedPuuids.Clear();
                if (!string.IsNullOrWhiteSpace(seedPuuid))
                {
                    visitedPuuids.Add(seedPuuid);
                }

                var storedPuuids = await _matchRepo.GetDistinctParticipantPuuidsAsync(limit: 80, ct);
                foreach (var p in storedPuuids)
                {
                    EnqueueCandidate(p);
                }

                logger?.Invoke($"[Collector] Refill #{refillAttempts}: додано {candidatePuuids.Count} кандидатів з локальної БД.");

                if (candidatePuuids.Count < 15)
                {
                    try
                    {
                        var ladder = await _apiClient.GetHighEloLadderPlayersAsync("RANKED_SOLO_5x5", maxCount: 40, ct: ct);
                        var before = candidatePuuids.Count;
                        foreach (var entry in ladder)
                        {
                            EnqueueCandidate(entry.Puuid, RankScoreHelper.FromTier(entry.Tier));
                        }
                        logger?.Invoke($"[Collector] Ladder refill: +{candidatePuuids.Count - before} гравців з Challenger/GM/Master (з міткою рангу).");
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"[Collector] Ladder refill failed: {ex.Message}");
                    }
                }

                if (candidatePuuids.Count == 0)
                {
                    logger?.Invoke("[Collector] Черга кандидатів порожня після refill. Завершення поточної ітерації.");
                    break;
                }
            }

            var puuid = candidatePuuids.Dequeue();

            // Deeper lookback: previously collected players still have older ranked games worth mining
            var matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 30, queue: 420, ct: ct);
            if (matchIds.Count == 0)
            {
                matchIds = await _apiClient.GetRecentMatchIdsByPuuidAsync(puuid, count: 20, queue: null, ct: ct);
            }

            foreach (var matchId in matchIds)
            {
                if (newlySavedMatches >= targetNewMatches || ct.IsCancellationRequested) break;

                var existing = await _matchRepo.GetMatchByMatchIdAsync(matchId, ct);
                if (existing != null && existing.Teams.Count >= 2 && existing.Teams.Any(t => t.GoldAt15 > 0))
                {
                    // Backfill towers/dragons/vision/deaths at 15' for older rows missing those snapshots.
                    if ((NeedsAt15ObjectiveBackfill(existing) || NeedsVisionDeathBackfill(existing) || NeedsCsXp10Backfill(existing))
                        && objectiveBackfills < 60)
                    {
                        var timelineBackfill = await _apiClient.GetMatchTimelineAsync(matchId, ct);
                        if (timelineBackfill != null && ApplyTimelineSnapshot(existing, timelineBackfill))
                        {
                            await _matchRepo.UpsertMatchAsync(existing, ct);
                            objectiveBackfills++;
                            logger?.Invoke($"[Collector] Backfill 15' timeline features: {matchId} ({objectiveBackfills}/60)");
                        }
                    }

                    // Critical: expand crawl graph from already-stored matches, otherwise the frontier dies
                    // once the seed player's recent games are fully collected.
                    // Cap queue growth to avoid burning the Riot rate limit on endless teammate BFS.
                    if (candidatePuuids.Count < 150)
                    {
                        EnqueueParticipants(existing.Participants);
                    }
                    continue;
                }

                var match = await _apiClient.GetMatchDetailsAsync(matchId, ct);
                // Strict filter: must not be remake, duration >= 5 mins, and must be Summoner's Rift
                if (match == null || match.IsRemake || match.GameDurationSeconds < 300)
                {
                    continue;
                }
                if (match.QueueId != 420 && match.QueueId != 440 && match.QueueId != 400)
                {
                    continue; // Skip ARAM, Arena, Fun modes
                }

                // Fetch real timeline to get exact 15-minute game state (Gold, CS, XP, Grubs, Herald, towers/dragons)
                var timeline = await _apiClient.GetMatchTimelineAsync(matchId, ct);
                if (timeline != null)
                {
                    ApplyTimelineSnapshot(match, timeline);
                }

                TagMatchRankScore(match, puuidRankHints);

                await _matchRepo.UpsertMatchAsync(match, ct);
                newlySavedMatches++;
                logger?.Invoke($"[Collector] Збережено реальний матч #{newlySavedMatches}/{targetNewMatches}: {match.MatchId} ({match.FormattedDuration}, {match.QueueName})");

                EnqueueParticipants(match.Participants);
            }
        }

        logger?.Invoke($"[Collector] Збір завершено. Додано {newlySavedMatches} нових матчів до тренувальної вибірки.");
        return newlySavedMatches;
    }

    /// <summary>
    /// Applies a minute-15 timeline snapshot onto team rows.
    /// Intentionally overwrites Voidgrub/Herald with at-15 counts so training never sees end-game leakage.
    /// Also derives per-role gold diffs and level diffs when frames + positions are available.
    /// </summary>
    public static bool ApplyTimelineSnapshot(Match match, MatchTimelineData timeline)
    {
        var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
        var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
        if (blue == null || red == null) return false;

        blue.GoldAt15 = timeline.GoldAt15Blue;
        red.GoldAt15 = timeline.GoldAt15Red;
        blue.KillsAt15 = timeline.KillsAt15Blue;
        red.KillsAt15 = timeline.KillsAt15Red;
        blue.CsAt15 = timeline.CsAt15Blue;
        red.CsAt15 = timeline.CsAt15Red;
        blue.XpAt15 = timeline.XpAt15Blue;
        red.XpAt15 = timeline.XpAt15Red;
        blue.TowersAt15 = timeline.TowersAt15Blue;
        red.TowersAt15 = timeline.TowersAt15Red;
        blue.DragonsAt15 = timeline.DragonsAt15Blue;
        red.DragonsAt15 = timeline.DragonsAt15Red;
        blue.VoidgrubKills = timeline.VoidgrubsAt15Blue;
        red.VoidgrubKills = timeline.VoidgrubsAt15Red;
        blue.RiftHeraldKills = timeline.HeraldsAt15Blue;
        red.RiftHeraldKills = timeline.HeraldsAt15Red;
        blue.FirstBlood = timeline.BlueFirstBlood;
        blue.FirstTower = timeline.BlueFirstTower;
        blue.FirstDragon = timeline.BlueFirstDragon;
        blue.FirstVoidgrub = timeline.VoidgrubsAt15Blue > 0;
        blue.FirstRiftHerald = timeline.HeraldsAt15Blue > 0;
        match.HasMinute15Objectives = true;

        match.GoldDiff10 = timeline.GoldDiffAt10;
        match.KillDiff10 = timeline.KillDiffAt10;
        match.CsDiff10 = timeline.CsDiffAt10;
        match.XpDiff10 = timeline.XpDiffAt10;
        match.HasCsXp10Features = true;

        match.FirstBloodTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstBlood, timeline.FirstBloodTimeMs);
        match.FirstTowerTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstTower, timeline.FirstTowerTimeMs);
        match.FirstDragonTempo = DragonValueHelper.SignedTempo(timeline.BlueFirstDragon, timeline.FirstDragonTimeMs);
        match.FirstDragonValue = DragonValueHelper.SignedDragonValue(
            timeline.BlueFirstDragon, timeline.FirstDragonSubType, timeline.FirstDragonTimeMs);
        if (timeline.CarryGoldDiff15 != 0)
            match.CarryGoldDiff15 = timeline.CarryGoldDiff15;
        match.PlatesDiff15 = timeline.PlatesDiffAt15;
        match.DeathDiff15 = timeline.DeathDiffAt15;
        match.VisionWardDiff15 = timeline.VisionWardDiffAt15;
        match.ControlWardDiff15 = timeline.ControlWardDiffAt15;
        match.HasVisionDeathFeatures = true;

        ApplyLaneAndLevelDiffs(match, timeline);
        return true;
    }

    /// <summary>Tags a match with the average known ladder rank among its participants (lobby skill).</summary>
    public static void TagMatchRankScore(Match match, IReadOnlyDictionary<string, float> puuidRankHints)
    {
        if (match.ApproxRankScore > 0) return;
        float sum = 0;
        var n = 0;
        foreach (var p in match.Participants)
        {
            if (string.IsNullOrWhiteSpace(p.Puuid)) continue;
            if (puuidRankHints.TryGetValue(p.Puuid, out var score) && score > 0)
            {
                sum += score;
                n++;
            }
        }

        if (n > 0)
            match.ApproxRankScore = sum / n;
    }

    public static void ApplyLaneAndLevelDiffs(Match match, MatchTimelineData timeline)
    {
        if (timeline.Frames.Count == 0 || match.Participants.Count == 0) return;

        const int fifteenMs = 15 * 60 * 1000;
        var frame = timeline.Frames
            .Where(f => f.TimestampMs <= fifteenMs)
            .OrderByDescending(f => f.TimestampMs)
            .FirstOrDefault()
            ?? timeline.Frames.LastOrDefault();
        if (frame == null || frame.Participants.Count == 0) return;

        var goldByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.TotalGold);
        var levelByPid = frame.Participants.ToDictionary(p => p.ParticipantId, p => p.Level);

        float RoleGold(TeamSide side, Position pos)
        {
            var players = match.Participants.Where(p => p.TeamSide == side && p.Position == pos).ToList();
            if (players.Count == 0) return 0;
            return (float)players.Average(p =>
                goldByPid.TryGetValue(ResolveParticipantId(match, p), out var g) ? g : 0);
        }

        float SideLevels(TeamSide side) =>
            match.Participants.Where(p => p.TeamSide == side)
                .Sum(p => levelByPid.TryGetValue(ResolveParticipantId(match, p), out var lvl) ? lvl : 0);

        match.TopGoldDiff15 = RoleGold(TeamSide.Blue, Position.Top) - RoleGold(TeamSide.Red, Position.Top);
        match.JungleGoldDiff15 = RoleGold(TeamSide.Blue, Position.Jungle) - RoleGold(TeamSide.Red, Position.Jungle);
        match.MidGoldDiff15 = RoleGold(TeamSide.Blue, Position.Middle) - RoleGold(TeamSide.Red, Position.Middle);
        match.BotDuoGoldDiff15 =
            (RoleGold(TeamSide.Blue, Position.Bottom) + RoleGold(TeamSide.Blue, Position.Utility))
            - (RoleGold(TeamSide.Red, Position.Bottom) + RoleGold(TeamSide.Red, Position.Utility));
        match.LevelDiff15 = SideLevels(TeamSide.Blue) - SideLevels(TeamSide.Red);

        // Carry concentration: richest player each side (feeds snowball / shutdown risk).
        float SideMaxGold(TeamSide side) =>
            match.Participants.Where(p => p.TeamSide == side)
                .Select(p => goldByPid.TryGetValue(ResolveParticipantId(match, p), out var g) ? g : 0)
                .DefaultIfEmpty(0)
                .Max();
        if (match.CarryGoldDiff15 == 0)
            match.CarryGoldDiff15 = SideMaxGold(TeamSide.Blue) - SideMaxGold(TeamSide.Red);
    }

    private static int ResolveParticipantId(Match match, Participant p)
    {
        if (p.ParticipantId > 0) return p.ParticipantId;
        var idx = match.Participants.IndexOf(p);
        return idx >= 0 ? idx + 1 : 0;
    }

    private static bool NeedsAt15ObjectiveBackfill(Match match)
    {
        if (match.HasMinute15Objectives) return false;
        var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
        var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
        if (blue == null || red == null) return false;
        return blue.GoldAt15 > 0 || red.GoldAt15 > 0;
    }

    /// <summary>Older rows may have gold@15 but never parsed wards/deaths — refresh timeline once.</summary>
    private static bool NeedsVisionDeathBackfill(Match match)
        => match.HasMinute15Objectives && !match.HasVisionDeathFeatures;

    private static bool NeedsCsXp10Backfill(Match match)
        => match.HasMinute15Objectives && !match.HasCsXp10Features;

    /// <summary>
    /// Converts stored historical matches into honest minute-15 ML features.
    /// Skips matches without a real 15' gold snapshot and never falls back to end-of-game towers/kills/CS.
    /// Champion win rates are chronological past-only (no future label leakage).
    /// Adds draft scaling, composition tags, gold@10 momentum, lane gold, and rank-aware gold conversion.
    /// Preserves chronological order when the input is ordered by GameCreation.
    /// </summary>
    public static List<MatchInputData> ExtractFeaturesFromMatches(IEnumerable<Match> matches)
    {
        var matchList = matches as IList<Match> ?? matches.ToList();
        // Ensure chronological processing so rolling WR never sees the future.
        var ordered = matchList
            .OrderBy(m => m.GameCreation)
            .ThenBy(m => m.MatchId, StringComparer.Ordinal)
            .ToList();

        var champWins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var champGames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matchupWins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matchupGames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<MatchInputData>();

        foreach (var match in ordered)
        {
            if (match.IsRemake || match.GameDurationSeconds < 300) continue;
            if (match.QueueId != 420 && match.QueueId != 440 && match.QueueId != 400 && match.QueueId != 0) continue;

            var blue = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Blue);
            var red = match.Teams.FirstOrDefault(t => t.TeamSide == TeamSide.Red);
            if (blue == null || red == null) continue;

            // Require a real timeline gold snapshot — never invent diffs from final scoreboard.
            if (blue.GoldAt15 <= 0 && red.GoldAt15 <= 0) continue;

            var goldDiff15 = (float)(blue.GoldAt15 - red.GoldAt15);
            var killDiff15 = (float)(blue.KillsAt15 - red.KillsAt15);
            var csDiff15 = (float)(blue.CsAt15 - red.CsAt15);
            var xpDiff15 = (blue.XpAt15 > 0 || red.XpAt15 > 0)
                ? (float)(blue.XpAt15 - red.XpAt15)
                : goldDiff15 * 0.65f;

            // Towers/dragons MUST be at-15 counts. End-of-game TowerKills/DragonKills are label leakage.
            var towerDiff = (float)(blue.TowersAt15 - red.TowersAt15);
            var dragonDiff = (float)(blue.DragonsAt15 - red.DragonsAt15);

            // After timeline apply, Voidgrub/Herald rows are also at-15 snapshots.
            var voidgrubDiff = (float)(blue.VoidgrubKills - red.VoidgrubKills);
            var heraldDiff = (float)(blue.RiftHeraldKills - red.RiftHeraldKills);

            var blueChamps = match.Participants.Where(p => p.TeamSide == TeamSide.Blue).Select(p => p.ChampionName).ToList();
            var redChamps = match.Participants.Where(p => p.TeamSide == TeamSide.Red).Select(p => p.ChampionName).ToList();

            var (earlyDiff, lateDiff) = ComputeDraftPowerDiffs(blueChamps, redChamps);
            var pastRates = SnapshotWinRates(champWins, champGames);
            var (blueWr, redWr) = ComputeTeamChampionWinRates(blueChamps, redChamps, pastRates);
            var (engageDiff, tankDiff, adApDiff) = ChampionCompositionHelper.ComputeDiffs(blueChamps, redChamps);
            var laneMatchupDiff = ComputeLaneMatchupDiff(match, matchupWins, matchupGames);

            var rankScore = match.ApproxRankScore > 0 ? match.ApproxRankScore : RankScoreHelper.DefaultMixedLobby;
            var goldPace = (blue.GoldAt15 + red.GoldAt15) / 1000f;
            var objectiveScore =
                towerDiff * 1.5f +
                dragonDiff * 1.2f +
                voidgrubDiff * 0.35f +
                heraldDiff * 1.0f;

            // High-elo lobbies convert leads more cleanly; low-elo is noisier.
            var rankAdjustedGold = goldDiff15 * (rankScore / RankScoreHelper.DefaultMixedLobby);

            var levelDiff = match.LevelDiff15 != 0
                ? match.LevelDiff15
                : xpDiff15 / 180f; // rough fallback: ~180 XP ≈ 1 level across a team

            var goldDiff10 = match.GoldDiff10;
            var killDiff10 = match.KillDiff10;
            // If 10' snapshot missing, approximate from 15' (slightly weaker signal, still honest).
            if (goldDiff10 == 0 && goldDiff15 != 0)
                goldDiff10 = goldDiff15 * 0.65f;
            var goldMomentum = goldDiff15 - goldDiff10;
            var winRateDiff = blueWr - redWr;
            var snowball = (killDiff15 * 400f + goldDiff15) / 1000f;
            var killMomentum = killDiff15 - killDiff10;
            var leadVsScaling = (goldDiff15 / 2000f) * (lateDiff / 10f);

            // Carry lead: prefer stored timeline value; fall back to fraction of team gold lead.
            var carryGold = match.CarryGoldDiff15 != 0
                ? match.CarryGoldDiff15
                : goldDiff15 * 0.35f;

            result.Add(new MatchInputData
            {
                FirstBlood = blue.FirstBlood ? 1f : 0f,
                FirstTower = blue.FirstTower ? 1f : 0f,
                FirstDragon = blue.FirstDragon ? 1f : 0f,
                GoldDiff15 = goldDiff15,
                KillDiff15 = killDiff15,
                CsDiff15 = csDiff15,
                XpDiff15 = xpDiff15,
                VoidgrubDiff = voidgrubDiff,
                HeraldDiff = heraldDiff,
                BlueAvgWinRate = blueWr,
                RedAvgWinRate = redWr,
                TowerDiff = towerDiff,
                DragonDiff = dragonDiff,
                EarlyPowerDiff = earlyDiff,
                LatePowerDiff = lateDiff,
                AvgRankScore = rankScore,
                GoldPace15 = goldPace,
                TopGoldDiff15 = match.TopGoldDiff15,
                JungleGoldDiff15 = match.JungleGoldDiff15,
                MidGoldDiff15 = match.MidGoldDiff15,
                BotDuoGoldDiff15 = match.BotDuoGoldDiff15,
                LevelDiff15 = levelDiff,
                ObjectiveScoreDiff = objectiveScore,
                RankAdjustedGoldDiff = rankAdjustedGold,
                GoldDiff10 = goldDiff10,
                KillDiff10 = killDiff10,
                CsDiff10 = match.CsDiff10 != 0
                    ? match.CsDiff10
                    : (match.HasCsXp10Features ? 0f : csDiff15 * 0.65f),
                XpDiff10 = match.XpDiff10 != 0
                    ? match.XpDiff10
                    : (match.HasCsXp10Features ? 0f : xpDiff15 * 0.65f),
                GoldMomentum15 = goldMomentum,
                WinRateDiff = winRateDiff,
                EngageDiff = engageDiff,
                TankDiff = tankDiff,
                AdApBalanceDiff = adApDiff,
                SnowballScore = snowball,
                CarryGoldDiff15 = carryGold,
                FirstBloodTempo = match.FirstBloodTempo,
                FirstTowerTempo = match.FirstTowerTempo,
                FirstDragonTempo = match.FirstDragonTempo,
                FirstDragonValue = match.FirstDragonValue,
                LaneMatchupDiff = laneMatchupDiff,
                PlatesDiff15 = match.PlatesDiff15,
                KillMomentum15 = killMomentum,
                LeadVsScaling = leadVsScaling,
                DeathDiff15 = match.DeathDiff15,
                VisionWardDiff15 = match.VisionWardDiff15,
                ControlWardDiff15 = match.ControlWardDiff15,
                Label = match.WinningTeam == TeamSide.Blue
            });

            // Update rolling WR AFTER featurizing this match so labels never leak into its own features.
            foreach (var p in match.Participants)
            {
                if (string.IsNullOrWhiteSpace(p.ChampionName)) continue;
                champGames.TryGetValue(p.ChampionName, out var g);
                champGames[p.ChampionName] = g + 1;
                if (p.Win)
                {
                    champWins.TryGetValue(p.ChampionName, out var w);
                    champWins[p.ChampionName] = w + 1;
                }
            }

            UpdateLaneMatchups(match, matchupWins, matchupGames);
        }

        return result;
    }

    private static float ComputeLaneMatchupDiff(
        Match match,
        IReadOnlyDictionary<string, int> matchupWins,
        IReadOnlyDictionary<string, int> matchupGames)
    {
        float sum = 0;
        var n = 0;
        foreach (var role in new[] { Position.Top, Position.Jungle, Position.Middle, Position.Bottom, Position.Utility })
        {
            var blue = match.Participants.FirstOrDefault(p => p.TeamSide == TeamSide.Blue && p.Position == role);
            var red = match.Participants.FirstOrDefault(p => p.TeamSide == TeamSide.Red && p.Position == role);
            if (blue == null || red == null) continue;
            if (string.IsNullOrWhiteSpace(blue.ChampionName) || string.IsNullOrWhiteSpace(red.ChampionName)) continue;

            var key = MatchupKey(role, blue.ChampionName, red.ChampionName);
            if (!matchupGames.TryGetValue(key, out var games) || games < 5)
            {
                // Sparse: neutral. Avoid overfitting tiny sample sizes.
                continue;
            }

            var wins = matchupWins.TryGetValue(key, out var w) ? w : 0;
            var raw = 100f * wins / games;
            var weight = Math.Min(1f, games / 20f);
            var shrunk = 50f + (raw - 50f) * weight;
            sum += shrunk - 50f;
            n++;
        }

        return n == 0 ? 0f : sum / n;
    }

    private static void UpdateLaneMatchups(
        Match match,
        Dictionary<string, int> matchupWins,
        Dictionary<string, int> matchupGames)
    {
        var blueWon = match.WinningTeam == TeamSide.Blue;
        foreach (var role in new[] { Position.Top, Position.Jungle, Position.Middle, Position.Bottom, Position.Utility })
        {
            var blue = match.Participants.FirstOrDefault(p => p.TeamSide == TeamSide.Blue && p.Position == role);
            var red = match.Participants.FirstOrDefault(p => p.TeamSide == TeamSide.Red && p.Position == role);
            if (blue == null || red == null) continue;
            if (string.IsNullOrWhiteSpace(blue.ChampionName) || string.IsNullOrWhiteSpace(red.ChampionName)) continue;

            var key = MatchupKey(role, blue.ChampionName, red.ChampionName);
            matchupGames.TryGetValue(key, out var g);
            matchupGames[key] = g + 1;
            if (blueWon)
            {
                matchupWins.TryGetValue(key, out var w);
                matchupWins[key] = w + 1;
            }
        }
    }

    private static string MatchupKey(Position role, string blueChamp, string redChamp) =>
        $"{role}|{blueChamp.Trim()}|{redChamp.Trim()}";

    private static Dictionary<string, float> SnapshotWinRates(
        IReadOnlyDictionary<string, int> wins,
        IReadOnlyDictionary<string, int> games)
    {
        var rates = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (champ, count) in games)
        {
            if (count < 6)
            {
                rates[champ] = 50f;
                continue;
            }

            var w = wins.TryGetValue(champ, out var ww) ? ww : 0;
            // Shrink toward 50% so sparse champs don't overfit.
            var raw = 100f * w / count;
            var weight = Math.Min(1f, count / 25f);
            rates[champ] = 50f + (raw - 50f) * weight;
        }

        return rates;
    }

    public static Dictionary<string, float> BuildChampionWinRates(IEnumerable<Match> matches)
    {
        var wins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var games = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches)
        {
            if (match.IsRemake || match.GameDurationSeconds < 300) continue;
            foreach (var p in match.Participants)
            {
                if (string.IsNullOrWhiteSpace(p.ChampionName)) continue;
                games.TryGetValue(p.ChampionName, out var g);
                games[p.ChampionName] = g + 1;
                if (p.Win)
                {
                    wins.TryGetValue(p.ChampionName, out var w);
                    wins[p.ChampionName] = w + 1;
                }
            }
        }

        return SnapshotWinRates(wins, games);
    }

    public static (float BlueWr, float RedWr) ComputeTeamChampionWinRates(
        IReadOnlyList<string> blueChamps,
        IReadOnlyList<string> redChamps,
        IReadOnlyDictionary<string, float> champWinRates)
    {
        static float Avg(IReadOnlyList<string> champs, IReadOnlyDictionary<string, float> rates)
        {
            if (champs.Count == 0) return 50f;
            var sum = 0f;
            foreach (var c in champs)
                sum += rates.TryGetValue(c, out var r) ? r : 50f;
            return sum / champs.Count;
        }

        return (Avg(blueChamps, champWinRates), Avg(redChamps, champWinRates));
    }

    public static (float EarlyDiff, float LateDiff) ComputeDraftPowerDiffs(
        IReadOnlyList<string> blueChamps,
        IReadOnlyList<string> redChamps)
    {
        if (blueChamps.Count == 0 && redChamps.Count == 0)
            return (0f, 0f);

        var comparison = ChampionScalingAnalyzer.CompareDraftCompositions(blueChamps, redChamps);
        var early = (float)(comparison.BlueTeam.EarlyGamePower - comparison.RedTeam.EarlyGamePower);
        var late = (float)(comparison.BlueTeam.LateGamePower - comparison.RedTeam.LateGamePower);
        return (early, late);
    }
}
