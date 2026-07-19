#!/usr/bin/env python3
"""
sync-contract-hash.py — Stamps each prose interface contract doc with the
content-hash from its corresponding snapshot file.

Steps:
  1. Regenerates all 4 snapshot files (runs gen-interface-contracts.py)
  2. Reads each snapshot's <!-- content-hash: XXXXXXXX --> from its last line
  3. Writes/updates <!-- contract-snapshot-hash: XXXXXXXX --> as line 1
     of the corresponding prose doc

Usage: python3 scripts/sync-contract-hash.py
"""

import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS   = REPO_ROOT / "scripts"
DOCS_DIR  = REPO_ROOT / "docs"

DOMAINS = ["core", "events", "snapshot", "tiles"]

HASH_PATTERN = re.compile(r"<!--\s*content-hash:\s*([0-9a-f]+)\s*-->")
PROSE_MARKER_PATTERN = re.compile(r"<!--\s*contract-snapshot-hash:\s*([0-9a-f]*)\s*-->")


def get_snapshot_hash(domain: str) -> str | None:
    """Extract the content-hash from the last non-empty line of a snapshot file."""
    snap = DOCS_DIR / f"interface_contracts_{domain}.snapshot.md"
    if not snap.exists():
        print(f"  ERROR: snapshot not found: {snap}", file=sys.stderr)
        return None
    text = snap.read_text(encoding="utf-8")
    # The hash is on the last line
    for line in reversed(text.splitlines()):
        line = line.strip()
        if not line:
            continue
        m = HASH_PATTERN.search(line)
        if m:
            return m.group(1)
        break  # Last non-empty line had no hash — unexpected
    print(f"  ERROR: no content-hash found in {snap.name}", file=sys.stderr)
    return None


def stamp_prose_doc(domain: str, hash_value: str) -> None:
    """Write/update <!-- contract-snapshot-hash: XXXXXXXX --> on line 1 of the prose doc."""
    prose = DOCS_DIR / f"interface_contracts_{domain}.md"
    if not prose.exists():
        print(f"  WARNING: prose doc not found: {prose.name}", file=sys.stderr)
        return

    marker = f"<!-- contract-snapshot-hash: {hash_value} -->"
    text = prose.read_text(encoding="utf-8")
    lines = text.splitlines(keepends=True)

    if lines and PROSE_MARKER_PATTERN.match(lines[0].strip()):
        # Update existing marker
        old_marker = lines[0].rstrip("\r\n")
        lines[0] = marker + "\n"
        prose.write_text("".join(lines), encoding="utf-8")
        if old_marker == marker:
            print(f"  {prose.name}: hash unchanged ({hash_value})")
        else:
            print(f"  {prose.name}: updated hash → {hash_value}")
    else:
        # Prepend marker
        new_text = marker + "\n" + text
        prose.write_text(new_text, encoding="utf-8")
        print(f"  {prose.name}: inserted hash marker ({hash_value})")


def main() -> int:
    # Step 1: Regenerate all snapshot files
    print("Regenerating interface contract snapshots...", file=sys.stderr)
    result = subprocess.run(
        [sys.executable, str(SCRIPTS / "gen-interface-contracts.py")],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    if result.returncode != 0:
        print(f"ERROR: gen-interface-contracts.py failed:\n{result.stderr}", file=sys.stderr)
        return 1
    print(result.stderr, end="", file=sys.stderr)

    # Step 2+3: Read each snapshot hash and stamp prose doc
    print("Stamping prose docs with snapshot hashes...", file=sys.stderr)
    errors = 0
    for domain in DOMAINS:
        h = get_snapshot_hash(domain)
        if h is None:
            errors += 1
            continue
        stamp_prose_doc(domain, h)

    if errors:
        print(f"\n{errors} error(s) encountered.", file=sys.stderr)
        return 1

    print("Done.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
