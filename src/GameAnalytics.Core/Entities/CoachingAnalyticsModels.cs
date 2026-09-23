using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Entities;

public class SkillPillarScore
{
    public string PillarName { get; set; } = string.Empty;
    public string Icon { get; set; } = "CMB";
    public int Score { get; set; } = 50;
    public string Grade { get; set; } = "B";
    public string GradeColor { get; set; } = "#5383E8";
    public string PlayerValueText { get; set; } = string.Empty;
    public string BenchmarkText { get; set; } = string.Empty;
    public string DiffText { get; set; } = string.Empty;
    public string DiffColor { get; set; } = "#0AC8B9";
    public string Description { get; set; } = string.Empty;
}

public class RoleBenchmark
{
    public Position Position { get; set; }
    public double TargetCsPerMin { get; set; }
    public int TargetKillParticipation { get; set; }
    public double TargetDpm { get; set; }
    public double MaxTargetDeaths { get; set; }
    public double TargetDamageSharePercent { get; set; }
    /// <summary>Vision score per minute (Support highest, ADC lowest).</summary>
    public double TargetVisionScorePerMin { get; set; }
    public double TargetControlWardsPerGame { get; set; }

    public static RoleBenchmark GetBenchmark(Position position, GameTier tier)
    {
        var baseBenchmark = GetBenchmark(position);
        var tierMultiplier = tier switch
        {
            GameTier.Challenger or GameTier.Grandmaster => 1.12,
            GameTier.Master => 1.08,
            GameTier.Diamond => 1.04,
            GameTier.Emerald => 1.00,
            GameTier.Platinum => 0.94,
            GameTier.Gold => 0.88,
            GameTier.Silver => 0.82,
            GameTier.Bronze or GameTier.Iron => 0.75,
            _ => 1.00
        };

        return new RoleBenchmark
        {
            Position = position,
            TargetCsPerMin = Math.Round(baseBenchmark.TargetCsPerMin * (position == Position.Utility ? 1.0 : tierMultiplier), 1),
            TargetKillParticipation = (int)Math.Round(baseBenchmark.TargetKillParticipation * Math.Min(1.1, tierMultiplier)),
            TargetDpm = Math.Round(baseBenchmark.TargetDpm * tierMultiplier, 0),
            MaxTargetDeaths = Math.Round(baseBenchmark.MaxTargetDeaths / tierMultiplier, 1),
            TargetDamageSharePercent = baseBenchmark.TargetDamageSharePercent,
            TargetVisionScorePerMin = Math.Round(baseBenchmark.TargetVisionScorePerMin * tierMultiplier, 2),
            TargetControlWardsPerGame = Math.Round(baseBenchmark.TargetControlWardsPerGame * tierMultiplier, 1)
        };
    }

    public static RoleBenchmark GetBenchmark(Position position) => position switch
    {
        Position.Jungle => new RoleBenchmark
        {
            Position = Position.Jungle,
            TargetCsPerMin = 6.8,
            TargetKillParticipation = 55,
            TargetDpm = 520,
            MaxTargetDeaths = 4.2,
            TargetDamageSharePercent = 20.0,
            TargetVisionScorePerMin = 0.95,
            TargetControlWardsPerGame = 3.5
        },
        Position.Middle => new RoleBenchmark
        {
            Position = Position.Middle,
            TargetCsPerMin = 7.6,
            TargetKillParticipation = 52,
            TargetDpm = 680,
            MaxTargetDeaths = 4.0,
            TargetDamageSharePercent = 24.5,
            TargetVisionScorePerMin = 0.55,
            TargetControlWardsPerGame = 2.0
        },
        Position.Bottom => new RoleBenchmark
        {
            Position = Position.Bottom,
            TargetCsPerMin = 8.2,
            TargetKillParticipation = 50,
            TargetDpm = 750,
            MaxTargetDeaths = 3.8,
            TargetDamageSharePercent = 27.0,
            TargetVisionScorePerMin = 0.45,
            TargetControlWardsPerGame = 1.5
        },
        Position.Top => new RoleBenchmark
        {
            Position = Position.Top,
            TargetCsPerMin = 7.4,
            TargetKillParticipation = 45,
            TargetDpm = 580,
            MaxTargetDeaths = 4.5,
            TargetDamageSharePercent = 21.0,
            TargetVisionScorePerMin = 0.50,
            TargetControlWardsPerGame = 2.0
        },
        Position.Utility => new RoleBenchmark
        {
            Position = Position.Utility,
            TargetCsPerMin = 1.5,
            TargetKillParticipation = 62,
            TargetDpm = 320,
            MaxTargetDeaths = 4.8,
            TargetDamageSharePercent = 9.5,
            TargetVisionScorePerMin = 1.45,
            TargetControlWardsPerGame = 6.0
        },
        _ => new RoleBenchmark
        {
            Position = Position.Middle,
            TargetCsPerMin = 7.0,
            TargetKillParticipation = 50,
            TargetDpm = 600,
            MaxTargetDeaths = 4.0,
            TargetDamageSharePercent = 20.0,
            TargetVisionScorePerMin = 0.60,
            TargetControlWardsPerGame = 2.0
        }
    };
}

public class GlobalCoachingReport
{
    public int OverallScore { get; set; } = 50;
    public string OverallGrade { get; set; } = "B";
    public string OverallGradeColor { get; set; } = "#5383E8";
    public Position PrimaryRole { get; set; } = Position.Middle;
    public string RoleName { get; set; } = "MID";
    public string RoleIcon { get; set; } = "MID";

    public SkillPillarScore Combat { get; set; } = new();
    public SkillPillarScore Economy { get; set; } = new();
    public SkillPillarScore Objectives { get; set; } = new();
    public SkillPillarScore Survival { get; set; } = new();
    public SkillPillarScore Vision { get; set; } = new();

    public int TotalMatchesAnalyzed { get; set; }
    public string CoachAdvice { get; set; } = string.Empty;
    public List<string> Highlights { get; set; } = new();
}
