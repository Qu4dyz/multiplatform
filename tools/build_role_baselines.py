"""Rebuilds src/GameAnalytics.Core/Data/role_baselines.json from a crawl database.

Usage: python tools/build_role_baselines.py <game_analytics.db> <out.json>

Reads ranked solo games (queue 420, 15+ minutes) and writes P25/P50/P75 of the
coaching stats per role and per lobby rank (Matches.ApproxRankScore), plus per role the
win rate of games in the worse vs the better quartile of each stat ("WinSwing"). Rows that
cannot be read (a copy taken while the crawler writes) end the scan early.
"""
import bisect
import collections
import json
import sqlite3
import sys

TIERS = {1: "Iron", 2: "Bronze", 3: "Silver", 4: "Gold", 5: "Platinum",
         6: "Emerald", 7: "Diamond", 8: "Master", 9: "Grandmaster", 10: "Challenger"}
ROLES = {0: "Top", 1: "Jungle", 2: "Middle", 3: "Bottom", 4: "Utility"}
TIER_ORDER = list(TIERS.values()) + ["All"]
METRICS = ["CsPerMin", "KillParticipation", "Deaths", "DamageShare", "VisionPerMin", "LaneGoldDiff15"]
# Matches column index of the lane gold diff (blue minus red) for each role; bot and support share the duo diff.
LANE_COLUMN = {0: 4, 1: 5, 2: 6, 3: 7, 4: 7}
LOWER_IS_BETTER = {"Deaths"}
MIN_GAMES = 300


def quantile(values, p, discrete=False):
    values = sorted(values)
    n = len(values)
    if not discrete:
        return values[min(n - 1, int(p * n))]
    # Integer counts: spread each value over [x-0.5, x+0.5] so medians are not stuck on whole numbers.
    target = p * n
    x = values[min(n - 1, int(target))]
    lo = bisect.bisect_left(values, x)
    hi = bisect.bisect_right(values, x)
    return x - 0.5 + (target - lo) / max(1, hi - lo)


def main(db_path, out_path):
    con = sqlite3.connect("file:" + db_path + "?mode=ro", uri=True)

    matches = {}
    try:
        for r in con.execute("select Id, GameDurationSeconds, IsRemake, ApproxRankScore, TopGoldDiff15, JungleGoldDiff15,"
                             " MidGoldDiff15, BotDuoGoldDiff15, HasMinute15Objectives, QueueId from Matches"):
            matches[r[0]] = r
    except sqlite3.DatabaseError as e:
        print("matches: stopped after", len(matches), "rows:", e)

    team = collections.defaultdict(lambda: [0, 0])  # (match, side) -> kills, damage
    rows = []
    try:
        for r in con.execute("select MatchEntityId, TeamSide, Position, Kills, Deaths, Assists, TotalMinionsKilled,"
                             " VisionScore, WardsPlaced, TotalDamageDealtToChampions, Win from Participants"):
            if r[0] not in matches:
                continue
            totals = team[(r[0], r[1])]
            totals[0] += r[3]
            totals[1] += r[9]
            rows.append(r)
    except sqlite3.DatabaseError as e:
        print("participants: stopped after", len(rows), "rows:", e)

    acc = collections.defaultdict(lambda: collections.defaultdict(list))
    samples = []  # (role, metric, value, win) for the win-swing pass
    for (mid, side, pos, k, d, a, cs, vision, wards, dmg, win) in rows:
        m = matches[mid]
        if m[2] or m[1] < 900 or m[9] != 420 or pos not in ROLES:
            continue
        minutes = m[1] / 60.0
        tier = TIERS[int(round(min(10, max(1, m[3] or 5.5))))]
        team_kills, team_dmg = team[(mid, side)]
        values = {
            "CsPerMin": cs / minutes,
            "KillParticipation": 100.0 * (k + a) / team_kills if team_kills else 0,
            "Deaths": d,
            "DamageShare": 100.0 * dmg / team_dmg if team_dmg else 20,
        }
        if vision > 0 or wards > 0:
            values["VisionPerMin"] = vision / minutes
        if m[8]:
            values["LaneGoldDiff15"] = m[LANE_COLUMN[pos]] * (1 if side == 100 else -1)
        for key in ((ROLES[pos], tier), (ROLES[pos], "All")):
            for metric, value in values.items():
                acc[key][metric].append(value)
        for metric, value in values.items():
            samples.append((ROLES[pos], metric, value, win))

    result = {}
    for (role, tier), stats in sorted(acc.items()):
        games = len(stats["Deaths"])
        if games < MIN_GAMES:
            continue
        entry = {"Games": games}
        for metric in METRICS:
            values = stats.get(metric, [])
            if len(values) >= 100:
                discrete = metric == "Deaths"
                entry[metric] = [round(quantile(values, p, discrete), 2) for p in (0.25, 0.5, 0.75)]
        result.setdefault(role, {})[tier] = entry

    # Win rate in the worse vs the better quartile of each stat (all ranks), per role.
    swing = collections.defaultdict(lambda: collections.defaultdict(lambda: [0, 0, 0, 0]))
    for role, metric, value, win in samples:
        quartiles = result.get(role, {}).get("All", {}).get(metric)
        if not quartiles:
            continue
        low, high = value <= quartiles[0], value >= quartiles[2]
        worse, better = (high, low) if metric in LOWER_IS_BETTER else (low, high)
        s = swing[role][metric]
        if worse:
            s[0] += 1
            s[1] += win
        elif better:
            s[2] += 1
            s[3] += win
    win_swing = {role: {metric: [round(s[1] / s[0], 3), round(s[3] / s[2], 3)]
                        for metric, s in metrics.items() if s[0] >= 100 and s[2] >= 100}
                 for role, metrics in swing.items()}

    lines = ["{",
             '  "Source": "Ranked Solo/Duo games (15+ min) from the training crawl, split by lobby rank. Values are [P25, P50, P75].",',
             '  "WinSwingNote": "Per role: win rate when the stat is in the worse quartile vs the better quartile (all ranks).",',
             '  "WinSwing": {']
    swing_roles = [r for r in result if r in win_swing]
    for ri, role in enumerate(swing_roles):
        comma = "," if ri < len(swing_roles) - 1 else ""
        lines.append(f'    "{role}": ' + json.dumps(win_swing[role], separators=(", ", ": ")) + comma)
    lines += ["  },", '  "Roles": {']
    role_names = list(result)
    for ri, role in enumerate(role_names):
        lines.append(f'    "{role}": {{')
        present = [t for t in TIER_ORDER if t in result[role]]
        for ti, tier in enumerate(present):
            comma = "," if ti < len(present) - 1 else ""
            lines.append(f'      "{tier}": ' + json.dumps(result[role][tier], separators=(", ", ": ")) + comma)
        lines.append("    }" + ("," if ri < len(role_names) - 1 else ""))
    lines += ["  }", "}"]
    with open(out_path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print("wrote", out_path, {role: result[role]["All"]["Games"] for role in result if "All" in result[role]})


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
