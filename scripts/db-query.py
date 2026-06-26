#!/usr/bin/env python3
"""
Diagnostic queries for world.db / any run snapshot.

Usage:
  python3 scripts/db-query.py [db_path] [query_name]

  db_path    — path to the SQLite file (default: world.db)
  query_name — which query to run (default: overview)

Available queries:
  overview       — event volume by type, settlement count trajectory, civ stats
  settlements    — per-settlement founding + survival (destroyed/abandoned/alive)
  expansion      — which civs expanded, how many settlements, year range
  wars           — war timeline: declared, ended, duration; rivalry count
  alliances      — alliance formed/broken events and net counts
  characters     — chars born/died per civ; top causes of death
  economy        — merchant trade volume; top routes by value
  ruins          — ruin records: most-destroyed tiles, cause breakdown
  conquest       — SettlementConquered events; empire growth via annexation
  events [N]     — last N events (default 50), newest first
  event_types    — raw count of every EventType numeric value in the DB
"""

import sqlite3
import json
import sys
import os

DB_DEFAULT = "world.db"

def open_db(path):
    if not os.path.exists(path):
        print(f"DB not found: {path}")
        sys.exit(1)
    return sqlite3.connect(f"file:{path}?mode=ro&immutable=1", uri=True)

def q_overview(c):
    print("=== EVENT VOLUME BY TYPE ===")
    for row in c.execute("""
        SELECT Type, COUNT(*) cnt FROM Events GROUP BY Type ORDER BY cnt DESC LIMIT 20
    """):
        print(f"  type {row[0]:5d}: {row[1]:>8,}")

    print("\n=== SETTLEMENT COUNT TRAJECTORY ===")
    for band in [25, 50, 100, 150, 200, 250, 300, 325, 400, 500]:
        founded   = sum(1 for _ in c.execute("SELECT 1 FROM Events WHERE Type=3203 AND Year<=?", (band,)))
        destroyed = sum(1 for _ in c.execute("SELECT 1 FROM Events WHERE Type IN (3204,3403) AND Year<=?", (band,)))
        conquered = sum(1 for _ in c.execute("SELECT 1 FROM Events WHERE Type=3207 AND Year<=?", (band,)))
        if founded == 0: break
        print(f"  Year {band:3d}: {founded:3d} founded, {destroyed:3d} gone, {conquered:3d} conquered → {founded-destroyed} active")

    print("\n=== CIV STATS ===")
    # CivId denormalized column replaces json_extract(PayloadJson,'$.civId')
    for row in c.execute("""
        SELECT CivId, COUNT(*) births
        FROM Events WHERE Type=3001
        GROUP BY CivId ORDER BY births DESC LIMIT 10
    """):
        print(f"  civ {row[0]}: {row[1]:,} chars born")


def q_settlements(c):
    print("=== SETTLEMENT SURVIVAL ===")
    # SettlementFoundedPayload: (FounderId, FounderName, CivId, CivName, StartingPopulation)
    # SettlementName and CivId come from denormalized columns; tile is no longer in the payload.
    # Cross-reference destroyed/abandoned/conquered by SettlementName column.
    for row in c.execute("""
        SELECT Year, SettlementName, CivId
        FROM Events WHERE Type=3203 ORDER BY Year
    """):
        year, name, civ = row
        name = name or '?'

        d = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3204 AND Year>=? AND SettlementName=?", (year, name)).fetchone()[0]
        a = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3403 AND Year>=? AND SettlementName=?", (year, name)).fetchone()[0]
        q = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3207 AND Year>=? AND SettlementName=?", (year, name)).fetchone()[0]

        if d:   status = f"destroyed yr {d} (lived {d-year}y)"
        elif a: status = f"abandoned yr {a} (lived {a-year}y)"
        elif q: status = f"conquered yr {q} (lived {q-year}y free)"
        else:   status = "SURVIVED"
        print(f"  {name:20s} civ{civ} yr{year:4d}: {status}")


def q_expansion(c):
    print("=== CIV EXPANSION (settlements founded per civ) ===")
    # CivId denormalized column replaces json_extract(PayloadJson,'$.civId')
    for row in c.execute("""
        SELECT CivId, COUNT(*) total, MIN(Year) first, MAX(Year) last
        FROM Events WHERE Type=3203
        GROUP BY CivId ORDER BY total DESC
    """):
        print(f"  civ {row[0]:3}: {row[1]:3d} settlements  yr {row[2]}–{row[3]}")

    print("\n=== CONQUEST GROWTH (settlements annexed per civ) ===")
    # SettlementConqueredPayload: (ConquererId, ConquererName, ConquerorCivId, PreviousCivId, SurvivingPop)
    # CivId column holds ConquerorCivId for conquest events.
    for row in c.execute("""
        SELECT CivId, COUNT(*) cnt
        FROM Events WHERE Type=3207
        GROUP BY CivId ORDER BY cnt DESC
    """):
        print(f"  civ {row[0]}: {row[1]} settlements conquered")


def q_wars(c):
    print("=== WAR TIMELINE ===")
    # WarDeclaredPayload: (DeclarerId, DeclarerName, DeclarerCivId, DeclarerCivName,
    #                      TargetCivId, TargetCivName, Cause, CauseDescription, WarNumber)
    # WarEndedPayload:    (CivAId, CivAName, CivBId, CivBName, Outcome, WarNumber)
    # Key wars by WarNumber since both declared and ended payloads carry it.
    wars = {}
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3103 ORDER BY Year"):
        p = json.loads(row[1])
        war_num = p.get('WarNumber')
        wars[war_num] = {
            'declared': row[0],
            # DeclarerCivName / TargetCivName are the fighting civilizations
            'a': p.get('DeclarerCivName', '?'),
            'b': p.get('TargetCivName', '?'),
            'cause': p.get('Cause', '?'),
        }
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3104 ORDER BY Year"):
        p = json.loads(row[1])
        war_num = p.get('WarNumber')
        if war_num in wars:
            wars[war_num]['ended'] = row[0]
            wars[war_num]['outcome'] = p.get('Outcome', '?')
    for war_num, w in sorted(wars.items(), key=lambda x: x[1]['declared']):
        ended = w.get('ended', '?')
        dur   = f"{ended-w['declared']}y" if isinstance(ended, int) else "ongoing"
        outcome = w.get('outcome', '')
        outcome_str = f" [{outcome}]" if outcome else ''
        print(f"  yr {w['declared']:4d}: {w['a']} vs {w['b']} ({w['cause']}) → ended yr {ended} ({dur}){outcome_str}")

    total = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3106").fetchone()[0]
    print(f"\n  Total rivalries formed: {total:,}")
    total_wars = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3103").fetchone()[0]
    total_ended = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3104").fetchone()[0]
    print(f"  Wars declared: {total_wars}  |  Wars ended: {total_ended}  |  Ongoing: {total_wars-total_ended}")


def q_alliances(c):
    print("=== ALLIANCE EVENTS ===")
    formed = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3101").fetchone()[0]
    broken = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3102").fetchone()[0]
    print(f"  Formed: {formed:,}  |  Broken: {broken:,}  |  Net active (estimate): {formed-broken}")
    print("\n  Sample alliances formed:")
    # AllianceFormedPayload: (DeclarerId, DeclarerName, TargetId, TargetName, DeclarerCivId, TargetCivId)
    # ActorName column holds DeclarerName; TargetName and civ IDs still parsed from JSON (PascalCase).
    for row in c.execute("""
        SELECT Year, ActorName, PayloadJson FROM Events WHERE Type=3101 ORDER BY Year LIMIT 15
    """):
        year, actor_name, pj = row
        p = json.loads(pj)
        print(f"    yr {year:4d}: {actor_name or p.get('DeclarerName','?')} "
              f"(civ{p.get('DeclarerCivId','?')}) ↔ "
              f"{p.get('TargetName','?')} (civ{p.get('TargetCivId','?')})")

    print("\n  Sample alliances broken:")
    # AllianceBrokenPayload: (CharacterAId, CharacterAName, CharacterBId, CharacterBName, Reason)
    for row in c.execute("""
        SELECT Year, ActorName, PayloadJson FROM Events WHERE Type=3102 ORDER BY Year LIMIT 10
    """):
        year, actor_name, pj = row
        p = json.loads(pj)
        print(f"    yr {year:4d}: {actor_name or p.get('CharacterAName','?')} ↔ "
              f"{p.get('CharacterBName','?')} — {p.get('Reason','?')}")


def q_characters(c):
    print("=== CHARACTERS BORN PER CIV ===")
    # CivId denormalized column replaces json_extract(PayloadJson,'$.civId')
    # CharacterBornPayload carries (CharacterId, CharacterName, Epithet, Ambition, Aggression, Role, Source);
    # the civ association is stored only in the denormalized CivId column.
    for row in c.execute("""
        SELECT CivId, COUNT(*) cnt
        FROM Events WHERE Type=3001
        GROUP BY CivId ORDER BY cnt DESC
    """):
        print(f"  civ {row[0]}: {row[1]:,} born")

    print("\n=== CHARACTERS DIED (top years) ===")
    for row in c.execute("""
        SELECT Year, COUNT(*) cnt FROM Events WHERE Type=3002
        GROUP BY Year ORDER BY cnt DESC LIMIT 10
    """):
        print(f"  yr {row[0]:4d}: {row[1]} deaths")

    total_born = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3001").fetchone()[0]
    total_died = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3002").fetchone()[0]
    print(f"\n  Total born: {total_born:,}  |  Died: {total_died:,}  |  Alive estimate: {total_born-total_died:,}")


def q_economy(c):
    print("=== MERCHANT TRADE VOLUME ===")
    total = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3303").fetchone()[0]
    print(f"  Total trade events: {total:,}")
    # MerchantTradePayload: (CharacterId, CharacterName, TradedResource, DestX, DestY)
    # SettlementName column holds the destination settlement name for trade events.
    print("\n  Top trade destinations (by SettlementName column):")
    for row in c.execute("""
        SELECT SettlementName, COUNT(*) cnt
        FROM Events WHERE Type=3303
        GROUP BY SettlementName ORDER BY cnt DESC LIMIT 10
    """):
        print(f"  {row[0] or '(unknown)'}: {row[1]:,} trades")

    print("\n  Top traded resources:")
    # TradedResource is still in the JSON payload (PascalCase)
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.TradedResource') resource, COUNT(*) cnt
        FROM Events WHERE Type=3303
        GROUP BY resource ORDER BY cnt DESC LIMIT 10
    """):
        print(f"  {row[0] or '?'}: {row[1]:,} trades")


def q_ruins(c):
    print("=== RUIN EVENTS (most-destroyed settlements) ===")
    # SettlementDestroyedPayload: (FounderId, DestroyerId, DestroyerName, TimesSettled)
    # No tile or name in payload; use SettlementName denormalized column.
    for row in c.execute("""
        SELECT SettlementName, COUNT(*) destroyed
        FROM Events WHERE Type=3204
        GROUP BY SettlementName ORDER BY destroyed DESC LIMIT 15
    """):
        print(f"  {(row[0] or '?'):20s}: destroyed {row[1]}x")

    print("\n  Abandoned settlements:")
    # SettlementAbandonedPayload: (FounderId, FoundedYear, TimesSettled, Population)
    # SettlementName column gives the settlement name directly.
    for row in c.execute("""
        SELECT SettlementName, Year FROM Events WHERE Type=3403 ORDER BY Year
    """):
        print(f"  yr {row[1]:4d}: {row[0] or '?'}")


def q_conquest(c):
    print("=== CONQUEST EVENTS ===")
    total = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3207").fetchone()[0]
    print(f"  Total conquests: {total}")
    # SettlementConqueredPayload: (ConquererId, ConquererName, ConquerorCivId, PreviousCivId, SurvivingPop)
    # SettlementName column — name of the conquered settlement.
    # ActorName column    — ConquererName (the character who led the conquest).
    # CivId column        — ConquerorCivId.
    for row in c.execute("""
        SELECT Year, ActorName, CivId, SettlementName, PayloadJson
        FROM Events WHERE Type=3207 ORDER BY Year
    """):
        year, actor_name, civ_id, settlement_name, pj = row
        p = json.loads(pj)
        print(f"  yr {year:4d}: {actor_name or p.get('ConquererName','?')} (civ{civ_id}) "
              f"seized {settlement_name or '?'} from civ{p.get('PreviousCivId','?')} "
              f"(pop → {p.get('SurvivingPop','?')})")

    print("\n=== CIV COLLAPSE VIA CONQUEST ===")
    # CivCollapsedPayload: (CivId, Reason) — PascalCase JSON fields.
    for row in c.execute("""
        SELECT Year, PayloadJson FROM Events WHERE Type=3202 ORDER BY Year
    """):
        p = json.loads(row[1])
        print(f"  yr {row[0]:4d}: civ {p.get('CivId','?')} collapsed ({p.get('Reason','?')})")


def q_events(c, n=50):
    print(f"=== LAST {n} EVENTS (newest first) ===")
    # TypeName and ActorName are denormalized columns; SettlementName for settlement context.
    for row in c.execute(f"""
        SELECT Year, Season, Type, TypeName, ActorName, SettlementName
        FROM Events ORDER BY Id DESC LIMIT {n}
    """):
        year, season, typ, type_name, actor_name, settlement_name = row
        label = type_name or f"type={typ}"
        summary = actor_name or ''
        if settlement_name:
            summary = f"{summary} @ {settlement_name}".lstrip(' @ ')
        print(f"  yr {year:4d} {(season or ''):6}  {label:35s}  {summary}")


def q_event_types(c):
    print("=== ALL EVENT TYPE COUNTS ===")
    for row in c.execute("SELECT Type, COUNT(*) cnt FROM Events GROUP BY Type ORDER BY Type"):
        print(f"  type {row[0]:5d}: {row[1]:>8,}")


QUERIES = {
    'overview':    lambda c, _: q_overview(c),
    'settlements': lambda c, _: q_settlements(c),
    'expansion':   lambda c, _: q_expansion(c),
    'wars':        lambda c, _: q_wars(c),
    'alliances':   lambda c, _: q_alliances(c),
    'characters':  lambda c, _: q_characters(c),
    'economy':     lambda c, _: q_economy(c),
    'ruins':       lambda c, _: q_ruins(c),
    'conquest':    lambda c, _: q_conquest(c),
    'events':      lambda c, args: q_events(c, int(args[0]) if args else 50),
    'event_types': lambda c, _: q_event_types(c),
}

if __name__ == '__main__':
    args = sys.argv[1:]
    db_path = DB_DEFAULT

    # First arg may be a db path (ends in .db) or a query name
    if args and (args[0].endswith('.db') or os.path.sep in args[0]):
        db_path = args.pop(0)

    query = args.pop(0) if args else 'overview'
    extra = args  # remaining args passed to query fn

    if query not in QUERIES:
        print(f"Unknown query '{query}'. Available: {', '.join(QUERIES)}")
        sys.exit(1)

    conn = open_db(db_path)
    QUERIES[query](conn.cursor(), extra)
    conn.close()
