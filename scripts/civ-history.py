#!/usr/bin/env python3
"""
civ-history.py — Civilization-wide historical narratives for WorldEngine databases.

Reads world.db and produces narrative history reports for civilizations,
tracing their rise, expansion, wars, culture, and eventual fate.

Usage:
    python3 scripts/civ-history.py world.db top 5
    python3 scripts/civ-history.py world.db 7
    python3 scripts/civ-history.py world.db compare 3 7
    python3 scripts/civ-history.py world.db era:0-500
"""

import sqlite3
import json
import sys
import re
from collections import defaultdict

# ── EventType constants ────────────────────────────────────────────────────────
EV_BORN           = 3001
EV_DIED           = 3002
EV_EXILED         = 3004
EV_ALLIANCE_F     = 3101
EV_ALLIANCE_B     = 3102
EV_WAR            = 3103
EV_WAR_END        = 3104
EV_BATTLE         = 3105
EV_RIVALRY        = 3106
EV_NEGOTIATED     = 3107
EV_ARTWORK        = 3108
EV_CIV_FOUNDED    = 3201
EV_CIV_COLLAPSED  = 3202
EV_SET_FOUNDED    = 3203
EV_SET_DESTROYED  = 3204
EV_SUCCESSION     = 3205
EV_SET_STRAINING  = 3206
EV_SET_CONQUERED  = 3207
EV_SET_GREW       = 3401
EV_SET_SHRANK     = 3402
EV_SET_ABANDONED  = 3403
EV_DISEASE        = 3404
EV_DISEASE_REC    = 3405
EV_WILDLIFE_RAID  = 3406
EV_SUCCESSION_CRISIS = 3407
EV_APPOINTED      = 3301
EV_SCHOLAR        = 3304
EV_ARTISAN        = 3307
EV_MERCHANT       = 3303

ENV_EVENTS = {1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010}

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
    raw = row["PayloadJson"] if hasattr(row, "__getitem__") else row
    try:
        return json.loads(raw or "{}")
    except Exception:
        return {}

def ordinal(n: int) -> str:
    if 11 <= (n % 100) <= 13:
        return f"{n}th"
    return f"{n}{['th','st','nd','rd','th'][min(n % 10, 4)]}"

# ── Database queries ───────────────────────────────────────────────────────────

def get_all_civs(conn: sqlite3.Connection) -> list[dict]:
    """All civs with their founding year and total event count."""
    sql = """
        SELECT
            e.CivId,
            MAX(CASE WHEN e.Type = ? THEN json_extract(e.PayloadJson, '$.CivName') END) AS civ_name,
            MIN(CASE WHEN e.Type = ? THEN e.Year END) AS founded_year,
            MAX(CASE WHEN e.Type = ? THEN e.Year END) AS collapsed_year,
            COUNT(DISTINCT e.Id) AS event_count
        FROM Events e
        WHERE e.CivId IS NOT NULL
        GROUP BY e.CivId
        HAVING founded_year IS NOT NULL
        ORDER BY founded_year ASC
    """
    rows = conn.execute(sql, (EV_CIV_FOUNDED, EV_CIV_FOUNDED, EV_CIV_COLLAPSED)).fetchall()
    return [dict(r) for r in rows]

def get_civ_events(conn: sqlite3.Connection, civ_id: int) -> list[sqlite3.Row]:
    """All events for a civilization, in chronological order."""
    sql = """
        SELECT * FROM Events
        WHERE CivId = ?
        ORDER BY Year, Season, Id
    """
    return conn.execute(sql, (civ_id,)).fetchall()

def get_civ_name_from_events(events: list[sqlite3.Row]) -> str:
    for ev in events:
        if ev["Type"] == EV_CIV_FOUNDED:
            p = pj(ev)
            return p.get("CivName") or ev["SettlementName"] or f"Civ {ev['CivId']}"
    # Fallback: try war/payload names
    for ev in events:
        p = pj(ev)
        name = p.get("DeclarerCivName") or p.get("CivName")
        if name:
            return name
    cid = events[0]["CivId"] if events else "?"
    return f"Civ {cid}"

def get_settlement_fate(conn: sqlite3.Connection, settlement_name: str) -> str | None:
    """Look up what happened to a settlement."""
    if not settlement_name:
        return None
    row = conn.execute("""
        SELECT Type, Year, PayloadJson FROM Events
        WHERE SettlementName = ?
          AND Type IN (?, ?, ?)
        ORDER BY Year DESC LIMIT 1
    """, (settlement_name, EV_SET_DESTROYED, EV_SET_ABANDONED, EV_SET_CONQUERED)).fetchone()
    if not row:
        return None
    p = pj(row)
    if row["Type"] == EV_SET_DESTROYED:
        by = p.get("DestroyerName", "unknown")
        return f"destroyed by {by} in yr {row['Year']}"
    elif row["Type"] == EV_SET_ABANDONED:
        return f"abandoned in yr {row['Year']} (pop {p.get('Population',0)})"
    elif row["Type"] == EV_SET_CONQUERED:
        by = p.get("ConquererName", "unknown")
        return f"conquered by {by} in yr {row['Year']}"
    return None

def get_other_civ_name(conn: sqlite3.Connection, civ_id: int) -> str:
    row = conn.execute(
        "SELECT PayloadJson FROM Events WHERE Type = ? AND CivId = ? LIMIT 1",
        (EV_CIV_FOUNDED, civ_id)
    ).fetchone()
    if row:
        return pj(row).get("CivName", f"Civ {civ_id}")
    return f"Civ {civ_id}"

def get_current_sim_year(conn: sqlite3.Connection) -> int:
    row = conn.execute("SELECT MAX(Year) AS y FROM Events").fetchone()
    return row["y"] or 0

# ── Archetype classifier ───────────────────────────────────────────────────────

def classify_civ_archetype(stats: dict) -> str:
    military  = stats["wars_declared"] * 3 + stats["battles"] + stats["conquests"] * 2
    cultural  = stats["artworks"] * 2 + stats["discoveries"] * 2 + stats["artisan_crafts"]
    expansion = stats["settlements_founded"] * 3 + stats["settlements_conquered"]
    diplomatic= stats["alliances"] * 2 + stats["negotiations"]
    peaceful  = stats["settlements_founded"] - stats["wars_declared"]

    scores = {
        "Expansionist": expansion,
        "Military":     military,
        "Cultural":     cultural,
        "Diplomatic":   diplomatic,
        "Peaceful":     max(0, peaceful),
    }
    if all(v == 0 for v in scores.values()):
        return "Unknown"
    return max(scores, key=scores.get)

# ── Timeline event formatter ───────────────────────────────────────────────────

def civ_narrative_event(ev) -> str | None:
    t = ev["Type"]
    p = pj(ev)
    yr = ev["Year"]

    if t == EV_CIV_FOUNDED:
        founder = p.get("FounderName", "unknown")
        return f"Civilization founded by {founder}"
    elif t == EV_CIV_COLLAPSED:
        reason = p.get("Reason") or "unknown causes"
        return f"CIVILIZATION COLLAPSED — {reason}"
    elif t == EV_SET_FOUNDED:
        sname  = ev["SettlementName"] or "a settlement"
        pop    = p.get("StartingPopulation", 0)
        founder = p.get("FounderName", "?")
        return f"Settlement '{sname}' founded by {founder} (pop {pop})"
    elif t == EV_SET_DESTROYED:
        sname   = ev["SettlementName"] or "a settlement"
        by      = p.get("DestroyerName", "unknown")
        return f"Settlement '{sname}' destroyed by {by}"
    elif t == EV_SET_ABANDONED:
        sname  = ev["SettlementName"] or "a settlement"
        pop    = p.get("Population", 0)
        return f"Settlement '{sname}' abandoned (pop {pop})"
    elif t == EV_SET_CONQUERED:
        sname  = ev["SettlementName"] or "a settlement"
        by     = p.get("ConquererName", "?")
        prev   = p.get("PreviousCivId", "?")
        surv   = p.get("SurvivingPop", 0)
        return f"Settlement '{sname}' conquered by {by} (surviving pop {surv})"
    elif t == EV_WAR:
        declarer  = p.get("DeclarerName", "?")
        target    = p.get("TargetCivName", "?")
        cause     = p.get("Cause", "?")
        desc      = p.get("CauseDescription", "")
        return f"War declared against {target} by {declarer} — {cause}: {desc}"
    elif t == EV_WAR_END:
        other = p.get("CivBName") or p.get("CivAName","?")
        return f"War ended with {other} — {p.get('Outcome','?')}"
    elif t == EV_BATTLE:
        sname   = ev["SettlementName"] or "settlement"
        raider  = p.get("RaiderName","?")
        outcome = p.get("RaidOutcome","?")
        return f"Raid on '{sname}' by {raider} — {outcome}"
    elif t == EV_SUCCESSION:
        pred  = p.get("PredecessorName","?")
        succ  = p.get("SuccessorName","?")
        ordnum = p.get("SuccessorOrdinal",0)
        return f"Succession: {pred} → {succ} ({ordinal(ordnum)} ruler)"
    elif t == EV_SUCCESSION_CRISIS:
        end_yr = p.get("CrisisEndYear","?")
        return f"Succession crisis — instability until year {end_yr}"
    elif t == EV_DISEASE:
        pop  = p.get("Population",0)
        sname = ev["SettlementName"] or "a settlement"
        return f"Disease outbreak in '{sname}' (pop {pop})"
    elif t == EV_DISEASE_REC:
        dur  = p.get("DurationYears",0)
        sname = ev["SettlementName"] or "a settlement"
        return f"Disease cleared in '{sname}' after {dur} yrs"
    elif t == EV_WILDLIFE_RAID:
        lost  = p.get("PopulationLost",0)
        sname = ev["SettlementName"] or "a settlement"
        dname = p.get("DefenderName")
        tail  = f", defended by {dname}" if dname else ""
        return f"Wildlife raid on '{sname}' — {lost} population lost{tail}"
    elif t == EV_ALLIANCE_F:
        decl   = p.get("DeclarerName","?")
        target = p.get("TargetName","?")
        t_civ  = p.get("TargetCivId","?")
        return f"Alliance formed: {decl} + {target} (civ {t_civ})"
    elif t == EV_ALLIANCE_B:
        a = p.get("CharacterAName","?")
        b = p.get("CharacterBName","?")
        return f"Alliance broken: {a} & {b} — {p.get('Reason','unknown')}"
    elif t == EV_SET_STRAINING:
        sname    = ev["SettlementName"] or "a settlement"
        resource = p.get("Resource","?")
        impact   = p.get("Impact","?")
        return f"'{sname}' straining on {resource} ({impact})"
    elif t == EV_SCHOLAR:
        disc  = p.get("DiscoveryType","?")
        actor = ev["ActorName"] or "a scholar"
        return f"Discovery by {actor}: {disc}"
    elif t == EV_ARTWORK:
        art   = p.get("ArtType","artwork")
        actor = ev["ActorName"] or "an artist"
        return f"Artwork ({art}) created by {actor}"
    elif t == EV_APPOINTED:
        role  = p.get("Role","?")
        actor = ev["ActorName"] or "someone"
        return f"{actor} appointed as {role}"
    return None

# ── Main report renderer ───────────────────────────────────────────────────────

def render_civ_history(conn: sqlite3.Connection, civ_id: int, rank: int | None = None) -> str:
    events = get_civ_events(conn, civ_id)
    if not events:
        return f"  [No events found for civilization {civ_id}]\n"

    lines: list[str] = []
    current_year = get_current_sim_year(conn)

    civ_name    = get_civ_name_from_events(events)
    found_ev    = next((e for e in events if e["Type"] == EV_CIV_FOUNDED), None)
    collapse_ev = next((e for e in events if e["Type"] == EV_CIV_COLLAPSED), None)

    founded_year   = found_ev["Year"] if found_ev else events[0]["Year"]
    collapsed_year = collapse_ev["Year"] if collapse_ev else None
    duration       = (collapsed_year or current_year) - founded_year
    still_active   = collapse_ev is None

    # Header
    rank_str = f"  #{rank}  " if rank is not None else "  "
    status_badge = "  [ACTIVE]" if still_active else "  [COLLAPSED]"
    lines.append(hr("═"))
    lines.append(f"{rank_str}{civ_name}  (Civ ID: {civ_id}){status_badge}")
    lines.append(hr("─"))

    # Foundation block
    if found_ev:
        fp      = pj(found_ev)
        founder = fp.get("FounderName","unknown")
        lines.append(f"  Founded:    Year {founded_year} by {founder}")
    if collapsed_year:
        cp = pj(collapse_ev)
        reason = cp.get("Reason","unknown")
        lines.append(f"  Collapsed:  Year {collapsed_year} — {reason}")
        lines.append(f"  Duration:   {duration} years")
    else:
        lines.append(f"  Active:     Year {founded_year} → present  ({duration}+ years)")

    # Collect stats
    stats = defaultdict(int)

    settlements_founded:   list[tuple[int,str]] = []
    settlements_destroyed: list[tuple[int,str]] = []
    settlements_abandoned: list[tuple[int,str]] = []
    settlements_conquered_away: list[tuple[int,str]] = []  # taken by someone else
    settlements_conquered_gain: list[tuple[int,str]] = []  # this civ took them
    wars_declared:    list[tuple[int,str,str]] = []  # (year, target_civ, cause)
    wars_ended:       list[tuple[int,str,str]] = []  # (year, other_civ, outcome)
    alliances:        list[tuple[int,str]] = []
    alliances_broken: list[tuple[int,str]] = []
    rulers:           list[tuple[int,str,str]] = []  # (year, name, ordinal_str)
    notable_events:   list[tuple[int,int,str]] = []  # (year, season, text)
    diseases:         list[tuple[int,str]] = []
    wildlife_raids:   list[tuple[int,str,int]] = []
    discoveries:      list[str] = []
    artworks:         int = 0

    interacted_civs: dict[int, dict] = defaultdict(lambda: {"ally": 0, "enemy": 0})

    for ev in events:
        t = ev["Type"]
        p = pj(ev)
        yr = ev["Year"]
        sn = ev["Season"]

        if t == EV_SET_FOUNDED:
            sname = ev["SettlementName"] or p.get("SettlementName","?")
            settlements_founded.append((yr, sname))
            stats["settlements_founded"] += 1
        elif t == EV_SET_DESTROYED:
            sname = ev["SettlementName"] or "?"
            settlements_destroyed.append((yr, sname))
            stats["settlements_destroyed"] += 1
        elif t == EV_SET_ABANDONED:
            sname = ev["SettlementName"] or "?"
            settlements_abandoned.append((yr, sname))
            stats["settlements_abandoned"] += 1
        elif t == EV_SET_CONQUERED:
            sname = ev["SettlementName"] or "?"
            prev_civ = p.get("PreviousCivId", 0)
            conqueror_civ = p.get("ConquerorCivId", 0)
            if conqueror_civ == civ_id:
                # We conquered something from someone else
                settlements_conquered_gain.append((yr, sname))
                stats["conquests"] += 1
                if prev_civ:
                    interacted_civs[prev_civ]["enemy"] += 1
            else:
                # Someone took our settlement
                settlements_conquered_away.append((yr, sname))
                stats["settlements_lost"] += 1
                if conqueror_civ:
                    interacted_civs[conqueror_civ]["enemy"] += 1
        elif t == EV_WAR:
            target_civ = p.get("TargetCivName","?")
            cause      = p.get("Cause","?")
            wars_declared.append((yr, target_civ, cause))
            stats["wars_declared"] += 1
            target_civ_id = p.get("TargetCivId", 0)
            if target_civ_id:
                interacted_civs[target_civ_id]["enemy"] += 3
        elif t == EV_WAR_END:
            other     = p.get("CivBName") or p.get("CivAName","?")
            outcome   = p.get("Outcome","?")
            wars_ended.append((yr, other, outcome))
            civ_a_id = p.get("CivAId", 0)
            civ_b_id = p.get("CivBId", 0)
            other_id  = civ_b_id if civ_a_id == civ_id else civ_a_id
            if other_id:
                interacted_civs[other_id]["enemy"] += 1
        elif t == EV_BATTLE:
            stats["battles"] += 1
        elif t == EV_ALLIANCE_F:
            decl   = p.get("DeclarerName","?")
            target = p.get("TargetName","?")
            other_civ_id = p.get("TargetCivId") if p.get("DeclarerCivId") == civ_id else p.get("DeclarerCivId",0)
            other_civ_name = p.get("TargetName","?") if p.get("DeclarerCivId") == civ_id else p.get("DeclarerName","?")
            alliances.append((yr, f"{decl} ↔ {target}"))
            stats["alliances"] += 1
            if other_civ_id:
                interacted_civs[int(other_civ_id)]["ally"] += 1
        elif t == EV_ALLIANCE_B:
            alliances_broken.append((yr, f"{p.get('CharacterAName','?')} & {p.get('CharacterBName','?')}"))
            stats["alliances_broken"] += 1
        elif t == EV_SUCCESSION:
            succ = p.get("SuccessorName","?")
            ordnum = p.get("SuccessorOrdinal",0)
            rulers.append((yr, succ, ordinal(ordnum)))
            stats["succession_events"] += 1
        elif t == EV_SUCCESSION_CRISIS:
            stats["succession_crises"] += 1
            notable_events.append((yr, sn, f"Succession crisis (instability until yr {p.get('CrisisEndYear','?')})"))
        elif t == EV_DISEASE:
            sname = ev["SettlementName"] or "a settlement"
            diseases.append((yr, sname))
            stats["disease_outbreaks"] += 1
            notable_events.append((yr, sn, f"Disease outbreak in '{sname}' (pop {p.get('Population',0)})"))
        elif t == EV_WILDLIFE_RAID:
            sname = ev["SettlementName"] or "a settlement"
            lost  = p.get("PopulationLost", 0)
            wildlife_raids.append((yr, sname, lost))
            if lost >= 5:
                notable_events.append((yr, sn, f"Wildlife raid on '{sname}' — {lost} lives lost"))
        elif t == EV_SET_STRAINING:
            sname = ev["SettlementName"] or "a settlement"
            res   = p.get("Resource","?")
            stats["resource_strains"] += 1
        elif t == EV_SCHOLAR:
            disc = p.get("DiscoveryType","?")
            actor = ev["ActorName"] or "scholar"
            discoveries.append(f"{disc} by {actor} (yr {yr})")
            stats["discoveries"] += 1
        elif t == EV_ARTWORK:
            artworks += 1
            stats["artworks"] += 1
        elif t == EV_ARTISAN:
            artworks += 1
            stats["artisan_crafts"] += 1
        elif t == EV_NEGOTIATED:
            stats["negotiations"] += 1
        elif t in ENV_EVENTS:
            evname = ev["TypeName"]
            notable_events.append((yr, sn, f"Environmental: {evname}"))

    archetype = classify_civ_archetype(stats)
    total_events = len(events)
    peak_settlements = max(stats["settlements_founded"] - stats["settlements_destroyed"] - stats["settlements_abandoned"], 0)

    lines.append(f"  Archetype:  {archetype}  |  Total recorded events: {total_events}")
    lines.append("")

    # ── Foundation & Expansion ─────────────────────────────────────────
    lines.append("  EXPANSION HISTORY")
    lines.append("  " + hr("·", 68))
    lines.append(f"  Settlements founded:  {stats['settlements_founded']}")
    lines.append(f"  Settlements conquered (gained):  {stats['conquests']}")
    lines.append(f"  Settlements lost (destroyed/abandoned/taken):  "
                 f"{stats['settlements_destroyed'] + stats['settlements_abandoned'] + stats['settlements_lost']}")

    if settlements_founded:
        sample = settlements_founded[:6]
        rest   = len(settlements_founded) - len(sample)
        names  = ", ".join(f"'{n}' (yr {yr})" for yr, n in sample)
        if rest > 0:
            names += f" +{rest} more"
        lines.append(f"  Founded:  {names}")

    if settlements_conquered_gain:
        sample = settlements_conquered_gain[:5]
        names  = ", ".join(f"'{n}' (yr {yr})" for yr, n in sample)
        lines.append(f"  Conquered:  {names}")

    if settlements_conquered_away or settlements_destroyed or settlements_abandoned:
        lost = [(yr, n) for yr, n in settlements_conquered_away] + \
               [(yr, n) for yr, n in settlements_destroyed] + \
               [(yr, n) for yr, n in settlements_abandoned]
        lost.sort()
        sample = lost[:5]
        names  = ", ".join(f"'{n}' (yr {yr})" for yr, n in sample)
        lines.append(f"  Lost:  {names}")

    lines.append("")

    # ── Military History ───────────────────────────────────────────────
    if stats["wars_declared"] or stats["battles"]:
        lines.append("  MILITARY HISTORY")
        lines.append("  " + hr("·", 68))
        lines.append(f"  Wars declared: {stats['wars_declared']}  |  Battles/raids: {stats['battles']}")

        victories = sum(1 for _, _, o in wars_ended if "victory" in o.lower() or "won" in o.lower())
        losses    = sum(1 for _, _, o in wars_ended if "defeat" in o.lower() or "lost" in o.lower())
        if wars_ended:
            lines.append(f"  War outcomes:  {victories} victories, {losses} defeats, "
                         f"{len(wars_ended) - victories - losses} other/unknown")

        if wars_declared:
            for yr, target, cause in wars_declared[:6]:
                lines.append(f"    Year {yr:>5}  War declared against {target} ({cause})")
        if wars_ended:
            for yr, other, outcome in wars_ended[:6]:
                lines.append(f"    Year {yr:>5}  War ended vs {other} — {outcome}")

        lines.append("")

    # ── Diplomacy ─────────────────────────────────────────────────────
    if alliances or stats["negotiations"]:
        lines.append("  DIPLOMATIC RECORD")
        lines.append("  " + hr("·", 68))
        lines.append(f"  Alliances formed: {stats['alliances']}  |  Broken: {stats['alliances_broken']}  |  Negotiations: {stats['negotiations']}")
        for yr, pair in alliances[:5]:
            lines.append(f"    Year {yr:>5}  Alliance: {pair}")
        for yr, pair in alliances_broken[:3]:
            lines.append(f"    Year {yr:>5}  Broken:   {pair}")
        lines.append("")

    # ── Succession ────────────────────────────────────────────────────
    if rulers:
        lines.append("  RULERS & SUCCESSION")
        lines.append("  " + hr("·", 68))
        lines.append(f"  Succession events: {stats['succession_events']}  |  Crises: {stats['succession_crises']}")
        for yr, name, ordstr in rulers[:10]:
            lines.append(f"    Year {yr:>5}  {ordstr} ruler: {name}")
        if len(rulers) > 10:
            lines.append(f"    ... and {len(rulers) - 10} more")
        lines.append("")

    # ── Culture ───────────────────────────────────────────────────────
    if stats["artworks"] or stats["artisan_crafts"] or discoveries:
        lines.append("  CULTURAL OUTPUT")
        lines.append("  " + hr("·", 68))
        total_art = stats["artworks"] + stats["artisan_crafts"]
        lines.append(f"  Total artworks/crafts: {total_art}  |  Scholarly discoveries: {stats['discoveries']}")
        if discoveries:
            for d in discoveries[:5]:
                lines.append(f"    · {d}")
        lines.append("")

    # ── Notable Events ────────────────────────────────────────────────
    if notable_events or diseases or wildlife_raids:
        lines.append("  NOTABLE EVENTS & DISASTERS")
        lines.append("  " + hr("·", 68))
        if stats["disease_outbreaks"]:
            lines.append(f"  Disease outbreaks: {stats['disease_outbreaks']}")
        if wildlife_raids:
            total_lost = sum(lost for _, _, lost in wildlife_raids)
            lines.append(f"  Wildlife raids: {len(wildlife_raids)} (total {total_lost} population lost)")
        if stats["resource_strains"]:
            lines.append(f"  Resource strain events: {stats['resource_strains']}")

        shown = sorted(notable_events, key=lambda x: (x[0], x[1]))[:8]
        for yr, sn, txt in shown:
            lines.append(f"    {season_str(yr, sn)}  {txt}")
        lines.append("")

    # ── Neighboring Civilizations ─────────────────────────────────────
    if interacted_civs:
        lines.append("  NEIGHBORING CIVILIZATIONS")
        lines.append("  " + hr("·", 68))

        # Sort by total interaction
        sorted_civs = sorted(
            interacted_civs.items(),
            key=lambda x: x[1]["ally"] + x[1]["enemy"],
            reverse=True
        )[:8]

        for other_civ_id, rel in sorted_civs:
            other_name = get_other_civ_name(conn, other_civ_id)
            ally_score  = rel["ally"]
            enemy_score = rel["enemy"]
            if ally_score > enemy_score * 2:
                rel_label = "ally"
            elif enemy_score > ally_score * 2:
                rel_label = "enemy"
            else:
                rel_label = "rival"
            lines.append(f"    {other_name} [{other_civ_id}]  — {rel_label}  "
                         f"(ally score {ally_score}, enemy score {enemy_score})")
        lines.append("")

    # ── Full Civ Timeline (headline events only) ───────────────────────
    lines.append("  CIVILIZATION TIMELINE  (Headline/Regional events)")
    lines.append("  " + hr("·", 68))
    TIMELINE_TYPES = {
        EV_CIV_FOUNDED, EV_CIV_COLLAPSED, EV_SET_FOUNDED, EV_SET_DESTROYED,
        EV_SET_ABANDONED, EV_SET_CONQUERED, EV_WAR, EV_WAR_END,
        EV_SUCCESSION, EV_SUCCESSION_CRISIS, EV_DISEASE, EV_WILDLIFE_RAID,
        EV_ALLIANCE_F, EV_SCHOLAR, EV_ARTWORK, EV_APPOINTED,
    }
    timeline_events = [
        e for e in events
        if e["Type"] in TIMELINE_TYPES or e["TierInvolvement"] >= 2
    ]
    # Deduplicate keeping one per (type, year) for high-volume types
    seen_keys: set[tuple] = set()
    filtered: list = []
    for ev in timeline_events:
        key = (ev["Type"], ev["Year"])
        if ev["Type"] in {EV_DISEASE, EV_WILDLIFE_RAID, EV_SET_STRAINING}:
            if key in seen_keys:
                continue
            seen_keys.add(key)
        filtered.append(ev)

    for ev in filtered[:40]:
        txt = civ_narrative_event(ev)
        if not txt:
            txt = ev["TypeName"]
        tier = ev["TierInvolvement"]
        star = " ★★" if tier == 3 else (" ★" if tier == 2 else "")
        lines.append(f"  {season_str(ev['Year'], ev['Season'])}  {txt}{star}")

    if len(filtered) > 40:
        lines.append(f"  ... and {len(filtered) - 40} more events omitted")

    lines.append("")
    return "\n".join(lines) + "\n"

def render_comparison(conn: sqlite3.Connection, civ_a: int, civ_b: int) -> str:
    """Side-by-side comparison of two civilizations."""
    lines: list[str] = []

    def summary(civ_id: int) -> dict:
        events = get_civ_events(conn, civ_id)
        if not events:
            return {}
        name        = get_civ_name_from_events(events)
        found_ev    = next((e for e in events if e["Type"] == EV_CIV_FOUNDED), None)
        collapse_ev = next((e for e in events if e["Type"] == EV_CIV_COLLAPSED), None)
        founded_yr  = found_ev["Year"] if found_ev else events[0]["Year"]
        collapsed_yr= collapse_ev["Year"] if collapse_ev else None
        current_yr  = get_current_sim_year(conn)
        duration    = (collapsed_yr or current_yr) - founded_yr
        stats       = defaultdict(int)
        for ev in events:
            t = ev["Type"]
            if t == EV_SET_FOUNDED:    stats["settlements_founded"] += 1
            elif t == EV_WAR:          stats["wars_declared"] += 1
            elif t == EV_BATTLE:       stats["battles"] += 1
            elif t == EV_ARTWORK:      stats["artworks"] += 1
            elif t == EV_ARTISAN:      stats["artworks"] += 1
            elif t == EV_SCHOLAR:      stats["discoveries"] += 1
            elif t == EV_ALLIANCE_F:   stats["alliances"] += 1
            elif t == EV_SUCCESSION:   stats["rulers"] += 1
            elif t == EV_SET_CONQUERED and pj(ev).get("ConquerorCivId") == civ_id:
                stats["conquests"] += 1
        return {
            "id": civ_id, "name": name, "founded": founded_yr,
            "duration": duration, "active": collapse_ev is None,
            "total_events": len(events), **stats
        }

    a = summary(civ_a)
    b = summary(civ_b)

    if not a:
        return f"  No data for civ {civ_a}\n"
    if not b:
        return f"  No data for civ {civ_b}\n"

    lines.append(hr("═"))
    lines.append("  CIVILIZATION COMPARISON")
    lines.append(hr("─"))

    W = 30
    fmt = f"  {{:<28}}  {{:>{W}}}  {{:>{W}}}"
    lines.append(fmt.format("", a["name"][:W], b["name"][:W]))
    lines.append(fmt.format("", f"[Civ {civ_a}]", f"[Civ {civ_b}]"))
    lines.append("  " + hr("·", 68))

    def row(label, key, suffix=""):
        va = str(a.get(key, 0)) + suffix
        vb = str(b.get(key, 0)) + suffix
        lines.append(fmt.format(label, va, vb))

    row("Founded (year)",         "founded")
    row("Duration (years)",       "duration")
    row("Still active",           "active")
    row("Total events",           "total_events")
    lines.append("  " + hr("·", 68))
    row("Settlements founded",    "settlements_founded")
    row("Conquests",              "conquests")
    row("Wars declared",          "wars_declared")
    row("Battles/raids",          "battles")
    row("Alliances formed",       "alliances")
    row("Succession events",      "rulers")
    lines.append("  " + hr("·", 68))
    row("Artworks/crafts",        "artworks")
    row("Scholarly discoveries",  "discoveries")
    lines.append("")

    # Verdict
    scores = {
        a["name"]: (a.get("duration",0) * 0.1 + a.get("settlements_founded",0) * 3
                    + a.get("wars_declared",0) * 2 + a.get("artworks",0)),
        b["name"]: (b.get("duration",0) * 0.1 + b.get("settlements_founded",0) * 3
                    + b.get("wars_declared",0) * 2 + b.get("artworks",0)),
    }
    winner = max(scores, key=scores.get)
    lines.append(f"  Overall prominence score: {a['name']} = {scores[a['name']]:.0f}, "
                 f"{b['name']} = {scores[b['name']]:.0f}")
    lines.append(f"  Historically more prominent: {winner}")
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

    if not args:
        args = ["top", "5"]

    mode = args[0].lower()

    print()
    print("╔" + hr("═", 70) + "╗")
    print("║" + "  WORLD ENGINE — CIVILIZATION HISTORY".center(70) + "║")
    print("╚" + hr("═", 70) + "╝")
    print()

    if mode == "top":
        n     = int(args[1]) if len(args) > 1 else 5
        sort  = args[2].lower() if len(args) > 2 else "longevity"
        civs  = get_all_civs(conn)
        if not civs:
            print("  No civilizations found in database.")
            return

        current_year = get_current_sim_year(conn)

        # Sort options
        if sort in ("longevity", "age"):
            civs.sort(key=lambda c: (
                (c.get("collapsed_year") or current_year) - (c.get("founded_year") or 0)
            ), reverse=True)
        elif sort in ("events", "activity"):
            civs.sort(key=lambda c: c.get("event_count", 0), reverse=True)
        else:
            civs.sort(key=lambda c: (c.get("founded_year") or 0))

        civs = civs[:n]
        print(f"  Top {n} civilizations (sorted by {sort})  —  {len(civs)} shown\n")
        for i, c in enumerate(civs, 1):
            print(render_civ_history(conn, c["CivId"], rank=i))

    elif mode == "compare":
        if len(args) < 3:
            print("  ERROR: compare requires two civ IDs")
            usage()
        civ_a = int(args[1])
        civ_b = int(args[2])
        print(render_comparison(conn, civ_a, civ_b))
        print("\n  Full history of each:\n")
        print(render_civ_history(conn, civ_a))
        print(render_civ_history(conn, civ_b))

    elif mode.startswith("era:"):
        m = re.match(r"era:(\d+)-(\d+)", mode)
        if not m:
            print("  ERROR: era format must be era:START-END  e.g. era:0-500")
            sys.exit(1)
        era_start, era_end = int(m.group(1)), int(m.group(2))
        civs = [c for c in get_all_civs(conn)
                if (c.get("founded_year") or 0) >= era_start
                and (c.get("founded_year") or 0) <= era_end]
        if not civs:
            print(f"  No civilizations founded in years {era_start}–{era_end}.")
            return
        print(f"  Civilizations founded in years {era_start}–{era_end}  ({len(civs)} found)\n")
        for i, c in enumerate(civs, 1):
            print(render_civ_history(conn, c["CivId"], rank=i))

    else:
        # Raw civ ID
        try:
            civ_id = int(mode)
        except ValueError:
            print(f"  ERROR: unknown argument '{mode}'")
            usage()
        print(render_civ_history(conn, civ_id))

    conn.close()

if __name__ == "__main__":
    main()
