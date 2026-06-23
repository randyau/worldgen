# World Engine

A procedural world generation and history simulation engine. It generates a world, runs thousands of years of history forward, and produces a queryable event log. The simulation is headless — the UI layer observes it; it doesn't drive it.

**Status: in active development (Milestone 2 complete, Milestone 3 in planning)**

---

## What it does

- Generates a tectonic and climate world (elevation, biome, moisture, temperature, resources)
- Runs a tick-based history simulation: characters form goals, build settlements, forge alliances, go to war, grieve, create art, migrate
- Records every meaningful event to a SQLite database
- Renders the world as a tile map with overlays, an event log, and a tile inspector

The target audience is worldbuilders and writers. The output is a rich, coherent history — not a game to win.

---

## Project structure

```
WorldEngine.Sim/        # Headless simulation core — no UI references
WorldEngine.UI/         # MonoGame + Myra frontend (Windows)
WorldEngine.Tests/      # xUnit test suite
config/                 # All simulation constants (TOML)
docs/                   # Design documents and architecture records
scripts/                # Build, publish, and analysis scripts
```

The sim and UI are intentionally decoupled: `WorldEngine.Sim` is a pure library with no rendering dependencies. `WorldEngine.UI` references it; never the reverse.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (for the UI — MonoGame targets Win/DirectX). The sim and tests build and run on Linux/WSL2.

---

## Building

**Sim + tests (Linux / WSL2):**
```bash
scripts/build.sh
```
This builds `WorldEngine.Sim` and runs the full test suite.

**UI (publish to Windows executable):**
```bash
scripts/publish-win.sh
```
Produces a self-contained `publish/win-x64/WorldEngine.UI.exe`. Run from Windows Explorer or PowerShell. No .NET install required on the Windows side.

If you already have .NET 8 Runtime on Windows and want a smaller output:
```bash
scripts/publish-win.sh --framework
```

**Full solution build (any platform with .NET 8):**
```bash
dotnet build WorldEngine.sln
dotnet test WorldEngine.Tests
```

---

## Running

Launch `WorldEngine.UI.exe` from the `publish/win-x64/` directory. It generates a world on startup and begins simulating immediately.

**First run:** a `world.db` SQLite file is created alongside the executable. Delete it before starting a fresh run — the sim appends to an existing database and will error on schema conflicts.

**Keyboard controls:**
- `Space` — pause/resume
- `1–5` — set simulation speed
- `B / E / T / M` — switch map overlay (Biome / Elevation / Temperature / Moisture)
- Click any tile — opens the tile inspector

---

## Configuration

All simulation constants live in `config/sim_config.toml`. No recompile needed — edit the file, restart the sim.

Key sections:

| Section | Controls |
|---|---|
| `[world_gen]` | World dimensions, tile size |
| `[world_gen.elevation]` | Tectonic intensity, mountain thresholds |
| `[world_gen.climate]` | Temperature bands, moisture |
| `[world_gen.resources]` | Deposit density and types |
| `[sim_loop.speed]` | TPS targets for each speed setting |
| `[events.gate]` | Which event types are suppressed before DB write |
| `[characters]` | Lifespan, needs decay, skill growth rates |
| `[utility]` | Action scoring weights |
| `[specialists]` | Population thresholds for Tier 2 character crystallization |
| `[resource_pressure]` | Food/water shortage thresholds, reach scaling |
| `[settlement_names]` | Prefix and suffix pools for generated settlement names |

**Ancestries and beasts** have their own files:
- `config/ancestries.toml` — the six playable ancestries (human, elf, dwarf, etc.), with spawn weights, personality biases, name pools, and cultural distance values
- `config/beasts.toml` — mythological beast species with biome ranges and behavior tuning

Any constant that affects simulation behavior belongs in config. Structural constants (enum values, save format version) stay in code.

---

## Code navigation

The project uses [SCIP](https://github.com/sourcegraph/scip) for symbol indexing. A post-commit hook regenerates `index.scip` automatically after each commit.

```bash
# Find where a type is defined
python3 scripts/scip-query.py defs TileData

# Find all files referencing an interface
python3 scripts/scip-query.py refs IWorldStateReadOnly

# List all defined types
python3 scripts/scip-query.py types
```

First-time setup:
```bash
git config core.hooksPath .githooks
dotnet tool restore
```

---

## Docs

Design decisions, architecture records, and interface contracts are in `docs/`. Start with:

- `docs/architecture_decision_records.md` — why the codebase is structured as it is
- `docs/implementation_decisions_v0.3.md` — all major technical decisions with rationale
- `docs/mvp_spec.md` — milestone definitions and current status
- `docs/interface_contracts.md` — key C# interface signatures
