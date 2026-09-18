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
            _context.Database.ExecuteSqlRaw("ALTER TABLE Matches ADD COLUMN IsRemake INTEGER NOT NULL DEFAULT 0;");
        }
        catch { /* Column already exists */ }
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

    public async Task<int> GetTotalMatchesCountAsync(CancellationToken ct = default)
    {
        return await _context.Matches.CountAsync(ct);
    }

    public async Task ClearAllMatchesAsync(CancellationToken ct = default)
    {
        _context.Matches.RemoveRange(_context.Matches);
        await _context.SaveChangesAsync(ct);
    }
}

