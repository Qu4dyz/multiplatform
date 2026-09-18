namespace GameAnalytics.Infrastructure.Configuration;

public class RiotApiOptions
{
    public const string SectionName = "RiotApi";

    public string ApiKey { get; set; } = string.Empty;
    public string PlatformRegion { get; set; } = "eun1";      // eun1, euw1, na1, kr
    public string RoutingRegion { get; set; } = "europe";     // europe, americas, asia, sea
    public bool UseMockFallback { get; set; } = true;
}

