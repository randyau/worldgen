#!/usr/bin/env python3
"""
doc-check.py — lints authored markdown docs and verifies generated files are in sync.

Checks:
  (a) Backticked identifiers in authored markdown resolve:
      - File paths (tokens ending in known extensions, prefixed with a directory component)
        must exist somewhere in the repo tree
      - PascalCase compound type names (clearly multi-word, e.g. WorldState, SimConfig)
        must appear in the SCIP symbol list
      - TOML dotted keys (snake.case.key) must exist in sim_config.toml
  (b) docs/phases/ contains no doc with "Status: COMPLETE" outside archive/
  (c) Generated files match fresh regeneration

Exit 0 = clean; exit 1 = violations found.

Allowlist: scripts/doc-check-allowlist.txt — one token per line, # = comment.
"""

import os
import re
import sys
import subprocess
import shutil
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS_DIR  = REPO_ROOT / "docs"
SCRIPTS   = REPO_ROOT / "scripts"
TOML_PATH = REPO_ROOT / "sim_config.toml"
ALLOWLIST_PATH = SCRIPTS / "doc-check-allowlist.txt"

# Generated files: skip check (a) but DO check (c) freshness
GENERATED_DOCS = {
    DOCS_DIR / "codebase_map.md",
    DOCS_DIR / "config_reference.md",
    DOCS_DIR / "queries" / "event_log_queries.md",
}

# Directories under docs/ to skip entirely for check (a)
SKIP_DIRS = {"archive", "design"}

# Known file extensions that trigger path-existence checks
PATH_EXTENSIONS = {".cs", ".md", ".toml", ".py", ".sh", ".txt", ".json", ".xml"}

# Extensions that mark something as a filename, NOT a TOML key
# (includes runtime artifacts that aren't checked into the repo)
FILENAME_EXTENSIONS = PATH_EXTENSIONS | {".db", ".bin", ".exe", ".dll", ".log", ".csv", ".scip"}

# Common English words that look like PascalCase type names but aren't — avoid false positives
NOT_TYPES = {
    "Status", "Phase", "Type", "Mode", "State", "Data", "Config", "Base", "Core",
    "True", "False", "None", "Null", "This", "That", "From", "Into", "Over",
    "With", "Note", "Each", "When", "Where", "Which", "Then", "Both",
    "Alpha", "Beta", "Delta", "Gamma", "Sigma", "Omega",
    "Slow", "Fast", "Normal", "Small", "Large", "High", "Low", "Min", "Max",
    # NuGet package names and external libs — not in SCIP
    "MessagePack", "FluentAssertions", "Dapper", "Tomlyn", "NetArchTest",
    "MonoGame", "SQLite", "FastNoiseLite", "Myra",
    # Common abbreviations / test method names
    "ReaderWriterLockSlim",
}


def load_allowlist() -> set[str]:
    """Load the exception allowlist (tokens that should not be flagged)."""
    if not ALLOWLIST_PATH.exists():
        return set()
    allowed: set[str] = set()
    for line in ALLOWLIST_PATH.read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            allowed.add(line)
    return allowed


def load_scip_types() -> set[str]:
    """Load short type names from index.scip via scip-query.py."""
    try:
        result = subprocess.run(
            [sys.executable, str(SCRIPTS / "scip-query.py"), "types"],
            capture_output=True, text=True, cwd=REPO_ROOT, timeout=30
        )
        types: set[str] = set()
        # Lines from 'types' command look like:
        #   WorldEngine.Sim/Config/SimConfig.cs:4  SimConfig
        #       scip-dotnet nuget . . Config/SimConfig#
        # We grab the short name from the first column and from the scip symbol
        for line in result.stdout.splitlines():
            line = line.strip()
            # Short name from the first line format: "file.cs:N  TypeName"
            m1 = re.match(r"[\w./]+\.cs:\d+\s+(\w+)", line)
            if m1:
                types.add(m1.group(1))
            # Also from scip symbol line
            m2 = re.search(r"/([A-Z][A-Za-z0-9_]+)#", line)
            if m2:
                types.add(m2.group(1))
        return types
    except Exception as e:
        print(f"Warning: could not load SCIP types: {e}", file=sys.stderr)
        return set()


def load_toml_keys() -> set[str]:
    """Load all simple keys and dotted section.key pairs from sim_config.toml."""
    keys: set[str] = set()
    if not TOML_PATH.exists():
        return keys
    text = TOML_PATH.read_text()
    current_section = ""
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("[") and not stripped.startswith("[["):
            current_section = stripped.strip("[]")
        elif "=" in stripped and not stripped.startswith("#"):
            key_part = stripped.split("=")[0].strip()
            if key_part and re.match(r"^[a-z][a-z0-9_]*$", key_part):
                keys.add(key_part)
                if current_section:
                    keys.add(f"{current_section}.{key_part}")
    return keys


def build_file_index() -> set[str]:
    """Build a set of all file basenames (and last-two-segment paths) in the repo."""
    names: set[str] = set()
    skip = {"obj", "bin", "worktrees", "Vendor", ".git", "node_modules"}
    for root, dirs, files in os.walk(REPO_ROOT):
        dirs[:] = [d for d in dirs if d not in skip]
        for f in files:
            names.add(f)
            names.add(f"{Path(root).name}/{f}")
    return names


def is_compound_pascal_type(token: str) -> bool:
    """
    Return True only if token VERY CLEARLY looks like a standalone C# type name.
    We are conservative here to minimize false positives.

    Strategy: must look like a class/interface/record name specifically —
    things that end with common type suffixes, or Interface names starting with I.
    We skip property names, method names, enum values, and field names entirely.
    """
    if token in NOT_TYPES:
        return False
    if not token[0].isupper():
        return False
    if len(token) < 5:
        return False

    # Only flag tokens that end with strong type-name suffixes
    TYPE_SUFFIXES = (
        "Config", "Registry", "Store", "Cache", "Queue", "Builder",
        "Provider", "Service", "Manager", "Handler", "Layer", "Phase",
        "Pipeline", "Loader", "Validator", "Resolver", "Runner",
        "Factory", "Snapshot", "Tracker", "Collector", "Assembler",
        "Context", "Result", "Data", "Stub", "Record",
    )
    # Interface names
    if token.startswith("I") and len(token) > 2 and token[1].isupper():
        return True
    # Ends with known suffix
    if any(token.endswith(s) for s in TYPE_SUFFIXES):
        return True

    return False


def check_backtick_identifiers(
    md_file: Path,
    scip_types: set[str],
    toml_keys: set[str],
    file_index: set[str],
    allowlist: set[str]
) -> list[str]:
    """Check that backticked identifiers resolve in the given doc."""
    v: list[str] = []
    text = md_file.read_text(encoding="utf-8", errors="replace")
    rel = str(md_file.relative_to(REPO_ROOT))

    # Skip code blocks (``` ... ```)
    # Replace code blocks with spaces to avoid matching code
    text_no_blocks = re.sub(r"```.*?```", lambda m: " " * len(m.group()), text, flags=re.DOTALL)

    for m in re.finditer(r"`([^`]+)`", text_no_blocks):
        token = m.group(1).strip()

        if not token or token in allowlist:
            continue
        if " " in token or "\n" in token or len(token) <= 2:
            continue
        if token.startswith("--") or token.startswith("//") or token[0].isdigit():
            continue
        # Skip tokens with special chars (SQL, code expressions)
        if any(c in token for c in "()[]<>{}=+*%@!?;,"):
            continue

        ext = Path(token.split("/")[-1]).suffix if "." in token else ""

        # PATH CHECK: token with known extension AND a directory component
        if ext in PATH_EXTENSIONS and ("/" in token or token.endswith(".toml")):
            # Check existence: match by basename or last two segments
            basename = Path(token).name
            last_two = "/".join(token.split("/")[-2:]) if "/" in token else token
            if (basename not in file_index
                    and last_two not in file_index
                    and not (REPO_ROOT / token).exists()
                    and not (DOCS_DIR / token).exists()):
                v.append(f"{rel}: broken path ref `{token}`")

        # TYPE CHECK: compound PascalCase that should be in SCIP
        elif is_compound_pascal_type(token) and "." not in token and "/" not in token:
            if token not in scip_types:
                v.append(f"{rel}: unresolved type ref `{token}` (not in SCIP index)")

        # TOML KEY CHECK: dotted snake_case that should be a config key
        # Exclude: filenames (ending in known extensions), URLs, version numbers
        elif re.match(r"^[a-z][a-z0-9_]+\.[a-z][a-z0-9_.]+$", token):
            # Skip if the token is clearly a filename (extension is a known file suffix)
            _suffix = "." + token.rsplit(".", 1)[-1] if "." in token else ""
            if _suffix in FILENAME_EXTENSIONS:
                pass  # It's a filename, not a TOML key — skip
            elif token not in toml_keys and token not in allowlist:
                v.append(f"{rel}: unresolved TOML key `{token}` (not in sim_config.toml)")

    return v


def check_phase_status() -> list[str]:
    """Check (b): docs/phases/ has no COMPLETE status outside archive/."""
    v: list[str] = []
    phases_dir = DOCS_DIR / "phases"
    if not phases_dir.exists():
        return v
    for f in phases_dir.iterdir():
        if f.is_file() and f.suffix == ".md":
            text = f.read_text(encoding="utf-8", errors="replace")
            if re.search(r"\bStatus\b.*\bCOMPLETE\b", text):
                v.append(f"docs/phases/{f.name}: Status: COMPLETE but not in archive/")
    return v


def check_generated_in_sync() -> list[str]:
    """Check (c): generated files match fresh regeneration."""
    v: list[str] = []
    generators = [
        (SCRIPTS / "gen-map.py",         DOCS_DIR / "codebase_map.md"),
        (SCRIPTS / "gen-config-ref.py",  DOCS_DIR / "config_reference.md"),
        (SCRIPTS / "gen-enum-tables.py", DOCS_DIR / "queries" / "event_log_queries.md"),
    ]

    for gen_script, output_file in generators:
        if not gen_script.exists():
            v.append(f"Generator missing: {gen_script.relative_to(REPO_ROOT)}")
            continue
        if not output_file.exists():
            v.append(f"Generated file missing: {output_file.relative_to(REPO_ROOT)}")
            continue

        current = output_file.read_text(encoding="utf-8")

        # Run the generator (it overwrites the output file in-place)
        result = subprocess.run(
            [sys.executable, str(gen_script)],
            capture_output=True, text=True, cwd=REPO_ROOT, timeout=120
        )

        if result.returncode != 0:
            # Restore original and report failure
            output_file.write_text(current, encoding="utf-8")
            v.append(f"Generator {gen_script.name} failed: {result.stderr.strip()[:200]}")
            continue

        fresh = output_file.read_text(encoding="utf-8")
        if fresh != current:
            # Restore original — drift is a warning, don't leave stale file
            output_file.write_text(current, encoding="utf-8")
            v.append(
                f"Drift: {output_file.relative_to(REPO_ROOT)} differs from fresh "
                f"{gen_script.name} output — run python3 scripts/{gen_script.name}"
            )

    return v


def collect_authored_docs() -> list[Path]:
    """Collect authored (non-generated) .md files under docs/."""
    docs: list[Path] = []
    for root, dirs, files in os.walk(DOCS_DIR):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if f.endswith(".md"):
                path = Path(root) / f
                if path not in GENERATED_DOCS:
                    docs.append(path)
    return docs


def main() -> int:
    allowlist = load_allowlist()

    print("Loading SCIP type index...", file=sys.stderr)
    scip_types = load_scip_types()
    print(f"  {len(scip_types)} types loaded.", file=sys.stderr)

    toml_keys = load_toml_keys()
    print(f"  {len(toml_keys)} TOML keys loaded.", file=sys.stderr)

    print("Building file index...", file=sys.stderr)
    file_index = build_file_index()
    print(f"  {len(file_index)} file names indexed.", file=sys.stderr)

    all_v: list[str] = []

    # Check (a)
    authored = collect_authored_docs()
    print(f"Checking {len(authored)} authored docs for broken refs...", file=sys.stderr)
    for doc in authored:
        all_v.extend(check_backtick_identifiers(doc, scip_types, toml_keys, file_index, allowlist))

    # Check (b)
    print("Checking phases/ for misplaced COMPLETE docs...", file=sys.stderr)
    all_v.extend(check_phase_status())

    # Check (c)
    print("Checking generated docs are in sync...", file=sys.stderr)
    all_v.extend(check_generated_in_sync())

    if all_v:
        print(f"\nDOC-CHECK: {len(all_v)} violation(s):\n", file=sys.stderr)
        for violation in all_v:
            print(f"  {violation}", file=sys.stderr)
        print("", file=sys.stderr)
        return 1

    print("doc-check: all checks passed.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
