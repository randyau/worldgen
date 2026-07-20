#!/usr/bin/env python3
"""
balance-run.py — Multi-seed headless balance sweep for WorldEngine.Sim.

Usage:
    # Run 5 seeds for 500 years using the fast_history profile:
    python3 scripts/balance-run.py --seeds 5 --years 500 --profile fast_history --label baseline

    # Specific seed list:
    python3 scripts/balance-run.py --seed-list 1,2,3 --years 200 --out sweeps/run1 --label test

    # Override a config key:
    python3 scripts/balance-run.py --seeds 3 --years 100 --set settlement.disease_base_chance=0.002

    # Parallel jobs (default: min(seeds, cpu_count//2)):
    python3 scripts/balance-run.py --seeds 8 --years 500 --jobs 4

    # Compare two previous sweep output dirs:
    python3 scripts/balance-run.py --compare sweeps/baseline sweeps/test

Output:
    <out>/<label>/seed<N>/world.db  — per-seed DB (yearly_metrics table inside)
    <out>/<label>_metrics.csv       — merged CSV of all yearly_metrics rows

Dependencies: Python 3 stdlib only (sqlite3, subprocess, csv, etc.)

Notes:
    - Invokes the sim as: dotnet run --project WorldEngine.Sim -c Release --
      (or the pre-built binary if --bin is given).
    - Each seed runs in its own subprocess to avoid EntityId counter cross-contamination.
    - SimLoop.RunSynchronous is used (no threading, no speed throttle).
"""

import argparse
import csv
import json
import os
import sqlite3
import subprocess
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

# --------------------------------------------------------------------------- #
# Constants                                                                    #
# --------------------------------------------------------------------------- #

# Columns to show in the cross-seed summary table
SUMMARY_COLS = [
    "world_population", "active_civs", "collapsed_civs",
    "settlements_total", "max_cities_per_civ_actual", "mean_cities_per_civ",
    "secessions_ytd", "mean_unrest", "civ_border_pairs",
    "settlements_in_shortage", "settlements_in_crisis",
    "active_diseases", "wars_active", "mean_food_ratio", "min_food_ratio",
    "mean_wellbeing", "tier1_count", "tier2_count",
    # Artifact telemetry (M5 W4) — stock metrics + YTD event counts
    "living_artifacts", "lost_artifacts", "artifacts_per_settlement",
    "artifacts_created_ytd", "artifacts_destroyed_ytd", "artifacts_transferred_ytd",
]

# Checkpoint years to include in the cross-seed summary
CHECKPOINT_YEARS = [50, 100, 200, 500, 1000]

# --------------------------------------------------------------------------- #
# Helpers                                                                      #
# --------------------------------------------------------------------------- #

def find_repo_root():
    """Walk up from this script's directory until we find WorldEngine.Sim/."""
    d = Path(__file__).resolve().parent
    for _ in range(6):
        if (d / "WorldEngine.Sim").is_dir():
            return d
        d = d.parent
    return Path(__file__).resolve().parent.parent  # best-effort fallback


def find_sim_binary(repo_root: Path, use_binary: str | None) -> list[str]:
    """Return the command prefix to invoke the sim."""
    if use_binary:
        return [use_binary]
    # Prefer pre-built Release binary if it exists (much faster startup)
    release_bin = repo_root / "WorldEngine.Sim" / "bin" / "Release" / "net8.0" / "WorldEngine.Sim"
    if release_bin.is_file():
        return [str(release_bin)]
    release_bin_win = release_bin.with_suffix(".exe")
    if release_bin_win.is_file():
        return [str(release_bin_win)]
    # Fall back to dotnet run (slower but always works)
    return ["dotnet", "run", "--project",
            str(repo_root / "WorldEngine.Sim"), "-c", "Release", "--"]


def run_one_seed(args_tuple):
    """
    Worker function for parallel execution. Runs the sim for one seed.
    Returns (seed, elapsed_sec, out_dir) on success, or (seed, -1, error_msg) on failure.
    """
    seed, years, sim_cmd, profile, set_overrides, out_dir, label = args_tuple
    seed_dir = Path(out_dir) / label / f"seed{seed}"
    seed_dir.mkdir(parents=True, exist_ok=True)

    cmd = sim_cmd + [
        "--seed", str(seed),
        "--years", str(years),
        "--out", str(seed_dir),
    ]
    if profile:
        cmd += ["--profile", profile]
    for kv in set_overrides:
        cmd += ["--set", kv]

    t0 = time.monotonic()
    try:
        result = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=3600,   # 1h hard cap
        )
        elapsed = time.monotonic() - t0
        if result.returncode != 0:
            return (seed, -1, f"exit code {result.returncode}\n{result.stdout[-2000:]}")
        return (seed, elapsed, str(seed_dir))
    except subprocess.TimeoutExpired:
        return (seed, -1, "timeout after 3600s")
    except Exception as ex:
        return (seed, -1, str(ex))


# --------------------------------------------------------------------------- #
# DB reading                                                                   #
# --------------------------------------------------------------------------- #

def read_metrics(db_path: Path) -> list[dict]:
    """Read all yearly_metrics rows from a world.db."""
    if not db_path.exists():
        return []
    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    try:
        rows = conn.execute("SELECT * FROM yearly_metrics ORDER BY year").fetchall()
        return [dict(r) for r in rows]
    except sqlite3.OperationalError:
        return []
    finally:
        conn.close()


def row_at_year(metrics: list[dict], target_year: int) -> dict | None:
    """Return the metrics row closest to target_year, or None."""
    if not metrics:
        return None
    best = min(metrics, key=lambda r: abs(r["year"] - target_year))
    return best


# --------------------------------------------------------------------------- #
# Summary printing                                                             #
# --------------------------------------------------------------------------- #

def fmt(v) -> str:
    if v is None:
        return "—"
    if isinstance(v, float):
        return f"{v:.3f}"
    return str(v)


def print_summary_table(seed_metrics: dict[int, list[dict]], years: int):
    """Print a cross-seed mean/min/max table for the final year and checkpoints."""
    checkpoints = [y for y in CHECKPOINT_YEARS if y <= years]
    if years not in checkpoints:
        checkpoints.append(years)
    checkpoints = sorted(set(checkpoints))

    print("\n── Cross-seed summary ──────────────────────────────────────────────────────")
    for yr in checkpoints:
        rows_at_yr = [row_at_year(m, yr) for m in seed_metrics.values() if row_at_year(m, yr)]
        if not rows_at_yr:
            continue
        label = f"Year {yr}"
        print(f"\n  {label}  (n={len(rows_at_yr)})")
        print(f"  {'Metric':<32} {'mean':>10} {'min':>10} {'max':>10}")
        print(f"  {'-'*32} {'-'*10} {'-'*10} {'-'*10}")
        for col in SUMMARY_COLS:
            vals = [r[col] for r in rows_at_yr if col in r and r[col] is not None]
            if not vals:
                continue
            mean_v = sum(vals) / len(vals)
            min_v  = min(vals)
            max_v  = max(vals)
            print(f"  {col:<32} {fmt(mean_v):>10} {fmt(min_v):>10} {fmt(max_v):>10}")


def print_compare(dir_a: Path, dir_b: Path):
    """Compare two sweep output dirs. Reads the <label>_metrics.csv files."""
    csv_a = _find_csv(dir_a)
    csv_b = _find_csv(dir_b)
    if not csv_a:
        print(f"ERROR: no *_metrics.csv found in {dir_a}", file=sys.stderr)
        sys.exit(1)
    if not csv_b:
        print(f"ERROR: no *_metrics.csv found in {dir_b}", file=sys.stderr)
        sys.exit(1)

    def load_csv(path: Path) -> dict[tuple, dict]:
        out = {}
        with open(path, newline="") as f:
            for row in csv.DictReader(f):
                seed = int(row.get("seed", 0))
                year = int(row.get("year", 0))
                out[(seed, year)] = row
        return out

    data_a = load_csv(csv_a)
    data_b = load_csv(csv_b)

    # Collect final year for each seed in A
    seeds_a = {k[0] for k in data_a}
    print(f"\n── Compare: {dir_a.name}  vs  {dir_b.name} ─────────────────────────────────")
    for seed in sorted(seeds_a):
        years_a = sorted(y for (s, y) in data_a if s == seed)
        if not years_a:
            continue
        final_yr = years_a[-1]
        row_a = data_a.get((seed, final_yr))
        row_b = data_b.get((seed, final_yr))
        if not row_a or not row_b:
            print(f"  seed {seed}: data missing in one dir at year {final_yr}")
            continue
        print(f"\n  Seed {seed} @ year {final_yr}:")
        print(f"  {'Metric':<32} {'A':>12} {'B':>12} {'delta':>12}")
        print(f"  {'-'*32} {'-'*12} {'-'*12} {'-'*12}")
        for col in SUMMARY_COLS:
            va = row_a.get(col)
            vb = row_b.get(col)
            try:
                fa, fb = float(va), float(vb)
                delta = fb - fa
                print(f"  {col:<32} {fmt(fa):>12} {fmt(fb):>12} {fmt(delta):>12}")
            except (TypeError, ValueError):
                pass


def _find_csv(d: Path) -> Path | None:
    for f in d.iterdir():
        if f.suffix == ".csv":
            return f
    return None


# --------------------------------------------------------------------------- #
# CSV export                                                                   #
# --------------------------------------------------------------------------- #

def export_csv(seed_metrics: dict[int, list[dict]], out_path: Path):
    """Write merged CSV with a 'seed' column prepended."""
    all_rows = []
    for seed, rows in sorted(seed_metrics.items()):
        for r in rows:
            all_rows.append({"seed": seed, **r})
    if not all_rows:
        return
    fieldnames = list(all_rows[0].keys())
    with open(out_path, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        w.writerows(all_rows)
    print(f"\n  CSV: {out_path}")


# --------------------------------------------------------------------------- #
# Main                                                                         #
# --------------------------------------------------------------------------- #

def main():
    parser = argparse.ArgumentParser(
        description="Multi-seed balance sweep for WorldEngine.Sim",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--seeds",     type=int,  default=5,
                        help="Number of seeds (1..N). Ignored if --seed-list is given.")
    parser.add_argument("--seed-list", type=str,  default=None,
                        help="Comma-separated explicit seed list, e.g. 1,2,3.")
    parser.add_argument("--years",     type=int,  default=500,
                        help="In-game years to simulate per seed.")
    parser.add_argument("--profile",   type=str,  default=None,
                        help="Config profile name (config/profiles/<name>.toml).")
    parser.add_argument("--set",       action="append", default=[], metavar="KEY=VALUE",
                        help="Dotted-path config override. Repeatable.")
    parser.add_argument("--out",       type=str,  default="sweeps",
                        help="Parent output directory.")
    parser.add_argument("--label",     type=str,  default="sweep",
                        help="Label for this sweep (becomes subdir name).")
    parser.add_argument("--jobs",      type=int,  default=None,
                        help="Parallel subprocess count (default: min(seeds, cpu//2)).")
    parser.add_argument("--bin",       type=str,  default=None,
                        help="Path to pre-built WorldEngine.Sim binary.")
    parser.add_argument("--compare",   nargs=2,   metavar=("DIR_A", "DIR_B"),
                        help="Compare two sweep output dirs and exit.")

    args = parser.parse_args()

    # ── Compare mode ──────────────────────────────────────────────────────────
    if args.compare:
        print_compare(Path(args.compare[0]), Path(args.compare[1]))
        return

    # ── Seed list ─────────────────────────────────────────────────────────────
    if args.seed_list:
        seeds = [int(s) for s in args.seed_list.split(",")]
    else:
        seeds = list(range(1, args.seeds + 1))

    n = len(seeds)
    cpu = os.cpu_count() or 2
    jobs = args.jobs if args.jobs else min(n, max(1, cpu // 2))

    repo_root = find_repo_root()
    sim_cmd   = find_sim_binary(repo_root, args.bin)
    out_dir   = args.out

    print(f"balance-run.py — {n} seeds × {args.years} years  ({jobs} parallel jobs)")
    print(f"  Sim:     {' '.join(sim_cmd[:3])}{'...' if len(sim_cmd) > 3 else ''}")
    print(f"  Profile: {args.profile or '(base)'}")
    print(f"  Overrides: {args.set or '(none)'}")
    print(f"  Out:     {out_dir}/{args.label}/")

    # ── Launch runs ───────────────────────────────────────────────────────────
    worker_args = [
        (seed, args.years, sim_cmd, args.profile, args.set, out_dir, args.label)
        for seed in seeds
    ]

    results = {}  # seed -> (elapsed, seed_dir or error)
    t_total_start = time.monotonic()

    if jobs == 1:
        # Sequential fallback (avoids ProcessPoolExecutor overhead for 1 job)
        for wa in worker_args:
            seed, elapsed, info = run_one_seed(wa)
            results[seed] = (elapsed, info)
            status = f"{elapsed:.1f}s" if elapsed >= 0 else f"FAIL: {info}"
            print(f"  seed {seed}: {status}")
    else:
        with ProcessPoolExecutor(max_workers=jobs) as pool:
            futures = {pool.submit(run_one_seed, wa): wa[0] for wa in worker_args}
            for fut in as_completed(futures):
                seed, elapsed, info = fut.result()
                results[seed] = (elapsed, info)
                status = f"{elapsed:.1f}s" if elapsed >= 0 else f"FAIL: {info}"
                print(f"  seed {seed}: {status}")

    total_elapsed = time.monotonic() - t_total_start
    failures = {s: info for s, (e, info) in results.items() if e < 0}
    successes = {s: (e, info) for s, (e, info) in results.items() if e >= 0}

    print(f"\n  Completed {len(successes)}/{n} seeds in {total_elapsed:.1f}s  "
          f"({len(failures)} failed)")
    if failures:
        for s, msg in failures.items():
            print(f"    seed {s} FAILED: {msg[:200]}")

    if not successes:
        print("No successful runs — nothing to report.", file=sys.stderr)
        sys.exit(1)

    # ── Read metrics from each world.db ───────────────────────────────────────
    seed_metrics: dict[int, list[dict]] = {}
    for seed, (_, seed_dir) in successes.items():
        db_path = Path(seed_dir) / "world.db"
        rows = read_metrics(db_path)
        if rows:
            seed_metrics[seed] = rows
        else:
            print(f"  WARNING: no yearly_metrics data in seed {seed} — "
                  "metrics_enabled may be false or sim crashed early")

    # ── Cross-seed summary ────────────────────────────────────────────────────
    if seed_metrics:
        print_summary_table(seed_metrics, args.years)

    # ── CSV export ────────────────────────────────────────────────────────────
    out_base = Path(out_dir)
    csv_path = out_base / f"{args.label}_metrics.csv"
    if seed_metrics:
        export_csv(seed_metrics, csv_path)

    # ── Wall-time calibration ─────────────────────────────────────────────────
    if successes:
        elapsed_list = [e for e, _ in successes.values()]
        mean_elapsed = sum(elapsed_list) / len(elapsed_list)
        per_100 = mean_elapsed / args.years * 100 if args.years > 0 else 0
        print(f"\n  Wall-time (mean per seed): {mean_elapsed:.1f}s "
              f"  ({per_100:.1f}s / 100 sim-years)")


if __name__ == "__main__":
    main()
