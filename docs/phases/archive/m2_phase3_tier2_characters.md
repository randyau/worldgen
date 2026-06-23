# Phase 2.3 — Tier 2 Characters

**Milestone:** 2 — The Character System  
**Status:** COMPLETE — 2026-06-23  
**Goal:** Introduce Tier 2 named characters — individuals below hero/ruler status who fill specialist and authority roles. They have livelihoods, simpler personality models, and produce economic and social events that feed the history log.

**Companion docs:**
- `docs/implementation_decisions_v0.3.md` §16 (Character Decision-Making), §18 (Livelihood System), §19 (Administrative Distance)
- `docs/interface_contracts.md` — Tier2Character, LivelihoodData
- `docs/mvp_spec.md` — Phase 2.3 scope

---

## Scope

Tier 2 characters are named but less individually powerful than Tier 1. They are:
- **Authority Tier 2**: generals, governors, administrators (assigned to Tier 1 rulers)
- **Specialist Tier 2**: physicians, merchants, scholars, artisans

They have:
- A simplified 6-trait PersonalityVector (subset: Ambition, Loyalty, Diligence, Sociability, Cunning, Rationality)
- A **LivelihoodData**: their role, employer (Tier 1 EntityId), income level, settlement affiliation
- Needs: Food, Safety, Belonging, Status (4-need subset — no Spiritual/Purpose/Shelter at Tier 2)
- Skills relevant to their specialization only

They do NOT have:
- Goals list (replaced by role-driven behavior)
- Utility scoring with softmax — simplified fixed behavior per role
- Combat (unless Military specialization)

Tier 2 characters:
- Spawn from settlements (one per X population, rounded from SettlementStub.Population)
- May die, be appointed, be dismissed, or become Tier 1 (crystallization — rare, high ambition + high achievement)
- Generate events: AppointedToRole, DismissedFromRole, MerchantTradeCompleted, ScholarDiscovery, PhysicianHealed

---

## Stories

### Story 2.3.1 — Tier2Character entity (data model)
- `Tier2Character.cs` implementing `IEntity`
- `LivelihoodData.cs` record: Role (enum), EmployerId (EntityId?), SettlementTile, IncomeLevel (0.0–1.0)
- `Tier2Role` enum: General, Governor, Merchant, Scholar, Physician, Artisan
- `NeedsVector4` mutable record struct: Food, Safety, Belonging, Status (4-need subset)
- `EntityRegistry` Add/Remove dispatch for Tier2Character
- `EntityKind.Tier2Character` already in enum

### Story 2.3.2 — Livelihood spawning
- `Tier2Spawner.SpawnFromSettlements(world, config)` — iterates Settlements, spawns N characters proportional to Population
- Names from CharacterNamesConfig (reuse existing pool)
- Personality: 3 uniform samples (CLT), same as Tier1 but only 6 traits
- Injected as `CharacterBorn` events at world start

### Story 2.3.3 — Role behavior (simplified)
- `Tier2BehaviorPhase` — one switch per Role:
  - General: if Tier1 employer at war → `MilitaryAid` command (boosts Tier1 combat skill this tick)
  - Governor: `AdminSupport` command (boosts settlement health regen)
  - Merchant: `TradeRoute` command between two connected settlements → `MerchantTradeCompleted` event
  - Scholar: `Research` command → chance to fire `ScholarDiscovery` event
  - Physician: `Heal` command → target Tier1 HP recovery boost this tick
  - Artisan: `Craft` command → minor settlement health boost

### Story 2.3.4 — Needs update + lifecycle
- 4-need decay (reuse NeedsUpdater pattern)
- Death by old age (max 120–180 seasons, narrower than Tier1)
- Death by starvation (Food ≤ 0)
- Crystallization to Tier1: Ambition > 0.8 AND Status > 0.7 AND tick random < 0.001/season
  → remove from Tier2 registry, spawn Tier1 with matching personality, fire `CharacterBorn`

### Story 2.3.5 — EventType additions
- `AppointedToRole = 3301`
- `DismissedFromRole = 3302`
- `MerchantTradeCompleted = 3303`
- `ScholarDiscovery = 3304`
- `PhysicianHealed = 3305`
- `CharacterCrystallized = 3306`
- Add to `VerbClassification` and `SignificanceClassifier` (all Character tier by default)

### Story 2.3.6 — WorldSnapshot + UI
- `EntitySnapshot` already supports Tier2Character (Kind + Name + Location + HealthFraction)
- `SnapshotBuilder` includes Tier2 characters in EntitySnapshots
- `TileInspectorPanel` already shows all characters via the AddCharacterSection method
- No new rendering needed — same blue marker from Phase 2.2

### Story 2.3.7 — Tests
- `Tier2SpawnerTests.cs`: spawn from known settlement → correct count, deterministic
- `Tier2BehaviorTests.cs`: Merchant TradeRoute fires event when two settlements exist
- `CrystallizationTests.cs`: forced high Ambition/Status triggers crystallization

---

## Definition of Done
- Tier2Characters spawn from existing settlements at world start
- Each tick: needs decay, role behavior fires if conditions met, lifecycle (death, crystallization)
- Events AppointedToRole/MerchantTradeCompleted/ScholarDiscovery fire and appear in event log
- Crystallization produces a new Tier1Character
- 218+ tests pass, zero warnings
