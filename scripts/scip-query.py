#!/usr/bin/env python3
"""
Query the SCIP code intelligence index (index.scip).

Usage:
  python3 scripts/scip-query.py refs <Pattern>    — find all references to symbols matching Pattern
  python3 scripts/scip-query.py defs <Pattern>    — find definitions matching Pattern
  python3 scripts/scip-query.py impls <Interface> — find types that implement Interface
  python3 scripts/scip-query.py types             — list all defined types
  python3 scripts/scip-query.py stats             — index statistics

Pattern matching is case-insensitive substring search on the symbol name segment.

Run: python3 scripts/scip-query.py --setup   to compile the protobuf bindings on first use.
"""

import sys
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT   = os.path.dirname(SCRIPT_DIR)
INDEX_PATH  = os.path.join(REPO_ROOT, "index.scip")
PB2_PATH    = os.path.join(SCRIPT_DIR, "scip_pb2.py")
PROTO_PATH  = os.path.join(SCRIPT_DIR, "scip.proto")

SYMBOL_ROLE_DEFINITION = 1

def ensure_pb2():
    """Compile scip.proto → scip_pb2.py if not already present."""
    if os.path.exists(PB2_PATH):
        return
    print("Compiling SCIP protobuf bindings...", file=sys.stderr)
    import subprocess
    result = subprocess.run(
        [sys.executable, "-m", "grpc_tools.protoc", f"-I{SCRIPT_DIR}",
         f"--python_out={SCRIPT_DIR}", PROTO_PATH],
        capture_output=True, text=True
    )
    if result.returncode != 0:
        print(f"Proto compile failed:\n{result.stderr}", file=sys.stderr)
        print("Install grpc_tools: pip3 install --user grpcio-tools", file=sys.stderr)
        sys.exit(1)

def load_index():
    if not os.path.exists(INDEX_PATH):
        print(f"index.scip not found at {INDEX_PATH}", file=sys.stderr)
        print("Generate it: dotnet tool run scip-dotnet index WorldEngine.sln", file=sys.stderr)
        sys.exit(1)
    ensure_pb2()
    sys.path.insert(0, SCRIPT_DIR)
    import scip_pb2
    idx = scip_pb2.Index()
    idx.ParseFromString(open(INDEX_PATH, "rb").read())
    return idx

def symbol_short_name(sym: str) -> str:
    """Extract the readable name from a SCIP symbol string."""
    # Format: 'scip-dotnet nuget . . Namespace/TypeName#MethodName().'
    parts = sym.split()
    if len(parts) >= 5:
        path = parts[4]
        # strip trailing punctuation
        path = re.sub(r'[#.()\[\]]+$', '', path)
        # take last segment
        return path.split('/')[-1]
    return sym

def sym_matches(symbol: str, pattern: str) -> bool:
    return pattern.lower() in symbol.lower()

def cmd_stats(idx):
    total_occ = sum(len(d.occurrences) for d in idx.documents)
    total_sym = sum(len(d.symbols) for d in idx.documents)
    print(f"Tool:        {idx.metadata.tool_info.name} {idx.metadata.tool_info.version}")
    print(f"Documents:   {len(idx.documents)}")
    print(f"Symbols:     {total_sym}")
    print(f"Occurrences: {total_occ}")

def cmd_defs(idx, pattern):
    """Print all definition sites matching pattern."""
    results = []
    for doc in idx.documents:
        for occ in doc.occurrences:
            if occ.symbol_roles == SYMBOL_ROLE_DEFINITION and sym_matches(occ.symbol, pattern):
                r = occ.range
                line = r[0] + 1 if r else 0
                results.append((doc.relative_path, line, occ.symbol))
    if not results:
        print(f"No definitions found for '{pattern}'")
        return
    for path, line, sym in sorted(results):
        print(f"{path}:{line}  {symbol_short_name(sym)}")
        print(f"    {sym}")

def cmd_refs(idx, pattern):
    """Print all reference sites (non-definition occurrences) matching pattern."""
    results = []
    for doc in idx.documents:
        for occ in doc.occurrences:
            if occ.symbol_roles != SYMBOL_ROLE_DEFINITION and sym_matches(occ.symbol, pattern):
                r = occ.range
                line = r[0] + 1 if r else 0
                results.append((doc.relative_path, line, occ.symbol))
    if not results:
        print(f"No references found for '{pattern}'")
        return
    # Group by file
    from collections import defaultdict
    by_file = defaultdict(list)
    for path, line, sym in results:
        by_file[path].append((line, sym))
    for path in sorted(by_file):
        entries = sorted(by_file[path])
        syms = {symbol_short_name(s) for _, s in entries}
        print(f"{path}  ({len(entries)} refs, symbols: {', '.join(sorted(syms))})")
        for line, sym in entries[:5]:
            print(f"  :{line}  {symbol_short_name(sym)}")
        if len(entries) > 5:
            print(f"  ... +{len(entries)-5} more")

def cmd_types(idx):
    """List all defined types (classes, interfaces, records, enums)."""
    seen = set()
    for doc in idx.documents:
        for occ in doc.occurrences:
            if occ.symbol_roles == SYMBOL_ROLE_DEFINITION:
                sym = occ.symbol
                # Type definitions end with # (not #Method or #field)
                parts = sym.split()
                if len(parts) >= 5:
                    path_part = parts[4]
                    if path_part.endswith('#') and '(' not in path_part:
                        short = symbol_short_name(sym)
                        if short and short not in seen:
                            seen.add(short)
                            r = occ.range
                            line = r[0] + 1 if r else 0
                            print(f"{doc.relative_path}:{line}  {short}")
                            print(f"    {sym}")

def cmd_impls(idx, interface_pattern):
    """Find types that appear to implement the given interface (best-effort heuristic)."""
    # SCIP doesn't store explicit inheritance, but we can find types that override
    # methods from the interface by looking for definition symbols that share method names.
    # More reliable: find all types defined in test/impl files that reference interface symbols.
    refs_by_file: dict[str, set] = {}
    iface_syms = set()

    for doc in idx.documents:
        for occ in doc.occurrences:
            if sym_matches(occ.symbol, interface_pattern):
                iface_syms.add(occ.symbol)
                refs_by_file.setdefault(doc.relative_path, set()).add(occ.symbol)

    if not iface_syms:
        print(f"No symbols found matching '{interface_pattern}'")
        return

    print(f"Symbols matching '{interface_pattern}':")
    for s in sorted(iface_syms)[:10]:
        print(f"  {s}")
    print(f"\nFiles referencing these symbols ({len(refs_by_file)} files):")
    for path in sorted(refs_by_file):
        print(f"  {path}")


if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        sys.exit(0)

    if sys.argv[1] == "--setup":
        ensure_pb2()
        print("Setup complete.")
        sys.exit(0)

    cmd = sys.argv[1]
    idx = load_index()

    if cmd == "stats":
        cmd_stats(idx)
    elif cmd == "defs" and len(sys.argv) >= 3:
        cmd_defs(idx, sys.argv[2])
    elif cmd == "refs" and len(sys.argv) >= 3:
        cmd_refs(idx, sys.argv[2])
    elif cmd == "types":
        cmd_types(idx)
    elif cmd == "impls" and len(sys.argv) >= 3:
        cmd_impls(idx, sys.argv[2])
    else:
        print(f"Unknown command or missing argument: {sys.argv[1:]}")
        print(__doc__)
        sys.exit(1)
