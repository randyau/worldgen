#!/usr/bin/env bash
# test-fast.sh — Run the fast test suite (excludes Balance category tests).
# Runs in ~25 seconds on the default world config.
# Also runs doc-check.py as a hard gate (generated-doc drift and broken refs fail the suite).
#
# Usage: scripts/test-fast.sh [extra dotnet test args]
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "Running doc-check (docs lint + generated-doc freshness gate)..."
python3 "$SCRIPT_DIR/doc-check.py"

echo "Running fast test suite (Category!=Balance)..."
dotnet test "$REPO_ROOT/WorldEngine.Tests/WorldEngine.Tests.csproj" \
    -c Release \
    --filter "Category!=Balance" \
    "$@"
