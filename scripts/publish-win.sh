#!/usr/bin/env bash
# Publish WorldEngine.UI as a self-contained Windows (x64) executable.
# Run this from WSL2. The output is written to publish/win-x64/ which is
# accessible on the Windows side at the same path under the drive letter.
#
# Usage:
#   scripts/publish-win.sh              # self-contained (no .NET install needed on Windows)
#   scripts/publish-win.sh --framework  # framework-dependent (requires .NET 8 Runtime on Windows)
set -euo pipefail
cd "$(dirname "$0")/.."

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'

SELF_CONTAINED=true
if [[ "${1:-}" == "--framework" ]]; then
  SELF_CONTAINED=false
fi

OUT="publish/win-x64"
rm -rf "$OUT"

echo -e "${YELLOW}=== Publishing WorldEngine.UI → win-x64 ===${NC}"
echo -e "    Self-contained: ${SELF_CONTAINED}"
echo ""

dotnet publish WorldEngine.UI/WorldEngine.UI.csproj \
  -c Release \
  -r win-x64 \
  --self-contained "$SELF_CONTAINED" \
  -o "$OUT" \
  --nologo \
  -v q

echo ""
echo -e "${GREEN}=== Publish complete ===${NC}"
echo ""

# Show Windows path (only works if running inside WSL2)
if command -v wslpath &>/dev/null; then
  WIN_PATH="$(wslpath -w "$(pwd)/$OUT")"
  echo -e "  Windows path : ${CYAN}${WIN_PATH}${NC}"
  echo -e "  Executable   : ${CYAN}${WIN_PATH}\\WorldEngine.UI.exe${NC}"
  echo ""
  echo -e "  Run from PowerShell:"
  echo -e "    ${CYAN}& '${WIN_PATH}\\WorldEngine.UI.exe'${NC}"
  echo ""
  echo -e "  Or navigate to the folder in Windows Explorer and double-click ${CYAN}WorldEngine.UI.exe${NC}"
else
  echo "  Output: $(pwd)/$OUT/WorldEngine.UI.exe"
fi

echo ""
echo -e "  Note: delete ${CYAN}${WIN_PATH}\\world.db${NC} before each test run for clean event history."
