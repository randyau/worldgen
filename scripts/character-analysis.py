#!/usr/bin/env python3
"""
character-analysis.py — Deep character behavior analysis for WorldEngine history databases.

Reads world.db and produces narrative-style profiles of individual characters,
tracing their lives from birth through death, relationships, and influence.

Usage:
    python3 scripts/character-analysis.py world.db top 10
    python3 scripts/character-analysis.py world.db 42
    python3 scripts/character-analysis.py world.db civ:5
    python3 scripts/character-analysis.py world.db era:100-200
"""

import sqlite3
import json
import sys
import re
from collections import defaultdict

# ── EventType numeric constants ────────────────────────────────────────────────
EV_BORN         = 3001
EV_DIED         = 3002
EV_MARRIED      = 3003
EV_EXILED       = 3004
EV_GRIEVED      = 3005
EV_FLOURISHING  = 3006
EV_SPIRALING    = 3007
EV_ALLIANCE_F   = 3101
EV_ALLIANCE_B   = 3102
EV_WAR          = 3103
EV_WAR_END      = 3104
EV_BATTLE       = 3105
EV_RIVALRY      = 3106
EV_NEGOTIATED   = 3107
EV_ARTWORK      = 3108
EV_GOAL_FORMED  = 3109
EV_GOAL_RESOLVE = 3110
EV_CIV_FOUNDED  = 3201
EV_SET_FOUNDED  = 3203
EV_SET_CONQUERED = 3207
EV_SUCCESSION   = 3205
EV_APPOINTED    = 3301
EV_DISMISSED    = 3302
EV_MERCHANT     = 3303
EV_SCHOLAR      = 3304
EV_PHYSICIAN    = 3305
EV_CRYSTALLIZED = 3306
EV_ARTISAN      = 3307
EV_BEAST_CHAR   = 2007

SEASON_NAMES = {0: "Spring", 1: "Summer", 2: "Autumn", 3: "Winter"}
TIER_NAMES   = {0: "Background", 1: "Character", 2: "Regional", 3: "Headline"}

# ── Helpers ────────────────────────────────────────────────────────────────────

def connect(db_path: str) -> sqlite3.Connection:
    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row
    return conn

def hr(char: str = "─", width: int = 72) -> str:
    return char * width

def season_str(year: int, season: int) -> str:
    return f"Year {year:>5} · {SEASON_NAMES.get(season, str(season)):<6}"

def pj(row) -> dict:
    """Parse PayloadJson from a row safely."""
    raw = row["PayloadJson"] if hasattr(row, "__getitem__") else row
    try:
        return json.loads(raw or "{}")
    except Exception:
        return {}

def narrative_event(ev) -> str | None:
    """Convert a database event row into a human-readable one-liner."""
    t   = ev["Type"]
    p   = pj(ev)
    loc = ""
    if ev["LocationX"] is not None:
        loc = f" (at {ev['LocationX']},{ev['LocationY']})"

    mapping = {
        EV_BORN:         lambda: f"Born — epithet: {p.get('Epithet','—')}, ambition {p.get('Ambition',0):.2f}, aggression {p.get('Aggression',0):.2f}",
        EV_DIED:         lambda: f"Died — cause: {p.get('Cause','unknown')}, age {p.get('AgeSeason',0)//4} yrs ({p.get('AgeSeason',0)} seasons)",
        EV_MARRIED:      lambda: "Married",
        EV_EXILED:       lambda: "Exiled from their homeland",
        EV_GRIEVED:      lambda: f"Grieved the loss of {p.get('DeceasedName','someone')} (intensity {p.get('Intensity',0):.2f}, wellbeing {p.get('Wellbeing',0):.2f})",
        EV_FLOURISHING:  lambda: f"Flourishing — wellbeing {p.get('Wellbeing',0):.2f}",
        EV_SPIRALING:    lambda: f"Spiraling — wellbeing {p.get('Wellbeing',0):.2f}",
        EV_ALLIANCE_F:   lambda: f"Formed alliance with {p.get('TargetName', '?')} (civ {p.get('TargetCivId','?')})",
        EV_ALLIANCE_B:   lambda: f"Alliance broken with {p.get('CharacterBName', p.get('CharacterAName','?'))} — {p.get('Reason','unknown')}",
        EV_WAR:          lambda: f"Declared war on civ {p.get('TargetCivName','?')} — cause: {p.get('Cause','?')}: {p.get('CauseDescription','')}",
        EV_WAR_END:      lambda: f"War ended between {p.get('CivAName','?')} and {p.get('CivBName','?')} — {p.get('Outcome','?')}",
        EV_BATTLE:       lambda: f"Raided {ev['SettlementName'] or 'a settlement'}{loc} — {p.get('RaidOutcome','?')}, dealt {p.get('Damage',0)} dmg",
        EV_RIVALRY:      lambda: f"Formed rivalry with {p.get('TargetName','?')}",
        EV_NEGOTIATED:   lambda: f"Negotiated with target {p.get('TargetId','?')} — trust gained: {p.get('TrustGain',0):.2f}",
        EV_ARTWORK:      lambda: f"Created artwork ({p.get('ArtType','?')}) — wellbeing {p.get('Wellbeing',0):.2f}",
        EV_GOAL_FORMED:  lambda: f"Formed goal: {p.get('GoalType','?')} → {p.get('GoalObject','?')} (intensity {p.get('Intensity',0):.2f})",
        EV_GOAL_RESOLVE: lambda: f"Goal {p.get('Outcome','resolved')}: {p.get('GoalType','?')} → {p.get('GoalObject','?')}",
        EV_CIV_FOUNDED:  lambda: f"Founded civilization '{p.get('CivName','?')}'",
        EV_SET_FOUNDED:  lambda: f"Founded settlement '{ev['SettlementName'] or p.get('SettlementName','?')}'{loc} for civ {p.get('CivName','?')}, pop {p.get('StartingPopulation',0)}",
        EV_SET_CONQUERED:lambda: f"Conquered settlement '{ev['SettlementName'] or '?'}'{loc} (surviving pop {p.get('SurvivingPop',0)})",
        EV_SUCCESSION:   lambda: f"Succeeded {p.get('PredecessorName','?')} (ordinal #{p.get('SuccessorOrdinal','?')}) as ruler",
        EV_APPOINTED:    lambda: f"Appointed to role: {p.get('Role','?')} (pop {p.get('Population',0)})",
        EV_DISMISSED:    lambda: f"Dismissed from role",
        EV_MERCHANT:     lambda: f"Completed trade of {p.get('TradedResource','?')} to ({p.get('DestX','?')},{p.get('DestY','?')})",
        EV_SCHOLAR:      lambda: f"Made discovery: {p.get('DiscoveryType','?')} — {p.get('BonusKey','?')} +{p.get('BonusAmount',0):.2f}",
        EV_PHYSICIAN:    lambda: f"Healed {p.get('PatientName','?')} (+{p.get('Healed',0)} hp){' — critical save' if p.get('Critical') else ''}",
        EV_CRYSTALLIZED: lambda: f"Crystallized into Tier 2 as '{p.get('NewName','?')}'",
        EV_ARTISAN:      lambda: f"Crafted {p.get('GoodType','?')}",
        EV_BEAST_CHAR:   lambda: f"Beast encounter with {p.get('BeastName','?')} — took {p.get('Damage',0)} dmg, dealt {p.get('CounterDamage',0)}",
    }

    fn = mapping.get(t)
    if fn:
        try:
            return fn()
        except Exception as e:
            return f"{ev['TypeName']} (parse error: {e})"
    return f"{ev['TypeName']}"

# ── Database queries ────────────────────────────────────────────────────────────

def get_all_characters(conn: sqlite3.Connection) -> list[dict]:
    """Return all characters sorted by total event count (most interesting first)."""
    sql = """
        SELECT
            c.ActorId  AS char_id,
            c.ActorName AS char_name,
            COUNT(DISTINCT e.Id) AS event_count
        FROM (
            SELECT ActorId, ActorName FROM Events WHERE Type = ? AND ActorId IS NOT NULL
        ) c
        JOIN Events e ON (
            e.ActorId = c.ActorId
            OR e.Id IN (SELECT EventId FROM EventEntities WHERE EntityId = c.ActorId)
        )
        GROUP BY c.ActorId, c.ActorName
        ORDER BY event_count DESC
    """
    return [dict(r) for r in conn.execute(sql, (EV_BORN,))]

def get_characters_by_civ(conn: sqlite3.Connection, civ_id: int) -> list[dict]:
    """Return characters whose birth event is associated with a given civilization."""
    sql = """
        SELECT
            e.ActorId  AS char_id,
            e.ActorName AS char_name,
            COUNT(DISTINCT ev.Id) AS event_count
        FROM Events e
        JOIN Events ev ON (
            ev.ActorId = e.ActorId
            OR ev.Id IN (SELECT EventId FROM EventEntities WHERE EntityId = e.ActorId)
        )
        WHERE e.Type = ? AND e.CivId = ? AND e.ActorId IS NOT NULL
        GROUP BY e.ActorId, e.ActorName
        ORDER BY event_count DESC
    """
    return [dict(r) for r in conn.execute(sql, (EV_BORN, civ_id))]

def get_characters_in_era(conn: sqlite3.Connection, era_start: int, era_end: int) -> list[dict]:
    """Return characters who were active (had events) in the given year range."""
    sql = """
        SELECT
            e.ActorId  AS char_id,
            e.ActorName AS char_name,
            COUNT(DISTINCT e.Id) AS event_count
        FROM Events e
        WHERE e.ActorId IS NOT NULL
          AND e.Year BETWEEN ? AND ?
        GROUP BY e.ActorId, e.ActorName
        ORDER BY event_count DESC
    """
    return [dict(r) for r in conn.execute(sql, (era_start, era_end))]

def get_character_events(conn: sqlite3.Connection, char_id: int) -> list[sqlite3.Row]:
    """All events involving a character, chronological."""
    sql = """
        SELECT DISTINCT e.*
        FROM Events e
        WHERE e.ActorId = ?
           OR e.Id IN (SELECT EventId FROM EventEntities WHERE EntityId = ?)
        ORDER BY e.Year, e.Season, e.Id
    """
    return conn.execute(sql, (char_id, char_id)).fetchall()

def get_civ_name(conn: sqlite3.Connection, civ_id: int) -> str:
    row = conn.execute(
        "SELECT PayloadJson FROM Events WHERE Type = ? AND CivId = ? LIMIT 1",
        (EV_CIV_FOUNDED, civ_id)
    ).fetchone()
    if row:
        p = pj(row)
        return p.get("CivName", f"Civ {civ_id}")
    row = conn.execute(
        "SELECT PayloadJson FROM Events WHERE CivId = ? LIMIT 1", (civ_id,)
    ).fetchone()
    if row:
        p = pj(row)
        return p.get("DeclarerCivName", p.get("CivName", f"Civ {civ_id}"))
    return f"Civ {civ_id}"

# ── Archetype classification ───────────────────────────────────────────────────

def classify_archetype(stats: dict) -> str:
    wars      = stats["wars_declared"]
    battles   = stats["battles"]
    alliances = stats["alliances"]
    artworks  = stats["artworks"] + stats["artisan_crafts"]
    scholar   = stats["discoveries"]
    trade     = stats["trades"]
    founded   = stats["settlements_founded"]
    rivalry   = stats["rivalries"]

    combat   = wars * 3 + battles
    culture  = artworks * 2 + scholar * 2 + trade
    social   = alliances * 2 - rivalry
    building = founded * 3

    scores = {
        "Warrior":    combat,
        "Diplomat":   social + alliances,
        "Artist":     artworks * 3 + scholar,
        "Merchant":   trade * 3,
        "Explorer":   founded * 2 + battles,
        "Builder":    building + founded,
        "Scholar":    scholar * 4,
    }
    if all(v == 0 for v in scores.values()):
        return "Unknown"
    return max(scores, key=scores.get)

# ── Profile renderer ───────────────────────────────────────────────────────────

def render_character_profile(conn: sqlite3.Connection, char_id: int, rank: int | None = None) -> str:
    events = get_character_events(conn, char_id)
    if not events:
        return f"  [No events found for character {char_id}]\n"

    lines: list[str] = []

    # Gather birth/death info
    birth_ev   = next((e for e in events if e["Type"] == EV_BORN), None)
    death_ev   = next((e for e in events if e["Type"] == EV_DIED), None)
    char_name  = birth_ev["ActorName"] if birth_ev else events[0]["ActorName"] or f"#{char_id}"
    epithet    = ""
    birth_year = None
    death_year = None
    age_years  = None

    if birth_ev:
        bp = pj(birth_ev)
        epithet    = bp.get("Epithet") or ""
        birth_year = birth_ev["Year"]
    if death_ev:
        dp = pj(death_ev)
        death_year = death_ev["Year"]
        age_seasons = dp.get("AgeSeason", 0)
        age_years  = age_seasons // 4

    # Title block
    rank_str = f"  #{rank}  " if rank is not None else "  "
    title = f"{char_name}"
    if epithet:
        title += f', "{epithet}"'
    lines.append(hr("═"))
    lines.append(f"{rank_str}{title}")
    lines.append(f"  Character ID: {char_id}")

    # Lifespan
    if birth_year is not None and death_year is not None:
        lines.append(f"  Lifespan:     Year {birth_year} → Year {death_year}  ({age_years} years)")
    elif birth_year is not None:
        lines.append(f"  Born:         Year {birth_year}  (status: alive or unknown)")
    lines.append(hr("─"))

    # Civilization(s)
    civ_ids = set()
    for ev in events:
        if ev["CivId"]:
            civ_ids.add(ev["CivId"])
    if civ_ids:
        civ_labels = ", ".join(f"{get_civ_name(conn, c)} [{c}]" for c in sorted(civ_ids))
        lines.append(f"  Civilizations: {civ_labels}")

    # Settlement at birth
    if birth_ev and birth_ev["SettlementName"]:
        lines.append(f"  Born in:      {birth_ev['SettlementName']}")

    # Collect stats while building timeline
    stats = defaultdict(int)
    allies:    list[str] = []
    rivals:    list[str] = []
    negots:    list[str] = []
    roles:     list[str] = []
    artworks:  list[str] = []
    discoveries: list[str] = []
    goals_completed: list[str] = []
    wars_declared: list[str] = []
    settlements_list: list[str] = []
    conquests: list[str] = []

    timeline: list[tuple[int,int,str]] = []  # (year, season, text)

    for ev in events:
        t   = ev["Type"]
        p   = pj(ev)
        yr  = ev["Year"]
        sn  = ev["Season"]
        txt = narrative_event(ev)
        if txt:
            timeline.append((yr, sn, txt))

        # Stats
        if t == EV_ALLIANCE_F:
            stats["alliances"] += 1
            target = p.get("TargetName")
            if target:
                allies.append(target)
        elif t == EV_ALLIANCE_B:
            stats["alliances_broken"] += 1
        elif t == EV_WAR and ev["ActorId"] == char_id:
            stats["wars_declared"] += 1
            target_civ = p.get("TargetCivName","?")
            wars_declared.append(f"{target_civ} (yr {yr})")
        elif t == EV_BATTLE:
            stats["battles"] += 1
        elif t == EV_RIVALRY:
            stats["rivalries"] += 1
            target = p.get("TargetName")
            if target:
                rivals.append(target)
        elif t == EV_NEGOTIATED:
            stats["negotiations"] += 1
            trust = p.get("TrustGain", 0)
            negots.append(f"target {p.get('TargetId','?')} (+{trust:.2f} trust, yr {yr})")
        elif t == EV_ARTWORK:
            stats["artworks"] += 1
            artworks.append(p.get("ArtType","artwork"))
        elif t == EV_ARTISAN:
            stats["artisan_crafts"] += 1
            artworks.append(f"crafted {p.get('GoodType','item')}")
        elif t == EV_GOAL_RESOLVE:
            if p.get("Outcome") == "completed":
                stats["goals_completed"] += 1
                goals_completed.append(f"{p.get('GoalType','?')}: {p.get('GoalObject','?')}")
            elif p.get("Outcome") == "abandoned":
                stats["goals_abandoned"] += 1
        elif t == EV_GOAL_FORMED:
            stats["goals_formed"] += 1
        elif t == EV_CIV_FOUNDED:
            stats["civs_founded"] += 1
        elif t == EV_SET_FOUNDED:
            stats["settlements_founded"] += 1
            sname = ev["SettlementName"] or p.get("SettlementName","?")
            settlements_list.append(f"{sname} (yr {yr})")
        elif t == EV_SET_CONQUERED:
            stats["conquests"] += 1
            sname = ev["SettlementName"] or "unknown"
            conquests.append(f"{sname} (yr {yr})")
        elif t == EV_SUCCESSION:
            stats["ruler_succession"] += 1
        elif t == EV_APPOINTED:
            roles.append(p.get("Role","?"))
            stats["roles_held"] += 1
        elif t == EV_MERCHANT:
            stats["trades"] += 1
        elif t == EV_SCHOLAR:
            stats["discoveries"] += 1
            discoveries.append(f"{p.get('DiscoveryType','?')}: {p.get('BonusKey','?')} +{p.get('BonusAmount',0):.2f}")
        elif t == EV_PHYSICIAN:
            stats["heals"] += 1
        elif t == EV_GRIEVED:
            stats["griefs"] += 1
        elif t == EV_FLOURISHING:
            stats["flourishing"] += 1
        elif t == EV_SPIRALING:
            stats["spiraling"] += 1

    archetype = classify_archetype(stats)
    lines.append(f"  Archetype:    {archetype}  |  Total events: {len(events)}")
    lines.append("")

    # ── Life Events Timeline ────────────────────────────────────────────
    lines.append("  LIFE TIMELINE")
    lines.append("  " + hr("·", 68))
    # Filter to most significant events for readability (skip repetitive background)
    shown_types = {
        EV_BORN, EV_DIED, EV_MARRIED, EV_EXILED, EV_GRIEVED, EV_FLOURISHING,
        EV_SPIRALING, EV_ALLIANCE_F, EV_ALLIANCE_B, EV_WAR, EV_WAR_END,
        EV_BATTLE, EV_RIVALRY, EV_NEGOTIATED, EV_ARTWORK, EV_GOAL_FORMED,
        EV_GOAL_RESOLVE, EV_CIV_FOUNDED, EV_SET_FOUNDED, EV_SET_CONQUERED,
        EV_SUCCESSION, EV_APPOINTED, EV_DISMISSED, EV_SCHOLAR, EV_ARTISAN,
        EV_CRYSTALLIZED, EV_BEAST_CHAR,
    }
    sig_events = [e for e in events if e["Type"] in shown_types]
    if not sig_events:
        sig_events = events[:20]  # fallback: first 20 events
    for ev in sig_events:
        txt = narrative_event(ev)
        if txt:
            prefix = season_str(ev["Year"], ev["Season"])
            tier = TIER_NAMES.get(ev["TierInvolvement"], "")
            tier_mark = " ★" if ev["TierInvolvement"] >= 2 else ""
            lines.append(f"  {prefix}  {txt}{tier_mark}")

    lines.append("")

    # ── Relationships ──────────────────────────────────────────────────
    lines.append("  RELATIONSHIPS")
    lines.append("  " + hr("·", 68))
    if allies:
        lines.append(f"  Alliances ({stats['alliances']}): {', '.join(allies[:8])}")
    if stats["alliances_broken"]:
        lines.append(f"  Alliances broken: {stats['alliances_broken']}")
    if rivals:
        lines.append(f"  Rivalries ({stats['rivalries']}): {', '.join(rivals[:8])}")
    if negots:
        lines.append(f"  Negotiations ({stats['negotiations']}): {'; '.join(negots[:5])}")
    if not (allies or rivals or negots):
        lines.append("  No recorded relationships.")
    lines.append("")

    # ── Military ──────────────────────────────────────────────────────
    if stats["wars_declared"] or stats["battles"] or stats["conquests"]:
        lines.append("  MILITARY RECORD")
        lines.append("  " + hr("·", 68))
        if wars_declared:
            lines.append(f"  Wars declared ({stats['wars_declared']}): {', '.join(wars_declared[:6])}")
        if stats["battles"]:
            lines.append(f"  Battles/raids: {stats['battles']}")
        if conquests:
            lines.append(f"  Settlements conquered ({stats['conquests']}): {', '.join(conquests[:6])}")
        lines.append("")

    # ── Culture & Achievements ────────────────────────────────────────
    lines.append("  ACHIEVEMENTS")
    lines.append("  " + hr("·", 68))
    if stats["civs_founded"]:
        lines.append(f"  Civilizations founded: {stats['civs_founded']}")
    if settlements_list:
        lines.append(f"  Settlements founded ({stats['settlements_founded']}): {', '.join(settlements_list[:6])}")
    if stats["ruler_succession"]:
        lines.append(f"  Ruled as sovereign: Yes (succession event recorded)")
    if roles:
        lines.append(f"  Specialist roles held: {', '.join(set(roles))}")
    if artworks:
        lines.append(f"  Artworks/crafts ({stats['artworks'] + stats['artisan_crafts']}): {', '.join(artworks[:6])}")
    if discoveries:
        lines.append(f"  Scholarly discoveries ({stats['discoveries']}): {'; '.join(discoveries[:4])}")
    if stats["trades"]:
        lines.append(f"  Merchant trades completed: {stats['trades']}")
    if stats["heals"]:
        lines.append(f"  Patients healed: {stats['heals']}")
    if goals_completed:
        lines.append(f"  Goals completed ({stats['goals_completed']}): {', '.join(goals_completed[:5])}")
    if stats["goals_abandoned"]:
        lines.append(f"  Goals abandoned: {stats['goals_abandoned']}")
    if not any([stats["civs_founded"], settlements_list, roles, artworks, discoveries,
                stats["trades"], stats["heals"], goals_completed]):
        lines.append("  No major achievements recorded.")
    lines.append("")

    # ── Personality & Patterns ────────────────────────────────────────
    lines.append("  BEHAVIORAL PATTERNS")
    lines.append("  " + hr("·", 68))
    total_social = stats["alliances"] + stats["negotiations"]
    total_combat = stats["wars_declared"] * 3 + stats["battles"]
    if total_combat > total_social * 2:
        lines.append("  Temperament: Aggressive — combat far outweighs diplomacy")
    elif total_social > total_combat * 2:
        lines.append("  Temperament: Diplomatic — social bonds far outweigh conflict")
    elif total_combat > 0 and total_social > 0:
        lines.append("  Temperament: Balanced — mix of conflict and diplomacy")
    elif total_combat > 0:
        lines.append("  Temperament: Combative")
    elif total_social > 0:
        lines.append("  Temperament: Social")
    else:
        lines.append("  Temperament: Isolated — minimal social or combat record")

    if birth_ev:
        bp = pj(birth_ev)
        lines.append(f"  Born traits:  Ambition {bp.get('Ambition',0):.2f} | Aggression {bp.get('Aggression',0):.2f}")

    grief_str = f"  Emotional:    Grieved {stats['griefs']} time(s)"
    if stats["flourishing"]:
        grief_str += f", flourished {stats['flourishing']} time(s)"
    if stats["spiraling"]:
        grief_str += f", spiraled {stats['spiraling']} time(s)"
    lines.append(grief_str)

    lines.append("")
    return "\n".join(lines) + "\n"

# ── Main ───────────────────────────────────────────────────────────────────────

def usage():
    print(__doc__)
    sys.exit(1)

def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help", "help"):
        usage()

    db_path = sys.argv[1]
    args    = sys.argv[2:]

    try:
        conn = connect(db_path)
    except Exception as e:
        print(f"ERROR: Cannot open database '{db_path}': {e}")
        sys.exit(1)

    # Determine mode
    if not args:
        args = ["top", "10"]

    mode = args[0].lower()

    print()
    print("╔" + hr("═", 70) + "╗")
    print("║" + "  WORLD ENGINE — CHARACTER ANALYSIS".center(70) + "║")
    print("╚" + hr("═", 70) + "╝")
    print()

    if mode == "top":
        n = int(args[1]) if len(args) > 1 else 10
        chars = get_all_characters(conn)[:n]
        if not chars:
            print("  No characters found in database.")
            return
        print(f"  Top {n} most active characters (by total event count)\n")
        for i, c in enumerate(chars, 1):
            print(render_character_profile(conn, c["char_id"], rank=i))

    elif mode.startswith("civ:"):
        civ_id = int(mode[4:])
        civ_name = get_civ_name(conn, civ_id)
        chars = get_characters_by_civ(conn, civ_id)
        if not chars:
            print(f"  No characters found for civilization {civ_id}.")
            return
        print(f"  Notable characters of {civ_name} [{civ_id}]  ({len(chars)} found)\n")
        for i, c in enumerate(chars, 1):
            print(render_character_profile(conn, c["char_id"], rank=i))

    elif mode.startswith("era:"):
        m = re.match(r"era:(\d+)-(\d+)", mode)
        if not m:
            print("  ERROR: era format must be era:START-END  e.g. era:100-200")
            sys.exit(1)
        era_start, era_end = int(m.group(1)), int(m.group(2))
        chars = get_characters_in_era(conn, era_start, era_end)
        if not chars:
            print(f"  No characters active in years {era_start}–{era_end}.")
            return
        print(f"  Characters active in years {era_start}–{era_end}  ({len(chars)} found)\n")
        for i, c in enumerate(chars[:20], 1):
            print(render_character_profile(conn, c["char_id"], rank=i))

    else:
        # Assume it's a raw character ID
        try:
            char_id = int(mode)
        except ValueError:
            print(f"  ERROR: unknown argument '{mode}'")
            usage()
        print(render_character_profile(conn, char_id))

    conn.close()

if __name__ == "__main__":
    main()
