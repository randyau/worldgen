#!/usr/bin/env bash
# test-balance.sh — Run the balance regression harness (Category=Balance tests only).
# Runs 2 seeds × 300 years in-process; expect ~4–5 minutes wall time.
#
# These tests are excluded from the fast suite (scripts/test-fast.sh) and should be
# run explicitly before/after balance tuning sessions or as a nightly CI job.
#
# Usage: scripts/test-balance.sh [extra dotnet test args]
set -e

# pwd -P resolves symlinks — MSBuild treats /home/agi/e (symlink) and /mnt/e as
# different projects and corrupts incremental state if the spelling varies between runs
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd -P)"

echo "Running balance regression harness (Category=Balance)..."
echo "Expected runtime: ~4-5 minutes (2 seeds × 300 years, default world)"
dotnet test "$REPO_ROOT/WorldEngine.Tests/WorldEngine.Tests.csproj" \
    -c Release \
    --filter "Category=Balance" \
    "$@"
