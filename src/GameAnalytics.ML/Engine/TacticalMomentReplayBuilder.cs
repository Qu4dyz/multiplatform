using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;

namespace GameAnalytics.ML.Engine;

public static class TacticalMomentReplayBuilder
{
    public const double DefaultMapSize = 280;
    private const double RiotMapMax = 14870.0;
    private const int MarkerSize = 24;
    private const int ContextWindowMs = 45_000;

    public static TacticalMomentReplay? BuildCombatMoment(
        Match match,
        MatchTimelineData timeline,
        TimelineEventRecord killEvent,
        int focusParticipantId,
        bool isDeath)
    {
        if (killEvent.EventType != "CHAMPION_KILL") return null;

        var frame = FindNearestFrame(timeline, killEvent.TimestampMs);
        if (frame == null || frame.Participants.Count == 0) return null;

        string Champ(int id)
        {
            var p = ResolveParticipant(match, id);
            return p != null && !string.IsNullOrWhiteSpace(p.ChampionName)
                ? ChampionNameHelper.ToDisplayName(p.ChampionName)
                : $"P{id}";
        }

        string IconUrl(int id)
        {
            var p = ResolveParticipant(match, id);
            var key = ChampionNameHelper.ToDDragonImageKey(p?.ChampionName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/champion/{key}.png";
        }

        var markers = new List<TacticalMomentMarker>();
        foreach (var pos in frame.Participants)
        {
            var isBlue = pos.ParticipantId <= 5;
            var isFocus = pos.ParticipantId == focusParticipantId;
            var isKiller = pos.ParticipantId == killEvent.KillerId;
            var isVictim = pos.ParticipantId == killEvent.VictimId;
            var isAssist = killEvent.AssistingParticipantIds.Contains(pos.ParticipantId);

            var x = pos.X;
            var y = pos.Y;
            // Prefer exact kill-event coordinates for victim (death location).
            if (isVictim && killEvent.HasPosition)
            {
                x = killEvent.PositionX!.Value;
                y = killEvent.PositionY!.Value;
            }

            string role;
            string border;
            if (isFocus && isDeath)
            {
                role = "ВИ";
                border = "#F43F5E";
            }
            else if (isFocus && !isDeath)
            {
                role = "ВИ";
                border = "#F59E0B";
            }
            else if (isKiller)
            {
                role = "KILLER";
                border = "#EF4444";
            }
            else if (isVictim)
            {
                role = "VICTIM";
                border = "#FB7185";
            }
            else if (isAssist)
            {
                role = "ASSIST";
                border = "#60A5FA";
            }
            else if (isBlue == (focusParticipantId <= 5))
            {
                role = "ALLY";
                border = "#34D399";
            }
            else
            {
                role = "ENEMY";
                border = "#64748B";
            }

            var (left, top) = ToMapPoint(x, y, DefaultMapSize);
            markers.Add(new TacticalMomentMarker
            {
                ParticipantId = pos.ParticipantId,
                ChampionName = Champ(pos.ParticipantId),
                ChampionIconUrl = IconUrl(pos.ParticipantId),
                RoleLabel = role,
                BorderColor = border,
                IsBlueTeam = isBlue,
                IsFocus = isFocus,
                MapLeft = left,
                MapTop = top,
                Level = pos.Level,
                Gold = pos.TotalGold
            });
        }

        if (markers.Count == 0) return null;

        var assistNames = killEvent.AssistingParticipantIds
            .Where(id => id > 0)
            .Select(Champ)
            .ToList();

        var ts = FormatTs(killEvent.TimestampMs);
        var killerName = Champ(killEvent.KillerId);
        var victimName = Champ(killEvent.VictimId);
        var focusName = Champ(focusParticipantId);

        return new TacticalMomentReplay
        {
            Title = isDeath ? $"Смерть на {ts}" : $"Кіл на {ts}",
            Subtitle = isDeath
                ? $"{killerName} усунув {victimName}"
                : $"{killerName} усунув {victimName}",
            TimestampText = ts,
            FocusChampionName = focusName,
            KillerChampionName = killerName,
            AssistsText = assistNames.Count == 0 ? "Без асистів" : $"Асисти: {string.Join(", ", assistNames)}",
            IsDeath = isDeath,
            MapSize = DefaultMapSize,
            Markers = markers.OrderBy(m => m.IsFocus ? 1 : 0).ToList(), // focus drawn last conceptually; UI stacks by order
            ContextLines = BuildContextLines(match, timeline, killEvent.TimestampMs, focusParticipantId)
        };
    }

    public static (double Left, double Top) ToMapPoint(int riotX, int riotY, double mapSize)
    {
        var nx = Math.Clamp(riotX / RiotMapMax, 0, 1);
        var ny = Math.Clamp(riotY / RiotMapMax, 0, 1);
        // Riot Y grows north; canvas Y grows down — invert.
        var left = nx * mapSize - MarkerSize / 2.0;
        var top = (1.0 - ny) * mapSize - MarkerSize / 2.0;
        return (left, top);
    }

    private static TimelineFrameSnapshot? FindNearestFrame(MatchTimelineData timeline, int timestampMs)
    {
        if (timeline.Frames.Count == 0) return null;
        return timeline.Frames
            .OrderBy(f => Math.Abs(f.TimestampMs - timestampMs))
            .FirstOrDefault();
    }

    private static List<TacticalMomentContextLine> BuildContextLines(
        Match match,
        MatchTimelineData timeline,
        int centerTs,
        int focusParticipantId)
    {
        var lines = new List<TacticalMomentContextLine>();
        var window = timeline.RealEvents
            .Where(e => Math.Abs(e.TimestampMs - centerTs) <= ContextWindowMs)
            .OrderBy(e => e.TimestampMs)
            .Take(12)
            .ToList();

        foreach (var ev in window)
        {
            var isCurrent = Math.Abs(ev.TimestampMs - centerTs) < 50;
            var text = DescribeEvent(match, ev, focusParticipantId);
            if (string.IsNullOrWhiteSpace(text)) continue;

            lines.Add(new TacticalMomentContextLine
            {
                TimestampText = FormatTs(ev.TimestampMs),
                Text = text,
                AccentColor = isCurrent ? "#F43F5E" : "#94A3B8",
                IsCurrent = isCurrent
            });
        }

        return lines;
    }

    private static string DescribeEvent(Match match, TimelineEventRecord ev, int focusId)
    {
        string Champ(int id)
        {
            var p = ResolveParticipant(match, id);
            return p != null && !string.IsNullOrWhiteSpace(p.ChampionName)
                ? ChampionNameHelper.ToDisplayName(p.ChampionName)
                : $"P{id}";
        }

        if (ev.EventType == "CHAMPION_KILL")
        {
            var mark = ev.VictimId == focusId ? " ★" : (ev.KillerId == focusId ? " ◆" : string.Empty);
            return $"{Champ(ev.KillerId)} → {Champ(ev.VictimId)}{mark}";
        }

        if (ev.EventType == "ELITE_MONSTER_KILL")
        {
            var name = ev.MonsterType.Equals("HORDE", StringComparison.OrdinalIgnoreCase) ? "Личинки"
                : ev.MonsterType.Equals("RIFTHERALD", StringComparison.OrdinalIgnoreCase) ? "Герольд"
                : ev.MonsterType.Equals("BARON_NASHOR", StringComparison.OrdinalIgnoreCase) ? "Барон"
                : ev.MonsterType.Equals("DRAGON", StringComparison.OrdinalIgnoreCase) ? "Дракон"
                : ev.MonsterType;
            var side = ev.KillerTeamId == 100 || ev.KillerId is > 0 and <= 5 ? "Сині" : "Червоні";
            return $"{side}: {name}";
        }

        if (ev.EventType == "BUILDING_KILL")
        {
            var lane = ev.LaneType.ToUpperInvariant() switch
            {
                "TOP_LANE" => "Топ",
                "MID_LANE" => "Мід",
                "BOT_LANE" => "Бот",
                _ => "Лінія"
            };
            return $"Вежа ({lane})";
        }

        return string.Empty;
    }

    private static Participant? ResolveParticipant(Match match, int participantId)
    {
        return match.Participants.FirstOrDefault(x =>
            (x.ParticipantId > 0 ? x.ParticipantId : match.Participants.IndexOf(x) + 1) == participantId);
    }

    private static string FormatTs(int timestampMs)
    {
        var min = timestampMs / 60000;
        var sec = (timestampMs % 60000) / 1000;
        return $"{min:D2}:{sec:D2}";
    }
}
