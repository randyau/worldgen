#!/usr/bin/env python3
"""
generate-story.py — Extract character history from world.db and generate narrative via Ollama.

Connects to a local Ollama instance. When running inside WSL2 with Ollama on the
Windows host, use --host with the WSL2 gateway IP (see docs/story-generation.md).

Usage:
    python3 scripts/generate-story.py world.db [options]

Options:
    --model MODEL        Ollama model name (default: gemma4:e4b)
    --host  HOST         Ollama base URL (default: http://localhost:11434)
    --top   N            Pick the N most event-rich characters (default: 3)
    --id    CHAR_ID      Process a specific character ID
    --prompt-only        Print the prompt without calling Ollama
    --out   DIR          Write story files to DIR (default: stdout)
    --style STYLE        Story style: biography | legend | epic (default: biography)

Examples (WSL2 with Ollama on Windows):
    HOST=$(ip route show default | awk '{print $3}')
    python3 scripts/generate-story.py publish/win-x64/world.db --host http://$HOST:11434 --top 5
    python3 scripts/generate-story.py world.db --host http://$HOST:11434 --id 6641948
    python3 scripts/generate-story.py world.db --prompt-only --top 1
"""

import argparse
import json
import os
import re
import sqlite3
import sys
import textwrap
import urllib.request
import urllib.error
from collections import defaultdict
from pathlib import Path

# ── Event type constants ───────────────────────────────────────────────────────
EV_BORN            = 3001
EV_DIED            = 3002
EV_MARRIED         = 3003
EV_EXILED          = 3004
EV_GRIEVED         = 3005
EV_FLOURISHING     = 3006
EV_SPIRALING       = 3007
EV_ALLIANCE_F      = 3101
EV_ALLIANCE_B      = 3102
EV_WAR             = 3103
EV_WAR_END         = 3104
EV_BATTLE          = 3105
EV_RIVALRY         = 3106
EV_NEGOTIATED      = 3107
EV_ARTWORK         = 3108
EV_GOAL_FORMED     = 3109
EV_GOAL_RESOLVED   = 3110
EV_CIV_FOUNDED     = 3201
EV_SET_FOUNDED     = 3203
EV_SUCCESSION      = 3205
EV_SET_CONQUERED   = 3207
EV_APPOINTED       = 3301
EV_DISMISSED       = 3302
EV_BEAST_CHAR      = 2007

# Goals worth including if formed/resolved (not noisy tactical ones)
NOTABLE_GOALS = {"FoundCity", "SlayBeast", "Avenge"}

# Event types always included verbatim
CORE_EVENTS = {
    EV_BORN, EV_DIED, EV_MARRIED, EV_EXILED,
    EV_FLOURISHING, EV_SPIRALING, EV_GRIEVED,
    EV_ALLIANCE_F, EV_ALLIANCE_B,
    EV_WAR, EV_WAR_END, EV_BATTLE,
    EV_ARTWORK, EV_SUCCESSION,
    EV_SET_FOUNDED, EV_SET_CONQUERED,
    EV_APPOINTED, EV_DISMISSED,
    EV_BEAST_CHAR,
}

SEASON = {0: "Spring", 1: "Summer", 2: "Autumn", 3: "Winter"}

# Maximum events to include in the LLM prompt (keeps prompt size manageable)
MAX_PROMPT_EVENTS = 45


# ── DB helpers ─────────────────────────────────────────────────────────────────

def connect(path: str) -> sqlite3.Connection:
    conn = sqlite3.connect(path)
    conn.row_factory = sqlite3.Row
    return conn


def civ_name_map(conn: sqlite3.Connection) -> dict[int, str]:
    """Build {civ_id: name} by mining civ names embedded in event payloads."""
    civs: dict[int, str] = {}
    # CivSummaries (may be empty if session wasn't closed cleanly)
    for r in conn.execute("SELECT CivId, Name FROM CivSummaries"):
        civs[r["CivId"]] = r["Name"]
    if civs:
        return civs
    # Fallback: mine WarDeclared payloads which carry both civ names
    for r in conn.execute("SELECT PayloadJson FROM Events WHERE Type IN (?,?)",
                           (EV_WAR, EV_WAR_END)):
        try:
            p = json.loads(r["PayloadJson"])
            for id_key, name_key in [("DeclarerCivId", "DeclarerCivName"),
                                      ("TargetCivId",   "TargetCivName"),
                                      ("WinnerCivId",   "WinnerCivName"),
                                      ("LoserCivId",    "LoserCivName")]:
                if p.get(id_key) and p.get(name_key):
                    civs[p[id_key]] = p[name_key]
        except (json.JSONDecodeError, KeyError):
            pass
    # Also mine CivFounded and SettlementFounded events
    for r in conn.execute("SELECT PayloadJson FROM Events WHERE Type IN (3201,3203)"):
        try:
            p = json.loads(r["PayloadJson"])
            if p.get("CivId") and p.get("CivName"):
                civs[p["CivId"]] = p["CivName"]
        except (json.JSONDecodeError, KeyError):
            pass
    return civs


def pick_characters(conn: sqlite3.Connection, top: int) -> list[int]:
    """Return IDs of the top-N most event-rich characters (excluding goal noise)."""
    rows = conn.execute("""
        SELECT ActorId, COUNT(*) AS n
        FROM Events
        WHERE ActorId IS NOT NULL
          AND Type NOT IN (?,?)
          AND Type != 3106  -- Rivalry (too frequent)
        GROUP BY ActorId
        ORDER BY n DESC
        LIMIT ?
    """, (EV_GOAL_FORMED, EV_GOAL_RESOLVED, top)).fetchall()
    return [r["ActorId"] for r in rows]


def load_character(conn: sqlite3.Connection, char_id: int) -> dict | None:
    row = conn.execute(
        "SELECT * FROM CharacterSummaries WHERE CharacterId = ?", (char_id,)
    ).fetchone()
    if row is None:
        born = conn.execute(
            "SELECT ActorName, Year FROM Events WHERE ActorId=? AND Type=? LIMIT 1",
            (char_id, EV_BORN)
        ).fetchone()
        died = conn.execute(
            "SELECT Year, PayloadJson FROM Events WHERE ActorId=? AND Type=? LIMIT 1",
            (char_id, EV_DIED)
        ).fetchone()
        if born is None:
            # Try getting name from any event
            any_ev = conn.execute(
                "SELECT ActorName FROM Events WHERE ActorId=? AND ActorName IS NOT NULL LIMIT 1",
                (char_id,)
            ).fetchone()
            if any_ev is None:
                return None
            born_name = any_ev["ActorName"]
        else:
            born_name = born["ActorName"]

        # Get ancestry from born event payload, civ from war/succession
        civ_name_str = None
        ancestry_id  = None
        born_full = conn.execute(
            "SELECT PayloadJson FROM Events WHERE ActorId=? AND Type=? LIMIT 1",
            (char_id, EV_BORN)
        ).fetchone()
        if born_full:
            try:
                bp = json.loads(born_full["PayloadJson"])
                ancestry_id = bp.get("AncestryId")
            except (json.JSONDecodeError, KeyError):
                pass
        civ_ev = conn.execute(
            "SELECT PayloadJson FROM Events WHERE ActorId=? AND Type IN (?,?) LIMIT 1",
            (char_id, EV_WAR, EV_SUCCESSION)
        ).fetchone()
        if civ_ev:
            try:
                cp = json.loads(civ_ev["PayloadJson"])
                civ_name_str = cp.get("DeclarerCivName") or cp.get("CivName")
            except (json.JSONDecodeError, KeyError):
                pass

        age_s = None
        if died:
            try:
                dp = json.loads(died["PayloadJson"])
                age_s = dp.get("AgeSeasons")
            except (json.JSONDecodeError, KeyError):
                pass

        return {
            "CharacterId": char_id,
            "Name": born_name,
            "Epithet": None,
            "CivName": civ_name_str,
            "BirthYear": born["Year"] if born else None,
            "DeathYear": died["Year"] if died else None,
            "AgeSeasons": age_s,
            "AncestryId": ancestry_id,
            "SettlementsFounded": conn.execute(
                "SELECT COUNT(*) FROM Events WHERE ActorId=? AND Type=?",
                (char_id, EV_SET_FOUNDED)).fetchone()[0],
            "WarsInitiated": conn.execute(
                "SELECT COUNT(*) FROM Events WHERE ActorId=? AND Type=?",
                (char_id, EV_WAR)).fetchone()[0],
            "ArtworksCreated": conn.execute(
                "SELECT COUNT(*) FROM Events WHERE ActorId=? AND Type=?",
                (char_id, EV_ARTWORK)).fetchone()[0],
        }
    return dict(row)


def compress_events(events: list[dict]) -> list[dict]:
    """
    Collapse runs of repetitive battle events into a single summary event,
    and cap the total to MAX_PROMPT_EVENTS, preferring variety over repetition.
    """
    out   = []
    i     = 0
    while i < len(events):
        ev = events[i]
        # Compress consecutive battle events at the same location in the same year
        if ev["Type"] == EV_BATTLE:
            loc   = ev["SettlementName"] or ""
            year  = ev["Year"]
            run   = [ev]
            j     = i + 1
            while j < len(events) and events[j]["Type"] == EV_BATTLE \
                    and events[j]["SettlementName"] == loc and events[j]["Year"] == year:
                run.append(events[j])
                j += 1
            if len(run) == 1:
                out.append(ev)
            else:
                # Build a summary pseudo-event
                victories = sum(1 for r in run
                                if json.loads(r["PayloadJson"] or "{}").get("RaidOutcome","") == "campaign_victory")
                summary = dict(ev)
                summary["_summary"] = f"{len(run)} battles at {loc or 'enemy settlement'} (year {year}): {victories} won"
                out.append(summary)
            i = j
        else:
            out.append(ev)
            i += 1

    # If still too long, keep first 5 + last 5 + a spread of the middle
    if len(out) > MAX_PROMPT_EVENTS:
        head  = out[:5]
        tail  = out[-5:]
        mid   = out[5:-5]
        step  = max(1, len(mid) // (MAX_PROMPT_EVENTS - 10))
        mid   = mid[::step][: MAX_PROMPT_EVENTS - 10]
        out   = head + mid + tail

    return out


def load_events(conn: sqlite3.Connection, char_id: int) -> list[dict]:
    """Return filtered, narrative-relevant events for this character.

    Only includes events where this character is the ActorId — EventEntities
    references are excluded because they pull in post-death settlement events
    that are about the character's civ/city, not the character themselves.
    """
    rows = conn.execute("""
        SELECT e.Type, e.Year, e.Season, e.PayloadJson, e.TypeName,
               e.ActorName, e.CivId, e.SettlementName
        FROM Events e
        WHERE e.ActorId = ?
        ORDER BY e.Tick
    """, (char_id,)).fetchall()

    events = []
    for r in rows:
        t = r["Type"]

        # Always include core event types
        if t in CORE_EVENTS:
            events.append(dict(r))
            continue

        # Goal events: only keep notable goals
        if t in (EV_GOAL_FORMED, EV_GOAL_RESOLVED):
            try:
                p = json.loads(r["PayloadJson"])
                goal_type = p.get("GoalType", "")
                if goal_type in NOTABLE_GOALS:
                    events.append(dict(r))
            except (json.JSONDecodeError, KeyError):
                pass
            continue

        # Skip everything else (BuildImprovement spam, trader/scholar noise, etc.)

    return events


# ── Prompt builder ─────────────────────────────────────────────────────────────

def format_event(ev: dict, civs: dict[int, str] | None = None) -> str:
    if "_summary" in ev:
        return f"Year {ev['Year']}: {ev['_summary']}"

    t    = ev["Type"]
    year = ev["Year"]
    s    = SEASON.get(ev["Season"], "")
    name = ev["ActorName"] or "?"
    sett = ev["SettlementName"] or ""
    civs = civs or {}

    try:
        p = json.loads(ev["PayloadJson"]) if ev["PayloadJson"] else {}
    except (json.JSONDecodeError, TypeError):
        p = {}

    def civ_name(civ_id_key: str) -> str:
        cid = p.get(civ_id_key)
        return civs.get(cid, f"civ#{cid}") if cid else "unknown"

    if t == EV_BORN:
        civ = ev.get("CivName") or p.get("CivName") or civs.get(p.get("CivId"), "")
        return f"Year {year}, {s}: Born in {civ or 'the wilderness'}"
    if t == EV_DIED:
        cause = p.get("Cause", "unknown causes")
        age_s = p.get("AgeSeasons")
        age_str = f", age {int(age_s)//4}" if age_s else ""
        return f"Year {year}, {s}: Died of {cause}{age_str}"
    if t == EV_MARRIED:
        partner = p.get("PartnerName", "someone")
        return f"Year {year}, {s}: Married {partner}"
    if t == EV_EXILED:
        return f"Year {year}, {s}: Exiled from {sett or 'their home'}"
    if t == EV_GRIEVED:
        lost = p.get("SubjectName", "someone dear")
        return f"Year {year}, {s}: Grieved the death of {lost}"
    if t == EV_FLOURISHING:
        return f"Year {year}, {s}: Flourishing — wellbeing at its peak"
    if t == EV_SPIRALING:
        return f"Year {year}, {s}: Spiraling — in a dark period"
    if t == EV_APPOINTED:
        role = p.get("Role", "a role")
        return f"Year {year}, {s}: Appointed as {role} in {sett}"
    if t == EV_DISMISSED:
        role = p.get("Role", "a role")
        return f"Year {year}, {s}: Dismissed from {role}"
    if t == EV_ARTWORK:
        kind = p.get("ArtworkType", "artwork")
        return f"Year {year}, {s}: Created a {kind.lower()}"
    if t == EV_ALLIANCE_F:
        other = p.get("OtherCivName", p.get("PartnerName", "another civ"))
        return f"Year {year}, {s}: Forged an alliance with {other}"
    if t == EV_ALLIANCE_B:
        other = p.get("OtherCivName", p.get("PartnerName", "another civ"))
        return f"Year {year}, {s}: Completed mutual alliance with {other}"
    if t == EV_WAR:
        enemy = p.get("TargetCivName") or civ_name("TargetCivId")
        cause = p.get("CauseDescription", "")
        suffix = f" ({cause})" if cause else ""
        return f"Year {year}, {s}: Declared war on {enemy}{suffix}"
    if t == EV_WAR_END:
        outcome = p.get("Outcome", "ended")
        enemy   = p.get("EnemyCivName") or civ_name("EnemyCivId")
        return f"Year {year}, {s}: War with {enemy} ended — {outcome}"
    if t == EV_BATTLE:
        outcome = p.get("RaidOutcome", p.get("Outcome", "battle"))
        dmg     = p.get("Damage", p.get("DamageDealt", 0))
        target  = sett or "an enemy settlement"
        return f"Year {year}, {s}: Battle at {target} — {outcome}" + (f", dealt {dmg} dmg" if dmg else "")
    if t == EV_SET_FOUNDED:
        return f"Year {year}, {s}: Founded the settlement of {sett}"
    if t == EV_SET_CONQUERED:
        loser = p.get("LosingCivName") or civs.get(p.get("PreviousCivId"), f"civ#{p.get('PreviousCivId','?')}")
        return f"Year {year}, {s}: Conquered {sett} from {loser}"
    if t == EV_SUCCESSION:
        return f"Year {year}, {s}: Became ruler of {p.get('CivName', 'their civ')}"
    if t == EV_BEAST_CHAR:
        beast = p.get("BeastName", p.get("BeastSpecies", "a beast"))
        dmg   = p.get("DamageDealt", 0)
        return f"Year {year}, {s}: Attacked by {beast}, took {dmg} damage"
    if t == EV_GOAL_FORMED:
        goal = p.get("GoalType", "a goal")
        return f"Year {year}, {s}: Set out to {goal.replace('_', ' ').lower()}"
    if t == EV_GOAL_RESOLVED:
        goal   = p.get("GoalType", "a goal")
        result = p.get("Resolution", "resolved")
        return f"Year {year}, {s}: {goal.replace('_', ' ')} — {result}"

    return f"Year {year}, {s}: {ev['TypeName']}"


def build_prompt(char: dict, events: list[dict], style: str,
                 civs: dict[int, str] | None = None) -> str:
    name    = char["Name"] or "Unknown"
    epithet = char.get("Epithet")
    full    = f'{name}, "{epithet}"' if epithet else name
    civ      = char.get("CivName") or "an unknown civilization"
    ancestry = char.get("AncestryId") or char.get("AncestryId")
    race_str = ancestry.replace("_", " ").title() if ancestry else "Unknown"
    birth   = char.get("BirthYear", "?")
    death   = char.get("DeathYear")
    age_s   = char.get("AgeSeasons")
    age_str = f" (age {int(age_s)//4})" if age_s else ""

    life_span = f"Year {birth}" + (f" – Year {death}{age_str}" if death else " – present")
    factions  = f"Race:      {race_str}\nAffiliated with: {civ}"
    stats = []
    if char.get("SettlementsFounded", 0):
        stats.append(f"founded {char['SettlementsFounded']} settlement(s)")
    if char.get("WarsInitiated", 0):
        stats.append(f"initiated {char['WarsInitiated']} war(s)")
    if char.get("ArtworksCreated", 0):
        stats.append(f"created {char['ArtworksCreated']} artwork(s)")
    stats_str = "; ".join(stats) if stats else "no major recorded achievements"

    timeline = "\n".join(f"  {format_event(e, civs)}" for e in events)
    if not timeline:
        timeline = "  (No significant recorded events)"

    style_instructions = {
        "biography": (
            "Write a short biographical account (300-500 words) in the style of a medieval chronicle. "
            "Use third person. Focus on this person's notable deeds, relationships, and the arc of their life. "
            "Do not invent events not suggested by the timeline."
        ),
        "legend": (
            "Write a legendary retelling (300-500 words) as if this figure has become mythologized over centuries. "
            "Use poetic, elevated language. Third person. Hint at exaggerations the way oral traditions do."
        ),
        "epic": (
            "Write the opening passage (300-500 words) of an epic poem about this character, "
            "in a style reminiscent of Beowulf or the Iliad. Third person. "
            "Open with an invocation, then describe their deeds."
        ),
    }.get(style, "Write a short biographical account (300-500 words) in the style of a medieval chronicle.")

    return textwrap.dedent(f"""\
        You are a worldbuilding narrative writer. Using ONLY the factual timeline below,
        write a story about this historical figure. Do not add facts not implied by the timeline.

        CHARACTER
        =========
        Name:      {full}
        Lifespan:  {life_span}
        {factions}
        Summary:   {stats_str}

        LIFE TIMELINE
        =============
        {timeline}

        TASK
        ====
        {style_instructions}
        Be specific — use names, places, and events from the timeline.
        Write only the story, no preamble.
    """)


# ── Ollama client ──────────────────────────────────────────────────────────────

def call_ollama(prompt: str, model: str, host: str) -> str:
    url  = f"{host.rstrip('/')}/api/generate"
    body = json.dumps({"model": model, "prompt": prompt, "stream": False}).encode()
    req  = urllib.request.Request(url, data=body,
                                  headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=600) as resp:
            data = json.loads(resp.read())
            return data.get("response", "").strip()
    except urllib.error.URLError as e:
        print(f"[ERROR] Could not reach Ollama at {host}: {e}", file=sys.stderr)
        print("  Make sure Ollama is running: ollama serve", file=sys.stderr)
        sys.exit(1)


# ── Main ───────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("db", help="Path to world.db")
    ap.add_argument("--model",       default="gemma4:e4b")
    ap.add_argument("--host",        default="http://localhost:11434")
    ap.add_argument("--top",         type=int, default=3)
    ap.add_argument("--id",          type=int, default=None)
    ap.add_argument("--prompt-only", action="store_true")
    ap.add_argument("--out",         default=None)
    ap.add_argument("--style",       default="biography",
                    choices=["biography", "legend", "epic"])
    args = ap.parse_args()

    if not os.path.exists(args.db):
        print(f"[ERROR] Database not found: {args.db}", file=sys.stderr)
        sys.exit(1)

    conn = connect(args.db)
    civs = civ_name_map(conn)

    char_ids = [args.id] if args.id else pick_characters(conn, args.top)
    if not char_ids:
        print("[ERROR] No characters found in the database.", file=sys.stderr)
        sys.exit(1)

    out_dir = Path(args.out) if args.out else None
    if out_dir:
        out_dir.mkdir(parents=True, exist_ok=True)

    for cid in char_ids:
        char = load_character(conn, cid)
        if char is None:
            print(f"[WARN] Character {cid} not found, skipping.", file=sys.stderr)
            continue

        events = compress_events(load_events(conn, cid))
        prompt = build_prompt(char, events, args.style, civs)

        name_slug = re.sub(r'[^a-z0-9]+', '_', (char.get("Name") or str(cid)).lower()).strip("_")
        print(f"\n{'='*70}")
        print(f"  Character: {char.get('Name')} (ID {cid})  |  {len(events)} narrative events")
        print(f"{'='*70}")

        if args.prompt_only:
            print(prompt)
            continue

        print(f"  Sending to Ollama ({args.model})...", flush=True)
        story = call_ollama(prompt, args.model, args.host)

        if out_dir:
            out_file = out_dir / f"{name_slug}_{cid}.txt"
            out_file.write_text(f"# {char.get('Name')}\n\n{story}\n", encoding="utf-8")
            print(f"  Saved → {out_file}")
        else:
            print()
            print(story)

    conn.close()


if __name__ == "__main__":
    main()
