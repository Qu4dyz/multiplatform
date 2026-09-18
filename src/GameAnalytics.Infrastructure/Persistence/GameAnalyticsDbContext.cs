using GameAnalytics.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameAnalytics.Infrastructure.Persistence;

public class GameAnalyticsDbContext : DbContext
{
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<TeamStats> TeamStats => Set<TeamStats>();

    private readonly string _dbPath;

    public GameAnalyticsDbContext()
    {
        _dbPath = ResolveDatabasePath();
    }

    public GameAnalyticsDbContext(DbContextOptions<GameAnalyticsDbContext> options)
        : base(options)
    {
        _dbPath = ResolveDatabasePath();
    }

    private static string ResolveDatabasePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var appPath = Path.Combine(baseDir, "game_analytics.db");
        if (File.Exists(appPath)) return appPath;

        var projPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "game_analytics.db");
        if (File.Exists(projPath)) return projPath;

        var rootPath = Path.Combine(Directory.GetCurrentDirectory(), "game_analytics.db");
        if (File.Exists(rootPath)) return rootPath;

        return appPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.MatchId).IsUnique();
            entity.Property(m => m.MatchId).IsRequired().HasMaxLength(64);
            entity.Property(m => m.GameVersion).HasMaxLength(32);

            entity.HasMany(m => m.Participants)
                  .WithOne(p => p.Match)
                  .HasForeignKey(p => p.MatchEntityId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(m => m.Teams)
                  .WithOne(t => t.Match)
                  .HasForeignKey(t => t.MatchEntityId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Participant>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.SummonerName).HasMaxLength(128);
            entity.Property(p => p.ChampionName).HasMaxLength(64);
            entity.Property(p => p.Puuid).HasMaxLength(128);
        });

        modelBuilder.Entity<TeamStats>(entity =>
        {
            entity.HasKey(t => t.Id);
        });
    }
}

