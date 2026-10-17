using System.Text.Json;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.Core.Helpers;

/// <summary>25th / 50th / 75th percentile of one stat among players of a role and rank.</summary>
public sealed record MetricPercentiles(double P25, double P50, double P75)
{
    /// <summary>
    /// Rough percentile (1–99) of <paramref name="value"/> in this distribution, linear between the
    /// stored quartiles and extrapolated outside them. With <paramref name="lowerIsBetter"/> the result
    /// is flipped, so 90 always means "better than 90% of players".
    /// </summary>
    public int EstimatePercentile(double value, bool lowerIsBetter = false)
    {
        double pct;
        if (value <= P50)
        {
            var span = P50 - P25;
            pct = span > 0 ? 50 - (P50 - value) / span * 25 : (value < P50 ? 25 : 50);
        }
        else
        {
            var span = P75 - P50;
            pct = span > 0 ? 50 + (value - P50) / span * 25 : 75;
        }

        pct = Math.Clamp(pct, 1, 99);
        return (int)Math.Round(lowerIsBetter ? 100 - pct : pct);
    }
}

public sealed class RoleTierBaseline
{
    public Position Position { get; init; }
    /// <summary>Tier the numbers come from; null when the all-ranks pool was used.</summary>
    public GameTier? Tier { get; init; }
    public int Games { get; init; }
    public MetricPercentiles? CsPerMin { get; init; }
    public MetricPercentiles? KillParticipation { get; init; }
    public MetricPercentiles? Deaths { get; init; }
    public MetricPercentiles? DamageShare { get; init; }
    public MetricPercentiles? VisionPerMin { get; init; }
    public MetricPercentiles? LaneGoldDiff15 { get; init; }
}

/// <summary>
/// Real per-role, per-rank stat distributions measured on the ranked crawl
/// (embedded <c>Data/role_baselines.json</c>), used instead of hand-picked targets.
/// </summary>
public static class RoleBaselines
{
    private const string ResourceName = "GameAnalytics.Core.Data.role_baselines.json";
    private static readonly Lazy<Dictionary<(Position, string), RoleTierBaseline>> Table = new(Load);

    /// <summary>Baseline for the role at the given tier, or the all-ranks pool for Unranked / unknown tiers.</summary>
    public static RoleTierBaseline? Get(Position position, GameTier tier)
    {
        var table = Table.Value;
        if (tier != GameTier.Unranked && table.TryGetValue((position, tier.ToString()), out var exact))
            return exact;
        return table.TryGetValue((position, "All"), out var all) ? all : null;
    }

    private static Dictionary<(Position, string), RoleTierBaseline> Load()
    {
        var result = new Dictionary<(Position, string), RoleTierBaseline>();
        using var stream = typeof(RoleBaselines).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            return result;

        using var doc = JsonDocument.Parse(stream);
        foreach (var role in doc.RootElement.GetProperty("Roles").EnumerateObject())
        {
            if (!Enum.TryParse<Position>(role.Name, out var position))
                continue;

            foreach (var tierEntry in role.Value.EnumerateObject())
            {
                var e = tierEntry.Value;
                GameTier? tier = Enum.TryParse<GameTier>(tierEntry.Name, out var t) ? t : null;
                result[(position, tierEntry.Name)] = new RoleTierBaseline
                {
                    Position = position,
                    Tier = tier,
                    Games = e.TryGetProperty("Games", out var g) ? g.GetInt32() : 0,
                    CsPerMin = Read(e, "CsPerMin"),
                    KillParticipation = Read(e, "KillParticipation"),
                    Deaths = Read(e, "Deaths"),
                    DamageShare = Read(e, "DamageShare"),
                    VisionPerMin = Read(e, "VisionPerMin"),
                    LaneGoldDiff15 = Read(e, "LaneGoldDiff15")
                };
            }
        }

        return result;
    }

    private static MetricPercentiles? Read(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var arr) || arr.GetArrayLength() != 3)
            return null;
        return new MetricPercentiles(arr[0].GetDouble(), arr[1].GetDouble(), arr[2].GetDouble());
    }
}
