using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;

namespace GameAnalytics.ML.Engine;

public class PlayerMomentumReport
{
    public int MomentumScore { get; set; } = 50;
    public string MomentumScoreText => $"{MomentumScore}/100";
    public string StatusText { get; set; } = "⚖️ Нейтральна форма";
    public string StatusColor { get; set; } = "#A0A8B6";
    public string StreakText { get; set; } = string.Empty;
    public double NextGameWinProbability { get; set; } = 50.0;
    public string NextGameWinChanceText => $"{NextGameWinProbability:F0}% Шанс успіху";
    public string AiAdvice { get; set; } = string.Empty;
    public int CurrentStreakCount { get; set; }
    public bool IsWinStreak { get; set; }
    public double RecentKdaAverage { get; set; }
    public double RecentDeathsAverage { get; set; }
}

public static class PlayerMomentumAnalyzer
{
    public static PlayerMomentumReport Analyze(IReadOnlyList<Match> matches, string searchedPuuidOrName)
    {
        var report = new PlayerMomentumReport();

        if (matches == null || matches.Count == 0)
        {
            report.AiAdvice = "Зіграйте кілька матчів, щоб ШІ міг оцінити вашу поточну бойову форму та тільт-фактор.";
            return report;
        }

        var cleanTarget = searchedPuuidOrName?.Trim() ?? string.Empty;

        // Extract player performance chronologically (from newest to oldest)
        var playerRecords = new List<(bool Win, bool IsRemake, int Kills, int Deaths, int Assists, string Champion)>();

        foreach (var match in matches)
        {
            var p = match.Participants.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(x.Puuid) && !string.IsNullOrWhiteSpace(cleanTarget) && x.Puuid.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(x.SummonerName) && !string.IsNullOrWhiteSpace(cleanTarget) && x.SummonerName.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase)));

            p ??= match.Participants.FirstOrDefault();

            if (p != null)
            {
                var isRemake = match.IsRemake || (match.GameDurationSeconds > 0 && match.GameDurationSeconds < 300);
                var isWin = !isRemake && (p.Win || (match.WinningTeam != 0 && p.TeamSide != 0 && match.WinningTeam == p.TeamSide));
                playerRecords.Add((isWin, isRemake, p.Kills, p.Deaths, p.Assists, p.ChampionName));
            }
        }

        if (playerRecords.Count == 0)
        {
            return report;
        }

        var nonRemakes = playerRecords.Where(r => !r.IsRemake).ToList();

        // 1. Streak Analysis
        int streakCount = 0;
        bool? isWinStreak = null;

        foreach (var r in nonRemakes)
        {
            if (isWinStreak == null)
            {
                isWinStreak = r.Win;
                streakCount = 1;
            }
            else if (r.Win == isWinStreak.Value)
            {
                streakCount++;
            }
            else
            {
                break;
            }
        }

        report.CurrentStreakCount = streakCount;
        report.IsWinStreak = isWinStreak ?? false;

        if (streakCount >= 2)
        {
            report.StreakText = isWinStreak == true
                ? $"🔥 {streakCount} перемог поспіль"
                : $"❄️ {streakCount} поразок поспіль";
        }
        else if (nonRemakes.Count > 0)
        {
            report.StreakText = nonRemakes[0].Win ? "🟢 Остання гра: Перемога" : "🔴 Остання гра: Поразка";
        }

        // 2. Performance Metrics (recent up to 5 games)
        var recentSample = nonRemakes.Take(5).ToList();
        var totalWins = nonRemakes.Count(r => r.Win);
        var baseWinRate = nonRemakes.Count > 0 ? (double)totalWins / nonRemakes.Count * 100 : 50.0;

        var sampleWins = recentSample.Count(r => r.Win);
        var recentWinRate = recentSample.Count > 0 ? (double)sampleWins / recentSample.Count * 100 : baseWinRate;

        var avgKills = recentSample.Count > 0 ? recentSample.Average(r => r.Kills) : 0;
        var avgDeaths = recentSample.Count > 0 ? recentSample.Average(r => r.Deaths) : 0;
        var avgAssists = recentSample.Count > 0 ? recentSample.Average(r => r.Assists) : 0;
        var recentKda = avgDeaths > 0 ? (avgKills + avgAssists) / avgDeaths : (avgKills + avgAssists);

        report.RecentKdaAverage = Math.Round(recentKda, 2);
        report.RecentDeathsAverage = Math.Round(avgDeaths, 1);

        // 3. Momentum Score calculation (0 to 100)
        // Baseline 50
        double score = 50.0;

        // Win velocity impact (-20 to +20)
        score += (recentWinRate - 50.0) * 0.4;

        // Streak factor
        if (isWinStreak == true)
        {
            score += Math.Min(18.0, streakCount * 6.0);
        }
        else if (isWinStreak == false)
        {
            score -= Math.Min(22.0, streakCount * 7.0);
        }

        // KDA bonus (-15 to +15)
        if (recentKda >= 4.0) score += 12;
        else if (recentKda >= 3.0) score += 6;
        else if (recentKda <= 1.2) score -= 12;
        else if (recentKda <= 1.8) score -= 6;

        // Tilt penalty: High deaths in recent games
        if (avgDeaths >= 7.5) score -= 10;
        else if (avgDeaths >= 6.0) score -= 5;

        // Clamp
        var finalScore = (int)Math.Clamp(Math.Round(score), 12, 98);
        report.MomentumScore = finalScore;

        // 4. Status designation
        if (finalScore >= 75)
        {
            report.StatusText = "🔥 У вогні (On Fire)";
            report.StatusColor = "#0AC8B9";
        }
        else if (finalScore >= 56)
        {
            report.StatusText = "⚡ Стабільна форма (Stable)";
            report.StatusColor = "#C8AA6E";
        }
        else if (finalScore >= 42)
        {
            report.StatusText = "⚖️ Збалансована (Neutral)";
            report.StatusColor = "#A0A8B6";
        }
        else
        {
            report.StatusText = "❄️ Тільт-ризик (Tilt Alert)";
            report.StatusColor = "#E84057";
        }

        // 5. Next Game Win Probability (32% to 80%)
        var predictedProb = Math.Clamp(Math.Round(baseWinRate * 0.45 + (finalScore * 0.4) + 8.0), 32.0, 80.0);
        report.NextGameWinProbability = predictedProb;

        // 6. AI Recommendation text
        var topChamp = nonRemakes.GroupBy(r => r.Champion).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "улюбленому чемпіоні";

        if (finalScore >= 75)
        {
            report.AiAdvice = $"Чудовий бойовий темп! Ви тримаєте KDA {recentKda:F1} та активну серію перемог. Модель прогнозує високий шанс успіху ({predictedProb:F0}%) — ідеальний момент для рейтингової черги на {topChamp}.";
        }
        else if (finalScore < 42)
        {
            if (avgDeaths >= 6.5)
            {
                report.AiAdvice = $"Увага: у крайніх матчах зафіксовано підвищену смертність ({avgDeaths:F1} смертей/гру). Рекомендуємо зіграти розминочну гру в Normal Draft або зробити 10-хвилинну паузу для скидання тільту.";
            }
            else
            {
                report.AiAdvice = $"Невдала серія матчів впливає на темп. Спробуйте звузити пул до найнадійнішого чемпіона ({topChamp}) та сфокусуватися на спокійному фармі на ранній стадії.";
            }
        }
        else
        {
            report.AiAdvice = $"Форма стабільна ({recentWinRate:F0}% за останні 5 ігор, KDA {recentKda:F1}). Для зростання вінрейту покращуйте контроль об'єктів (Дракони/Герольд) та контролюйте смерті після 20-ї хвилини.";
        }

        return report;
    }
}
