using GameAnalytics.ML.Training;
using Xunit;

namespace GameAnalytics.Tests;

public class VpsMemoryGuardTests
{
    private const long MiB = 1024 * 1024;

    [Theory]
    // cgroup at 99% blocks even with plenty of MemAvailable (the 2GB VPS case from the logs)
    [InlineData(1266L, 1381L, 1400L, true, false, "cgroup")]
    // low MemAvailable blocks
    [InlineData(100L, null, null, true, false, "MemAvailable")]
    // enough MemAvailable, cgroup under the threshold
    [InlineData(900L, 500L, 1400L, true, true, "cgroup")]
    // enough MemAvailable, no cgroup
    [InlineData(900L, null, null, true, true, "MemAvailable")]
    // cgroup fine, MemAvailable unknown
    [InlineData(null, 500L, 1400L, true, true, "no MemAvailable")]
    // nothing readable: Linux stays safe, dev machines allow
    [InlineData(null, null, null, true, false, "safe default")]
    [InlineData(null, null, null, false, true, "non-Linux")]
    public void Decide_CoversEveryBranch(long? availMiB, long? currentMiB, long? limitMiB, bool isLinux, bool expected, string reasonPart)
    {
        var allowed = VpsMemoryGuard.Decide(availMiB * MiB, currentMiB * MiB, limitMiB * MiB, isLinux, out var reason);

        Assert.Equal(expected, allowed);
        Assert.Contains(reasonPart, reason);
    }

    [Fact]
    public void ShouldAllowHeavyTrain_OnThisMachine_GivesAReason()
    {
        VpsMemoryGuard.ShouldAllowHeavyTrain(out var reason);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
