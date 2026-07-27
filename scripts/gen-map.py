#!/usr/bin/env python3
"""
gen-map.py — regenerates docs/codebase_map.md from <summary> XML docs.

GENERATED FILE TARGET: docs/codebase_map.md
Edit the <summary> XML docs in source, not the output file.
Regenerate: python3 scripts/gen-map.py

How it works:
  1. Builds the project to get the XML doc file (WorldEngine.Sim.xml).
  2. Parses T: members to get type→summary mappings.
  3. Scans source files to map each .cs file to its primary public type.
  4. Falls back to a // MAP: first-line comment if no type summary found.
  5. Groups files by directory and emits markdown grouped sections.
"""

import os
import re
import sys
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from collections import defaultdict

REPO_ROOT = Path(__file__).resolve().parent.parent
SIM_DIR   = REPO_ROOT / "WorldEngine.Sim"
UI_DIR    = REPO_ROOT / "WorldEngine.UI"
TESTS_DIR = REPO_ROOT / "WorldEngine.Tests"
SCRIPTS_DIR = REPO_ROOT / "scripts"
DOCS_DIR  = REPO_ROOT / "docs"
OUTPUT    = DOCS_DIR / "codebase_map.md"
XML_PATH  = SIM_DIR / "bin" / "Release" / "net10.0" / "WorldEngine.Sim.xml"
PROJ_FILE = SIM_DIR / "WorldEngine.Sim.csproj"

BANNER = """\
# Codebase Map
<!-- GENERATED from <summary> XML docs — edit the source <summary>, not this file. -->
<!-- Regenerate: python3 scripts/gen-map.py -->
One-line description of every non-trivial source file. Check here before running `find`. Updated when files are added/removed.
"""


def build_xml_if_needed():
    """Build the project to produce the XML doc file if it's missing or stale."""
    if not XML_PATH.exists() or XML_PATH.stat().st_mtime < PROJ_FILE.stat().st_mtime:
        print("Building WorldEngine.Sim to generate XML docs...", file=sys.stderr)
        result = subprocess.run(
            ["dotnet", "build", str(PROJ_FILE), "-c", "Release", "--nologo", "-v", "q"],
            capture_output=True, text=True, cwd=REPO_ROOT
        )
        if result.returncode != 0:
            print(result.stderr, file=sys.stderr)
            sys.exit(1)


def xml_element_text(el: ET.Element) -> str:
    """
    Flatten an XML doc element to plain text, resolving <see cref="X"/>,
    <paramref name="x"/>, and <typeparamref name="x"/> to their referenced
    name (stripping the leading "T:"/"M:"/"P:" kind prefix) instead of
    dropping them, which ET.text/.itertext() alone would do.
    """
    parts = [el.text or ""]
    for child in el:
        ref = child.get("cref") or child.get("name")
        if ref:
            parts.append(re.sub(r"^[A-Z]:", "", ref).split("(")[0])
        parts.append(child.tail or "")
    return "".join(parts)


def load_xml_summaries(xml_path: Path) -> dict[str, str]:
    """Return {fully_qualified_type_name: summary_text} for T: members."""
    summaries: dict[str, str] = {}
    try:
        tree = ET.parse(xml_path)
    except Exception as e:
        print(f"Warning: could not parse {xml_path}: {e}", file=sys.stderr)
        return summaries
    for member in tree.findall(".//member"):
        name = member.get("name", "")
        if not name.startswith("T:"):
            continue
        summary_el = member.find("summary")
        if summary_el is None:
            continue
        raw_text = xml_element_text(summary_el)
        if not raw_text:
            continue
        text = " ".join(raw_text.split())  # normalize whitespace
        type_name = name[2:]  # strip "T:"
        summaries[type_name] = text
    return summaries


def get_primary_type(cs_file: Path) -> tuple[str | None, str | None]:
    """
    Return (fully_qualified_name, short_name) of the first public type in the file.
    Returns (None, None) if no public type found.
    """
    text = cs_file.read_text(encoding="utf-8", errors="replace")

    # Detect namespace
    ns_match = re.search(r"^namespace\s+([\w.]+)", text, re.MULTILINE)
    namespace = ns_match.group(1) if ns_match else ""

    # Find first public class/record/interface/enum/struct
    type_match = re.search(
        r"^(?:public)\s+(?:sealed\s+|partial\s+|static\s+|abstract\s+)*"
        r"(class|record|interface|enum|struct)\s+(\w+)",
        text, re.MULTILINE
    )
    if not type_match:
        return None, None

    short_name = type_match.group(2)
    fqn = f"{namespace}.{short_name}" if namespace else short_name
    return fqn, short_name


def get_map_comment(cs_file: Path) -> str | None:
    """Return text after '// MAP:' anywhere before the primary public type declaration, or None."""
    try:
        text = cs_file.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return None
    # Only scan up to the first public type declaration
    type_match = re.search(
        r"^(?:public)\s+(?:sealed\s+|partial\s+|static\s+|abstract\s+)*"
        r"(class|record|interface|enum|struct)\s+",
        text, re.MULTILINE
    )
    scan_text = text[:type_match.start()] if type_match else text[:800]
    for line in scan_text.splitlines():
        m = re.match(r"\s*//\s*MAP:\s*(.*)", line)
        if m:
            return m.group(1).strip()
    return None


def get_inline_summary(cs_file: Path) -> str | None:
    """
    Extract <summary> text directly from source before the primary public type declaration.
    Used as fallback when the XML doc file doesn't cover the project (e.g. WorldEngine.UI).
    Returns the first summary block's text, normalized to a single line, or None.
    """
    try:
        text = cs_file.read_text(encoding="utf-8", errors="replace")
    except Exception:
        return None
    # Find '/// <summary>...(optional content).../// </summary>' immediately before a public type
    pattern = re.compile(
        r"/// <summary>(.*?)/// </summary>",
        re.DOTALL
    )
    # Find all summary blocks and pick the one closest before the primary public type
    type_match = re.search(
        r"^(?:public)\s+(?:sealed\s+|partial\s+|static\s+|abstract\s+)*"
        r"(class|record|interface|enum|struct)\s+",
        text, re.MULTILINE
    )
    if not type_match:
        return None
    type_pos = type_match.start()
    # Find the last summary block before the type
    best_text: str | None = None
    for m in pattern.finditer(text[:type_pos]):
        inner = m.group(1).strip()
        # Remove leading /// and whitespace from each line
        lines = [re.sub(r"^\s*///\s*", "", l).strip() for l in inner.splitlines()]
        combined = " ".join(l for l in lines if l)
        best_text = combined
    return best_text if best_text else None


def describe_file(cs_file: Path, xml_summaries: dict[str, str]) -> str | None:
    """
    Return a one-line description for a .cs file, or None if it should be skipped.
    Priority: compiled XML summary > inline /// <summary> > // MAP: comment > None (excluded).
    """
    fqn, _ = get_primary_type(cs_file)
    if fqn and fqn in xml_summaries:
        return xml_summaries[fqn]
    # Try inline /// <summary> (for projects without compiled XML, e.g. WorldEngine.UI)
    inline = get_inline_summary(cs_file)
    if inline:
        return inline
    # Try // MAP: comment
    map_comment = get_map_comment(cs_file)
    if map_comment:
        return map_comment
    # For files with a public type but no summary, return None so doc-check.py can flag it
    return None


def collect_files(base_dir: Path, rel_prefix: str, xml_summaries: dict[str, str]) -> dict[str, list[tuple[str, str]]]:
    """
    Walk base_dir, collect .cs files (excluding obj/, Vendor/, worktrees/), and group by directory.
    Returns {dir_header: [(filename_or_group, description), ...]}
    """
    SKIP_DIRS = {"obj", "Vendor", "worktrees", "bin"}
    groups: dict[str, list[tuple[str, str]]] = defaultdict(list)

    for root, dirs, files in os.walk(base_dir):
        # Prune skip dirs in-place
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        root_path = Path(root)
        rel = root_path.relative_to(REPO_ROOT)
        rel_str = str(rel).replace(os.sep, "/")

        cs_files = sorted([f for f in files if f.endswith(".cs")])
        if not cs_files:
            continue

        entries = []
        for fname in cs_files:
            fpath = root_path / fname
            desc = describe_file(fpath, xml_summaries)
            if desc:
                entries.append((fname, desc))

        if entries:
            groups[rel_str] = entries

    return groups


def format_section(dir_path: str, entries: list[tuple[str, str]]) -> str:
    lines = [f"## {dir_path}/"]
    for fname, desc in entries:
        lines.append(f"- `{fname}` — {desc}")
    return "\n".join(lines)


def collect_manual_sections() -> list[str]:
    """
    Return manually maintained sections that aren't auto-derived from .cs files
    (docs/, scripts/, config/, etc.). These are kept as authored text in a template
    appended after the generated sections.
    """
    # Read existing codebase_map for non-generated sections (docs, scripts, config sections)
    if not OUTPUT.exists():
        return []
    text = OUTPUT.read_text()
    # Find sections that cover non-generated directories (docs, scripts, config, Tests)
    # WorldEngine.Sim/ and WorldEngine.UI/ are fully generated from .cs summaries.
    manual_prefixes = ["## docs/", "## scripts/", "## config/profiles/", "## WorldEngine.Tests/"]
    sections = []
    current_section_lines: list[str] = []
    in_manual = False
    for line in text.splitlines():
        if line.startswith("## "):
            if in_manual and current_section_lines:
                # Strip trailing blank lines before storing
                while current_section_lines and not current_section_lines[-1].strip():
                    current_section_lines.pop()
                sections.append("\n".join(current_section_lines))
            current_section_lines = [line]
            in_manual = any(line.startswith(p) for p in manual_prefixes)
        elif in_manual:
            current_section_lines.append(line)
    if in_manual and current_section_lines:
        while current_section_lines and not current_section_lines[-1].strip():
            current_section_lines.pop()
        sections.append("\n".join(current_section_lines))
    return sections


def main():
    build_xml_if_needed()

    xml_summaries = load_xml_summaries(XML_PATH)
    print(f"Loaded {len(xml_summaries)} type summaries from XML docs.", file=sys.stderr)

    lines = [BANNER]

    # Collect Sim files
    sim_groups = collect_files(SIM_DIR, "WorldEngine.Sim", xml_summaries)
    for dir_path in sorted(sim_groups.keys()):
        entries = sim_groups[dir_path]
        lines.append(format_section(dir_path, entries))
        lines.append("")

    # Collect UI files
    ui_groups = collect_files(UI_DIR, "WorldEngine.UI", xml_summaries)
    for dir_path in sorted(ui_groups.keys()):
        entries = ui_groups[dir_path]
        lines.append(format_section(dir_path, entries))
        lines.append("")

    # Manual sections (preserved from existing map)
    manual_sections = collect_manual_sections()
    for section in manual_sections:
        lines.append(section)
        lines.append("")

    output_text = "\n".join(lines).rstrip() + "\n"
    OUTPUT.write_text(output_text, encoding="utf-8")
    print(f"Written {OUTPUT.relative_to(REPO_ROOT)}", file=sys.stderr)


if __name__ == "__main__":
    main()
