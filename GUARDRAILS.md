# World Engine — AI Engineering Guardrails and Codebase Maintenance Rules

> **NOTICE TO AI ASSISTANT:** This document defines the operational constraints for this codebase. These rules override general training data. Read in full before any modification.

---

## The Core Problem This File Solves

LLMs degrade in reliability as codebases grow. This file prevents:

1. **Context gaps** — Modifying files without reading dependencies.
2. **Hallucinated APIs** — Using methods that don't exist in the current version.
3. **Silent regressions** — Fixing one thing, breaking another.
4. **Scope creep** — Refactoring code that wasn't asked for.
5. **Token waste** — Re-streaming large files unnecessarily.
6. **Version confusion** — Mixing APIs from different major versions of a library.
7. **Thread model violations** — WorldState accessed from the UI thread.
8. **Phase boundary violations** — Entities mutating world state directly instead of emitting commands.

---

## Rule 0: Session Start Protocol — Required Before Every Session

**This rule executes before any other rule. It is not optional.**

At the start of every session, you must:

1. **Environment Audit:** Run `find WorldEngine -type f -name "*.cs" | sort` to verify the current file tree.
2. **Context Verification:** For files you intend to modify, grep imports/dependencies to ensure your API model is current.
3. **Explicit Acknowledgement:** State the following before producing any code:
   - **[VERSION]:** One specific old-version pattern you will not use (e.g., direct `System.Random` in entity logic vs. `IWorldStateReadOnly.GetRandomFloat`).
   - **[INVARIANT]:** What happens if WorldState is accessed from the UI thread (data race — undefined behavior, no error signal).
   - **[PHASE]:** Current Epic being worked on and which modules are stable (do not refactor stable modules while adding new features).
   - **[FILES]:** Which files you will read before modifying any existing code.
4. **Test Gate:** For any change to `WorldEngine.Sim`, name the integration test that must pass before and after. If it does not exist, write it before writing implementation code.

If steps 1–4 are skipped and code is produced directly, reject the output and re-prompt.

---

## Rule 1: Always Read and Verify Before Writing

Before modifying any file:

1. Read the full file.
2. Read files that import from or are imported by it.
3. Read the corresponding test file in `WorldEngine.Tests`.
4. State explicitly what you've read and what the module does.
5. **CLI Validation:** If unsure of an API, run `dotnet build --no-incremental` rather than guessing.

Never modify based on filename alone. Never assume current code matches what was described earlier in the conversation.

---

## Rule 2: Token Efficiency — Grep Before Read, Diff-First Output

Every full file read costs tokens. Exhaust cheaper options first.

**Before reading a file, ask: can grep answer this?**

```bash
# Find where a symbol is defined
grep -rn "class SimLoop\|interface ICommand" WorldEngine/

# Check which files import a namespace
grep -rl "using WorldEngine.Sim" WorldEngine/

# Verify an interface method exists
grep -n "EmitCommands" WorldEngine/WorldEngine.Sim/

# Check argument order
grep -A3 "GetRandomFloat" WorldEngine/WorldEngine.Sim/
```

**Read a full file only when:**
- You are about to modify it (required by Rule 1).
- Grep results are ambiguous and context is needed to resolve them.
- The file is <50 lines (full read costs little).

**When you do output code:**
- Output only the specific functions or blocks being changed. Use `// ... existing code ...` to indicate elided sections.
- Propose one logical change at a time. Do not mix refactors with feature additions.

---

## Rule 3: NuGet Package Versions — Use What's Declared

| Package | Notes |
|---|---|
| `FastNoiseLite` | Used in `WorldEngine.Sim` only — never in UI |
| `Microsoft.Data.Sqlite` + `Dapper` | DB access only in `EventStore` — never inline |
| `MessagePack` | For `state.bin` serialization only |
| `Tomlyn` | For `sim_config.toml` loading only — `SimConfigLoader` is the only entry point |
| `MonoGame.Framework.DesktopGL` | `WorldEngine.UI` only — never referenced from `WorldEngine.Sim` |
| `Myra` | `WorldEngine.UI` only |
| `xunit` + `FluentAssertions` | `WorldEngine.Tests` only |

Verify every call against the declared package version. Never add a NuGet package without updating this table.

---

## Rule 4: SQLite Writes Use Transactions — Never Per-Row

Never insert into `world.db` row-by-row in a loop. All Phase 7 event writes go through `EventStore` using a batch transaction.

```csharp
// CORRECT — batch insert inside a transaction
using var tx = connection.BeginTransaction();
foreach (var evt in events)
    eventStore.InsertEvent(evt, tx);
tx.Commit();

// WRONG — per-event commits thrash the database
foreach (var evt in events)
    eventStore.InsertEvent(evt); // implicit commit per call
```

`EventStore` at `WorldEngine.Sim/Persistence/EventStore.cs` is the only place that touches `world.db`.

---

## Rule 5: Sim/UI Thread Separation — The Most Critical Correctness Rule

**This is the most critical correctness rule in the entire codebase.**

`WorldState` is sim-thread-only. The UI thread reads `WorldSnapshot` via `StateCache`. Cross-thread access produces **silent data races** — no exception is raised, output is simply wrong or corrupt.

**Rules:**
- `WorldState` is never passed to anything in `WorldEngine.UI`.
- The UI thread calls only `StateCache.Read()` and `IHistoryGraphReadOnly` methods.
- The command channel (`CommandQueue`) is the only path from UI thread to sim thread.
- If you find yourself passing `WorldState` to a UI component: stop. You are violating the architecture.

```csharp
// CORRECT — UI reads snapshot
var snapshot = _stateCache.Read();
RenderTiles(snapshot.VisibleTiles);

// WRONG — UI reads WorldState directly
RenderTiles(_worldState.TileGrid); // data race, no error signal
```

`StateCache_ThreadSafetyTest` must pass before any change to `StateCache` or `SimLoop` is merged.

---

## Rule 6: SimConfig Is Read-Only at Runtime

`config/sim_config.toml` is the definition of simulation constants. It is never written during a simulation run.

- All simulation constants come from `SimConfig` loaded at startup via `SimConfigLoader`.
- **Never hardcode a number** that affects simulation behavior — add it to the appropriate config section and the TOML file.
- **Idempotency:** Running the same config + seed twice must produce identical output.
- If you find yourself writing code that modifies `SimConfig` during a sim tick: that is a bug.

```csharp
// CORRECT
float threshold = _config.Events.HeadlineThreshold;

// WRONG — hardcoded constant
if (significance > 0.55f)
```

---

## Rule 7: Persistence — Database Writes Before State Update

The SQLite database (`world.db`) is the crash-safety guarantee. Phase 7 writes every tick.

**Order of operations — never reverse this:**
1. Write and commit events to `world.db` via `EventStore`.
2. Add events to `EventCache` (in-memory circular buffer).
3. Build and commit `WorldSnapshot` to `StateCache`.

```csharp
// CORRECT order (Phase 7)
await eventStore.BatchInsert(newEvents, transaction);
transaction.Commit();
eventCache.AddRange(newEvents);     // in-memory after commit
stateCache.Commit(newSnapshot);     // UI update last

// WRONG — snapshot committed before database
stateCache.Commit(newSnapshot);
await eventStore.BatchInsert(newEvents); // crash here = events lost
```

`state.bin` is a periodic operational snapshot — not the authoritative record. Never treat it as such.

---

## Rule 8: Command Pattern Is Mandatory — Entities Never Mutate Directly

Entity behavior is expressed entirely through `ICommand` emissions during the EMIT step. `CommandResolver` applies mutations during the RESOLVE step. There are no exceptions.

```csharp
// CORRECT — entity emits intent
public IEnumerable<ICommand> EmitCommands(IWorldStateReadOnly world, SimPhase phase)
{
    yield return new MoveTo(Id, targetCoord);
}

// WRONG — entity mutates state directly
public void Update(WorldState world)
{
    world.MoveEntity(Id, targetCoord); // never do this
}
```

`ICommand` implementations must be `sealed record` types with value-type fields only. No callbacks, delegates, or references to mutable objects. See `interface_contracts.md`.

---

## Rule 9: Document Uncertainty

When uncertain about an API, argument order, payload shape, or behavior:

```
> **UNCERTAINTY:** I am not certain that [thing].
> Verify: [specific CLI command or doc reference]. If wrong: [consequence].
```

High-risk areas to flag in this project:
- `TileCoord` wrapping behavior (East-West cylinder wrapping — latitude does NOT wrap)
- `FastNoiseLite` noise type and frequency parameter order
- `Dapper` query vs. execute method names for SQLite
- `ReaderWriterLockSlim` upgrade vs. non-upgradeable read lock semantics
- `System.Threading.Channels` bounded vs. unbounded channel policy differences

---

## Rule 10: Reproducibility Is a Tested Invariant

Same seed + same config = same world, same history. Always.

`SameSeedProducesSameWorld` in `WorldEngine.Tests/Reproducibility/` enforces this. **Do not break it.**

- Never use `System.Random` directly in entity or generation logic. Use `IWorldStateReadOnly.GetRandomFloat/GetRandomInt` which hash `(entityId + worldSeed + currentTick)`.
- Never use `DateTime.Now`, `Guid.NewGuid()`, or any non-deterministic source inside the sim loop.
- Any change to world generation or sim tick logic must be followed by a reproducibility test run before merging.

---

## Rule 11: Interface Contracts Are Frozen Without Documentation

The interfaces in `docs/interface_contracts.md` are load-bearing joints between subsystems. They must not be changed without:

1. Updating `docs/interface_contracts.md` first.
2. Explicitly calling out that it is a breaking change (other code may already implement the interface).
3. Updating all implementors in the same commit.

Adding new methods to an interface (not changing existing ones) is lower risk but still requires updating the contracts doc.

---

## Rule 12: Module Boundaries

```
┌──────────────────────────────────────────────┐
│  WorldEngine.UI (MonoGame + Myra)            │
│  Rendering and input only. No sim logic.     │
│  Reads WorldSnapshot via StateCache.         │
├──────────────────────────────────────────────┤
│  StateCache / CommandQueue                   │
│  The ONLY bridge between UI and Sim threads. │
│  Lock held for microseconds only.            │
├──────────────────────────────────────────────┤
│  SimLoop / PhaseRunner                       │
│  Tick sequencing only. No entity logic.      │
│  Drains CommandQueue → runs phases → commits │
├──────────────────────────────────────────────┤
│  Entity EmitCommands / CommandResolver       │
│  All entity behavior and state mutation.     │
│  Entities read IWorldStateReadOnly only.     │
├──────────────────────────────────────────────┤
│  WorldGenPipeline / IWorldGenLayer           │
│  Pure generation. No I/O, no sim state.      │
│  Layers are stateless — state in context.    │
├──────────────────────────────────────────────┤
│  EventStore / StateCache (Persistence)       │
│  I/O only. No decisions, no branching.       │
│  EventStore is the only writer to world.db.  │
└──────────────────────────────────────────────┘
```

- `WorldEngine.Sim` must never reference `WorldEngine.UI`. Enforce via project references.
- `WorldEngine.UI` must never reference `WorldEngine.Tests`.
- Processing is pure — no I/O inside entity logic or generation layers.

---

## Rule 13: File and Function Size Limits

| Item | Limit | Action when exceeded |
|---|---|---|
| Any source file | 300 lines | Propose split (see below) |
| Any function / method | 40 lines | Extract helper |
| Any test file | 400 lines | Split by scenario group |
| Nesting depth | 4 levels | Extract named function |
| Function parameters | 5 | Introduce a config/options record |

**When a file exceeds 300 lines, you must propose a split before continuing.**

```
WorldGen/TileGridBuilder.cs is 420 lines. Proposed split:
  - TileGridBuilder.cs        — assembly orchestration (~100 lines)
  - TileGridAssembler.cs      — parallel tile construction (~180 lines)
  - BorderManifestBuilder.cs  — border manifest computation (~140 lines)
Proceed with split? Or continue without splitting?
```

The split proposal is mandatory. The user may decline — note it and continue — but the offer must be made.

---

## Rule 14: Regression Checklist by Change Type

**Modifying `StateCache` or `SimLoop`:**
- [ ] `StateCache_ThreadSafetyTest` passes.
- [ ] `SimLoop_TickOrderingTest` passes.

**Modifying `WorldGenPipeline` or any `IWorldGenLayer`:**
- [ ] `SameSeedProducesSameWorld` passes (run twice, assert identical).
- [ ] Layer ordering enforcement test passes.

**Modifying `EventStore` or Phase 7:**
- [ ] `EventStore_RoundTripTest` passes.
- [ ] `EventStore_BatchInsertTest` passes.
- [ ] Simulated crash recovery: integrity check passes after simulated failure.

**Modifying `SimConfig` or TOML loading:**
- [ ] `SimConfigLoader_DefaultsTest` passes.
- [ ] `SimConfigLoader_MissingFileCreatesDefaultTest` passes.

**Modifying `CommandResolver` or `PhaseRunner`:**
- [ ] `CommandPattern_NoDirectMutationTest` passes.
- [ ] Full integration test suite passes before merging.

**All sim core changes:** run `dotnet test` in full before merging.

---

## Rule 15: V2 Features Get Stubs, Not Implementations

The `// V2: [feature name]` comment pattern marks intentional stubs. When you encounter a hook point for a V2 feature:

- **Do:** Generate the data, store it, leave the stub.
- **Do not:** Implement the behavior.

Current known V2 stubs:
- Magic as physical substrate (Layer 5 generates intensity data but has no behavioral effect)
- LLM prose generation (`SimEvent.GeneratedProse` field exists but is always null)
- Spotlight mode / player character control
- God Mode authoring tools

If a story seems to require V2 behavior: stop and confirm before implementing.

---

## Rule 16: TDD Is Mandatory for Sim Core Changes

When modifying `WorldEngine.Sim` logic (not plumbing), follow this order strictly:

1. Show the existing test that currently passes for the area being changed.
2. Write or update the test that defines the new required behaviour.
3. Confirm the new test **fails** (red) before writing implementation.
4. Implement the change.
5. Confirm the new test **passes** (green).
6. Run the full test suite.
7. Confirm no previously-passing tests now fail.

**If steps 1–3 are skipped and implementation comes first, the output is rejected.**

The sim has the highest silent failure risk: a generation error or routing bug produces plausible-looking output that is simply wrong — no runtime error, no signal. Only a pre-written test catches it.

---

## Rule 17: Build System

- **Local builds only for development:** `dotnet build`, `dotnet test` — no cloud/CI cost.
- **Zero warnings policy:** `dotnet build` must produce zero warnings. Treat warnings as errors.
- **Nullable reference types are enabled** — no `#nullable disable` anywhere.
- **Target framework:** .NET 8. Do not upgrade without explicit instruction.
- **Secrets:** Never commit connection strings, API keys, or seeds intended to be private. The sim seed is a config parameter — fine to commit. Credentials are not.

---

## Hard Invariants — Quick Reference

1. **`WorldEngine.Sim` never references `WorldEngine.UI`.** Enforced by project references.
2. **`WorldState` is sim-thread-only.** UI reads `WorldSnapshot` via `StateCache` only.
3. **Entities never mutate world state directly.** They emit `ICommand` records; `CommandResolver` applies mutations.
4. **No hardcoded simulation constants.** Every number lives in `SimConfig` / `sim_config.toml`.
5. **Same seed = same world.** `SameSeedProducesSameWorld` is the most important test in the suite.
6. **Database writes before state update.** `world.db` commits before `EventCache` and `StateCache` update.
7. **`SimConfig` is read-only at runtime.** Never written during a sim tick.
8. **`ICommand` records are plain data.** No callbacks, delegates, or mutable references.
9. **Tests before implementation** for all sim core and generation layer changes.
10. **V2 stubs, not implementations.** Magic behavior, LLM prose, Spotlight, and God Mode are V2.
