namespace GameAnalytics.Core.Entities;

public class ChampionInfo
{
    public string Id { get; set; } = string.Empty;       // e.g. "Ahri", "Aatrox"
    public int Key { get; set; }                         // e.g. 103, 266
    public string Name { get; set; } = string.Empty;     // Display name
    public string Title { get; set; } = string.Empty;    // Title, e.g. "the Nine-Tailed Fox"
    public List<string> Roles { get; set; } = new();     // "Mage", "Assassin", "Fighter", "Tank", "Marksman", "Support"
    public string IconUrl { get; set; } = string.Empty;
    public float WinRate { get; set; } = 50.0f;
    public float PickRate { get; set; } = 5.0f;
    public float BanRate { get; set; } = 2.0f;

    public string PrimaryRole => Roles.Count > 0 ? Roles[0] : "All";
}

