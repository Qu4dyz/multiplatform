namespace GameAnalytics.Core.Helpers;

public static class TacticalIconHelper
{
    public const string SwordPath = "M14.7 3.3a1 1 0 0 0-1.4 0l-4 4-2-2a1 1 0 0 0-1.4 0l-3 3a1 1 0 0 0 0 1.4l2 2-3.6 3.6a1 1 0 0 0 0 1.4l1.4 1.4a1 1 0 0 0 1.4 0l3.6-3.6 2 2a1 1 0 0 0 1.4 0l3-3a1 1 0 0 0 0-1.4l-2-2 4-4a1 1 0 0 0 0-1.4z";
    public const string ShieldPath = "M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm0 2.18l6 2.25v4.66c0 4.1-2.67 7.9-6 8.91-3.33-1.01-6-4.81-6-8.91V6.43l6-2.25z";
    public const string DragonPath = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";
    public const string SkullPath = "M12 2C7.58 2 4 5.58 4 10c0 2.21.9 4.21 2.35 5.65L7 20h10l.65-4.35C19.1 14.21 20 12.21 20 10c0-4.42-3.58-8-8-8zm-3 10a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm6 0a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z";
    public const string WarningPath = "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z";
    public const string EyePath = "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z";
    public const string TowerPath = "M19 4h-2V2h-2v2h-2V2h-2v2H9V2H7v2H5v3h14V4zm-1 5H6v11h12V9zm-5 4h-2v5h2v-5z";
    public const string GoldPath = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-1.1c-1.38-.28-2.5-1.3-2.5-2.9h2c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5c0-1.85-3.5-1.7-3.5-4.5 0-1.38.92-2.38 2.5-2.73V5h2v1.1c1.2.26 2.2 1.15 2.37 2.4h-2.02c-.15-.47-.52-.8-.98-.8-.83 0-1.37.67-1.37 1.3 0 1.63 3.5 1.56 3.5 4.5 0 1.48-.97 2.62-2.5 2.9V17z";
    public const string StarPath = "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z";
    public const string LightningPath = "M7 2v11h3v9l7-12h-4l4-8z";

    public static string GetPathForKind(string kind) => kind?.ToLowerInvariant() switch
    {
        "sword" or "kill" or "fight" or "firstblood" => SwordPath,
        "shield" or "defense" or "tank" => ShieldPath,
        "dragon" or "drake" or "soul" => DragonPath,
        "baron" or "nashor" or "voidgrub" => DragonPath,
        "skull" or "death" => SkullPath,
        "warning" or "mistake" => WarningPath,
        "eye" or "vision" or "ward" => EyePath,
        "tower" or "turret" or "plate" => TowerPath,
        "gold" or "farm" or "cs" => GoldPath,
        "star" or "ace" or "mvp" => StarPath,
        "lightning" or "burst" or "spikes" => LightningPath,
        _ => StarPath
    };
}
