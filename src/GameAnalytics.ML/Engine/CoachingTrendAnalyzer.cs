using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.ML.Engine;

/// <summary>Compares the latest games with the games before them, so the player sees whether a habit is getting better.</summary>
public static class CoachingTrendAnalyzer
{
    public const int RecentWindow = 10;
    private const int MinPreviousGames = 5;
    private const int MaxPreviousGames = 20;

    /// <param name="roleGames">Main-role Summoner's Rift games.</param>
    /// <param name="riftGames">All Summoner's Rift games, any role.</param>
    public static List<CoachingTrend> Analyze(
        IReadOnlyList<CoachingHabitAnalyzer.GameSample> roleGames,
        IReadOnlyList<CoachingHabitAnalyzer.GameSample> riftGames,
        Position role)
    {
        var trends = new List<CoachingTrend>();

        var (recentRift, previousRift) = Split(riftGames);
        if (previousRift.Count >= MinPreviousGames)
        {
            trends.Add(Build("Перемоги", recentRift, previousRift, g => g.Player.Win ? 100.0 : 0.0, "{0:F0}%", higherIsBetter: true, threshold: 10));
            trends.Add(Build("Смерті за гру", recentRift, previousRift, g => g.Player.Deaths, "{0:F1}", higherIsBetter: false, threshold: 0.7));
        }

        var (recentRole, previousRole) = Split(roleGames);
        if (previousRole.Count >= MinPreviousGames)
        {
            if (role != Position.Utility)
                trends.Add(Build("CS/хв", recentRole, previousRole, g => g.CsPerMin, "{0:F1}", higherIsBetter: true, threshold: 0.4));
            trends.Add(Build("Участь у вбивствах", recentRole, previousRole, g => g.KillParticipation, "{0:F0}%", higherIsBetter: true, threshold: 5));
        }

        return trends;
    }

    private static (List<CoachingHabitAnalyzer.GameSample> Recent, List<CoachingHabitAnalyzer.GameSample> Previous) Split(
        IReadOnlyList<CoachingHabitAnalyzer.GameSample> games)
    {
        var ordered = games.OrderByDescending(g => g.Match.GameCreation).ToList();
        return (ordered.Take(RecentWindow).ToList(), ordered.Skip(RecentWindow).Take(MaxPreviousGames).ToList());
    }

    private static CoachingTrend Build(
        string metric,
        IReadOnlyList<CoachingHabitAnalyzer.GameSample> recent,
        IReadOnlyList<CoachingHabitAnalyzer.GameSample> previous,
        Func<CoachingHabitAnalyzer.GameSample, double> selector,
        string format,
        bool higherIsBetter,
        double threshold)
    {
        var now = recent.Average(selector);
        var before = previous.Average(selector);
        var delta = now - before;
        var direction = Math.Abs(delta) < threshold ? 0 : (delta > 0) == higherIsBetter ? 1 : -1;
        var verdict = direction switch { > 0 => "краще", < 0 => "гірше", _ => "без змін" };
        var nowText = string.Format(format, now);
        var beforeText = string.Format(format, before);

        return new CoachingTrend
        {
            Metric = metric,
            Direction = direction,
            Text = $"{metric}: {beforeText} → {nowText} за останні {recent.Count} ігор ({verdict})"
        };
    }
}
