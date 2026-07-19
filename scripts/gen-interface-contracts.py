#!/usr/bin/env python3
"""
gen-interface-contracts.py — Generates snapshot files of the raw public API for
key C# types in each interface contract domain.

Output files (one per domain):
  docs/interface_contracts_core.snapshot.md
  docs/interface_contracts_events.snapshot.md
  docs/interface_contracts_snapshot.snapshot.md
  docs/interface_contracts_tiles.snapshot.md

Each file starts with an auto-generated header and ends with a content-hash line.
Run python3 scripts/sync-contract-hash.py to stamp the prose docs with the hash.
"""

import hashlib
import re
import subprocess
import sys

from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SCRIPTS = REPO_ROOT / "scripts"
DOCS_DIR = REPO_ROOT / "docs"

DOMAINS: dict[str, list[str]] = {
    "core": [
        "PendingEvent", "IEntity", "ICommand",
        "IWorldStateReadOnly", "StateCache",
    ],
    "events": [
        "SimEvent", "IHistoryGraphReadOnly", "IHistoryQuery", "EventType",
    ],
    "snapshot": [
        "WorldSnapshot", "SettlementStub", "SettlementSnapshot",
        "Civilization", "EntitySnapshot", "AncestryConfig",
        "TerritorySnapshot", "ImprovementSnapshot", "CharacterWatchSnapshot",
    ],
    "tiles": ["TileData"],
}


# ---------------------------------------------------------------------------
# SCIP helpers
# ---------------------------------------------------------------------------

def scip_defs(type_name: str) -> list[str]:
    """Return raw lines from `scip-query.py defs <type_name>`."""
    result = subprocess.run(
        [sys.executable, str(SCRIPTS / "scip-query.py"), "defs", type_name],
        capture_output=True, text=True, cwd=REPO_ROOT, timeout=30,
    )
    return result.stdout.splitlines()


def find_primary_def(type_name: str) -> tuple[str, int] | None:
    """
    Return (repo-relative path, 1-based line number) for the primary definition
    of type_name, or None if not found.

    Strategy: look for lines like:
        WorldEngine.Sim/Foo/Bar.cs:N  TypeName
    Prefer lines that match the type name exactly and are NOT in test files.
    """
    lines = scip_defs(type_name)
    candidates: list[tuple[str, int]] = []

    for line in lines:
        # Match lines like: "WorldEngine.Sim/Foo/Bar.cs:12  TypeName"
        m = re.match(r"^(WorldEngine[.\w/]+\.cs):(\d+)\s+(\w+)\s*$", line.strip())
        if not m:
            continue
        rel_path, lineno_str, sym = m.group(1), m.group(2), m.group(3)
        if sym != type_name:
            continue
        # Skip test files
        if "Tests" in rel_path or "test" in rel_path.lower():
            continue
        candidates.append((rel_path, int(lineno_str)))

    if not candidates:
        return None

    # Prefer files whose name matches the type (e.g. TypeName.cs)
    for path, lineno in candidates:
        if Path(path).stem == type_name:
            return (path, lineno)

    return candidates[0]


# ---------------------------------------------------------------------------
# Extraction helpers
# ---------------------------------------------------------------------------

def read_source(rel_path: str) -> list[str]:
    """Return all lines of a source file (1-indexed list, line 0 is empty)."""
    full = REPO_ROOT / rel_path
    if not full.exists():
        return [""]
    lines = [""]  # dummy line 0 so we can use 1-based indexing
    lines.extend(full.read_text(encoding="utf-8", errors="replace").splitlines())
    return lines


def strip_xml_doc(lines: list[str]) -> list[str]:
    """Remove /// XML doc comment lines from a list of source lines."""
    return [ln for ln in lines if not re.match(r"\s*///", ln)]


def detect_kind(lines: list[str], start: int, type_name: str) -> str:
    """Detect: sealed record (primary ctor), record (body only), interface, enum, class, struct."""
    for i in range(start, min(start + 5, len(lines))):
        ln = lines[i]
        if re.search(r"\brecord\b", ln) and type_name in ln:
            # Determine if it's primary-constructor or body-only
            # A primary constructor record has `(` on the same or next line before `{`
            is_sealed = "sealed" in ln or (i > 1 and "sealed" in lines[i - 1])
            # Check if a `(` appears before the first `{` in the following lines
            has_primary_ctor = False
            for k in range(i, min(i + 3, len(lines))):
                seg = lines[k]
                paren_pos = seg.find("(")
                brace_pos = seg.find("{")
                if paren_pos != -1 and (brace_pos == -1 or paren_pos < brace_pos):
                    has_primary_ctor = True
                    break
                if brace_pos != -1:
                    break  # `{` before any `(` → body-only record
            if has_primary_ctor:
                return "sealed record" if is_sealed else "record"
            else:
                # Body-only record — treat like class for property extraction
                return "class"
        if re.search(r"\binterface\b", ln):
            return "interface"
        if re.search(r"\benum\b", ln):
            return "enum"
        if re.search(r"\bstruct\b", ln):
            return "struct"
        if re.search(r"\bclass\b", ln):
            return "class"
    return "class"


def extract_primary_constructor_record(lines: list[str], start: int) -> list[str]:
    """
    Extract a primary-constructor record from:
      public [sealed] record TypeName(
          ...
      );
    or with a body:
      public [sealed] record TypeName(
          ...
      ) { ... }

    Also collects any public properties in the body.
    Returns the cleaned lines to include in the snapshot.
    """
    result_lines: list[str] = []
    i = start
    n = len(lines)

    # Collect all lines from the declaration through the closing ");", ") {", or ");"
    # Track paren depth across ALL lines starting from the first opening paren.
    depth = 0
    found_open = False
    found_close = False

    while i < n and not found_close:
        ln = lines[i]
        if not re.match(r"\s*///", ln):
            result_lines.append(ln)
        i += 1

        for ch in ln:
            if ch == "(":
                depth += 1
                found_open = True
            elif ch == ")":
                depth -= 1

        # Once we've seen an opening paren and depth drops to 0, constructor params are done
        if found_open and depth <= 0:
            found_close = True

    # If there's a body after the closing paren, collect public members from it
    if found_close and i <= n:
        # The last collected line may end with ") {" or the next line may be "{"
        closing_ln = result_lines[-1] if result_lines else ""
        has_body = "{" in closing_ln
        if not has_body and i < n and lines[i].strip() in ("{", ""):
            if i < n and "{" in lines[i]:
                has_body = True

        if has_body:
            brace_depth = 0
            body_lines: list[str] = []
            # Start scanning from where we left off (or from the closing line)
            scan = i - 1  # step back to the last line we processed
            while scan < n:
                ln = lines[scan]
                for ch in ln:
                    if ch == "{":
                        brace_depth += 1
                    elif ch == "}":
                        brace_depth -= 1
                if brace_depth <= 0 and scan > i - 1:
                    break
                stripped = ln.strip()
                if (stripped.startswith("public") and
                        not re.match(r"\s*///", ln) and
                        not stripped.startswith("public static")):
                    body_lines.append(ln)
                scan += 1
            if body_lines:
                result_lines.extend(body_lines)

    return strip_xml_doc(result_lines)


def extract_interface(lines: list[str], start: int) -> list[str]:
    """Extract interface from `public interface IFoo` through its closing `}`."""
    result: list[str] = []
    i = start
    n = len(lines)
    brace_depth = 0
    in_block = False

    while i < n:
        ln = lines[i]
        is_doc = bool(re.match(r"\s*///", ln))
        # Skip attribute lines like [Obsolete]
        is_attr = bool(re.match(r"\s*\[", ln))

        if not is_doc:
            result.append(ln)

        for ch in ln:
            if ch == "{":
                brace_depth += 1
                in_block = True
            elif ch == "}":
                brace_depth -= 1

        i += 1
        if in_block and brace_depth <= 0:
            break

    return result


def extract_enum(lines: list[str], start: int) -> list[str]:
    """Extract enum including all member = value lines."""
    result: list[str] = []
    i = start
    n = len(lines)
    brace_depth = 0
    in_block = False

    while i < n:
        ln = lines[i]
        if not re.match(r"\s*///", ln):
            result.append(ln)
        for ch in ln:
            if ch == "{":
                brace_depth += 1
                in_block = True
            elif ch == "}":
                brace_depth -= 1
        i += 1
        if in_block and brace_depth <= 0:
            break

    return result


def extract_class_properties(lines: list[str], start: int, type_name: str) -> list[str]:
    """
    Extract public members from a class.
    For classes with <= 30 public members: include all public members (properties + methods).
    For larger classes: include only public properties.
    Returns the class header + selected public member lines.
    """
    result: list[str] = []
    n = len(lines)
    i = start
    brace_depth = 0
    in_class = False

    # First pass: count public members to decide strategy
    count_i = start
    count_depth = 0
    count_in = False
    pub_count = 0
    while count_i < n:
        ln = lines[count_i]
        for ch in ln:
            if ch == "{":
                count_depth += 1
                count_in = True
            elif ch == "}":
                count_depth -= 1
        if count_in and count_depth == 1 and ln.strip().startswith("public"):
            pub_count += 1
        count_i += 1
        if count_in and count_depth <= 0:
            break

    only_props = pub_count > 30

    # Find the class header
    while i < n:
        ln = lines[i]
        if not re.match(r"\s*///", ln):
            result.append(ln)
        for ch in ln:
            if ch == "{":
                brace_depth += 1
                in_class = True
            elif ch == "}":
                brace_depth -= 1
        i += 1
        if in_class:
            break

    # Scan body for public members at top-level depth (brace_depth == 1)
    while i < n and brace_depth > 0:
        ln = lines[i]
        stripped = ln.strip()
        prev_depth = brace_depth

        for ch in ln:
            if ch == "{":
                brace_depth += 1
            elif ch == "}":
                brace_depth -= 1

        if (prev_depth == 1 and
                stripped.startswith("public") and
                not re.match(r"\s*///", ln)):
            is_prop = ("{ get" in ln or "{ set" in ln or "get;" in ln or
                       "get; }" in ln or "get; set; }" in ln or
                       "get; init; }" in ln or "= new()" in ln or "= []" in ln or
                       "= new Dictionary" in ln or "= new HashSet" in ln or
                       ("HashSet<" in ln and "{" in ln))
            is_constructor = f"{type_name}(" in stripped
            if not only_props or is_prop:
                if not is_constructor:
                    result.append(ln)

        i += 1

    if brace_depth <= 0:
        result.append("}")

    return result


def extract_struct(lines: list[str], start: int) -> list[str]:
    """Extract struct fields (public fields and properties)."""
    result: list[str] = []
    n = len(lines)
    i = start
    brace_depth = 0
    in_struct = False

    while i < n:
        ln = lines[i]
        stripped = ln.strip()
        is_doc = bool(re.match(r"\s*///", ln))
        is_attr = bool(re.match(r"\s*\[", ln))

        # Include the struct header and attributes before it
        if not in_struct:
            if not is_doc:
                result.append(ln)
            for ch in ln:
                if ch == "{":
                    brace_depth += 1
                    in_struct = True
                elif ch == "}":
                    brace_depth -= 1
            i += 1
            if in_struct:
                continue
        else:
            for ch in ln:
                if ch == "{":
                    brace_depth += 1
                elif ch == "}":
                    brace_depth -= 1

            if brace_depth <= 0:
                result.append("}")
                i += 1
                break

            # Include public fields and properties; skip methods/static
            if (stripped.startswith("public") and
                    not is_doc and
                    not stripped.startswith("public static")):
                # Skip method bodies (opening brace on same line)
                if "{" not in stripped or "{ get" in stripped or "get;" in stripped:
                    result.append(ln)
            elif stripped == "" and not is_doc:
                # Preserve blank lines for readability
                result.append(ln)
            i += 1

    return result


# ---------------------------------------------------------------------------
# Type extraction dispatcher
# ---------------------------------------------------------------------------

def extract_type(type_name: str) -> tuple[str, str, str] | None:
    """
    Returns (rel_path:lineno, kind, extracted_code) or None if not found.
    extracted_code is multi-line string ready for the csharp code block.
    """
    defn = find_primary_def(type_name)
    if defn is None:
        return None

    rel_path, start_line = defn
    lines = read_source(rel_path)
    kind = detect_kind(lines, start_line, type_name)

    # For extraction, back up a few lines to grab any [Attribute] lines
    # that are part of the declaration but not the doc comment
    search_start = max(1, start_line - 3)

    # Find the actual declaration line (with `record`/`interface`/etc.)
    decl_line = start_line
    for k in range(search_start, min(start_line + 3, len(lines))):
        ln = lines[k]
        if re.search(
            r"\b(record|interface|enum|class|struct)\b.*\b" + re.escape(type_name) + r"\b",
            ln
        ):
            decl_line = k
            break

    # Include struct layout attributes from lines just before decl
    attr_lines: list[str] = []
    for k in range(max(1, decl_line - 3), decl_line):
        ln = lines[k].strip()
        if ln.startswith("[") and not re.match(r"\s*///", lines[k]):
            attr_lines.append(lines[k])

    if kind in ("sealed record", "record"):
        extracted = extract_primary_constructor_record(lines, decl_line)
    elif kind == "interface":
        extracted = extract_interface(lines, decl_line)
    elif kind == "enum":
        extracted = extract_enum(lines, decl_line)
    elif kind == "struct":
        extracted = extract_struct(lines, decl_line)
    elif kind == "class":
        extracted = extract_class_properties(lines, decl_line, type_name)
    else:
        extracted = [lines[decl_line]] if decl_line < len(lines) else []

    all_lines = attr_lines + extracted
    code = "\n".join(all_lines).strip()
    file_ref = f"{rel_path}:{start_line}"
    return file_ref, kind, code


# ---------------------------------------------------------------------------
# Snapshot file generation
# ---------------------------------------------------------------------------

def generate_domain(domain: str, type_names: list[str]) -> str:
    """Generate the full markdown content for one domain snapshot."""
    lines: list[str] = []
    lines.append(f"<!-- AUTO-GENERATED — do not edit. Run: python3 scripts/gen-interface-contracts.py -->")
    lines.append(f"")
    lines.append(f"# Interface Contracts Snapshot — {domain}")
    lines.append(f"")

    for type_name in type_names:
        result = extract_type(type_name)
        if result is None:
            lines.append(f"## {type_name}")
            lines.append(f"")
            lines.append(f"<!-- WARNING: type not found in SCIP index — skipped -->")
            lines.append(f"")
            print(f"  WARNING: {type_name} not found in SCIP", file=sys.stderr)
            continue

        file_ref, kind, code = result
        print(f"  {type_name}: {kind} @ {file_ref}", file=sys.stderr)

        lines.append(f"## {type_name}")
        lines.append(f"**File:** `{file_ref}`  ")
        lines.append(f"**Kind:** `{kind}`")
        lines.append(f"")
        lines.append("```csharp")
        lines.append(code)
        lines.append("```")
        lines.append(f"")

    content = "\n".join(lines)

    # Compute content hash and append
    sha = hashlib.sha256(content.encode("utf-8")).hexdigest()[:16]
    content += f"\n<!-- content-hash: {sha} -->\n"
    return content


def main() -> int:
    for domain, type_names in DOMAINS.items():
        out_path = DOCS_DIR / f"interface_contracts_{domain}.snapshot.md"
        print(f"Generating {out_path.name} ...", file=sys.stderr)
        content = generate_domain(domain, type_names)
        out_path.write_text(content, encoding="utf-8")
        print(f"  Written: {out_path}", file=sys.stderr)

    print("Done.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
