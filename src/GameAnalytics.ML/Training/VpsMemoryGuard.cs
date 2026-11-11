using System.Globalization;

namespace GameAnalytics.ML.Training;

/// <summary>
/// Pre-flight RAM check before heavy FastTree lab trains on 1–2GB Linux VPS hosts.
/// Reads MemAvailable and/or cgroup memory.current vs high/max when present.
/// </summary>
public static class VpsMemoryGuard
{
    /// <summary>Skip lab if MemAvailable is below this (bytes).</summary>
    public const long MinAvailableBytes = 280L * 1024 * 1024;

    /// <summary>Skip lab if cgroup current usage exceeds this fraction of high/max.</summary>
    public const double MaxCgroupUsageFraction = 0.82;

    /// <summary>
    /// Returns whether it is safe to start a lab FastTree train.
    /// On Linux: skip when pressure is high OR when memory metrics cannot be read (safe default).
    /// On non-Linux (dev): allow when metrics are missing.
    /// </summary>
    public static bool ShouldAllowHeavyTrain(out string reason)
    {
        var isLinux = OperatingSystem.IsLinux();
        long? memAvailable = TryReadMemAvailableBytes();
        var (cgroupCurrent, cgroupLimit) = TryReadCgroupMemory();

        if (cgroupCurrent is > 0 && cgroupLimit is > 0)
        {
            var fraction = (double)cgroupCurrent.Value / cgroupLimit.Value;
            if (fraction >= MaxCgroupUsageFraction)
            {
                reason =
                    $"cgroup memory {FormatMiB(cgroupCurrent.Value)}/{FormatMiB(cgroupLimit.Value)} " +
                    $"({fraction:P0} ≥ {MaxCgroupUsageFraction:P0})";
                return false;
            }
        }

        if (memAvailable is long avail)
        {
            if (avail < MinAvailableBytes)
            {
                reason = $"MemAvailable {FormatMiB(avail)} < {FormatMiB(MinAvailableBytes)}";
                return false;
            }

            reason =
                cgroupCurrent is > 0 && cgroupLimit is > 0
                    ? $"MemAvailable {FormatMiB(avail)}; cgroup {FormatMiB(cgroupCurrent.Value)}/{FormatMiB(cgroupLimit.Value)}"
                    : $"MemAvailable {FormatMiB(avail)}";
            return true;
        }

        // No MemAvailable — if cgroup looked fine above, allow; else Linux → skip.
        if (cgroupCurrent is > 0 && cgroupLimit is > 0)
        {
            reason = $"cgroup {FormatMiB(cgroupCurrent.Value)}/{FormatMiB(cgroupLimit.Value)} (no MemAvailable)";
            return true;
        }

        if (isLinux)
        {
            reason = "Linux memory metrics unavailable — skip lab (safe default)";
            return false;
        }

        reason = "non-Linux / metrics unavailable — allow lab";
        return true;
    }

    /// <summary>Parse MemAvailable from a /proc/meminfo-style blob (kB → bytes).</summary>
    public static long? ParseMemAvailableBytes(string memInfoText)
    {
        if (string.IsNullOrWhiteSpace(memInfoText))
            return null;

        foreach (var line in memInfoText.Split('\n'))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // MemAvailable: 301234 kB
            if (parts.Length >= 2 &&
                long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
            {
                return kb * 1024;
            }
        }

        return null;
    }

    /// <summary>Parse a single cgroup memory counter file (plain integer bytes).</summary>
    public static long? ParseCgroupBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (trimmed.Equals("max", StringComparison.OrdinalIgnoreCase))
            return null;

        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : null;
    }

    internal static long? TryReadMemAvailableBytes()
    {
        try
        {
            if (!File.Exists("/proc/meminfo"))
                return null;
            return ParseMemAvailableBytes(File.ReadAllText("/proc/meminfo"));
        }
        catch
        {
            return null;
        }
    }

    internal static (long? Current, long? Limit) TryReadCgroupMemory()
    {
        try
        {
            // cgroup v2 (systemd unified)
            var currentPath = "/sys/fs/cgroup/memory.current";
            var highPath = "/sys/fs/cgroup/memory.high";
            var maxPath = "/sys/fs/cgroup/memory.max";

            // Per-service slice often under memory.current relative to process cgroup
            var procCgroup = TryResolveProcessCgroupV2Dir();
            if (procCgroup != null)
            {
                currentPath = Path.Combine(procCgroup, "memory.current");
                highPath = Path.Combine(procCgroup, "memory.high");
                maxPath = Path.Combine(procCgroup, "memory.max");
            }

            long? current = File.Exists(currentPath) ? ParseCgroupBytes(File.ReadAllText(currentPath)) : null;
            long? high = File.Exists(highPath) ? ParseCgroupBytes(File.ReadAllText(highPath)) : null;
            long? max = File.Exists(maxPath) ? ParseCgroupBytes(File.ReadAllText(maxPath)) : null;

            // Prefer high (throttle) when set; else max (hard limit).
            var limit = high ?? max;
            return (current, limit);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? TryResolveProcessCgroupV2Dir()
    {
        try
        {
            if (!File.Exists("/proc/self/cgroup"))
                return null;

            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                // v2: 0::/system.slice/gameanalytics-trainer.service
                if (!line.StartsWith("0::", StringComparison.Ordinal))
                    continue;
                var rel = line[3..].Trim();
                if (string.IsNullOrEmpty(rel) || rel == "/")
                    return "/sys/fs/cgroup";
                var dir = Path.Combine("/sys/fs/cgroup", rel.TrimStart('/'));
                return Directory.Exists(dir) ? dir : "/sys/fs/cgroup";
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string FormatMiB(long bytes) => $"{bytes / (1024.0 * 1024.0):F0}MiB";
}
