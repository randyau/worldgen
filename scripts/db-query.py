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
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.civId') civId,
               COUNT(*) births
        FROM Events WHERE Type=3001
        GROUP BY civId ORDER BY births DESC LIMIT 10
    """):
        print(f"  civ {row[0]}: {row[1]:,} chars born")


def q_settlements(c):
    print("=== SETTLEMENT SURVIVAL ===")
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3203 ORDER BY Year"):
        year, pj = row
        p = json.loads(pj)
        tile = p.get('tile', [0,0])
        name = p.get('settlementName', p.get('name', '?'))
        civ  = p.get('civId', '?')
        key  = f'%[{tile[0]}, {tile[1]}]%'

        d = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3204 AND Year>=? AND PayloadJson LIKE ?", (year, key)).fetchone()[0]
        a = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3403 AND Year>=? AND PayloadJson LIKE ?", (year, key)).fetchone()[0]
        q = c.execute("SELECT MIN(Year) FROM Events WHERE Type=3207 AND Year>=? AND PayloadJson LIKE ?", (year, key)).fetchone()[0]

        if d:   status = f"destroyed yr {d} (lived {d-year}y)"
        elif a: status = f"abandoned yr {a} (lived {a-year}y)"
        elif q: status = f"conquered yr {q} (lived {q-year}y free)"
        else:   status = "SURVIVED"
        print(f"  [{tile[0]:3d},{tile[1]:3d}] {name:16s} civ{civ} yr{year:4d}: {status}")


def q_expansion(c):
    print("=== CIV EXPANSION (settlements founded per civ) ===")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.civId') civId,
               COUNT(*) total, MIN(Year) first, MAX(Year) last
        FROM Events WHERE Type=3203
        GROUP BY civId ORDER BY total DESC
    """):
        print(f"  civ {row[0]:3}: {row[1]:3d} settlements  yr {row[2]}–{row[3]}")

    print("\n=== CONQUEST GROWTH (settlements annexed per civ) ===")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.conqueredByCivId') civId, COUNT(*) cnt
        FROM Events WHERE Type=3207
        GROUP BY civId ORDER BY cnt DESC
    """):
        print(f"  civ {row[0]}: {row[1]} settlements conquered")


def q_wars(c):
    print("=== WAR TIMELINE ===")
    wars = {}
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3103 ORDER BY Year"):
        p = json.loads(row[1])
        k = tuple(sorted([p.get('declarerId'), p.get('targetId')]))
        wars[k] = {'declared': row[0], 'a': p.get('declarerName','?'), 'b': p.get('targetName','?')}
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3104 ORDER BY Year"):
        p = json.loads(row[1])
        k = tuple(sorted([p.get('characterAId'), p.get('characterBId')]))
        if k in wars: wars[k]['ended'] = row[0]
    for k, w in sorted(wars.items(), key=lambda x: x[1]['declared']):
        ended = w.get('ended', '?')
        dur   = f"{ended-w['declared']}y" if isinstance(ended, int) else "ongoing"
        print(f"  yr {w['declared']:4d}: {w['a']} vs {w['b']} → ended yr {ended} ({dur})")

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
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3101 ORDER BY Year LIMIT 15"):
        p = json.loads(row[1])
        print(f"    yr {row[0]:4d}: {p.get('declarerName','?')} (civ{p.get('declarerCiv','?')}) ↔ {p.get('targetName','?')} (civ{p.get('targetCiv','?')})")


def q_characters(c):
    print("=== CHARACTERS BORN PER CIV ===")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.civId') civId, COUNT(*) cnt
        FROM Events WHERE Type=3001
        GROUP BY civId ORDER BY cnt DESC
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
    print("\n  Top trade destinations (by settlement name in payload):")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.destinationName') dest, COUNT(*) cnt
        FROM Events WHERE Type=3303
        GROUP BY dest ORDER BY cnt DESC LIMIT 10
    """):
        print(f"  {row[0]}: {row[1]:,} trades")


def q_ruins(c):
    print("=== RUIN EVENTS (most-destroyed tiles) ===")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.tile') tile,
               json_extract(PayloadJson,'$.settlementName') name,
               COUNT(*) destroyed
        FROM Events WHERE Type=3204
        GROUP BY tile ORDER BY destroyed DESC LIMIT 15
    """):
        print(f"  {row[0]:15s} {row[1]:16s}: destroyed {row[2]}x")
    print("\n  Abandoned settlements:")
    for row in c.execute("""
        SELECT json_extract(PayloadJson,'$.settlementName') name, Year
        FROM Events WHERE Type=3403 ORDER BY Year
    """):
        print(f"  yr {row[1]:4d}: {row[0]}")


def q_conquest(c):
    print("=== CONQUEST EVENTS ===")
    total = c.execute("SELECT COUNT(*) FROM Events WHERE Type=3207").fetchone()[0]
    print(f"  Total conquests: {total}")
    for row in c.execute("SELECT Year, PayloadJson FROM Events WHERE Type=3207 ORDER BY Year"):
        p = json.loads(row[1])
        print(f"  yr {row[0]:4d}: {p.get('conquererName','?')} (civ{p.get('conqueredByCivId','?')}) "
              f"seized {p.get('settlementName','?')} from civ{p.get('previousCivId','?')} "
              f"(pop → {p.get('survivingPop','?')})")

    print("\n=== CIV COLLAPSE VIA CONQUEST ===")
    for row in c.execute("""
        SELECT Year, PayloadJson FROM Events WHERE Type=3202 ORDER BY Year
    """):
        p = json.loads(row[1])
        print(f"  yr {row[0]:4d}: civ {p.get('civId','?')} collapsed ({p.get('reason','?')})")


def q_events(c, n=50):
    print(f"=== LAST {n} EVENTS (newest first) ===")
    for row in c.execute(f"SELECT Year, Season, Type, PayloadJson FROM Events ORDER BY Id DESC LIMIT {n}"):
        year, season, typ, pj = row
        # Brief summary from payload
        try:
            p = json.loads(pj)
            summary = (p.get('settlementName') or p.get('name') or
                       p.get('declarerName') or p.get('founderName') or
                       p.get('conquererName') or p.get('characterName') or '')
        except Exception:
            summary = ''
        print(f"  yr {year:4d} {season:6} type={typ:4d}  {summary}")


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
