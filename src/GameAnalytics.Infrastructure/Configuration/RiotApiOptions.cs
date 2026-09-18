using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameAnalytics.Infrastructure.Configuration;

public class ServerRegionInfo
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string RoutingRegion { get; set; } = string.Empty;
    public string DefaultTag { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public class RiotApiOptions
{
    public const string SectionName = "RiotApi";

    public string ApiKey { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = "euw1";
    public string RoutingRegion { get; set; } = "europe";
    public bool UseMockFallback { get; set; } = true;
    public string VpsServerUrl { get; set; } = "http://45.77.53.46:5050";

    // Configurable demo stats when offline / without Riot API key
    public GameAnalytics.Core.Enums.GameTier DemoTier { get; set; } = GameAnalytics.Core.Enums.GameTier.Gold;
    public string DemoRank { get; set; } = "II";
    public int DemoLp { get; set; } = 48;

    [JsonIgnore]
    public bool HasValidApiKey => !string.IsNullOrWhiteSpace(ApiKey) &&
                                  !ApiKey.StartsWith("RGAPI-XXXX", StringComparison.OrdinalIgnoreCase) &&
                                  ApiKey.Length > 20;

    public static readonly IReadOnlyList<ServerRegionInfo> AvailableRegions = new[]
    {
        new ServerRegionInfo { Code = "EUW", DisplayName = "🇪🇺 EUW - Europe West", PlatformId = "euw1", RoutingRegion = "europe", DefaultTag = "EUW" },
        new ServerRegionInfo { Code = "EUNE", DisplayName = "🇪🇺 EUNE - Europe Nordic & East", PlatformId = "eun1", RoutingRegion = "europe", DefaultTag = "EUNE" },
        new ServerRegionInfo { Code = "NA", DisplayName = "🇺🇸 NA - North America", PlatformId = "na1", RoutingRegion = "americas", DefaultTag = "NA1" },
        new ServerRegionInfo { Code = "KR", DisplayName = "🇰🇷 KR - Korea", PlatformId = "kr", RoutingRegion = "asia", DefaultTag = "KR1" },
        new ServerRegionInfo { Code = "TR", DisplayName = "🇹🇷 TR - Turkey", PlatformId = "tr1", RoutingRegion = "europe", DefaultTag = "TR1" },
        new ServerRegionInfo { Code = "RU", DisplayName = "🇷🇺 RU - Russia", PlatformId = "ru", RoutingRegion = "europe", DefaultTag = "RU1" },
        new ServerRegionInfo { Code = "OCE", DisplayName = "🇦🇺 OCE - Oceania", PlatformId = "oc1", RoutingRegion = "sea", DefaultTag = "OCE" },
        new ServerRegionInfo { Code = "BR", DisplayName = "🇧🇷 BR - Brazil", PlatformId = "br1", RoutingRegion = "americas", DefaultTag = "BR1" },
        new ServerRegionInfo { Code = "LAN", DisplayName = "🇲🇽 LAN - Latin America North", PlatformId = "la1", RoutingRegion = "americas", DefaultTag = "LAN" },
        new ServerRegionInfo { Code = "LAS", DisplayName = "🇨🇱 LAS - Latin America South", PlatformId = "la2", RoutingRegion = "americas", DefaultTag = "LAS" },
        new ServerRegionInfo { Code = "JP", DisplayName = "🇯🇵 JP - Japan", PlatformId = "jp1", RoutingRegion = "asia", DefaultTag = "JP1" }
    };

    public void SetRegionByCode(string code)
    {
        var reg = AvailableRegions.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase) ||
                                                       r.PlatformId.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (reg != null)
        {
            PlatformRegion = reg.PlatformId;
            RoutingRegion = reg.RoutingRegion;
        }
    }

    public static RiotApiOptions LoadFromFile(string filePath)
    {
        var options = LoadSingle(filePath) ?? new RiotApiOptions();

        // 1. Check local override file (appsettings.local.json)
        var dir = Path.GetDirectoryName(filePath);
        string[] candidateLocalPaths =
        [
            !string.IsNullOrEmpty(dir) ? Path.Combine(dir, "appsettings.local.json") : "appsettings.local.json",
            Path.Combine(Directory.GetCurrentDirectory(), "src", "GameAnalytics.Desktop", "appsettings.local.json")
        ];

        foreach (var localPath in candidateLocalPaths)
        {
            if (File.Exists(localPath))
            {
                var localOptions = LoadSingle(localPath);
                if (localOptions != null)
                {
                    if (!string.IsNullOrWhiteSpace(localOptions.ApiKey))
                        options.ApiKey = localOptions.ApiKey;
                    if (!string.IsNullOrWhiteSpace(localOptions.PlatformRegion))
                        options.PlatformRegion = localOptions.PlatformRegion;
                    if (!string.IsNullOrWhiteSpace(localOptions.RoutingRegion))
                        options.RoutingRegion = localOptions.RoutingRegion;
                    if (!string.IsNullOrWhiteSpace(localOptions.VpsServerUrl))
                        options.VpsServerUrl = localOptions.VpsServerUrl;
                    options.UseMockFallback = localOptions.UseMockFallback;
                    options.DemoTier = localOptions.DemoTier;
                    break;
                }
            }
        }

        // 2. Check environment variable override
        var envKey = Environment.GetEnvironmentVariable("RIOT_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            options.ApiKey = envKey.Trim();
        }

        return options;
    }

    private static RiotApiOptions? LoadSingle(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("RiotApi", out var section))
                {
                    return JsonSerializer.Deserialize<RiotApiOptions>(section.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }
        }
        catch
        {
            // Fall back
        }

        return null;
    }

    public void SaveToFile(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            var localPath = !string.IsNullOrEmpty(dir)
                ? Path.Combine(dir, "appsettings.local.json")
                : "appsettings.local.json";

            WriteConfig(localPath);
            WriteConfig(filePath);
        }
        catch
        {
            // Ignore disk write errors if read-only
        }
    }

    private void WriteConfig(string path)
    {
        var config = new Dictionary<string, object>();
        if (File.Exists(path))
        {
            var existingJson = File.ReadAllText(path);
            var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson);
            if (existing != null) config = existing;
        }

        config["RiotApi"] = this;
        var updatedJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, updatedJson);
    }
}
