# Phase 3.6 — Save / Resume

**Milestone:** 3 — Narrative Exploration
**Status:** COMPLETE — 2026-06-26
**Goal:** Players can save a running simulation to disk and resume it in a later session.
Without save/resume, M3 is a demo — any interesting world state is lost when the
application closes. A 1000-year run takes ~5 minutes at target TPS; players need to be
able to return to their world.

---

## Scope

**In scope for M3:**
- Save world state to a single directory (`.worldsave/`)
- Resume from save: restore full `WorldState`, re-attach UI
- Single save slot per world (the active world auto-saves; no named save management)
- Auto-save every N minutes (configurable)
- Manual save via `Ctrl+S`

**Out of scope for M3 (M4):**
- Multiple named save slots
- Save browser UI
- Cloud sync
- Save migration across sim version changes (best-effort only in M3)

---

## Save format

A save directory contains:
```
worldsave/
  meta.json          — seed, world dimensions, year, tick, version string
  world.db           — event store (already on disk; just included in bundle)
  state.bin          — serialized WorldState (all mutable sim state)
  config_snapshot/   — copy of sim_config.toml and ancestries.toml at save time
```

`state.bin` uses `System.Text.Json` with source-generated serialization contexts for
performance and AOT safety. The sim core types are already records/plain data — they
serialize cleanly.

---

## Epic 3.6.1 — WorldState Serialization

**Goal:** Serialize and deserialize the full mutable `WorldState` to/from `state.bin`.

### What needs to serialize

| Component | Notes |
|-----------|-------|
| `TileGrid` | Static after world gen — store seed + world gen params and regenerate on load, OR include in state.bin. Regeneration is preferred (smaller file, deterministic). |
| `SeasonalProfiles` | Regenerate from world gen params |
| `ResourceRegistry` | Serialize — resource deposits are permanent |
| `Civilizations` | Full dict including `CityTerritories`, `BorderTension`, wars, traits |
| `Settlements` | All `SettlementStub` records |
| `TerritoryMap` | The `Dictionary<TileCoord, TileCoord>` |
| `ImprovementMap` | The `Dictionary<TileCoord, TileImprovement>` |
| `Ruins` | `Dictionary<TileCoord, RuinRecord>` |
| `Entities` | All live entities (Tier1Character, Tier2Character, LegendaryBeast) |
| `Relationships` | Full `RelationshipGraph` |
| `CurrentYear`, `CurrentTick`, `CurrentSeason` | Sim clock |
| `GlobalTemperatureAnomaly`, drift params | Environmental state |
| `ActiveTileDisasters`, `ActiveDroughts` | Live disaster state |
| `BeastEmergenceSchedule` | Pending beast spawns |
| `NameOrdinals` | Name collision tracking |
| `ActiveFounders` | Set of EntityId |

**3.6.1.1 — WorldStateDto**

Define a `WorldStateDto` (plain record with only JSON-serializable types) that mirrors
`WorldState`. The sim types use `TileCoord`, `CivId`, `EntityId` etc. — these need
`JsonConverter` implementations or string-key adaptors for dictionary keys.

Add `[JsonSerializable]` source-gen context: `WorldStateSerializerContext`.

**3.6.1.2 — WorldStateSaver**

```csharp
public static class WorldStateSaver
{
    public static void Save(WorldState world, string saveDir);
    public static WorldState Load(string saveDir, SimConfig cfg);
}
```

`Save`: serialize `WorldStateDto` to `state.bin` via source-gen JSON writer.
Also writes `meta.json` with version string (checked on load for compatibility).

`Load`: deserialize `WorldStateDto`, reconstruct `WorldState`, regenerate `TileGrid`
and `SeasonalProfiles` from the saved `WorldConfig` (seed + dimensions).

**3.6.1.3 — Round-trip test**

```csharp
[Fact]
public void SaveLoad_ProducesIdenticalState()
{
    // Run sim for 200 ticks
    // Save to temp dir
    // Load from temp dir
    // Assert: year, tick, settlement count, entity count, civ count all match
    // Assert: CityTerritories tile count per city matches
    // Assert: ImprovementMap entry count matches
}
```

---

## Epic 3.6.2 — Auto-Save and Manual Save

**Goal:** Sim auto-saves on a configurable interval; player can trigger manual save.

### Stories

**3.6.2.1 — Auto-save in SimLoop**

`SimLoop` gains an `AutoSaveIntervalTicks` config (default: every 960 ticks = once per
year at 16 ticks/year × 60 years). After each interval, enqueue a save on a background
`Task` (fire-and-forget; never block the sim thread). Log save completion.

Config addition:
```toml
[sim]
auto_save_interval_ticks = 960   # save every ~60 in-game years
auto_save_dir = "worldsave"
```

**3.6.2.2 — Manual save (Ctrl+S)**

`Game1.HandleInput` detects `Ctrl+S` and enqueues a `SaveWorld` UI command. The game
loop resolves this by calling `WorldStateSaver.Save(...)` on a background task.
Shows a brief "Saving..." overlay label that disappears after 2 seconds.

**3.6.2.3 — Resume on startup**

At startup, before showing the world-gen screen: check if `worldsave/meta.json` exists.
If it does, show a "Resume saved world (Year N)?" prompt with [Resume] and [New World]
buttons. [Resume] loads from the save dir and skips world gen; [New World] discards the
save and proceeds as normal.

**3.6.2.4 — Save purged on N (new world)**

`ResetToNewWorld()` deletes `worldsave/` directory in addition to `world.db`. The new
world starts with no prior save.

---

## Definition of Done

- `Ctrl+S` saves to `worldsave/`; the save directory is a complete resumable snapshot
- Resuming from save restores the exact year, all settlements, entities, and territory
- Auto-save fires every 60 in-game years without sim thread stutter
- The round-trip test passes for a 200-tick run
- Startup shows resume prompt when a save exists
- New world (`N`) clears the save directory
- All tests pass
