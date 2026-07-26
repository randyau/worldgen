# CLAUDE.md — World Engine Project Instructions

This file is read at the start of every Claude Code session. Follow everything here without being asked.

---

## What This Project Is

A procedural world generation and history simulation engine. The simulation generates a world, runs history forward in time (potentially thousands of years), and produces a queryable history log. Players can observe history, author world events (God Mode), and control characters (Spotlight).

The primary audience is worldbuilders and writers, not traditional gamers. The core product is the richness and coherence of the generated history, not gameplay challenge.

**Companion documents (read these before any significant implementation work):**
- `docs/roadmap.md` — **forward source of truth** for milestone/phase planning (M6+); mvp_spec.md is frozen history
- `docs/implementation_decisions_v0.3.md` — all architectural decisions with rationale
- `docs/architecture_decision_records.md` — ADR quick-reference (why the codebase is structured as it is)
- `docs/design_session_decisions.md` — tile layout, world gen algorithms, env sim, UI boundary decisions (DS-A through DS-D)
- `docs/mvp_spec.md` — milestone and epic definitions
- `docs/interface_contracts.md` — **index only** — links to 4 split files; load only the relevant one
- `docs/implementation_plan_m1.md` — M1 phase ordering and story-level guide (archived reference)

**Current milestone status:** M9 (Created-Object Unification & Economic Depth) COMPLETE (2026-07-26) — phases 9.0–9.2 shipped; see `docs/phases/archive/m9_created_object_unification.md` for close-out notes (trade-network topology and 3 inert `bonus_*` keys deliberately left out of scope). Next up: **M10 — Worldgen Preview & Modding**; see `docs/roadmap.md` § "M10". Milestones were renumbered on 2026-07-23 when M8 was inserted (old M8→M9, M9→M10, M10→M11) — see `docs/roadmap.md`.

**For coding sessions — read the active phase doc:**
- `docs/phases/m8_ui_framework_rewrite.md` — **M8 index; read this first**, then only the specific `docs/phases/m8_phaseN_*.md` for the phase in progress (8.0 → 8.5, sequential)
- `docs/phases/archive/` — all M1–M7 phases archived here for reference
- `docs/testing/runbook_m1.md` — M1 manual test runbook (reference for regression testing)

**Reusable code patterns and test templates:**
- `docs/snippets/patterns.md` — command pattern, WorldRng, tile iteration, StateCache, etc.
- `docs/snippets/test_templates.md` — reproducibility test, unit/integration/thread-safety templates

---

## Token Efficiency

### Model routing
Use cheap models for navigation; reserve Sonnet for writing/editing code:
- **Exploration and search:** spawn an `Explore` subagent, or pass `--model haiku` to one-off reads
- **Implementation:** stay on Sonnet
- Never load a large file speculatively — locate the symbol with SCIP first, then read only the relevant lines

### Bash searches — always exclude worktrees and build output
```bash
# Always add these exclusions or you get duplicate results from mirrored worktrees
find . -name "*.cs" ! -path "*worktrees*" ! -path "*/obj/*" ! -path "*/Vendor/*"
grep -r "Symbol" --include="*.cs" . --exclude-dir=obj --exclude-dir=Vendor --exclude-dir=worktrees
```

### Generated docs — trust without re-checking
The post-commit hook regenerates three docs automatically after every commit:
- `docs/codebase_map.md` — derived from XML doc summaries; every source file listed with description
- `docs/config_reference.md` — derived from `sim_config.toml`; all 200 config keys with C# paths
- Enum tables inside `docs/queries/event_log_queries.md` — derived from `WorldEngine.Sim` source

These are always current after any commit. Read them directly — do not grep or re-derive by hand.
`scripts/doc-check.py` enforces freshness; drift fails `scripts/test-fast.sh`.

### Codebase map
Before running `find`, check `docs/codebase_map.md` — every source file is listed with a one-line description. Often you can skip the filesystem scan entirely.

### Interface contracts — split by domain
`docs/interface_contracts.md` is now an index. Load only what you need:
- `interface_contracts_tiles.md` — TileData, flag enums, disasters, resources
- `interface_contracts_core.md` — IEntity, ICommand, IWorldStateReadOnly, StateCache, PendingEvent
- `interface_contracts_snapshot.md` — WorldSnapshot, SettlementStub/Snapshot, Civilization, AncestryConfig
- `interface_contracts_events.md` — SimEvent, EventType ranges, IHistoryGraphReadOnly, enumerations

---

## Code Intelligence (SCIP)

This project uses [SCIP](https://github.com/sourcegraph/scip) for compact, queryable code symbol navigation. A `post-commit` hook regenerates `index.scip` (a 1–2 MB binary index) automatically after every commit. The index is git-ignored.

**At session start, prefer SCIP queries over `grep` for symbol navigation:**

```bash
# Find where a type/method is defined
python3 scripts/scip-query.py defs TileData

# Find all files that reference a symbol
python3 scripts/scip-query.py refs IWorldStateReadOnly

# List all defined types
python3 scripts/scip-query.py types

# Find files that reference an interface (best-effort impl search)
python3 scripts/scip-query.py impls IWorldGenLayer

# Index statistics
python3 scripts/scip-query.py stats
```

**If `index.scip` is missing** (fresh clone or hooks not yet run):
```bash
dotnet tool restore           # installs scip-dotnet from .config/dotnet-tools.json
scip-dotnet index WorldEngine.sln --skip-dotnet-restore
```

**First-time setup on a new machine:**
```bash
git config core.hooksPath .githooks   # enables the post-commit hook
dotnet tool restore                   # installs scip-dotnet
```

The `scripts/scip_pb2.py` file is the compiled protobuf binding for the SCIP format; regenerate it with `python3 scripts/scip-query.py --setup` if it ever becomes stale after a proto update.

---

## Project Structure

```
WorldEngine/
├── WorldEngine.Sim/        # Headless simulation core — NO UI references ever
├── WorldEngine.UI/         # MonoGame + Myra frontend
├── WorldEngine.Tests/      # xUnit test suite
├── config/
│   └── sim_config.toml     # All simulation constants — never hardcode numbers
└── docs/                   # All design and specification documents
```

**The most important rule in this codebase:** `WorldEngine.Sim` must never reference `WorldEngine.UI` or any UI/rendering library. The sim runs completely headless. Enforce this via project references — `WorldEngine.UI` references `WorldEngine.Sim`, never the reverse.

---

## Mandatory Patterns

These patterns are non-negotiable. Do not deviate without explicit instruction.

### 1. Command Pattern for All Entity Behavior

Entities never mutate world state directly. They emit `ICommand` records during the EMIT step. World state mutates only during the RESOLVE step via `CommandResolver`.

```csharp
// CORRECT
public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
{
    yield return new MoveTo(Id, targetCoord);
}

// WRONG — never do this
public void Update(WorldState world)
{
    world.MoveEntity(Id, targetCoord); // direct mutation
}
```

### 2. All Simulation Constants in SimConfig

Never hardcode a number that affects simulation behavior. Every threshold, rate, weight, and probability lives in `SimConfig` loaded from `config/sim_config.toml`.

```csharp
// CORRECT
float threshold = _config.Events.HeadlineThreshold;

// WRONG
if (significance > 0.55f) // hardcoded
```

If you need a new constant, add it to the appropriate config section and the TOML file with a comment explaining what it controls.

### 3. WorldState is Sim-Thread-Only

`WorldState` is never accessed from the UI thread. The UI reads `WorldSnapshot` via `StateCache`. If you find yourself passing `WorldState` to anything in `WorldEngine.UI`, stop — you're violating the architecture.

### 4. UI Interaction Is Decoupled from Sim Tick Cadence

The sim can be paused, running at any speed, or between ticks at any moment. A user action's **visible effect** — a panel opening/closing, a button's highlight state, anything that is the direct consequence of a click/key rather than a reflection of sim data — must take effect on the very next render frame, unconditionally. It must never be gated behind "a new `WorldSnapshot` arrived," because while paused no new snapshot ever arrives and the UI appears to hang.

Split any per-frame `Update`/`Refresh` method in two:
- **Interaction state** (open/closed, highlighted/not, anything a click just changed): update every render frame, unconditionally.
- **Displayed data** (event log rows, character stats, anything read from `WorldSnapshot`): fine to update only when a fresh snapshot arrives — that's genuinely tick-linked.

```csharp
// CORRECT — visibility/highlight sync runs every frame regardless of tick cadence
_workspace?.SyncVisibility();
_panelMenuBar?.RefreshHighlights();

if (!ReferenceEquals(snapshot, _lastSnapshot))
{
    // WRONG to put SyncVisibility()/RefreshHighlights() here — while paused this
    // block never runs again, so a Hide() click just sits there until the next tick.
    _workspace.RefreshVisible(); // OK here: rebuilds *data-driven* panel content
}
```

Also: a panel opened for the first time must have its content populated immediately at open
time (using the already-bound context), not wait for the next gated data refresh — otherwise it
renders empty until a tick happens to land.

### 5. Plain Data Commands and Events

`ICommand` implementations are sealed records with value-type fields only. No callbacks, no delegates, no references to mutable objects.

```csharp
// CORRECT
public sealed record MoveTo(EntityId EntityId, TileCoord Destination) : ICommand;

// WRONG
public sealed record MoveTo(EntityId EntityId, TileCoord Destination, 
    Action<WorldState> callback) : ICommand; // no callbacks
```

Same rule applies to `SimEvent` payloads.

### 6. Disk as System of Record

The SQLite database (`world.db`) is always current — Phase 7 writes every tick. `state.bin` is the operational snapshot written periodically. Never treat in-memory state as the authoritative record for anything that needs to survive a crash.

---

## How to Handle Ambiguity

When the docs don't answer a question:

1. **Check the design doc and implementation decisions doc first.** The answer is usually there.

2. **If genuinely unspecced:** Make the simplest reasonable choice, implement it, and leave a `// DECISION: [description of choice made]` comment at the decision point. Do not block on ambiguity — make a call and flag it.

3. **If the choice affects a cross-cutting concern** (persistence format, interface signatures, thread model): stop and ask rather than guessing. These are expensive to undo.

4. **Prefer reversible over irreversible.** If two approaches are equally plausible, pick the one that's easier to change later.

---

## Code Style

- **C# 12 / .NET 10** features are fine — use them where they make code clearer (the solution targets `net10.0`; requires the .NET 10 SDK, pinned in `global.json`)
- **Records** for immutable data, **sealed classes** for entity types, **interfaces** for contracts
- **Primary constructors** acceptable for simple dependency injection
- **Pattern matching** preferred over long if-else chains for event type switching
- **Nullable reference types enabled** — no `#nullable disable`
- **Async/await** only at the UI boundary and persistence layer — sim core is synchronous
- XML doc comments on all public interfaces and their methods
- Internal implementation does not need comments unless the logic is non-obvious

## Naming
- Interfaces: `IEntityName`
- Configs: `EntityNameConfig`  
- Commands: verb + noun, `MoveTo`, `ClaimArtifact`, `DeclareWar`
- Events: noun + past tense verb, `CharacterDied`, `SettlementFounded`, `WarDeclared`
- Layer results: `ElevationResult`, `ClimateResult` etc.

---

## Testing Requirements

Every Epic must have tests before it is considered complete. Minimum requirements per Epic:

- **Unit tests** for each non-trivial class: given known inputs, assert known outputs
- **Integration test** for the Epic's primary workflow end-to-end
- **Reproducibility test** where applicable: same seed + same inputs = same outputs

The reproducibility test is the most important test in the suite. Any change that breaks it is a regression.

**Architecture tests** live in `WorldEngine.Tests/Architecture/ArchitectureRuleTests.cs` and are enforced on every run. They check: ICommand sealed records, no delegate fields, no async outside Persistence/WorldGen, interface naming, Config namespace naming, and UI panel isolation. Do not add code that breaks these — they will fail `scripts/test-fast.sh`.

```csharp
[Fact]
public void SameSeedProducesSameWorld()
{
    var config = new WorldConfig { Seed = 12345, WidthKm = 1000, HeightKm = 800 };
    var world1 = WorldGenerator.Generate(config);
    var world2 = WorldGenerator.Generate(config);
    world1.Should().BeEquivalentTo(world2);
}
```

---

## What NOT to Build

Unless explicitly instructed, do not implement:

- LLM prose generation (V2 feature)
- Magic as physical substrate (V2 feature)
- Full voxel rendering (post-Milestone 4)
- Modding/plugin system (post-Milestone 4)
- Multiplayer anything

When you encounter a hook point for a V2 feature (e.g., the magic intensity layer), implement the stub — generate the data, store it — but do not implement the behavior. Leave a `// V2: [feature name]` comment.

---

## Starting a New Session

At the start of each session:

1. Read this file
2. Run `python3 scripts/scip-query.py stats` — confirms the SCIP index is fresh and tells you the document/symbol counts. If missing, run `scip-dotnet index WorldEngine.sln --skip-dotnet-restore` first.
3. Read the active phase doc from `docs/phases/` (whichever phase is in progress). M8 and M9 are complete (archived); the current milestone is **M10 — Worldgen Preview & Modding** — check `docs/roadmap.md` § "M10" and `docs/phases/` for whether a phase doc exists yet.
4. Use `docs/codebase_map.md` to orient yourself — one-line description of every source file; skip filesystem scans when possible. This file is generated per-commit and is current.
5. Check only the relevant `docs/interface_contracts_*.md` split file for interfaces you'll be implementing against.
6. Use `python3 scripts/scip-query.py defs <TypeName>` to locate types before reading files.
7. Load `docs/snippets/patterns.md` when you need code boilerplate.

Do not assume continuity from a previous session. Read the code to understand what exists.

**When a phase is complete:** Move its doc from `docs/phases/` to `docs/phases/archive/`, update the Status field to `COMPLETE — [date]`.

---

## Definition of Done for a Story

A story is done when:
- Code compiles with zero warnings
- All tests pass (including the 6 architecture rule tests in `ArchitectureRuleTests.cs`)
- `scripts/doc-check.py` exits 0 (run via `scripts/test-fast.sh` — generated-doc freshness gate)
- The feature works as described in the story definition
- Any `// DECISION:` comments have been added for non-obvious choices
- SimConfig has entries for any new tunable constants
- The relevant interface contract (if any) is satisfied exactly
