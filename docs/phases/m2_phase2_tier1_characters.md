# Phase 2.2 — Tier 1 Characters

**Milestone:** 2 — The Character System  
**Status:** IN PROGRESS  
**Goal:** Introduce Tier 1 (hero/ruler) characters. Each character has personality, aptitude, skills, and needs. They make utility-scored decisions each season, form relationships, establish settlements, and trigger civilization emergence. This is the first phase that produces political and social history.

**Companion docs:**
- `docs/implementation_decisions_v0.3.md` §16 (Character Decision-Making), §17 (Trait System), §19 (Administrative Distance), §23 (Significance Classification)
- `docs/interface_contracts.md` — will be updated to v0.5 with new IEntity types and snapshot fields
- `docs/snippets/patterns.md` — utility scoring and softmax pattern

**What is deliberately out of scope for this phase:**
- Administrative distance penalty (Dijkstra influence maps) — Phase 2.3 when settlements are dense enough to matter
- Cultural trait modifiers (Phase 2.3)
- Tier 2 characters (Phase 2.3)
- Full religion/artifact system (Phase 2.3+)
- Spotlight mode (Milestone 4)

---

## Design Decisions for Phase 2.2

### DS-C1 — Character Storage: EntityRegistry (existing)
Tier1Character implements IEntity and is stored in the existing EntityRegistry alongside beasts. The registry already has typed lists; add `List<Tier1Character> Characters` to EntityRegistry. No new registry type needed.

### DS-C2 — Trait Vectors as Readonly Record Structs
PersonalityVector, AptitudeVector, SkillVector, NeedsVector are `readonly record struct` types. All float fields. Structs live in `WorldEngine.Sim/Entities/Characters/`. They are value types passed by reference where needed.

All trait values are 0.0–1.0 floats. Skills grow dynamically through use (capped at 1.0). Personality and Aptitude are fixed at character generation.

### DS-C3 — NeedsVector Update: Per-Season Decay + Situational Events
Each season, needs decay at rates from `SimConfig.Character`. Needs are partially restored by actions:
- Safety: restored by being in allied/own territory
- Food: restored if settlement has food surplus (stub: always partially restored in Phase 2.2)
- Shelter: restored if in settlement
- Belonging: restored by social actions (marry, ally)
- Status: restored by leading, expanding, winning
- Purpose: restored by pursuing goals
- Spiritual: slowly decays; restored by Piety actions (stub in Phase 2.2)

Unmet needs (< 0.2) generate urgent goals that override utility scoring for anything else.

### DS-C4 — Initial Character Count and Placement
World spawns `initial_character_count` Tier 1 characters from `SimConfig.Character`, default 20. Characters are placed on land tiles with above-average fertility. No two characters start on the same tile. Characters do not spawn in ocean or HighMountain biomes.

### DS-C5 — Action Library (Phase 2.2 Subset)
8 actions for the initial release. More added in Phase 2.3 as complexity warrants.

| Action | Condition | Effect | Command emitted |
|--------|-----------|--------|-----------------|
| Rest | Any | Partially restores all needs | `Rest(Id)` |
| Travel | Any land tile adjacent | Moves character | `MoveToTile(Id, dest)` |
| EstablishSettlement | No settlement on tile, Food ≥ 0.5 | Creates settlement stub | `EstablishSettlement(Id, tile)` |
| AllyWith | Nearby Tier1, relationship Trust > 0.4 | Raises Trust, sets IsAlly | `AllyWith(Id, targetId)` |
| DeclareRivalry | Nearby Tier1, relationship Trust < 0.2 | Flags IsRival, raises Fear | `DeclareRivalry(Id, targetId)` |
| DeclareWar | Rival exists, Aggression > 0.5 | Sets IsAtWar | `DeclareWar(Id, targetId)` |
| RaidSettlement | Rival's settlement on adjacent tile | Damages settlement | `RaidSettlement(Id, targetSettlementId)` |
| Negotiate | Any Tier1 on same tile | Adjusts Trust | `Negotiate(Id, targetId)` |

### DS-C6 — Perception Radius: Flat 3 Tiles (Phase 2.2 Stub)
Information travels at messenger speed in the full design (§16). For Phase 2.2, use a flat perception radius of 3 tiles. Characters can see/target other entities within 3 tiles. Phase 2.3 will replace this with actual messenger delay.

### DS-C7 — Utility Scoring: Simplified Weights from SimConfig
Full utility function (§16) used, but RelationshipEffects and CulturalModifierBias are zero in Phase 2.2 (no relationship graph yet). The four active terms:

```
Utility = NeedsSatisfaction × config.NeedsWeight
        + GoalAdvancement × config.GoalsWeight
        + PersonalityFit × config.PersonalityWeight
        × SuccessProbability
```

RelationshipEffects added in Story 2.2.5 when the relationship graph is implemented.

### DS-C8 — Civilization Emergence: First Settlement Triggers Formation
When a character establishes a settlement, if they have no CivId assigned, a new civilization is formed:
- New `CivId` assigned
- `Civilization` record created: name, founder character, founding year, capital tile
- Character assigned to that civilization
- `CivilizationFounded` event fired

Characters who later `AllyWith` the founder can optionally join the civilization (probability based on Loyalty + character Ambition). Phase 2.3 will formalize civ membership rules.

### DS-C9 — Settlement Stub
In Phase 2.2, settlements are lightweight data attached to a tile in WorldState:
```csharp
public sealed record SettlementStub(
    EntityId FounderId, CivId CivId, TileCoord Tile,
    int FoundedYear, int Population, int Health);
```
`Population` starts at 50 (stub). `Health` starts at 100. Raids reduce Health. No production, no real population dynamics. Phase 2.4 replaces SettlementStub with the full Settlement system.

### DS-C10 — DB Extension: EventEntities Table
Add EventEntities table as specified in M1 architecture notes:
```sql
CREATE TABLE IF NOT EXISTS EventEntities (
    EventId INTEGER NOT NULL REFERENCES Events(Id),
    EntityId INTEGER NOT NULL,
    Role TEXT,
    PRIMARY KEY (EventId, EntityId)
);
```
Populated for every character event. Enables "all events involving character X" queries.

### DS-C11 — Character Name Generation
Names generated from config arrays in `sim_config.toml` under `[character_names]`. Separate arrays for first names and epithets (e.g., "the Bold", "the Wise"). Full name: `"{FirstName} {Epithet}"`. Names are seeded via WorldRng. Name lists from `SimConfig.CharacterNames`.

### DS-C12 — Character Death
Characters die of old age or in battle. Age in seasons tracked. Max age drawn from `SimConfig.Character.MaxAgeSeasonsMin/Max`. Battle death: health reduced by raid outcomes. `CharacterDied` event is always recorded. Dead characters remain in EntityRegistry with `IsAlive = false` for historical queries. Removed from active processing after death.

---

## Stories

### Story 2.2.1 — Character Data Model

**Files to create:**
- `WorldEngine.Sim/Entities/Characters/PersonalityVector.cs`
- `WorldEngine.Sim/Entities/Characters/AptitudeVector.cs`
- `WorldEngine.Sim/Entities/Characters/SkillVector.cs`
- `WorldEngine.Sim/Entities/Characters/NeedsVector.cs`
- `WorldEngine.Sim/Entities/Characters/IdentityData.cs`
- `WorldEngine.Sim/Entities/Characters/GoalData.cs`
- `WorldEngine.Sim/Entities/Characters/CharacterSnapshot.cs`
- `WorldEngine.Sim/Entities/Characters/Tier1Character.cs`
- `WorldEngine.Sim/Core/CivId.cs`
- `WorldEngine.Sim/Civilizations/SettlementStub.cs`
- `WorldEngine.Sim/Civilizations/Civilization.cs`

**Files to modify:**
- `WorldEngine.Sim/Entities/EntityRegistry.cs` — add `List<Tier1Character> Characters`, typed Add/Remove
- `WorldEngine.Sim/World/WorldSnapshot.cs` — add `IReadOnlyList<CharacterSnapshot> Characters`
- `WorldEngine.Sim/World/SnapshotBuilder.cs` — project characters into snapshot
- `WorldEngine.Sim/World/WorldState.cs` — add `Dictionary<CivId, Civilization> Civilizations`, `Dictionary<TileCoord, SettlementStub> Settlements`
- `WorldEngine.Sim/World/IWorldStateReadOnly.cs` — expose `IReadOnlyDictionary<TileCoord, SettlementStub> Settlements`
- `WorldEngine.Sim/Entities/EntityCommands.cs` — add character commands
- `docs/interface_contracts.md` — update to v0.5

**Key types:**

```csharp
// All traits are 0.0–1.0 floats
public readonly record struct PersonalityVector(
    float Ambition, float Greed, float Aggression, float Compassion,
    float Curiosity, float Creativity, float Rationality, float Wonder,
    float Loyalty, float Sociability, float Honesty, float Stability);

public readonly record struct AptitudeVector(
    float Diligence, float Focus, float Perfectionism,
    float Composure, float Acuity, float Ingenuity);

public readonly record struct SkillVector(
    float Combat, float Leadership, float Administration,
    float Diplomacy, float Crafting, float Knowledge, float Stealth, float Piety);

public readonly record struct NeedsVector(
    float Safety, float Food, float Shelter,
    float Belonging, float Status, float Purpose, float Spiritual);

public sealed record IdentityData(
    string Name, string Epithet, EntityId? MotherId, EntityId? FatherId,
    CivId? CivId, int BirthYear, int BirthSeason);

public sealed class GoalData
{
    public GoalType Type { get; init; }
    public EntityId? TargetEntityId { get; init; }
    public TileCoord? TargetTile { get; init; }
    public float Priority { get; set; }   // recomputed each tick
    public float Progress { get; set; }   // 0.0–1.0
    public bool IsComplete { get; set; }
}

public enum GoalType
{
    Survive, SecurityGoal, ExpansionGoal, DominanceGoal, AllianceGoal, UnifyGoal
}
```

**Tier1Character** implements IEntity with:
- `PersonalityVector Personality` (readonly)
- `AptitudeVector Aptitude` (readonly)
- `SkillVector Skills` (mutable, grows through use)
- `NeedsVector Needs` (mutable, updated each season)
- `IdentityData Identity` (readonly except CivId)
- `List<GoalData> Goals`
- `int AgeSeason`, `int MaxAgeSeason`, `int Health`, `int MaxHealth`
- `bool IsAlive`
- `EmitCommands(IWorldStateReadOnly, SimPhase)` — utility-scored
- `ToSnapshot()` → CharacterSnapshot

**CharacterSnapshot:**
```csharp
public sealed record CharacterSnapshot(
    EntityId Id, EntityKind Kind, string Name, string Epithet,
    TileCoord Location, CivId? CivId, bool IsAlive,
    float Ambition, float Aggression, float Loyalty,    // key personality traits for display
    float Safety, float Status, float Purpose,           // key needs for display
    float Combat, float Leadership, float Diplomacy,     // key skills for display
    int AgeSeason, float HealthFraction);
```

**Character commands to add to EntityCommands.cs:**
```csharp
public sealed record EstablishSettlement(EntityId CharacterId, TileCoord Tile) : ICommand;
public sealed record AllyWith(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record DeclareRivalry(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record DeclareWar(EntityId CharacterId, EntityId TargetId) : ICommand;
public sealed record RaidSettlement(EntityId CharacterId, TileCoord SettlementTile) : ICommand;
public sealed record Negotiate(EntityId CharacterId, EntityId TargetId) : ICommand;
```

---

### Story 2.2.2 — Character Generation and World Seeding

**Files to create:**
- `WorldEngine.Sim/Entities/Characters/CharacterFactory.cs`
- `WorldEngine.Sim/Entities/Characters/CharacterSpawner.cs`
- `WorldEngine.Sim/Config/CharacterSimConfig.cs`

**Files to modify:**
- `WorldEngine.Sim/Config/SimConfig.cs` — add `CharacterSimConfig Character`
- `config/sim_config.toml` — add `[character]` section

Trait generation:
```
Personality trait = Clamp(0.5 + GaussianNoise(stdDev=0.2), 0.1, 0.9)
Aptitude trait    = Clamp(0.5 + GaussianNoise(stdDev=0.15), 0.1, 0.9)
Skill             = GaussianNoise centered on 0.1 (skills start low; grow through use)
```
Use WorldRng for all randomness. Each character gets a unique `entitySeq`.

Character placement: collect land tiles with Fertility ≥ 100, shuffle by seed, place one character per tile (no stacking at world start).

`CharacterSpawner.SpawnAll(WorldState, SimConfig)` returns `List<PendingEvent>` with `CharacterBorn` for each character.

**`[character]` section in sim_config.toml:**
```toml
[character]
initial_count             = 20
max_age_seasons_min       = 80    # 20 years minimum
max_age_seasons_max       = 200   # 50 years maximum
needs_decay_safety        = 0.05
needs_decay_food          = 0.08
needs_decay_shelter       = 0.04
needs_decay_belonging     = 0.03
needs_decay_status        = 0.03
needs_decay_purpose       = 0.04
needs_decay_spiritual     = 0.02
needs_weight              = 0.5
goals_weight              = 0.3
personality_weight        = 0.2
softmax_temp_min          = 0.5
softmax_temp_max          = 2.0
perception_radius         = 3
health_per_season_heal    = 5
combat_damage_base        = 20
```

---

### Story 2.2.3 — Needs Update System

**Files to create:**
- `WorldEngine.Sim/Entities/Characters/NeedsUpdater.cs`

Called at the start of `EmitCommands` and also in a pre-pass in `CharacterBehaviorPhase`.

`NeedsUpdater.Update(Tier1Character, IWorldStateReadOnly, SimConfig)`:
1. Decay all needs by per-season rates from config
2. Restore Safety if character is in own-civ territory (Phase 2.3 does this properly; stub: always restore +0.05)
3. Restore Food partially each season (lower food web stub: +0.06)
4. Restore Shelter if on settlement tile (+0.10)
5. Clamp all to [0.0, 1.0]

Unmet needs threshold: 0.25. Any need below 0.25 generates a `SurviveGoal` with priority 1.0 that dominates utility scoring.

---

### Story 2.2.4 — Goals, Utility Scoring, and Action Selection

**Files to create:**
- `WorldEngine.Sim/Entities/Characters/UtilityScorer.cs`
- `WorldEngine.Sim/Entities/Characters/ActionEvaluator.cs`
- `WorldEngine.Sim/Entities/Characters/GoalManager.cs`

`GoalManager.UpdateGoals(Tier1Character, IWorldStateReadOnly)`:
- Prune completed/obsolete goals
- Add new goals based on personality and current needs:
  - Ambition > 0.6 → add ExpansionGoal if no settlement in territory
  - Aggression > 0.6 → add DominanceGoal against nearest rival
  - Sociability > 0.5 → add AllianceGoal if no allies in range
  - Any need < 0.25 → SurviveGoal (overrides all)
- Recompute priorities: `priority = baseFromPersonality × (1 - currentProgress)`

`UtilityScorer.Score(action, character, world, config)`:
```
utility = NeedsSatisfaction(action) × config.NeedsWeight
        + GoalAdvancement(action, character.Goals) × config.GoalsWeight
        + PersonalityFit(action, character.Personality) × config.PersonalityWeight
        × SuccessProbability(action, character, world)
```

Softmax selection:
```csharp
temperature = config.SoftmaxTempMin 
            + character.Personality.Curiosity 
            × (config.SoftmaxTempMax - config.SoftmaxTempMin);
// Boltzmann weights: e^(utility / temp)
// Select weighted random using WorldRng
```

`ActionEvaluator` computes `SuccessProbability` per action type:
- Travel: always 1.0
- EstablishSettlement: `Leadership × 0.5 + Diligence × 0.5`
- AllyWith: `Diplomacy × trust_factor`
- DeclareWar: always 1.0 (declaration succeeds; battle outcome is separate)
- RaidSettlement: `Combat × Diligence × (1 - target_health/100)`

---

### Story 2.2.5 — Relationship Graph

**Files to create:**
- `WorldEngine.Sim/Entities/Characters/RelationshipEdge.cs`
- `WorldEngine.Sim/Entities/Characters/RelationshipGraph.cs`

**Files to modify:**
- `WorldEngine.Sim/World/WorldState.cs` — add `RelationshipGraph Relationships`
- `WorldEngine.Sim/World/IWorldStateReadOnly.cs` — expose `RelationshipEdge? GetRelationship(EntityId a, EntityId b)`
- `WorldEngine.Sim/World/WorldSnapshot.cs` — add focused relationship projection for inspected character

```csharp
public sealed record RelationshipEdge(
    EntityId From, EntityId To,
    float Trust,    // -1.0 to 1.0
    float Fear,     //  0.0 to 1.0
    float Debt,     // -1.0 to 1.0
    RelationshipFlags Flags);

[Flags]
public enum RelationshipFlags
{
    None = 0, IsAlly = 1, IsRival = 2, IsAtWar = 4,
    IsFamily = 8, IsMarried = 16
}
```

RelationshipGraph: `Dictionary<(EntityId, EntityId), RelationshipEdge>` with canonical ordering (smaller Id first). `Get(a, b)` returns edge in either direction. `Upsert(edge)` replaces existing.

Now wire RelationshipEffects into utility scoring (story 2.2.4 added it at zero; now enable it):
```
RelationshipEffects(action, character, world) = 
    trust_delta × TrustWeight + fear_delta × FearWeight
```

---

### Story 2.2.6 — Civilization Emergence

**Files to create:**
- `WorldEngine.Sim/Civilizations/CivTracker.cs`
- `WorldEngine.Sim/Civilizations/CharacterCommands.cs` (command resolvers for char commands)

**Files to modify:**
- `WorldEngine.Sim/World/WorldState.cs` — already has Civilizations dictionary (added in 2.2.1)
- `WorldEngine.Sim/Core/Enumerations.cs` — add character EventTypes

`CivTracker.ResolveEstablishSettlement(EstablishSettlement cmd, WorldState)`:
1. If tile already has a settlement → no-op
2. Create `SettlementStub` on that tile
3. If character has no CivId:
   - Generate CivId = new `CivId(world.NextCivId++)`
   - Create Civilization record
   - Assign CivId to character
   - Fire `CivilizationFounded` event
4. Fire `SettlementFounded` event
5. If character has CivId → settlement belongs to that civ

`CivTracker.ResolveAllyWith` → sets IsAlly flag, adjusts Trust, fires `AllianceFormed`

`CivTracker.ResolveDeclareWar` → sets IsAtWar, fires `WarDeclared`

`CivTracker.ResolveRaidSettlement` → reduces settlement Health by 10-30, fires `BattleOccurred`; if Health ≤ 0 fires `SettlementDestroyed` and removes stub

---

### Story 2.2.7 — Character Event Types

**Files to modify:**
- `WorldEngine.Sim/Core/Enumerations.cs` — add EventType values 3001-3020
- `WorldEngine.Sim/Events/SignificanceClassifier.cs` — add character event tiers
- `WorldEngine.Sim/Persistence/EventStore.cs` — add EventEntities table + batch insert method

**New EventTypes (stable IDs):**
```csharp
// 3000-range: Character lifecycle
CharacterBorn           = 3001,
CharacterDied           = 3002,
CharacterMarried        = 3003,
CharacterExiled         = 3004,
// 3100-range: Character actions
AllianceFormed          = 3101,
AllianceBroken          = 3102,
WarDeclared             = 3103,
WarEnded                = 3104,
BattleOccurred          = 3105,
RivalryFormed           = 3106,
Negotiated              = 3107,
// 3200-range: Civilization/Settlement
CivilizationFounded     = 3201,
CivilizationCollapsed   = 3202,
SettlementFounded       = 3203,
SettlementDestroyed     = 3204,
SuccessionOccurred      = 3205,
```

**Significance for character events** (all Tier 1 involved → Headline by rule §23):
- CharacterBorn, CharacterDied → Headline (Tier 1 involved)
- CivilizationFounded, CivilizationCollapsed → Headline
- WarDeclared, WarEnded, BattleOccurred → Headline (Conflict verb, Tier 1 involved)
- SettlementFounded → Regional (Creation verb floor)
- SettlementDestroyed → Regional (Destruction verb floor)
- AllianceFormed, RivalryFormed → Regional (first-of-kind bump) / Character otherwise
- Negotiated → Character

**EventEntities table insert:** after inserting a character event, extract EntityId(s) from payload JSON and insert rows into EventEntities for querying history by character.

**Always-record types** (bypass EventGate): `CharacterBorn`, `CharacterDied`, `CivilizationFounded`, `CivilizationCollapsed`, `WarDeclared`, `WarEnded`.

---

### Story 2.2.8 — Phase Runner and CharacterBehaviorPhase Integration

**Files to create:**
- `WorldEngine.Sim/Simulation/Phases/CharacterBehaviorPhase.cs`

**Files to modify:**
- `WorldEngine.Sim/Simulation/PhaseRunner.cs` — replace CharacterDecisions stub with real phase
- `WorldEngine.UI/Game1.cs` — call CharacterSpawner.SpawnAll after world gen

`CharacterBehaviorPhase.RunTick(WorldState, List<PendingEvent>)`:
1. Update needs for all living characters (NeedsUpdater)
2. Update goals for all living characters (GoalManager)
3. Emit commands from all living characters (utility scoring)
4. Resolve all commands (CivTracker for establish/ally/war/raid, RelationshipGraph for negotiate)
5. Age all characters; kill if age ≥ max or health ≤ 0
6. Health regeneration: heal config.HealthPerSeasonHeal if in settlement

---

### Story 2.2.9 — UI: Character Markers and Inspector

**Files to modify:**
- `WorldEngine.UI/Rendering/TileMapRenderer.cs` — blue triangle marker for Tier1Character
- `WorldEngine.UI/UI/TileInspectorPanel.cs` — character section (name, civ, top 3 needs, top skills)
- `WorldEngine.UI/UI/EventLogPanel.cs` — Headline events show in gold (already works; character events will naturally appear)

TileMapRenderer: when zoom ≥ 4x, draw a blue ▲ (3-pixel triangle) over each tile with a living Tier1Character. If the character has a CivId, use the civ's color instead of plain blue.

TileInspectorPanel: if tile has a character, add section:
```
--- Characters ---
Aelindra the Bold  [Civ: Ironhold Dominion]  Age 12
  Needs: Safety 0.82  Status 0.54  Purpose 0.31
  Skills: Leadership 0.4  Combat 0.3  Diplomacy 0.2
  Goals: ExpansionGoal (prio 0.7)
```

---

## Testing Requirements

**Story 2.2.1:** Unit tests for PersonalityVector, NeedsVector struct initialization and clamping.

**Story 2.2.2:** CharacterFactory same-seed produces same traits. SpawnAll places no two characters on same tile.

**Story 2.2.3:** NeedsUpdater decays correctly; unmet need generates SurviveGoal.

**Story 2.2.4:** UtilityScorer produces higher score for actions that address lowest need. Softmax returns higher-temperature outputs for high-Curiosity characters.

**Story 2.2.5:** RelationshipGraph Get returns null for unknown pairs. Upsert round-trips correctly. AllyWith sets IsAlly flag.

**Story 2.2.6:** EstablishSettlement on empty tile creates settlement. Second establishment on same tile is no-op. First settlement for character fires CivilizationFounded. CivId persists across ticks.

**Story 2.2.7:** EventTypes compile. Always-record types bypass gate in PhaseRunner tests. EventEntities row inserted for CharacterBorn.

**Integration:** Run world for 100 ticks with 20 characters. Assert: ≥1 CivilizationFounded event, ≥1 SettlementFounded, ≥1 AllianceFormed or WarDeclared, all characters alive at tick 1.

**Reproducibility:** Same seed + same initial count produces same first 10 character decisions.
