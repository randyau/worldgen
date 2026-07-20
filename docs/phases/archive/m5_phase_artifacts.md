# M5 Phase — Artifacts System

**Status:** COMPLETE — 2026-07-20. Built via 5 parallel workers (W0 foundation, W1 creation/forging/lifecycle, W2 covet/goals, W3 snapshot/UI/queries, W4 telemetry) + an orchestrator-added ArtifactDecayPhase destruction sink calibrated empirically (living artifacts bounded ~9 at year 1000, peak 14, vs 18-and-climbing without decay). 496 fast tests green. Move to archive/ per workflow.
**Scope (locked with user):** MVP (masterwork creation + registry + ownership transfer on death/conquest + persistence + snapshot + tests) **plus** battle/heroic-death forging, covet & goal-seeking, and UI inspector display. **God Mode placement is DEFERRED** (the `GodModeArtifactPlaced=9004` event type already exists but is not wired this phase).

Artifacts are legendary items that persist through history independently of their creator. They are created by exceptional character work (masterwork), major battles, or a legendary character's death in combat. They are owned by a character or a settlement, transfer ownership on death (inheritance or become Lost) and on conquest, can be destroyed, are coveted by ambitious characters, and are tracked in the history log and UI inspector.

---

## Existing scaffolding (already in the tree — do NOT recreate)

- `WorldEngine.Sim/Core/ArtifactId.cs` — `readonly record struct ArtifactId(long Value)` with `.New()`.
- `WorldEngine.Sim/Core/Enumerations.cs` — `EventType.ArtifactCreated=6001`, `ArtifactDestroyed=6002`, `GodModeArtifactPlaced=9004` (deferred), plus VerbClass mappings for 6001/6002.
- `Tier2Character.HasMasterwork` flag + the masterwork roll in `Tier2BehaviorPhase.TryEmitNotableWork` (`// V2: ARTIFACT` hook, ~line 151).
- `[artifacts]` config block drafted in `docs/config_future.md` (`base_generation_probability`, `notable_performance_threshold`, `covet_threshold`).

---

## SHARED CONTRACT (defined by W0 Foundation — all other workers build against these signatures)

### Data model — `WorldEngine.Sim/Entities/Artifacts/`

```csharp
public enum ArtifactCategory { Weapon, Armor, Regalia, Tome, Relic, Jewelry, Artwork }

public enum ArtifactOwnerKind { Character, Settlement, Lost }

// Lost = ownerless (resting in a ruin / wilderness) until re-claimed.
public readonly record struct ArtifactOwner(
    ArtifactOwnerKind Kind, long CharacterId, TileCoord SettlementTile)
{
    public static ArtifactOwner OfCharacter(EntityId id) => new(ArtifactOwnerKind.Character, id.Value, default);
    public static ArtifactOwner OfSettlement(TileCoord t) => new(ArtifactOwnerKind.Settlement, 0, t);
    public static readonly ArtifactOwner Lost = new(ArtifactOwnerKind.Lost, 0, default);
    public string Describe(...); // human-readable "Character #id" / "Settlement <tile>" / "Lost" for payloads
}

public sealed record Artifact(
    ArtifactId Id,
    string Name,
    ArtifactCategory Category,
    int CreatedYear,
    long CreatorId,          // EntityId.Value of creator char; 0 if world/battle-forged
    string CreatorName,
    string Origin,           // "masterwork" | "battle" | "heroic_death"
    float Quality,           // 0..1 power/property score; drives covet
    ArtifactOwner Owner,
    bool IsDestroyed = false,
    int DestroyedYear = 0);
```

### Registry — on `WorldState` + a static ops helper

`WorldState` gains: `public Dictionary<ArtifactId, Artifact> Artifacts { get; } = new();`
(Follows the existing raw-dictionary style of `Settlements`/`Civilizations`/`Ruins`.)

Static helper `WorldEngine.Sim.Entities.Artifacts.ArtifactRegistry`:
```csharp
static Artifact Create(WorldState w, string name, ArtifactCategory cat, int year,
                       long creatorId, string creatorName, string origin, float quality, ArtifactOwner owner);
static void SetOwner(WorldState w, ArtifactId id, ArtifactOwner owner);   // records replace-in-place
static void Destroy(WorldState w, ArtifactId id, int year);
static IEnumerable<Artifact> Active(WorldState w);                         // not destroyed
static IEnumerable<Artifact> OwnedByCharacter(WorldState w, EntityId id);
static IEnumerable<Artifact> InSettlement(WorldState w, TileCoord t);
```
`Create`/`SetOwner`/`Destroy` only mutate the registry — they do NOT emit events (the calling phase emits the `PendingEvent`). Artifacts are immutable records; transfer = `dict[id] = a with { Owner = ... }`.

### Naming — `ArtifactNameGenerator`

`WorldEngine.Sim/Entities/Artifacts/ArtifactNameGenerator.cs` — deterministic legendary-item names seeded from `WorldRng` (use `world.GetRandomFloat`/existing name-gen utilities; MUST be reproducible). Style: `"<Epithet> <Noun>"` e.g. "Dawnbreaker", "The Sundered Crown". Category-appropriate noun lists in config or a static table.

### Config — `ArtifactConfig` bound from `[artifacts]`

Move the block from `docs/config_future.md` into `config/sim_config.toml`, bind to `ArtifactConfig` under `SimConfig`, add to `SimConfigValidator`. Keys:
```toml
[artifacts]
base_generation_probability   = 0.05
notable_performance_threshold = 0.75
covet_threshold               = 0.6
battle_forge_probability      = 0.03   # chance a decisive battle forges an artifact
heroic_death_forge_probability= 0.10   # chance a legendary char's combat death forges one
lost_on_death_probability     = 0.35   # chance an owned artifact becomes Lost (vs inherited) on owner death
```

### Event payloads — append to `WorldEngine.Sim/Events/Payloads.cs`

```csharp
internal sealed record ArtifactCreatedPayload(
    long ArtifactId, string ArtifactName, string Category,
    long CreatorId, string CreatorName, string Origin, float Quality);
internal sealed record ArtifactTransferredPayload(
    long ArtifactId, string ArtifactName, string FromOwner, string ToOwner, string Reason); // "inheritance"|"conquest"|"claim"
internal sealed record ArtifactDestroyedPayload(
    long ArtifactId, string ArtifactName, string Cause);
```
Foundation adds `EventType.ArtifactTransferred=6003` to `Enumerations.cs` with an appropriate existing `VerbClass` (reuse the closest existing value, e.g. Movement/Transaction — match the enum that already exists; do NOT invent a new VerbClass).

### Snapshot contract (implemented by W3, declared here so others know the shape)

`ArtifactSnapshot(long Id, string Name, string Category, string Origin, float Quality, int CreatedYear, string CreatorName, string OwnerDesc, bool IsDestroyed)` in `WorldSnapshot.cs`; exposed as `IReadOnlyList<ArtifactSnapshot> Artifacts` on `WorldSnapshot`.

---

## WORKER BREAKDOWN

### W0 — Foundation (RUN FIRST, blocks all others)
Implement the entire **SHARED CONTRACT** section above: model files, `Artifacts` dict on `WorldState`, `ArtifactRegistry` ops, `ArtifactNameGenerator`, `ArtifactConfig` + toml + validator, `EventType.ArtifactTransferred=6003`, the three payload records. Unit tests: registry create/transfer/destroy, name-gen reproducibility (same seed → same names), config binds. Do NOT wire any behavior (creation/death/conquest) — that is W1. Commit.

### W1 — Creation, forging & ownership lifecycle  *(files: Tier2BehaviorPhase, CharacterBehaviorPhase, CivTracker.War, PhaseRunner, new ArtifactLifecyclePhase.cs)*
- Wire the masterwork hook (`Tier2BehaviorPhase` ~line 151): on `HasMasterwork` becoming true, `ArtifactRegistry.Create(... origin:"masterwork", owner: OfCharacter(creator) ...)` with a `Quality` derived from the work roll, and emit `PendingEvent(EventType.ArtifactCreated, ...)` with `ArtifactCreatedPayload`.
- Battle/heroic-death forging in `CivTracker.War.cs`: on a decisive battle roll `battle_forge_probability`; on a legendary (high-significance) character's combat death roll `heroic_death_forge_probability` → forge an artifact (`CreatorId=0`, origin "battle"/"heroic_death", owner = victor settlement or the fallen's settlement).
- Ownership transfer: on character death (find the death site in `CharacterBehaviorPhase`), for each artifact owned by the deceased, roll `lost_on_death_probability` → `ArtifactOwner.Lost`, else transfer to their settlement; emit `ArtifactTransferred` ("inheritance"). On settlement conquest (`CivTracker.War`), transfer all artifacts owned by the settlement to the conqueror settlement; emit `ArtifactTransferred` ("conquest").
- Fold lifecycle/destruction/claim logic into the existing phases (`Tier2BehaviorPhase`, `CharacterBehaviorPhase`, `CivTracker.War`). **Do NOT edit `PhaseRunner.cs`** — W4 (telemetry) owns that file. Lost artifacts in a settlement that becomes a Ruin stay Lost.
- Tests: masterwork creates+owns; conquest transfers; death inherits-or-loses (both branches); reproducibility.

### W2 — Covet & goal-seeking  *(files: GoalManager, UtilityScorer, GoalData; read-only use of ArtifactRegistry)*
- Ambitious characters (high Ambition) evaluate `ArtifactRegistry.Active(world)` for artifacts with `Quality >= covet_threshold` that they do not own; add a "covet artifact" goal (extend `GoalType`/goal object to reference `ArtifactId`) scored via `UtilityScorer`.
- A satisfied covet goal for an artifact owned by another character/settlement should raise conflict pressure — expose the desire so the existing war/rivalry cause system can read it (add a goal-derived signal; do NOT edit `CivTracker.War.cs` — that is W1's file; instead surface via GoalData/UtilityScorer that the diplomacy layer already consults, or leave a documented `// covet→conflict` seam).
- On acquiring a coveted artifact (claiming a Lost one at the owner's tile), emit `ArtifactTransferred` ("claim") and complete the goal.
- Tests: covet goal forms only above threshold; goal completes on acquisition; reproducibility.

### W3 — Snapshot + UI inspector  *(files: WorldSnapshot, StateCache/snapshot builder, TileInspectorPanel; + docs/queries/event_log_queries.md)*
- Add `ArtifactSnapshot` (shape in contract) and `IReadOnlyList<ArtifactSnapshot> Artifacts` to `WorldSnapshot`; build it in the snapshot builder from `world.Artifacts`.
- `TileInspectorPanel`: when a tile has a settlement, list artifacts located there (`InSettlement`) and, for an inspected character, artifacts they own. Show name, category, origin, quality, creator.
- Add example artifact SQL queries to `docs/queries/event_log_queries.md` (artifact creation timeline, lineage/transfer chain via event payloads, most-coveted). This file is hand-written (its enum tables are generated, the prose queries are not).
- Tests: snapshot exposes artifacts; owner-desc formatting.

### W4 — Telemetry & balance instrumentation  *(files: PhaseRunner (metrics switch only), MetricsAccumulator, MetricsCollector, YearlyMetricsRow + metrics DB schema, config/balance_invariants.toml, scripts/balance-run.py)*
**Goal:** the headless sweep runner must surface artifact event counts AND the living-artifact stock so rarity/destruction can be tuned empirically. The greatest risk is unbounded persistent-artifact accumulation overflowing the world — the headline metric is **living (active, non-destroyed) artifact count over time** and the create-vs-destroy rate.

- Add YTD counters to `MetricsAccumulator`: `ArtifactsCreatedYtd`, `ArtifactsDestroyedYtd`, `ArtifactsTransferredYtd` (add them to `ResetYtd()`). Optionally split created-by-origin if cheap.
- In `PhaseRunner.UpdateMetricsAccumulator(pe)` add `case EventType.ArtifactCreated/ArtifactDestroyed/ArtifactTransferred` incrementing the counters (mirror the existing `SettlementFounded`/`GoalFormed` cases). This is the ONLY artifact edit to `PhaseRunner.cs` and this worker owns it.
- In `MetricsCollector.Sample(...)` compute STOCK metrics from `world.Artifacts`: `livingArtifacts` (count where `!IsDestroyed`), `lostArtifacts` (Owner.Kind==Lost), and `artifactsPerSettlement` (living / max(1, settlement count)) — the last is the key overflow indicator.
- Extend `YearlyMetricsRow` + the metrics table schema (`DatabaseSchema`) + `store.WriteMetricsRow` with the new columns. Make sure `scripts/balance-run.py` picks them up (add to its column list / report if it enumerates explicitly).
- Add PROVISIONAL bands to `config/balance_invariants.toml` for `living_artifacts` and `artifacts_per_settlement` with generous ranges and a `# PROVISIONAL — calibrate after first sweep` comment. Do NOT tighten — real bands come from a post-merge calibration run (see below). Ensure `BalanceRegressionTests` still passes with the loose bands.
- Tests: accumulator increments on each artifact event type; stock metrics computed correctly for a hand-built registry; metrics row round-trips the new columns through the DB.

**Post-merge (owner: orchestrator, not a worker):** after W0–W4 merge, run `python3 scripts/balance-run.py --seed-list 42,777,9999 --years 300 --label artifacts` and calibrate the artifact bands to observed-healthy ±margin per `docs/balance_invariants.md`. If living-artifact count grows without bound, tune `[artifacts]` rarity (`base_generation_probability`, `battle_forge_probability`, `heroic_death_forge_probability`) down and/or add destruction pressure before locking bands.

---

## Definition of Done (whole phase)
- `scripts/test-fast.sh` green (build zero-warning, all tests + 6 arch rules + doc-check).
- Reproducibility holds (same seed → identical artifact set/names/ownership over a fixed run).
- Architecture rules: command/event records sealed, value-type fields only, no async in sim core.
- `sim_config.toml` holds `[artifacts]`; `config_future.md` Artifacts section removed (moved).
- A multi-thousand-year run produces artifacts that are created, transferred on death/conquest, coveted, and visible in the inspector + history log.
