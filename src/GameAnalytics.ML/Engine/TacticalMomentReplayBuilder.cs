using GameAnalytics.Core.Entities;
using GameAnalytics.Core.Enums;
using GameAnalytics.Core.Helpers;

namespace GameAnalytics.ML.Engine;

public enum TacticalMomentRole
{
    Kill,
    Death,
    Assist
}

public static class TacticalMomentReplayBuilder
{
    public const double DefaultMapSize = 280;
    private const double RiotMapMax = 14870.0;
    private const int FightMarkerSize = 28;
    private const int NearbyMarkerSize = 20;
    private const int NearbyRadius = 3800;
    private const int KillRingSize = 14;
    private const int ContextWindowMs = 45_000;

    public static string SummonersRiftMapUrl => GameConstants.SummonersRiftMinimapAsset;

    /// <summary>Network fallback if the embedded asset is missing.</summary>
    public static string SummonersRiftMapUrlFallback =>
        $"https://ddragon.leagueoflegends.com/cdn/{GameConstants.DDragonVersion}/img/map/map11.png";

    public static TacticalMomentReplay? BuildCombatMoment(
        Match match,
        MatchTimelineData timeline,
        TimelineEventRecord killEvent,
        int focusParticipantId,
        TacticalMomentRole role)
    {
        if (killEvent.EventType != "CHAMPION_KILL") return null;

        var frame = FindBestFrame(timeline, killEvent.TimestampMs);
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

        // People actually in this fight — pin them to the kill coordinate (with tiny offsets).
        var fightIds = new List<int> { killEvent.KillerId, killEvent.VictimId };
        fightIds.AddRange(killEvent.AssistingParticipantIds.Where(id => id > 0));
        fightIds = fightIds.Distinct().Where(id => id > 0).ToList();

        var markers = new List<TacticalMomentMarker>();
        var fightIndex = 0;
        foreach (var pos in frame.Participants.OrderBy(p => p.ParticipantId))
        {
            var isBlue = pos.ParticipantId <= 5;
            var isFocus = pos.ParticipantId == focusParticipantId;
            var isKiller = pos.ParticipantId == killEvent.KillerId;
            var isVictim = pos.ParticipantId == killEvent.VictimId;
            var isAssist = killEvent.AssistingParticipantIds.Contains(pos.ParticipantId);
            var inFight = fightIds.Contains(pos.ParticipantId);

            var x = pos.X;
            var y = pos.Y;

            // Ignore (0,0) frame stubs — common for unloaded / fountain placeholders.
            if (x == 0 && y == 0 && !inFight)
            {
                continue;
            }

            if (inFight && killEvent.HasPosition)
            {
                var (ox, oy) = FightOffset(fightIndex++, fightIds.Count);
                x = killEvent.PositionX!.Value + ox;
                y = killEvent.PositionY!.Value + oy;
            }
            else if (isVictim && killEvent.HasPosition)
            {
                x = killEvent.PositionX!.Value;
                y = killEvent.PositionY!.Value;
            }

            // Keep the board readable: fight cast + nearby champs only.
            if (!inFight && !isFocus && killEvent.HasPosition)
            {
                var dx = x - killEvent.PositionX!.Value;
                var dy = y - killEvent.PositionY!.Value;
                if (dx * dx + dy * dy > NearbyRadius * NearbyRadius)
                {
                    continue;
                }
            }

            string roleLabel;
            string border;
            if (isFocus)
            {
                roleLabel = "ВИ";
                border = role == TacticalMomentRole.Death ? "#F43F5E" : "#F59E0B";
            }
            else if (isKiller)
            {
                roleLabel = "KILLER";
                border = "#EF4444";
            }
            else if (isVictim)
            {
                roleLabel = "VICTIM";
                border = "#FB7185";
            }
            else if (isAssist)
            {
                roleLabel = "ASSIST";
                border = "#60A5FA";
            }
            else if (isBlue == (focusParticipantId <= 5))
            {
                roleLabel = "ALLY";
                border = "#34D399";
            }
            else
            {
                roleLabel = "ENEMY";
                border = "#64748B";
            }

            var size = inFight || isFocus ? FightMarkerSize : NearbyMarkerSize;
            var (left, top) = ToMapPoint(x, y, DefaultMapSize, size);
            markers.Add(new TacticalMomentMarker
            {
                ParticipantId = pos.ParticipantId,
                ChampionName = Champ(pos.ParticipantId),
                ChampionIconUrl = IconUrl(pos.ParticipantId),
                RoleLabel = roleLabel,
                BorderColor = border,
                IsBlueTeam = isBlue,
                IsFocus = isFocus,
                IsInFight = inFight,
                MarkerSize = size,
                MapLeft = left,
                MapTop = top,
                Level = pos.Level,
                Gold = pos.TotalGold
            });
        }

        if (markers.Count == 0) return null;

        // Draw focus / fight participants above everyone else.
        markers = markers
            .OrderBy(m => m.IsFocus ? 2 : (m.IsInFight ? 1 : 0))
            .ToList();

        var assistNames = killEvent.AssistingParticipantIds
            .Where(id => id > 0)
            .Select(Champ)
            .ToList();

        var ts = FormatTs(killEvent.TimestampMs);
        var killerName = Champ(killEvent.KillerId);
        var victimName = Champ(killEvent.VictimId);

        // Same-second death of the focused player while viewing an assist/kill looks contradictory — surface it.
        var simultaneousDeath = timeline.RealEvents.FirstOrDefault(e =>
            e.EventType == "CHAMPION_KILL"
            && e.VictimId == focusParticipantId
            && e.VictimId != killEvent.VictimId
            && Math.Abs(e.TimestampMs - killEvent.TimestampMs) <= 1000);

        var (title, subtitle) = role switch
        {
            TacticalMomentRole.Death => (
                $"Смерть на {ts}",
                $"{killerName} усунув {victimName}"),
            TacticalMomentRole.Assist => (
                $"Асист на {ts}",
                $"{killerName} усунув {victimName} (ваш асист)"),
            _ => (
                $"Кіл на {ts}",
                $"{killerName} усунув {victimName}")
        };

        if (simultaneousDeath != null && role == TacticalMomentRole.Assist)
        {
            subtitle += $" · у ту ж секунду вас убив {Champ(simultaneousDeath.KillerId)}";
        }

        double killLeft = 0, killTop = 0;
        var hasKill = false;
        if (killEvent.HasPosition)
        {
            (killLeft, killTop) = ToMapPoint(
                killEvent.PositionX!.Value,
                killEvent.PositionY!.Value,
                DefaultMapSize,
                KillRingSize);
            hasKill = true;
        }

        return new TacticalMomentReplay
        {
            Title = title,
            Subtitle = subtitle,
            TimestampText = ts,
            FocusChampionName = Champ(focusParticipantId),
            KillerChampionName = killerName,
            AssistsText = assistNames.Count == 0 ? "Без асистів" : $"Асисти: {string.Join(", ", assistNames)}",
            IsDeath = role == TacticalMomentRole.Death,
            KillerId = killEvent.KillerId,
            VictimId = killEvent.VictimId,
            TimestampMs = killEvent.TimestampMs,
            MapSize = DefaultMapSize,
            MapImageUrl = SummonersRiftMapUrl,
            HasKillMarker = hasKill,
            KillMarkerLeft = killLeft,
            KillMarkerTop = killTop,
            Markers = markers,
            ContextLines = BuildContextLines(match, timeline, killEvent, focusParticipantId)
        };
    }

    public static (double Left, double Top) ToMapPoint(int riotX, int riotY, double mapSize, double markerSize = FightMarkerSize)
    {
        var nx = Math.Clamp(riotX / RiotMapMax, 0, 1);
        var ny = Math.Clamp(riotY / RiotMapMax, 0, 1);
        var left = nx * mapSize - markerSize / 2.0;
        var top = (1.0 - ny) * mapSize - markerSize / 2.0;
        return (left, top);
    }

    private static (int X, int Y) FightOffset(int index, int total)
    {
        if (total <= 1) return (0, 0);
        // Spread fight icons around the kill so portraits stay readable.
        var angle = (Math.PI * 2 * index) / total - Math.PI / 2;
        const int radius = 980;
        return ((int)(Math.Cos(angle) * radius), (int)(Math.Sin(angle) * radius));
    }

    private static TimelineFrameSnapshot? FindBestFrame(MatchTimelineData timeline, int timestampMs)
    {
        if (timeline.Frames.Count == 0) return null;

        // Prefer the last frame at or before the event (positions right before the fight).
        var before = timeline.Frames
            .Where(f => f.TimestampMs <= timestampMs)
            .OrderByDescending(f => f.TimestampMs)
            .FirstOrDefault();
        if (before != null) return before;

        return timeline.Frames.OrderBy(f => f.TimestampMs).FirstOrDefault();
    }

    private static List<TacticalMomentContextLine> BuildContextLines(
        Match match,
        MatchTimelineData timeline,
        TimelineEventRecord selectedKill,
        int focusParticipantId)
    {
        var lines = new List<TacticalMomentContextLine>();
        var window = timeline.RealEvents
            .Where(e => Math.Abs(e.TimestampMs - selectedKill.TimestampMs) <= ContextWindowMs)
            .OrderBy(e => e.TimestampMs)
            .Take(14)
            .ToList();

        foreach (var ev in window)
        {
            var isCurrent = ev.EventType == "CHAMPION_KILL"
                            && ev.KillerId == selectedKill.KillerId
                            && ev.VictimId == selectedKill.VictimId
                            && Math.Abs(ev.TimestampMs - selectedKill.TimestampMs) < 50;

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
            var mark = ev.VictimId == focusId ? " ★" : (ev.KillerId == focusId ? " ◆" : (ev.AssistingParticipantIds.Contains(focusId) ? " ✚" : string.Empty));
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
