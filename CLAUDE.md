# CLAUDE.md — World Engine Project Instructions

This file is read at the start of every Claude Code session. Follow everything here without being asked.

---

## What This Project Is

A procedural world generation and history simulation engine. The simulation generates a world, runs history forward in time (potentially thousands of years), and produces a queryable history log. Players can observe history, author world events (God Mode), and control characters (Spotlight).

The primary audience is worldbuilders and writers, not traditional gamers. The core product is the richness and coherence of the generated history, not gameplay challenge.

**Companion documents (read these before any significant implementation work):**
- `docs/implementation_decisions_v0.3.md` — all architectural decisions with rationale
- `docs/architecture_decision_records.md` — ADR quick-reference (why the codebase is structured as it is)
- `docs/design_session_decisions.md` — tile layout, world gen algorithms, env sim, UI boundary decisions (DS-A through DS-D)
- `docs/mvp_spec.md` — milestone and epic definitions
- `docs/interface_contracts.md` — critical C# interface signatures (TileData, WorldSnapshot, etc.)
- `docs/implementation_plan_m1.md` — phase ordering and story-level implementation guide

**For coding sessions — read the active phase doc:**
- `docs/phases/phase_1_foundation.md` — Epic 1.1 stories, tests, file paths
- `docs/phases/phase_2_tile_structures.md` — Epic 1.2 stories, tests, file paths
- `docs/phases/phase_3_world_gen.md` — Epic 1.3 stories, tests, file paths
- `docs/phases/phase_4_sim_loop.md` — Epic 1.4 stories, tests, file paths
- `docs/phases/phase_5_environmental.md` — Epic 1.5 stories, tests, file paths
- `docs/phases/phase_6_events.md` — Epic 1.6 stories, tests, file paths
- `docs/phases/phase_7_ui.md` — Epic 1.7 stories, manual tests
- `docs/phases/archive/` — completed phases moved here

**Reusable code patterns and test templates:**
- `docs/snippets/patterns.md` — command pattern, WorldRng, tile iteration, StateCache, etc.
- `docs/snippets/test_templates.md` — reproducibility test, unit/integration/thread-safety templates

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

### 4. Plain Data Commands and Events

`ICommand` implementations are sealed records with value-type fields only. No callbacks, no delegates, no references to mutable objects.

```csharp
// CORRECT
public sealed record MoveTo(EntityId EntityId, TileCoord Destination) : ICommand;

// WRONG
public sealed record MoveTo(EntityId EntityId, TileCoord Destination, 
    Action<WorldState> callback) : ICommand; // no callbacks
```

Same rule applies to `SimEvent` payloads.

### 5. Disk as System of Record

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

- **C# 12 / .NET 8** features are fine — use them where they make code clearer
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
- Spotlight mode / player character control (Milestone 2+)
- God Mode UI (Milestone 3)
- Full voxel rendering (post-Milestone 4)
- Modding/plugin system (post-Milestone 4)
- Multiplayer anything

When you encounter a hook point for a V2 feature (e.g., the magic intensity layer), implement the stub — generate the data, store it — but do not implement the behavior. Leave a `// V2: [feature name]` comment.

---

## Starting a New Session

At the start of each session:

1. Read this file
2. Read the active phase doc from `docs/phases/` (whichever phase is in progress)
3. Check `docs/interface_contracts.md` for any interfaces you'll be implementing against
4. Check existing code in the relevant project directories to understand what's already built
5. Load `docs/snippets/patterns.md` when you need code boilerplate

Do not assume continuity from a previous session. Read the code to understand what exists.

**When a phase is complete:** Move its doc from `docs/phases/` to `docs/phases/archive/`, update the Status field to `COMPLETE — [date]`.

---

## Definition of Done for a Story

A story is done when:
- Code compiles with zero warnings
- All tests pass
- The feature works as described in the story definition
- Any `// DECISION:` comments have been added for non-obvious choices
- SimConfig has entries for any new tunable constants
- The relevant interface contract (if any) is satisfied exactly
