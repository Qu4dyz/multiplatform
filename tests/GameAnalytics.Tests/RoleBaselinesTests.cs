using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;
using Xunit;

namespace GameAnalytics.Tests;

public class RoleBaselinesTests
{
    [Theory]
    [InlineData(Position.Top)]
    [InlineData(Position.Jungle)]
    [InlineData(Position.Middle)]
    [InlineData(Position.Bottom)]
    [InlineData(Position.Utility)]
    public void Get_EveryRoleHasMeasuredAllRanksBaseline(Position position)
    {
        var baseline = RoleBaselines.Get(position, GameTier.Unranked);

        Assert.NotNull(baseline);
        Assert.Null(baseline!.Tier);
        Assert.True(baseline.Games > 1000);
        Assert.NotNull(baseline.CsPerMin);
        Assert.NotNull(baseline.Deaths);
        Assert.True(baseline.CsPerMin!.P25 <= baseline.CsPerMin.P50 && baseline.CsPerMin.P50 <= baseline.CsPerMin.P75);
    }

    [Fact]
    public void Get_KnownTier_ReturnsThatTiersNumbers()
    {
        var platinum = RoleBaselines.Get(Position.Jungle, GameTier.Platinum);
        var master = RoleBaselines.Get(Position.Jungle, GameTier.Master);

        Assert.Equal(GameTier.Platinum, platinum!.Tier);
        Assert.True(master!.CsPerMin!.P50 > platinum.CsPerMin!.P50);
    }

    [Fact]
    public void Benchmark_UsesMeasuredMedians()
    {
        var benchmark = RoleBenchmark.GetBenchmark(Position.Middle, GameTier.Gold);
        var baseline = RoleBaselines.Get(Position.Middle, GameTier.Gold)!;

        Assert.Same(baseline, benchmark.Baseline);
        Assert.Equal(Math.Round(baseline.CsPerMin!.P50, 1), benchmark.TargetCsPerMin);
        Assert.Equal(Math.Round(baseline.Deaths!.P50, 1), benchmark.MaxTargetDeaths);
    }

    [Theory]
    [InlineData(5.0, false, 50)]
    [InlineData(4.0, false, 25)]
    [InlineData(6.0, false, 75)]
    [InlineData(4.0, true, 75)]
    [InlineData(100.0, false, 99)]
    public void EstimatePercentile_InterpolatesBetweenQuartiles(double value, bool lowerIsBetter, int expected)
    {
        var dist = new MetricPercentiles(4.0, 5.0, 6.0);

        Assert.Equal(expected, dist.EstimatePercentile(value, lowerIsBetter));
    }
}
