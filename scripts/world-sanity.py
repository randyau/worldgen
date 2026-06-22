#!/usr/bin/env python3
"""
world-sanity.py — Post-run sanity checks against world.db.

Usage:
    python3 scripts/world-sanity.py [path/to/world.db]

Defaults to publish/win-x64/world.db relative to the repo root.
Exits 0 if all checks pass, 1 if any check fails (suitable for CI).
"""

import sys
import sqlite3
import os
import shutil
import tempfile

# --------------------------------------------------------------------------- #
# Config — thresholds that reflect design intent                              #
# --------------------------------------------------------------------------- #

# World dimensions (200x160 at 10km/tile, 2000x1600 km)
WORLD_TILES = 200 * 160

# Sim parameters (must match config/sim_config.toml)
VOLCANIC_BASE_PROB  = 0.0002   # volcanic_eruption_probability_per_tick
EARTHQUAKE_BASE_PROB = 0.0005  # earthquake_probability_per_tick
VOLCANIC_MULTIPLIER_CAP = 10.0

# Sanity thresholds
MAX_ERUPTION_MULTIPLIER   = VOLCANIC_MULTIPLIER_CAP * 1.1   # allow small float slop
MAX_FAULT_TILE_FRACTION   = 0.20   # fault lines should not exceed 20% of world
MIN_YEARS_FOR_EVENTS      = 5      # by year 5 we expect at least some events
MIN_DISTINCT_EVENT_TILES  = 50     # world should have varied activity
MAX_UNRESOLVED_DROUGHT_FRACTION = 0.05  # at most 5% of droughts unresolved at end

# EventType int codes (must match Core/Enumerations.cs — never renumber)
T_VOLCANIC   = 1001
T_EARTHQUAKE = 1002
T_WILDFIRE   = 1003
T_FLOOD      = 1004
T_DROUGHT_B  = 1005
T_DROUGHT_E  = 1006

# --------------------------------------------------------------------------- #

PASS = "\033[32mPASS\033[0m"
FAIL = "\033[31mFAIL\033[0m"
WARN = "\033[33mWARN\033[0m"

failures = []

def check(name, condition, detail="", warn_only=False):
    tag = PASS if condition else (WARN if warn_only else FAIL)
    print(f"  [{tag}] {name}")
    if detail:
        print(f"         {detail}")
    if not condition and not warn_only:
        failures.append(name)


def main():
    # Locate DB
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    default_db = os.path.join(repo_root, "publish", "win-x64", "world.db")
    db_path = sys.argv[1] if len(sys.argv) > 1 else default_db

    if not os.path.exists(db_path):
        print(f"ERROR: world.db not found at {db_path}")
        print("Run publish-win.sh and launch the game first to generate world.db.")
        sys.exit(1)

    # Copy to temp to avoid lock contention if game is running
    tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
    tmp.close()
    shutil.copy2(db_path, tmp.name)
    conn = sqlite3.connect(tmp.name)

    print(f"\nworld-sanity.py — {db_path}\n")

    # ------------------------------------------------------------------ #
    # Basic DB structure                                                   #
    # ------------------------------------------------------------------ #
    print("=== DB Structure ===")
    tables = {r[0] for r in conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table'")}
    check("Events table exists", "Events" in tables)
    check("CausalEdges table exists", "CausalEdges" in tables)

    total_events = conn.execute("SELECT COUNT(*) FROM Events").fetchone()[0]
    check("Events table is non-empty", total_events > 0,
          f"{total_events} events total")

    # ------------------------------------------------------------------ #
    # Temporal coverage                                                    #
    # ------------------------------------------------------------------ #
    print("\n=== Temporal Coverage ===")
    max_year = conn.execute("SELECT MAX(Year) FROM Events").fetchone()[0] or 0
    ticks    = conn.execute("SELECT COUNT(DISTINCT Tick) FROM Events").fetchone()[0]
    check("Sim ran at least 5 years", max_year >= MIN_YEARS_FOR_EVENTS,
          f"max year: {max_year}")
    check("Multiple ticks recorded", ticks > 4,
          f"{ticks} distinct ticks")

    ticks_per_year = ticks / max_year if max_year > 0 else 0
    check("Ticks-per-year in expected range (4–20)",
          4 <= ticks_per_year <= 20,
          f"{ticks_per_year:.1f} ticks/year")

    # ------------------------------------------------------------------ #
    # Event type presence                                                  #
    # ------------------------------------------------------------------ #
    print("\n=== Event Type Presence ===")
    type_counts = {r[0]: r[1] for r in conn.execute(
        "SELECT Type, COUNT(*) FROM Events GROUP BY Type")}

    for tid, name in [(T_VOLCANIC,  "VolcanicEruption"),
                      (T_EARTHQUAKE,"EarthquakeOccurred"),
                      (T_WILDFIRE,  "WildfireOccurred"),
                      (T_DROUGHT_B, "DroughtBegan")]:
        count = type_counts.get(tid, 0)
        check(f"{name} events present", count > 0, f"{count} events")

    # Floods are optional (need the right terrain)
    flood_count = type_counts.get(T_FLOOD, 0)
    check("FloodOccurred events present", flood_count > 0,
          f"{flood_count} events", warn_only=True)

    # ------------------------------------------------------------------ #
    # Spatial coverage                                                     #
    # ------------------------------------------------------------------ #
    print("\n=== Spatial Coverage ===")
    distinct_tiles = conn.execute(
        'SELECT COUNT(DISTINCT LocationX || "," || LocationY) '
        'FROM Events WHERE LocationX IS NOT NULL').fetchone()[0]
    check(f"At least {MIN_DISTINCT_EVENT_TILES} distinct tiles had events",
          distinct_tiles >= MIN_DISTINCT_EVENT_TILES,
          f"{distinct_tiles} distinct tiles ({distinct_tiles/WORLD_TILES*100:.1f}% of world)")

    max_x = conn.execute(
        "SELECT MAX(LocationX) FROM Events WHERE LocationX IS NOT NULL").fetchone()[0] or 0
    max_y = conn.execute(
        "SELECT MAX(LocationY) FROM Events WHERE LocationY IS NOT NULL").fetchone()[0] or 0
    check("Events span full world width (max X ≥ 150)",  max_x >= 150, f"max X: {max_x}")
    check("Events span full world height (max Y ≥ 120)", max_y >= 120, f"max Y: {max_y}")

    # ------------------------------------------------------------------ #
    # Volcanic eruption rate (detects global-multiplier runaway)          #
    # ------------------------------------------------------------------ #
    print("\n=== Volcanic Eruption Rate ===")
    vol_count = type_counts.get(T_VOLCANIC, 0)
    if vol_count > 0 and ticks > 0:
        # Count confirmed volcanic tile locations
        vol_tiles = conn.execute(
            'SELECT COUNT(DISTINCT LocationX || "," || LocationY) '
            'FROM Events WHERE Type=1001').fetchone()[0]
        check("At least 1 volcanic tile exists", vol_tiles > 0, f"{vol_tiles} volcanic tiles")

        if vol_tiles > 0:
            expected_base = vol_tiles * VOLCANIC_BASE_PROB * ticks
            observed_multiplier = vol_count / expected_base if expected_base > 0 else 0
            check(
                f"Volcanic activity multiplier ≤ {MAX_ERUPTION_MULTIPLIER:.0f}x cap",
                observed_multiplier <= MAX_ERUPTION_MULTIPLIER,
                f"observed avg effective multiplier: {observed_multiplier:.1f}x "
                f"(base expected {expected_base:.0f}, actual {vol_count})"
            )

    # ------------------------------------------------------------------ #
    # Fault line tile fraction (detects over-generation)                  #
    # ------------------------------------------------------------------ #
    print("\n=== Fault Line Coverage ===")
    eq_count = type_counts.get(T_EARTHQUAKE, 0)
    if eq_count > 0 and ticks > 0:
        implied_fault_tiles = eq_count / (EARTHQUAKE_BASE_PROB * ticks)
        fault_fraction = implied_fault_tiles / WORLD_TILES
        check(
            f"Fault line tiles ≤ {MAX_FAULT_TILE_FRACTION*100:.0f}% of world",
            fault_fraction <= MAX_FAULT_TILE_FRACTION,
            f"implied ~{implied_fault_tiles:.0f} fault tiles ({fault_fraction*100:.1f}% of world)"
        )

    # ------------------------------------------------------------------ #
    # Drought begin/end balance                                           #
    # ------------------------------------------------------------------ #
    print("\n=== Drought Balance ===")
    began = type_counts.get(T_DROUGHT_B, 0)
    ended = type_counts.get(T_DROUGHT_E, 0)
    if began > 0:
        unresolved_fraction = (began - ended) / began
        check(
            f"Drought ends roughly match begins (≤{MAX_UNRESOLVED_DROUGHT_FRACTION*100:.0f}% unresolved)",
            unresolved_fraction <= MAX_UNRESOLVED_DROUGHT_FRACTION,
            f"began: {began}, ended: {ended}, unresolved: {began - ended} ({unresolved_fraction*100:.1f}%)"
        )

    # ------------------------------------------------------------------ #
    # Tier distribution (detects EventGate misconfiguration)             #
    # ------------------------------------------------------------------ #
    print("\n=== Event Tier Distribution ===")
    tiers = {r[0]: r[1] for r in conn.execute(
        "SELECT TierInvolvement, COUNT(*) FROM Events GROUP BY TierInvolvement")}
    # Expect tiers 0 (background), 1 (regional), 2 (headline) at minimum
    check("Tier 0 (background) events exist", tiers.get(0, 0) > 0,
          f"{tiers.get(0, 0)} background events")
    check("Tier 2+ (headline) events exist",
          sum(v for k, v in tiers.items() if k >= 2) > 0,
          f"{sum(v for k, v in tiers.items() if k >= 2)} headline+ events")
    for tier, count in sorted(tiers.items()):
        print(f"         Tier {tier}: {count} events")

    # ------------------------------------------------------------------ #
    # Summary                                                             #
    # ------------------------------------------------------------------ #
    print(f"\n{'='*50}")
    if failures:
        print(f"\033[31mFAILED\033[0m — {len(failures)} check(s) failed:")
        for f in failures:
            print(f"  • {f}")
        sys.exit(1)
    else:
        print(f"\033[32mALL CHECKS PASSED\033[0m — {total_events} events, {max_year} years simulated")

    os.unlink(tmp.name)


if __name__ == "__main__":
    main()
