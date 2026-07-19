#!/usr/bin/env python3
"""
gen-enum-tables.py — regenerates enum integer tables inside event_log_queries.md.

Updates only the region between:
  <!-- GENERATED:enums -->
  <!-- GENERATED:enums END -->

Source: WorldEngine.Sim/Core/Enumerations.cs
Target: docs/queries/event_log_queries.md
Regenerate: python3 scripts/gen-enum-tables.py
"""

import re
import sys
from pathlib import Path

REPO_ROOT  = Path(__file__).resolve().parent.parent
ENUM_SRC   = REPO_ROOT / "WorldEngine.Sim" / "Core" / "Enumerations.cs"
QUERIES_MD = REPO_ROOT / "docs" / "queries" / "event_log_queries.md"

START_MARKER = "<!-- GENERATED:enums — DO NOT EDIT BELOW THIS LINE; run python3 scripts/gen-enum-tables.py -->"
END_MARKER   = "<!-- GENERATED:enums END -->"


def parse_enum(text: str, enum_name: str) -> list[tuple[str, int]] | None:
    """Extract (name, value) pairs from a named enum in C# source text."""
    # Match: enum EnumName\n{\n ... }
    pattern = re.compile(
        r"public\s+enum\s+" + re.escape(enum_name) + r"[^{]*\{([^}]+)\}",
        re.DOTALL
    )
    m = pattern.search(text)
    if not m:
        return None
    body = m.group(1)
    entries: list[tuple[str, int]] = []
    current_value = 0
    # Tokenize: split by commas, handling inline comments
    # First strip block comments (// to end of line)
    body_no_comments = re.sub(r"//[^\n]*", "", body)
    # Split by commas
    tokens = [t.strip() for t in body_no_comments.split(",")]
    for token in tokens:
        token = token.strip()
        if not token:
            continue
        # name = value or just name
        if "=" in token:
            name_part, val_part = token.split("=", 1)
            name = name_part.strip()
            val_str = val_part.strip()
            try:
                current_value = int(val_str)
            except ValueError:
                continue  # skip non-integer (e.g. computed values)
        else:
            name = token.strip()
        if name and re.match(r"^[A-Za-z_]\w*$", name):
            entries.append((name, current_value))
            current_value += 1
    return entries


def format_event_type(entries: list[tuple[str, int]]) -> str:
    """Format EventType entries grouped by range with comments."""
    lines = ["```"]

    # Group by range
    groups: dict[str, list[tuple[str, int]]] = {}
    for name, value in entries:
        if value < 2000:
            g = "Environmental (1000–1099)"
        elif value < 3000:
            g = "Beast events (2000–2099)"
        elif value < 3100:
            g = "Character lifecycle (3000–3099)"
        elif value < 3200:
            g = "Character actions (3100–3199)"
        elif value < 3300:
            g = "Civilization/Settlement (3200–3299)"
        elif value < 3400:
            g = "Tier 2 character events (3300–3399)"
        elif value < 3500:
            g = "Population events (3400–3499)"
        elif value < 5000:
            g = "Artifacts/Religion (4000–4999)"
        elif value < 6000:
            g = "Emissary/Diplomatic (5000–5999)"
        elif value < 7000:
            g = "Artifact (6000–6999)"
        elif value >= 9000:
            g = "God Mode (9000+)"
        else:
            g = "Other"
        groups.setdefault(g, []).append((name, value))

    for group_name, group_entries in groups.items():
        lines.append(f"-- {group_name}")
        # Format pairs per row
        row: list[str] = []
        col_width = max(len(f"{v} = {n}") for n, v in group_entries) + 2
        for name, value in group_entries:
            entry = f"{value} = {name}"
            row.append(entry.ljust(col_width))
            if len(row) >= 2:
                lines.append("  " + "  ".join(row))
                row = []
        if row:
            lines.append("  " + "  ".join(row))
        lines.append("")

    lines[-1] = ""  # ensure trailing blank before next section
    return "\n".join(lines)


def format_simple_enum(entries: list[tuple[str, int]], comment: str) -> str:
    """Format a simple enum as a single-line comment."""
    parts = "    ".join(f"{v} = {n}" for n, v in entries)
    return f"-- {comment}\n{parts}"


def generate_block(src_text: str) -> str:
    """Generate the full replacement block between the markers."""
    event_type = parse_enum(src_text, "EventType")
    verb_class = parse_enum(src_text, "VerbClass")
    event_tier = parse_enum(src_text, "EventTier")
    pop_impact = parse_enum(src_text, "PopulationImpact")
    season     = parse_enum(src_text, "Season")

    lines = ["```"]

    if event_type:
        # EventType grouped by range
        grouped_lines = format_event_type(event_type).splitlines()
        lines.extend(grouped_lines[1:])  # skip opening ```

    if verb_class:
        lines.append("")
        lines.append(format_simple_enum(verb_class, "VerbClass integers"))

    if event_tier:
        lines.append("")
        lines.append(format_simple_enum(event_tier, "TierInvolvement integers"))

    if pop_impact:
        lines.append("")
        lines.append(format_simple_enum(pop_impact, "PopulationImpact integers"))

    if season:
        lines.append("")
        lines.append(format_simple_enum(season, "Season integers"))

    lines.append("```")
    return "\n".join(lines)


def main():
    src_text = ENUM_SRC.read_text(encoding="utf-8")
    md_text  = QUERIES_MD.read_text(encoding="utf-8")

    # Find the marked region
    start_idx = md_text.find(START_MARKER)
    end_idx   = md_text.find(END_MARKER)

    if start_idx == -1 or end_idx == -1:
        print(
            f"ERROR: Could not find GENERATED:enums markers in {QUERIES_MD}",
            file=sys.stderr
        )
        sys.exit(1)

    new_block = generate_block(src_text)

    # Replace everything between (and including) the markers
    after_start = start_idx + len(START_MARKER)
    new_md = (
        md_text[:start_idx]
        + START_MARKER + "\n"
        + new_block + "\n"
        + END_MARKER
        + md_text[end_idx + len(END_MARKER):]
    )

    QUERIES_MD.write_text(new_md, encoding="utf-8")
    print(f"Written {QUERIES_MD.relative_to(REPO_ROOT)}", file=sys.stderr)

    # Report what we found
    for enum_name in ["EventType", "VerbClass", "EventTier", "PopulationImpact", "Season"]:
        entries = parse_enum(src_text, enum_name)
        count = len(entries) if entries else 0
        print(f"  {enum_name}: {count} entries", file=sys.stderr)


if __name__ == "__main__":
    main()
