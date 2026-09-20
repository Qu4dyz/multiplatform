using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Interfaces;
using GameAnalytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GameAnalytics.Infrastructure.Repositories;

public class MatchRepository : IMatchRepository
{
    private readonly GameAnalyticsDbContext _context;

    public MatchRepository(GameAnalyticsDbContext context)
    {
        _context = context;
        _context.Database.EnsureCreated();
        ApplyMissingColumns();
    }

    private void ApplyMissingColumns()
    {
        try
        {
            _context.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN QueueId INTEGER NOT NULL DEFAULT 420;");
        }
        catch { /* Column already exists */ }

        try
        {
            _context.Database.ExecuteSqlRaw("ALTER TABLE Participants ADD COLUMN ChampLevel INTEGER NOT NULL DEFAULT 1;");
        }
        catch { /* Column already exists */ }

        try
        {
            _context.Database.ExecuteSqlRaw("ALTER TABLE Participants ADD COLUMN ParticipantId INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* Column already exists */ }

        try
        {
            _context.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN IsRemake INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* Column already exists */ }

        string[] itemColumns =
        {
            "ALTER TABLE Participants ADD COLUMN Item0 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item1 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item2 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item3 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item4 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item5 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Item6 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN Summoner1Id INTEGER NOT NULL DEFAULT 4;",
            "ALTER TABLE Participants ADD COLUMN Summoner2Id INTEGER NOT NULL DEFAULT 14;",
            "ALTER TABLE Participants ADD COLUMN PrimaryRuneId INTEGER NOT NULL DEFAULT 8010;",
            "ALTER TABLE Participants ADD COLUMN SecondaryRuneStyleId INTEGER NOT NULL DEFAULT 8100;",
            "ALTER TABLE Participants ADD COLUMN VisionScore INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN WardsPlaced INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN WardsKilled INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN ControlWardsBought INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN PhysicalDamageDealtToChampions INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN MagicDamageDealtToChampions INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN TrueDamageDealtToChampions INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN TotalDamageTaken INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DamageSelfMitigated INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DamageDealtToObjectives INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DamageDealtToTurrets INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DoubleKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN TripleKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN QuadraKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN PentaKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN SoloKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN TurretPlatesTaken INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN KillParticipation REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN VisionScorePerMinute REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN GoldPerMinute REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DamagePerMinute REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN TeamDamagePercentage REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN ControlWardsPlaced INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN EffectiveHealAndShielding INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Participants ADD COLUMN DamageDealtToObjectivesChallenge INTEGER NOT NULL DEFAULT 0;"
        };

        foreach (var sql in itemColumns)
        {
            try
            {
                _context.Database.ExecuteSqlRaw(sql);
            }
            catch { /* Column already exists */ }
        }

        try
        {
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS TeamStats (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MatchEntityId INTEGER NOT NULL,
                    TeamSide INTEGER NOT NULL,
                    Win INTEGER NOT NULL DEFAULT 0,
                    FirstBlood INTEGER NOT NULL DEFAULT 0,
                    FirstTower INTEGER NOT NULL DEFAULT 0,
                    FirstDragon INTEGER NOT NULL DEFAULT 0,
                    FirstBaron INTEGER NOT NULL DEFAULT 0,
                    TowerKills INTEGER NOT NULL DEFAULT 0,
                    DragonKills INTEGER NOT NULL DEFAULT 0,
                    BaronKills INTEGER NOT NULL DEFAULT 0,
                    GoldAt15 INTEGER NOT NULL DEFAULT 0,
                    KillsAt15 INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (MatchEntityId) REFERENCES Matches(Id) ON DELETE CASCADE
                );");
        }
        catch { /* Table already exists */ }

        string[] teamColumns =
        {
            "ALTER TABLE TeamStats ADD COLUMN GoldAt15 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN KillsAt15 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN CsAt15 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN XpAt15 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN VoidgrubKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN RiftHeraldKills INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN FirstVoidgrub INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN FirstRiftHerald INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN TowersAt15 INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE TeamStats ADD COLUMN DragonsAt15 INTEGER NOT NULL DEFAULT 0;"
        };

        foreach (var sql in teamColumns)
        {
            try
            {
                _context.Database.ExecuteSqlRaw(sql);
            }
            catch { /* Column already exists */ }
        }

        try
        {
            _context.Database.ExecuteSqlRaw(
                "ALTER TABLE Matches ADD COLUMN HasMinute15Objectives INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* Column already exists */ }

        string[] matchMlColumns =
        {
            "ALTER TABLE Matches ADD COLUMN ApproxRankScore REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN TopGoldDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN JungleGoldDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN MidGoldDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN BotDuoGoldDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN LevelDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN GoldDiff10 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN KillDiff10 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN CarryGoldDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN FirstBloodTempo REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN FirstTowerTempo REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN FirstDragonTempo REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN FirstDragonValue REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN PlatesDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN DeathDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN VisionWardDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN ControlWardDiff15 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN HasVisionDeathFeatures INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN CsDiff10 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN XpDiff10 REAL NOT NULL DEFAULT 0;",
            "ALTER TABLE Matches ADD COLUMN HasCsXp10Features INTEGER NOT NULL DEFAULT 0;"
        };
        foreach (var sql in matchMlColumns)
        {
            try { _context.Database.ExecuteSqlRaw(sql); }
            catch { /* Column already exists */ }
        }

        try
        {
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS PlayerLpSnapshots (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Puuid TEXT NOT NULL,
                    QueueId INTEGER NOT NULL DEFAULT 420,
                    LeaguePoints INTEGER NOT NULL DEFAULT 0,
                    Tier INTEGER NOT NULL DEFAULT 6,
                    Rank TEXT NOT NULL DEFAULT 'IV',
                    Wins INTEGER NOT NULL DEFAULT 0,
                    Losses INTEGER NOT NULL DEFAULT 0,
                    LastMatchId TEXT NOT NULL DEFAULT '',
                    LastUpdatedAt TEXT NOT NULL
                );");
        }
        catch { /* Table already exists */ }

        try
        {
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS PlayerMatchLpRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Puuid TEXT NOT NULL,
                    MatchId TEXT NOT NULL,
                    QueueId INTEGER NOT NULL DEFAULT 420,
                    LpDelta INTEGER NOT NULL DEFAULT 0,
                    LeaguePointsAfter INTEGER NOT NULL DEFAULT 0,
                    RecordedAt TEXT NOT NULL
                );");
        }
        catch { /* Table already exists */ }

        try
        {
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS MatchTimelineCaches (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MatchId TEXT NOT NULL UNIQUE,
                    TimelineJson TEXT NOT NULL DEFAULT '',
                    CachedAtUtc TEXT NOT NULL
                );");
        }
        catch { /* Table already exists */ }
    }

    public async Task<IReadOnlyList<Match>> GetAllMatchesAsync(CancellationToken ct = default)
    {
        return await _context.Matches
            .Include(m => m.Participants)
            .Include(m => m.Teams)
            .OrderByDescending(m => m.GameCreation)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<Match?> GetMatchByMatchIdAsync(string matchId, CancellationToken ct = default)
    {
        return await _context.Matches
            .Include(m => m.Participants)
            .Include(m => m.Teams)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.MatchId == matchId, ct);
    }

    public async Task SaveMatchAsync(Match match, CancellationToken ct = default)
    {
        var existing = await _context.Matches.AnyAsync(m => m.MatchId == match.MatchId, ct);
        if (existing)
        {
            return; // Duplicate match protection
        }

        await _context.Matches.AddAsync(match, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpsertMatchAsync(Match match, CancellationToken ct = default)
    {
        var existing = await _context.Matches
            .Include(m => m.Participants)
            .Include(m => m.Teams)
            .FirstOrDefaultAsync(m => m.MatchId == match.MatchId, ct);

        if (existing != null)
        {
            _context.Matches.Remove(existing);
            await _context.SaveChangesAsync(ct);
        }

        await _context.Matches.AddAsync(match, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> GetTotalMatchesCountAsync(CancellationToken ct = default)
    {
        return await _context.Matches.CountAsync(ct);
    }

    public async Task ClearAllMatchesAsync(CancellationToken ct = default)
    {
        _context.Matches.RemoveRange(_context.Matches);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetDistinctParticipantPuuidsAsync(int limit = 50, CancellationToken ct = default)
    {
        // Pull a larger pool then randomize so each training epoch explores different players
        // instead of always replaying the same first N rows from SQLite.
        var poolSize = Math.Max(limit * 8, 200);
        var pool = await _context.Participants
            .AsNoTracking()
            .Where(p => !string.IsNullOrEmpty(p.Puuid) && !p.Puuid.StartsWith("puuid-"))
            .Select(p => p.Puuid)
            .Distinct()
            .Take(poolSize)
            .ToListAsync(ct);

        return pool
            .OrderBy(_ => Random.Shared.Next())
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetMatchIdsNeedingTimelineBackfillAsync(int limit = 50, CancellationToken ct = default)
    {
        // Prefer CS/XP@10 gaps first (lowest coverage), then vision/death, randomized within each tier.
        var missingCsXp = await _context.Matches
            .AsNoTracking()
            .Where(m => m.HasMinute15Objectives && !m.HasCsXp10Features)
            .Select(m => m.MatchId)
            .ToListAsync(ct);

        var missingVision = await _context.Matches
            .AsNoTracking()
            .Where(m => m.HasMinute15Objectives && m.HasCsXp10Features && !m.HasVisionDeathFeatures)
            .Select(m => m.MatchId)
            .ToListAsync(ct);

        var ordered = missingCsXp
            .OrderBy(_ => Random.Shared.Next())
            .Concat(missingVision.OrderBy(_ => Random.Shared.Next()))
            .Take(limit)
            .ToList();

        return ordered;
    }

    public async Task<IReadOnlyDictionary<string, int>> GetPlayerMatchLpMapAsync(string puuid, CancellationToken ct = default)
    {
        return await _context.PlayerMatchLpRecords
            .Where(r => r.Puuid == puuid)
            .ToDictionaryAsync(r => r.MatchId, r => r.LpDelta, ct);
    }

    public async Task SaveMatchLpRecordAsync(PlayerMatchLpRecord record, CancellationToken ct = default)
    {
        var existing = await _context.PlayerMatchLpRecords
            .FirstOrDefaultAsync(r => r.Puuid == record.Puuid && r.MatchId == record.MatchId, ct);

        if (existing != null)
        {
            existing.LpDelta = record.LpDelta;
            existing.LeaguePointsAfter = record.LeaguePointsAfter;
            existing.RecordedAt = DateTime.UtcNow;
        }
        else
        {
            await _context.PlayerMatchLpRecords.AddAsync(record, ct);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<PlayerLpSnapshot?> GetLpSnapshotAsync(string puuid, int queueId = 420, CancellationToken ct = default)
    {
        return await _context.PlayerLpSnapshots
            .FirstOrDefaultAsync(s => s.Puuid == puuid && s.QueueId == queueId, ct);
    }

    public async Task SaveLpSnapshotAsync(PlayerLpSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await _context.PlayerLpSnapshots
            .FirstOrDefaultAsync(s => s.Puuid == snapshot.Puuid && s.QueueId == snapshot.QueueId, ct);

        if (existing != null)
        {
            existing.LeaguePoints = snapshot.LeaguePoints;
            existing.Tier = snapshot.Tier;
            existing.Rank = snapshot.Rank;
            existing.Wins = snapshot.Wins;
            existing.Losses = snapshot.Losses;
            existing.LastMatchId = snapshot.LastMatchId;
            existing.LastUpdatedAt = DateTime.UtcNow;
        }
        else
        {
            await _context.PlayerLpSnapshots.AddAsync(snapshot, ct);
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task<MatchTimelineData?> GetCachedTimelineAsync(string matchId, CancellationToken ct = default)
    {
        var row = await _context.MatchTimelineCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.MatchId == matchId, ct);
        if (row == null || string.IsNullOrWhiteSpace(row.TimelineJson))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<MatchTimelineData>(row.TimelineJson);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveTimelineCacheAsync(string matchId, MatchTimelineData data, CancellationToken ct = default)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var existing = await _context.MatchTimelineCaches
            .FirstOrDefaultAsync(c => c.MatchId == matchId, ct);

        if (existing != null)
        {
            existing.TimelineJson = json;
            existing.CachedAtUtc = DateTime.UtcNow;
        }
        else
        {
            await _context.MatchTimelineCaches.AddAsync(new MatchTimelineCache
            {
                MatchId = matchId,
                TimelineJson = json,
                CachedAtUtc = DateTime.UtcNow
            }, ct);
        }

        await _context.SaveChangesAsync(ct);
    }
}

