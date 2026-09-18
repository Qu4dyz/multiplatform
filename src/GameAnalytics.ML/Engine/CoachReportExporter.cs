using System.Text;
using GameAnalytics.Core.Entities;

namespace GameAnalytics.ML.Engine;

public static class CoachReportExporter
{
    public static string GenerateMarkdownReport(Match match, Participant player, MatchTacticalReport tacticalReport)
    {
        var sb = new StringBuilder();

        var durMin = match.GameDurationSeconds > 0 ? match.GameDurationSeconds / 60.0 : 25.0;
        var resultText = tacticalReport.IsVictory ? "🏆 ПЕРЕМОГА (VICTORY)" : "💀 ПОРАЗКА (DEFEAT)";

        sb.AppendLine($"# 📋 Тренерський звіт матчу — {tacticalReport.ChampionName} ({tacticalReport.RoleName})");
        sb.AppendLine();
        sb.AppendLine($"* **ID Матчу:** `{match.MatchId}`");
        sb.AppendLine($"* **Результат:** **{resultText}**");
        sb.AppendLine($"* **Тривалість:** {(int)durMin} хв {match.GameDurationSeconds % 60:D2} сек");
        sb.AppendLine($"* **Оцінка гри (Grade):** **{tacticalReport.MatchGrade}**");
        sb.AppendLine($"* **KDA:** {player.Kills}/{player.Deaths}/{player.Assists} ({(player.Deaths == 0 ? "Perfect" : $"{(player.Kills + player.Assists) / (double)player.Deaths:F2}:1")})");
        sb.AppendLine($"* **Фарм:** {player.TotalMinionsKilled} CS ({player.TotalMinionsKilled / Math.Max(1.0, durMin):F1} CS/хв)");
        sb.AppendLine($"* **Шкода чемпіонам:** {player.TotalDamageDealtToChampions:N0}");
        sb.AppendLine($"* **Зароблене золото:** {player.GoldEarned:N0}");
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("## 🎯 Головний вердикт тренера");
        sb.AppendLine();
        sb.AppendLine($"> {tacticalReport.OverallMatchVerdict}");
        sb.AppendLine();
        sb.AppendLine($"**Ключовий висновок:** {tacticalReport.KeyTakeaway}");
        sb.AppendLine();

        if (tacticalReport.GapAnalysis.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("## 📊 Порівняльний Gap-аналіз проти стандарту рангу");
            sb.AppendLine();
            sb.AppendLine("| Категорія | Метрика | Фактичний показник | Еталон рангу | Оцінка тренера |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");

            foreach (var gap in tacticalReport.GapAnalysis)
            {
                var sign = gap.IsPositive ? "✅" : "⚠️";
                sb.AppendLine($"| {gap.Category} | {gap.MetricName} | **{gap.ActualValueText}** | {gap.BenchmarkValueText} | {sign} {gap.EvaluationText} — {gap.Advice} |");
            }
            sb.AppendLine();
        }

        if (tacticalReport.TimelineEvents.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("## ⏱️ Хронологія ключових рішень гри (Play-by-play)");
            sb.AppendLine();

            foreach (var ev in tacticalReport.TimelineEvents)
            {
                var icon = ev.IsMistake ? "❌" : ev.Icon;
                sb.AppendLine($"* **[{ev.TimestampText}]** {icon} **{ev.Title}** ({ev.PhaseName})");
                sb.AppendLine($"  * {ev.Description}");
                if (!string.IsNullOrWhiteSpace(ev.ImpactText))
                {
                    sb.AppendLine($"  * *Вплив на гру:* **{ev.ImpactText}**");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("## 💡 Рекомендації тренера для наступних ігор");
        sb.AppendLine();
        sb.AppendLine("1. Контролюйте таймінги повернення на базу (Recall) під час пушу ворожої пачки.");
        sb.AppendLine("2. Синхронізуйте кулдауни ультимейтів перед битвами за Дракона та Барона.");
        sb.AppendLine("3. Звертайте увагу на пріоритет ліній перед роумінгами та заходами у ворожий ліс.");
        sb.AppendLine();
        sb.AppendLine("*Звіт згенеровано аналітичною системою GameAnalytics (Avalonia UI + ML.NET)*");

        return sb.ToString();
    }
}
