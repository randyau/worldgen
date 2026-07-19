#!/usr/bin/env python3
"""
gen-config-ref.py — regenerates docs/config_reference.md from config/sim_config.toml.

GENERATED FILE TARGET: docs/config_reference.md
Edit the source config/sim_config.toml comments, not this file.
Regenerate: python3 scripts/gen-config-ref.py

For each key this outputs: value, inline TOML comment, and the bound C# property
(derived via the same snake_case→PascalCase convention used by SimConfigLoader).
"""

import re
import sys
from pathlib import Path
from collections import defaultdict

REPO_ROOT   = Path(__file__).resolve().parent.parent
TOML_PATH   = REPO_ROOT / "sim_config.toml"
OUTPUT      = REPO_ROOT / "docs" / "config_reference.md"

BANNER = """\
# Config Reference
<!-- GENERATED from config/sim_config.toml — edit the source TOML comments, not this file. -->
<!-- Regenerate: python3 scripts/gen-config-ref.py -->

All simulation tuning constants in one place. For balance guidance see `docs/sim_tuning.md`.
Edit values in `sim_config.toml`; all keys live there without recompiling.

"""


def snake_to_pascal(name: str) -> str:
    """Convert snake_case to PascalCase using the same logic as SimConfigLoader.PascalToSnakeCase (reversed)."""
    # Split on underscores, capitalize each segment
    return "".join(part.capitalize() for part in name.split("_"))


def parse_toml(path: Path):
    """
    Parse sim_config.toml and return a list of sections with their keys.
    Each section: { "header": str, "section_comment": str, "keys": [(key, value, comment)] }
    We handle the TOML structure manually to preserve inline comments.
    """
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()

    sections = []
    current_section = None
    current_comment_lines: list[str] = []
    section_banner_lines: list[str] = []
    in_array = False
    array_key: str | None = None
    array_lines: list[str] = []

    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        # Detect separator comment banners (=== style)
        if stripped.startswith("# ==="):
            section_banner_lines = [stripped]
            i += 1
            while i < len(lines) and lines[i].strip().startswith("# ==="):
                section_banner_lines.append(lines[i].strip())
                i += 1
            # Next line is usually a # TITLE comment
            if i < len(lines) and lines[i].strip().startswith("# ") and not lines[i].strip().startswith("# ==="):
                # This is the section title comment - captured below as current_comment_lines
                pass
            continue

        # Section header [section.subsection]
        if stripped.startswith("[") and not stripped.startswith("[["):
            section_name = stripped.strip("[]")
            current_section = {"header": section_name, "section_comment": "", "keys": []}
            sections.append(current_section)
            # Attach any accumulated comments as section comment
            if current_comment_lines:
                current_section["section_comment"] = " ".join(
                    l.lstrip("# ").strip() for l in current_comment_lines if l.strip().lstrip("#").strip()
                )
                current_comment_lines = []
            i += 1
            continue

        # Blank line
        if not stripped:
            # Reset comment accumulator on blank line (comments are key-specific inline, not block)
            current_comment_lines = []
            i += 1
            continue

        # Comment-only line
        if stripped.startswith("#"):
            current_comment_lines.append(stripped)
            i += 1
            continue

        # Array start (key = [)
        if "=" in stripped and stripped.split("=", 1)[1].strip().startswith("[") and not stripped.split("=", 1)[1].strip().startswith("[["):
            # Check if it's a multi-line array
            rhs = stripped.split("=", 1)[1].strip()
            if rhs.count("]") == 0 or (rhs.count("[") > rhs.count("]")):
                # Multi-line array — consume until closing ]
                key_part = stripped.split("=")[0].strip()
                array_key = key_part
                array_lines = [stripped]
                i += 1
                while i < len(lines) and "]" not in lines[i]:
                    array_lines.append(lines[i])
                    i += 1
                if i < len(lines):
                    array_lines.append(lines[i])
                    i += 1
                # Build value as compact list
                all_content = "\n".join(array_lines)
                m = re.search(r"\[(.*?)\]", all_content, re.DOTALL)
                value_str = "[" + (m.group(1).strip() if m else "") + "]"
                # Strip inline values from array entries
                values = [v.strip().strip('"').strip("'") for v in re.findall(r'(\d+|"[^"]*")', value_str)]
                comment = " ".join(
                    l.lstrip("# ").strip() for l in current_comment_lines if l.strip().lstrip("#").strip()
                )
                if current_section is not None:
                    current_section["keys"].append((array_key, ", ".join(values) if values else value_str, comment))
                current_comment_lines = []
                continue
            else:
                # Single-line array or empty
                pass

        # Key = value line
        if "=" in stripped and current_section is not None:
            # Split on = but only the first one
            eq_idx = stripped.index("=")
            key = stripped[:eq_idx].strip()
            rest = stripped[eq_idx+1:].strip()
            # Extract inline comment
            inline_comment = ""
            # Match value (possibly quoted), then optional # comment
            # Pattern: value (string or number/bool), optional # comment
            val_m = re.match(r'^(".*?"|\'.*?\'|\[.*?\]|[^#\s][^#]*)(?:\s*#\s*(.*))?$', rest)
            if val_m:
                value = val_m.group(1).strip()
                inline_comment = (val_m.group(2) or "").strip()
            else:
                value = rest
            # Block comments above the key
            block_comment = " ".join(
                l.lstrip("# ").strip() for l in current_comment_lines if l.strip().lstrip("#").strip()
            )
            comment = inline_comment or block_comment
            current_section["keys"].append((key, value, comment))
            current_comment_lines = []

        i += 1

    return sections


def format_c_property(section_header: str, key: str) -> str:
    """
    Derive the C# property path from the TOML section + key using the same
    snake_case→PascalCase convention as SimConfigLoader.FindProperty.
    Returns something like SimConfig.WorldGen.DefaultTileSizeKm.
    """
    # Map TOML section to C# property chain
    parts = section_header.split(".")
    prop_path = ["SimConfig"] + [snake_to_pascal(p) for p in parts] + [snake_to_pascal(key)]
    return ".".join(prop_path)


def main():
    sections = parse_toml(TOML_PATH)

    lines = [BANNER]

    # Table of contents
    lines.append("## Sections\n")
    for section in sections:
        anchor = section["header"].replace(".", "").replace("_", "-").lower()
        lines.append(f"- [{section['header']}](#{anchor})")
    lines.append("")

    # Per-section tables
    for section in sections:
        if not section["keys"]:
            continue
        header = section["header"]
        anchor_id = header.replace(".", "").replace("_", "-").lower()
        lines.append(f"## `[{header}]` {{#{anchor_id}}}\n")
        if section["section_comment"]:
            lines.append(f"_{section['section_comment']}_\n")
        lines.append("| Key | Value | C# Property | Description |")
        lines.append("|-----|-------|-------------|-------------|")
        for key, value, comment in section["keys"]:
            c_prop = format_c_property(header, key)
            # Escape pipes in values
            value_safe = str(value).replace("|", "\\|")
            comment_safe = comment.replace("|", "\\|")
            lines.append(f"| `{key}` | `{value_safe}` | `{c_prop}` | {comment_safe} |")
        lines.append("")

    output_text = "\n".join(lines).rstrip() + "\n"
    OUTPUT.write_text(output_text, encoding="utf-8")
    print(f"Written {OUTPUT.relative_to(REPO_ROOT)}", file=sys.stderr)
    total_keys = sum(len(s["keys"]) for s in sections)
    print(f"Documented {len(sections)} sections, {total_keys} keys.", file=sys.stderr)


if __name__ == "__main__":
    main()
