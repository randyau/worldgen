#!/usr/bin/env python3
"""
generate-civ-story.py — Extract a civilization's history from world.db and generate a
long-form narrative via Ollama, weaving in bios of its founder, rulers, and other
notable figures rather than treating the civ as faceless.

Sibling to generate-story.py (single-character stories); this one is civ-scale and
deliberately longer, since a civ's arc spans generations. See docs/story-generation.md.

Auto-detects where Ollama is reachable (localhost, or — under WSL2 — the Windows
host gateway) unless --host is given explicitly. See docs/story-generation.md.

Usage:
    python3 scripts/generate-civ-story.py world.db [options]

Options:
    --model MODEL        Ollama model name (default: gemma4:e4b)
    --host  HOST         Ollama base URL (default: auto-detected)
    --top   N            Pick the N most notable civilizations (default: 1)
    --id    CIV_ID       Process a specific civilization ID
    --characters N       Max number of key figures to weave into the story (default: 4)
    --prompt-only        Print the prompt without calling Ollama
    --out   DIR          Write story files to DIR (default: stdout)
    --style STYLE        Story style: chronicle | saga (default: chronicle)

Examples:
    python3 scripts/generate-civ-story.py world.db --top 1
    python3 scripts/generate-civ-story.py world.db --id 7
    python3 scripts/generate-civ-story.py world.db --prompt-only --id 7
"""

import argparse
import json
import os
import re
import sqlite3
import subprocess
import sys
import textwrap
import urllib.request
import urllib.error
from collections import defaultdict
from pathlib import Path

# ── Event type constants ───────────────────────────────────────────────────────
EV_BORN              = 3001
EV_DIED               = 3002
EV_MARRIED            = 3003
EV_EXILED             = 3004
EV_GRIEVED            = 3005
EV_FLOURISHING        = 3006
EV_SPIRALING          = 3007
EV_ALLIANCE_F         = 3101
EV_ALLIANCE_B         = 3102
EV_WAR                = 3103
EV_WAR_END            = 3104
EV_BATTLE             = 3105
EV_RIVALRY            = 3106
EV_NEGOTIATED         = 3107
EV_ARTWORK            = 3108
EV_GOAL_FORMED        = 3109
EV_GOAL_RESOLVED      = 3110
EV_CIV_FOUNDED        = 3201
EV_CIV_COLLAPSED      = 3202
EV_SET_FOUNDED        = 3203
EV_SET_DESTROYED      = 3204
EV_SUCCESSION         = 3205
EV_SET_STRAINING      = 3206
EV_SET_CONQUERED      = 3207
EV_SUCCESSION_CRISIS  = 3407
EV_DISEASE            = 3404
EV_DISEASE_REC        = 3405
EV_WILDLIFE_RAID      = 3406
EV_APPOINTED          = 3301
EV_DISMISSED          = 3302
EV_SCHOLAR            = 3304
EV_ARTISAN            = 3307
EV_BEAST_CHAR         = 2007

SEASON = {0: "Spring", 1: "Summer", 2: "Autumn", 3: "Winter"}

# Character/settlement MaxHealth is 100 across the sim (CharacterSimConfig.MaxHealth,
# SettlementConfig.MaxHealth) — damage/health fields in event payloads are on this scale.
HEALTH_SCALE = 100

# Civ narratives span generations, so they get a much bigger event budget than a
# single-character story.
MAX_TIMELINE_EVENTS  = 80
MAX_CHAR_BIO_EVENTS  = 10


def damage_severity(dmg: float, scale: int = HEALTH_SCALE) -> str:
    """
    Translate a raw damage number (0-100 scale) into a qualitative severity phrase.

    Raw numbers like "dealt 35 damage" mean nothing to an LLM or a reader without
    the 0-100 scale as context — it either omits them or, worse, cites them verbatim
    as if they were meaningful on their own. Bucketing into severity tiers gives the
    narrative something it can actually describe.
    """
    if dmg <= 0:
        return ""
    frac = dmg / scale
    if frac < 0.05:
        return "a glancing blow"
    if frac < 0.15:
        return "minor damage"
    if frac < 0.35:
        return "significant damage"
    if frac < 0.60:
        return "heavy damage"
    return "devastating, near-crippling damage"


# ── DB helpers ─────────────────────────────────────────────────────────────────

def connect(path: str) -> sqlite3.Connection:
    conn = sqlite3.connect(path)
    conn.row_factory = sqlite3.Row
    return conn


def pj(row) -> dict:
    raw = row["PayloadJson"] if hasattr(row, "__getitem__") else row
    try:
        return json.loads(raw or "{}")
    except (json.JSONDecodeError, TypeError):
        return {}


def civ_name_map(conn: sqlite3.Connection) -> dict[int, str]:
    """Build {civ_id: name}, preferring CivSummaries, falling back to mined payloads."""
    civs: dict[int, str] = {}
    for r in conn.execute("SELECT CivId, Name FROM CivSummaries"):
        civs[r["CivId"]] = r["Name"]
    if civs:
        return civs
    for r in conn.execute("SELECT PayloadJson FROM Events WHERE Type IN (?,?)", (EV_WAR, EV_WAR_END)):
        p = pj(r)
        for id_key, name_key in [("DeclarerCivId", "DeclarerCivName"), ("TargetCivId", "TargetCivName"),
                                  ("WinnerCivId", "WinnerCivName"), ("LoserCivId", "LoserCivName")]:
            if p.get(id_key) and p.get(name_key):
                civs[p[id_key]] = p[name_key]
    return civs


def get_all_civs(conn: sqlite3.Connection) -> list[dict]:
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
    return conn.execute(
        "SELECT * FROM Events WHERE CivId = ? ORDER BY Year, Season, Id", (civ_id,)
    ).fetchall()


def get_civ_name_from_events(events: list[sqlite3.Row], civ_id: int) -> str:
    for ev in events:
        if ev["Type"] == EV_CIV_FOUNDED:
            p = pj(ev)
            return p.get("CivName") or ev["SettlementName"] or f"Civ {civ_id}"
    for ev in events:
        p = pj(ev)
        name = p.get("DeclarerCivName") or p.get("CivName")
        if name:
            return name
    return f"Civ {civ_id}"


def get_current_sim_year(conn: sqlite3.Connection) -> int:
    row = conn.execute("SELECT MAX(Year) AS y FROM Events").fetchone()
    return row["y"] or 0


def ordinal(n: int) -> str:
    if 11 <= (n % 100) <= 13:
        return f"{n}th"
    return f"{n}{['th', 'st', 'nd', 'rd', 'th'][min(n % 10, 4)]}"


# ── Archetype classifier (mirrors civ-history.py) ───────────────────────────────

def classify_civ_archetype(stats: dict) -> str:
    military   = stats["wars_declared"] * 3 + stats["battles"] + stats["conquests"] * 2
    cultural   = stats["artworks"] * 2 + stats["discoveries"] * 2 + stats["artisan_crafts"]
    expansion  = stats["settlements_founded"] * 3 + stats["settlements_conquered"]
    diplomatic = stats["alliances"] * 2 + stats["negotiations"]
    peaceful   = stats["settlements_founded"] - stats["wars_declared"]

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


# ── Civ-level timeline formatting ────────────────────────────────────────────────

def format_civ_event(ev: sqlite3.Row, civs: dict[int, str]) -> str | None:
    t = ev["Type"]
    p = pj(ev)
    year, s = ev["Year"], SEASON.get(ev["Season"], "")
    sn = ev["SettlementName"] or "a settlement"

    if t == EV_CIV_FOUNDED:
        return f"Year {year}, {s}: Civilization founded by {p.get('FounderName', 'unknown')}"
    if t == EV_CIV_COLLAPSED:
        return f"Year {year}, {s}: CIVILIZATION COLLAPSED — {p.get('Reason', 'unknown causes')}"
    if t == EV_SET_FOUNDED:
        return f"Year {year}, {s}: Settlement '{sn}' founded by {p.get('FounderName', '?')} (pop {p.get('StartingPopulation', 0)})"
    if t == EV_SET_DESTROYED:
        return f"Year {year}, {s}: Settlement '{sn}' destroyed by {p.get('DestroyerName', 'unknown')}"
    if t == EV_SET_CONQUERED:
        return f"Year {year}, {s}: Settlement '{sn}' conquered by {p.get('ConquererName', '?')}"
    if t == EV_WAR:
        cause = p.get("CauseDescription", "")
        suffix = f" ({cause})" if cause else ""
        return f"Year {year}, {s}: War declared against {p.get('TargetCivName', '?')} by {p.get('DeclarerName', '?')}{suffix}"
    if t == EV_WAR_END:
        other = p.get("CivBName") or p.get("CivAName", "?")
        return f"Year {year}, {s}: War with {other} ended — {p.get('Outcome', '?')}"
    if t == EV_BATTLE:
        severity = damage_severity(p.get("Damage", 0))
        outcome = p.get("RaidOutcome", "?")
        return f"Year {year}, {s}: Battle at '{sn}' — {outcome}" + (f" ({severity})" if severity else "")
    if t == EV_SUCCESSION:
        return f"Year {year}, {s}: {p.get('SuccessorName', '?')} became the {ordinal(p.get('SuccessorOrdinal', 0))} ruler, succeeding {p.get('PredecessorName', '?')}"
    if t == EV_SUCCESSION_CRISIS:
        return f"Year {year}, {s}: A succession crisis destabilized the civilization"
    if t == EV_DISEASE:
        return f"Year {year}, {s}: Disease broke out in '{sn}'"
    if t == EV_WILDLIFE_RAID:
        lost = p.get("PopulationLost", 0)
        return f"Year {year}, {s}: Wildlife raid on '{sn}' — {lost} lives lost" if lost else None
    if t == EV_ALLIANCE_F:
        return f"Year {year}, {s}: Alliance formed with {p.get('TargetName', 'another civilization')}"
    if t == EV_ALLIANCE_B:
        return f"Year {year}, {s}: Alliance broken — {p.get('Reason', 'unknown cause')}"
    if t == EV_SCHOLAR:
        return f"Year {year}, {s}: {p.get('ActorName') or 'A scholar'} made a discovery ({p.get('DiscoveryType', '?')})"
    if t == EV_ARTWORK:
        return f"Year {year}, {s}: {ev['ActorName'] or 'An artist'} created a {p.get('ArtType', 'work of art')}"
    if t == EV_APPOINTED:
        return f"Year {year}, {s}: {ev['ActorName'] or 'Someone'} appointed as {p.get('Role', 'an official')}"
    return None


def build_civ_timeline(events: list[sqlite3.Row], civ_id: int, civs: dict[int, str]) -> tuple[list[str], dict]:
    """Returns (formatted timeline lines, stats dict) — mirrors civ-history.py's stat pass."""
    stats = defaultdict(int)
    lines: list[str] = []
    seen_dedup_keys: set[tuple] = set()

    TIMELINE_TYPES = {
        EV_CIV_FOUNDED, EV_CIV_COLLAPSED, EV_SET_FOUNDED, EV_SET_DESTROYED,
        EV_SET_CONQUERED, EV_WAR, EV_WAR_END, EV_SUCCESSION, EV_SUCCESSION_CRISIS,
        EV_DISEASE, EV_WILDLIFE_RAID, EV_ALLIANCE_F, EV_ALLIANCE_B, EV_SCHOLAR,
        EV_ARTWORK, EV_APPOINTED, EV_BATTLE,
    }

    for ev in events:
        t = ev["Type"]
        p = pj(ev)

        if t == EV_SET_FOUNDED: stats["settlements_founded"] += 1
        elif t == EV_SET_DESTROYED: stats["settlements_destroyed"] += 1
        elif t == EV_SET_CONQUERED:
            if p.get("ConquerorCivId") == civ_id: stats["conquests"] += 1
            else: stats["settlements_lost"] += 1
        elif t == EV_WAR: stats["wars_declared"] += 1
        elif t == EV_BATTLE: stats["battles"] += 1
        elif t == EV_ALLIANCE_F: stats["alliances"] += 1
        elif t == EV_SUCCESSION: stats["succession_events"] += 1
        elif t == EV_SCHOLAR: stats["discoveries"] += 1
        elif t == EV_ARTWORK: stats["artworks"] += 1
        elif t == EV_ARTISAN: stats["artisan_crafts"] += 1
        elif t == EV_NEGOTIATED: stats["negotiations"] += 1

        if t not in TIMELINE_TYPES and ev["TierInvolvement"] < 2:
            continue

        # High-volume event types: keep at most one per (type, year) so a bad
        # decade of wildlife raids doesn't crowd out the rest of the civ's life.
        if t in (EV_DISEASE, EV_WILDLIFE_RAID):
            key = (t, ev["Year"])
            if key in seen_dedup_keys:
                continue
            seen_dedup_keys.add(key)

        txt = format_civ_event(ev, civs)
        if txt:
            lines.append(txt)

    if len(lines) > MAX_TIMELINE_EVENTS:
        head = lines[:10]
        tail = lines[-10:]
        mid  = lines[10:-10]
        step = max(1, len(mid) // (MAX_TIMELINE_EVENTS - 20))
        mid  = mid[::step][: MAX_TIMELINE_EVENTS - 20]
        lines = head + mid + tail

    return lines, stats


# ── Character bio extraction (adapted from generate-story.py) ──────────────────

def get_key_characters(conn: sqlite3.Connection, civ_id: int, events: list[sqlite3.Row], limit: int) -> list[dict]:
    """
    Pick up to `limit` figures to weave into the story: the founder always takes
    one slot, and the remaining slots go to rulers — sampled across the civ's
    lifespan (first, last, and an even spread between) rather than all of them,
    since a long-running civ can rack up dozens of successions and dumping every
    ruler into the prompt would drown out the actual narrative. Any slots still
    free after that go to the civ's most event-rich non-ruler members.
    """
    picked: dict[int, str] = {}  # char_id -> role label, insertion-ordered

    found_ev = next((e for e in events if e["Type"] == EV_CIV_FOUNDED), None)
    founder_id = None
    if found_ev:
        fid = pj(found_ev).get("FounderId")
        if fid:
            founder_id = int(fid)
            picked[founder_id] = "Founder"

    rulers: list[tuple[int, str]] = []  # (char_id, role), chronological
    seen_rulers: set[int] = set()
    for ev in events:
        if ev["Type"] == EV_SUCCESSION:
            p = pj(ev)
            sid = p.get("SuccessorId")
            if sid and int(sid) not in seen_rulers and int(sid) != founder_id:
                seen_rulers.add(int(sid))
                rulers.append((int(sid), f"{ordinal(p.get('SuccessorOrdinal', 0))} Ruler"))

    ruler_budget = max(0, limit - len(picked))
    if len(rulers) <= ruler_budget:
        sample = rulers
    else:
        # Head + tail + an even spread of the middle, same pattern as compress_events.
        head = rulers[:1]
        tail = rulers[-1:]
        mid  = rulers[1:-1]
        mid_budget = max(0, ruler_budget - len(head) - len(tail))
        step = max(1, len(mid) // max(1, mid_budget)) if mid_budget else len(mid) + 1
        sample = head + mid[::step][:mid_budget] + tail
    for cid, role in sample:
        picked[cid] = role

    if len(picked) < limit:
        rows = conn.execute("""
            SELECT ActorId, COUNT(*) AS n
            FROM Events
            WHERE CivId = ? AND ActorId IS NOT NULL
              AND Type NOT IN (?, ?)
            GROUP BY ActorId
            ORDER BY n DESC
        """, (civ_id, EV_GOAL_FORMED, EV_GOAL_RESOLVED)).fetchall()
        for r in rows:
            if len(picked) >= limit:
                break
            cid = r["ActorId"]
            if cid not in picked:
                picked[cid] = "Notable figure"

    return [{"CharacterId": cid, "Role": role} for cid, role in picked.items()]


def load_character(conn: sqlite3.Connection, char_id: int) -> dict | None:
    row = conn.execute("SELECT * FROM CharacterSummaries WHERE CharacterId = ?", (char_id,)).fetchone()
    if row is not None:
        return dict(row)

    born = conn.execute("SELECT ActorName, Year, PayloadJson FROM Events WHERE ActorId=? AND Type=? LIMIT 1",
                         (char_id, EV_BORN)).fetchone()
    died = conn.execute("SELECT Year, PayloadJson FROM Events WHERE ActorId=? AND Type=? LIMIT 1",
                         (char_id, EV_DIED)).fetchone()
    if born is None:
        any_ev = conn.execute("SELECT ActorName FROM Events WHERE ActorId=? AND ActorName IS NOT NULL LIMIT 1",
                               (char_id,)).fetchone()
        if any_ev is None:
            return None
        return {"CharacterId": char_id, "Name": any_ev["ActorName"], "Epithet": None,
                "BirthYear": None, "DeathYear": None, "AgeSeasons": None}

    age_s = None
    if died:
        age_s = pj(died).get("AgeSeasons")

    return {
        "CharacterId": char_id,
        "Name": born["ActorName"],
        "Epithet": None,
        "BirthYear": born["Year"],
        "DeathYear": died["Year"] if died else None,
        "AgeSeasons": age_s,
    }


def load_character_events(conn: sqlite3.Connection, char_id: int) -> list[sqlite3.Row]:
    """A character's own core-event slice — same CORE_EVENTS filter as generate-story.py."""
    CORE = {
        EV_BORN, EV_DIED, EV_MARRIED, EV_EXILED, EV_FLOURISHING, EV_SPIRALING,
        EV_GRIEVED, EV_ALLIANCE_F, EV_ALLIANCE_B, EV_WAR, EV_WAR_END, EV_BATTLE,
        EV_ARTWORK, EV_SUCCESSION, EV_SET_FOUNDED, EV_SET_CONQUERED,
        EV_APPOINTED, EV_DISMISSED, EV_BEAST_CHAR,
    }
    rows = conn.execute(
        "SELECT * FROM Events WHERE ActorId = ? ORDER BY Tick", (char_id,)
    ).fetchall()
    return [r for r in rows if r["Type"] in CORE][:MAX_CHAR_BIO_EVENTS]


def format_char_bio_event(ev: sqlite3.Row) -> str:
    t, year, s = ev["Type"], ev["Year"], SEASON.get(ev["Season"], "")
    p = pj(ev)
    if t == EV_BORN:
        return f"Year {year}: born"
    if t == EV_DIED:
        cause = p.get("Cause", "unknown causes")
        return f"Year {year}: died of {cause}"
    if t == EV_MARRIED:
        return f"Year {year}: married {p.get('PartnerName', 'someone')}"
    if t == EV_SUCCESSION:
        return f"Year {year}: became ruler"
    if t == EV_SET_FOUNDED:
        return f"Year {year}: founded the settlement of {ev['SettlementName'] or '?'}"
    if t == EV_WAR:
        return f"Year {year}: declared war on {p.get('TargetCivName', '?')}"
    if t == EV_BATTLE:
        severity = damage_severity(p.get("Damage", 0))
        outcome = p.get("RaidOutcome", "battle")
        return f"Year {year}: fought at {ev['SettlementName'] or 'a settlement'} — {outcome}" + (f" ({severity})" if severity else "")
    if t == EV_ARTWORK:
        return f"Year {year}: created a {p.get('ArtworkType', 'work of art').lower()}"
    if t == EV_APPOINTED:
        return f"Year {year}: appointed as {p.get('Role', 'an official')}"
    if t == EV_BEAST_CHAR:
        severity = damage_severity(p.get("DamageDealt", p.get("Damage", 0)))
        beast = p.get("BeastName", p.get("BeastSpecies", "a beast"))
        return f"Year {year}: attacked by {beast}" + (f", suffered {severity}" if severity else "")
    return f"Year {year}: {ev['TypeName']}"


def build_character_bios(conn: sqlite3.Connection, key_chars: list[dict]) -> list[str]:
    bios = []
    for kc in key_chars:
        char = load_character(conn, kc["CharacterId"])
        if char is None:
            continue
        name = char.get("Name") or f"#{kc['CharacterId']}"
        epithet = char.get("Epithet")
        full = f'{name} "{epithet}"' if epithet else name
        lifespan = f"b. Year {char['BirthYear']}" if char.get("BirthYear") else "birth year unknown"
        if char.get("DeathYear"):
            lifespan += f", d. Year {char['DeathYear']}"
        events = load_character_events(conn, kc["CharacterId"])
        beats = "; ".join(format_char_bio_event(e) for e in events) or "no further detail recorded"
        bios.append(f"- {full} ({kc['Role']}, {lifespan}): {beats}")
    return bios


# ── Prompt builder ─────────────────────────────────────────────────────────────

def build_prompt(civ_name: str, civ_id: int, founded_year: int, collapsed_year: int | None,
                  current_year: int, stats: dict, archetype: str,
                  character_bios: list[str], timeline_lines: list[str], style: str) -> str:
    still_active = collapsed_year is None
    duration = (collapsed_year or current_year) - founded_year
    status = f"collapsed in Year {collapsed_year} ({duration} years)" if not still_active \
        else f"still active as of Year {current_year} ({duration}+ years and counting)"

    stats_str = (
        f"{stats['settlements_founded']} settlement(s) founded, "
        f"{stats['conquests']} conquered, {stats['settlements_lost']} lost, "
        f"{stats['wars_declared']} war(s) declared, {stats['succession_events']} ruler succession(s), "
        f"{stats['alliances']} alliance(s), {stats['artworks'] + stats['artisan_crafts']} artwork(s)/craft(s), "
        f"{stats['discoveries']} scholarly discoveries"
    )

    figures = "\n".join(character_bios) if character_bios else "(no individually notable figures recorded)"
    timeline = "\n".join(f"  {line}" for line in timeline_lines) if timeline_lines else "  (no significant recorded events)"

    style_instructions = {
        "chronicle": (
            "Write a long-form historical chronicle (900-1500 words), in the voice of a court "
            "historian looking back over the civilization's full arc — its founding, growth, wars, "
            "culture, and (if it happened) its collapse. Weave the key figures below into the "
            "narrative as characters whose choices shaped events, not as a bolted-on roster. "
            "Organize it roughly chronologically, in eras if the span is long. Third person throughout."
        ),
        "saga": (
            "Write a mythologized saga (900-1500 words) as if this civilization's history has been "
            "retold and embellished over generations of oral tradition — elevated language, legendary "
            "framing of its rulers and founders, but still following the actual sequence of events "
            "below. Third person throughout."
        ),
    }.get(style, "Write a long-form historical chronicle (900-1500 words) in the voice of a court historian.")

    return textwrap.dedent(f"""\
        You are a worldbuilding narrative writer. Using ONLY the factual record below,
        write the history of this civilization. Do not add facts not implied by the record.

        CIVILIZATION
        ============
        Name:      {civ_name}  (ID {civ_id})
        Founded:   Year {founded_year}
        Status:    {status}
        Archetype: {archetype}
        Summary:   {stats_str}

        KEY FIGURES
        ===========
        {figures}

        CIVILIZATION TIMELINE
        ======================
        {timeline}

        TASK
        ====
        {style_instructions}
        Be specific — use the names, places, and events from the record above.
        The record avoids raw game statistics on purpose — do not invent or cite numeric
        stats (damage, health, population counts) as if they were meaningful figures; convey
        severity and scale qualitatively instead (a crushing blow, a narrow victory, a starving
        village), the way the record itself already does.
        Write only the story, no preamble.
    """)


# ── Ollama client (shared shape with generate-story.py) ────────────────────────

def detect_ollama_host() -> str:
    """
    Auto-detect where Ollama is reachable, so WSL2 users don't have to remember
    the Windows-host gateway IP every session. Order: explicit OLLAMA_HOST env
    var, then localhost, then — if running under WSL2 — the default-route
    gateway IP (that's what Windows-side Ollama binds to when OLLAMA_HOST=0.0.0.0
    is set on the Windows side; see docs/story-generation.md).
    """
    if os.environ.get("OLLAMA_HOST"):
        return os.environ["OLLAMA_HOST"]

    candidates = ["http://localhost:11434"]
    try:
        with open("/proc/version") as f:
            is_wsl = "microsoft" in f.read().lower()
    except OSError:
        is_wsl = False
    if is_wsl:
        try:
            result = subprocess.run(["ip", "route", "show", "default"],
                                    capture_output=True, text=True, timeout=2)
            m = re.search(r"default via (\S+)", result.stdout)
            if m:
                candidates.append(f"http://{m.group(1)}:11434")
        except (OSError, subprocess.SubprocessError):
            pass

    for host in candidates:
        try:
            urllib.request.urlopen(f"{host}/api/tags", timeout=1.5)
            return host
        except (urllib.error.URLError, OSError):
            continue

    return candidates[0]  # nothing answered; fall back to localhost and let call_ollama report the real error


# Civ chronicles target 900-1500 words (~1300-2000 tokens) and the prompt itself
# (80-event timeline + character bios) is long. Ollama's /api/generate defaults
# num_ctx to a small value (often 2048) unless told otherwise — prompt + output
# together blow past that, and the response gets silently cut off mid-sentence.
# These are generous enough for the largest civ prompts this script builds.
OLLAMA_NUM_CTX     = 8000
OLLAMA_NUM_PREDICT = 2400


def call_ollama(prompt: str, model: str, host: str) -> str:
    url = f"{host.rstrip('/')}/api/generate"
    body = json.dumps({
        "model": model, "prompt": prompt, "stream": False,
        "options": {"num_ctx": OLLAMA_NUM_CTX, "num_predict": OLLAMA_NUM_PREDICT},
    }).encode()
    req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=900) as resp:
            data = json.loads(resp.read())
            if data.get("done_reason") == "length":
                print(f"  [WARN] Response was cut off at the {OLLAMA_NUM_PREDICT}-token output limit "
                      f"— try --characters/fewer events, or edit OLLAMA_NUM_PREDICT in this script.",
                      file=sys.stderr)
            return data.get("response", "").strip()
    except urllib.error.URLError as e:
        print(f"[ERROR] Could not reach Ollama at {host}: {e}", file=sys.stderr)
        print("  Make sure Ollama is running: ollama serve", file=sys.stderr)
        sys.exit(1)


# ── Civ selection ────────────────────────────────────────────────────────────────

def pick_civs(conn: sqlite3.Connection, top: int) -> list[int]:
    """Most notable civs: longest-lived weighted by how event-rich they are."""
    civs = get_all_civs(conn)
    current_year = get_current_sim_year(conn)
    civs.sort(key=lambda c: (
        ((c.get("collapsed_year") or current_year) - (c.get("founded_year") or 0)) * 0.3
        + c.get("event_count", 0)
    ), reverse=True)
    return [c["CivId"] for c in civs[:top]]


# ── Main ───────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("db", help="Path to world.db")
    ap.add_argument("--model", default="gemma4:e4b")
    ap.add_argument("--host", default=None, help="Ollama base URL (default: auto-detected)")
    ap.add_argument("--top", type=int, default=1)
    ap.add_argument("--id", type=int, default=None)
    ap.add_argument("--characters", type=int, default=4)
    ap.add_argument("--prompt-only", action="store_true")
    ap.add_argument("--out", default=None)
    ap.add_argument("--style", default="chronicle", choices=["chronicle", "saga"])
    args = ap.parse_args()

    if not os.path.exists(args.db):
        print(f"[ERROR] Database not found: {args.db}", file=sys.stderr)
        sys.exit(1)

    if not args.prompt_only:
        args.host = args.host or detect_ollama_host()
        print(f"  Using Ollama at {args.host}", file=sys.stderr)

    conn = connect(args.db)
    civs = civ_name_map(conn)
    current_year = get_current_sim_year(conn)

    civ_ids = [args.id] if args.id else pick_civs(conn, args.top)
    if not civ_ids:
        print("[ERROR] No civilizations found in the database.", file=sys.stderr)
        sys.exit(1)

    out_dir = Path(args.out) if args.out else None
    if out_dir:
        out_dir.mkdir(parents=True, exist_ok=True)

    for cid in civ_ids:
        events = get_civ_events(conn, cid)
        if not events:
            print(f"[WARN] Civilization {cid} not found, skipping.", file=sys.stderr)
            continue

        civ_name = get_civ_name_from_events(events, cid)
        found_ev = next((e for e in events if e["Type"] == EV_CIV_FOUNDED), None)
        collapse_ev = next((e for e in events if e["Type"] == EV_CIV_COLLAPSED), None)
        founded_year = found_ev["Year"] if found_ev else events[0]["Year"]
        collapsed_year = collapse_ev["Year"] if collapse_ev else None

        timeline_lines, stats = build_civ_timeline(events, cid, civs)
        archetype = classify_civ_archetype(stats)
        key_chars = get_key_characters(conn, cid, events, args.characters)
        bios = build_character_bios(conn, key_chars)

        prompt = build_prompt(civ_name, cid, founded_year, collapsed_year, current_year,
                              stats, archetype, bios, timeline_lines, args.style)

        name_slug = re.sub(r'[^a-z0-9]+', '_', civ_name.lower()).strip("_")
        print(f"\n{'=' * 70}")
        print(f"  Civilization: {civ_name} (ID {cid})  |  {len(timeline_lines)} timeline events, {len(bios)} key figures")
        print(f"{'=' * 70}")

        if args.prompt_only:
            print(prompt)
            continue

        print(f"  Sending to Ollama ({args.model})...", flush=True)
        story = call_ollama(prompt, args.model, args.host)

        if out_dir:
            out_file = out_dir / f"{name_slug}_{cid}.txt"
            out_file.write_text(f"# {civ_name}\n\n{story}\n", encoding="utf-8")
            print(f"  Saved → {out_file}")
        else:
            print()
            print(story)

    conn.close()


if __name__ == "__main__":
    main()
