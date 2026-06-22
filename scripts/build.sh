#!/usr/bin/env bash
# Build and test the WorldEngine sim core (WSL2 / Linux).
# Does NOT build WorldEngine.UI — that targets Windows.
set -euo pipefail
cd "$(dirname "$0")/.."

GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'

echo -e "${YELLOW}=== WorldEngine — build + test ===${NC}"
echo ""

echo -e "${YELLOW}[1/2] Building WorldEngine.Sim ...${NC}"
dotnet build WorldEngine.Sim/WorldEngine.Sim.csproj -c Debug --nologo -v q
echo -e "${GREEN}      Build OK${NC}"
echo ""

echo -e "${YELLOW}[2/2] Running test suite ...${NC}"
dotnet test WorldEngine.Tests/WorldEngine.Tests.csproj \
  -c Debug \
  --nologo \
  --logger "console;verbosity=normal" \
  -- xunit.maxParallelThreads=1
echo ""
echo -e "${GREEN}=== All done ===${NC}"
