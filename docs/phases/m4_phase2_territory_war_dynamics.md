# Phase 4.2 — Territory Dynamics & War Outcomes

**Milestone:** 4 — World Dynamics
**Status:** PLANNED — 2026-06-27
**Goal:** Make territory expansion visible and meaningful, give wars real territorial outcomes,
and let expanding borders — not just wandering characters — be the primary trigger for conflict.
This closes the remaining gaps in Issues #6, #7, and #14 from `SIMULATION_ISSUES_ANALYSIS.md`.

---

## Background & Motivation

After M4 Phase 1 (emissaries), civs now know each other and trade/diplomacy fires regularly.
Wars are still rare because:

1. **Territory expansion events are invisible** — `TerritoryExpanded`/`TerritoryLost` are not
   in `always_record_types`, so every event is filtered by the significance gate. The expansion
   *does* run (code is correct) but nothing appears in the history log.

2. **Border tension is settlement-proximity-only** — `RunBorderTension` checks whether
   settlement tiles of two civs are within `war_proximity_radius = 15` tiles. With a 400-tile
   world, most civs have their cities far enough apart that tension never builds — even if their
   *territories* are adjacent. A civ whose capital is 20 tiles from an enemy capital accrues
   zero tension even if their farmland tiles are touching.

3. **Wars have no territory outcomes** — `EndWarBetween` removes the war state and emits
   `WarEnded` but transfers zero territory. Winning a war has no tangible consequence.

4. **Battles depend on character proximity** — raids are triggered by characters who happen
   to be near an enemy settlement. A declared war can have zero battles if characters are
   occupied elsewhere. Wars are declared (via border tension) but end by timeout with 0–2
   battles (average 1.6/war from the production run).

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Territory event gate | Add TerritoryExpanded/Lost to `always_record_types` | Every territory shift is historically meaningful |
| Border tension source | Add territory-adjacency tension on top of settlement-proximity | Lets expanding empires create conflict pressure without needing city proximity |
| Annual war campaign | Add abstract campaign tick: one battle attempt per war per year | Wars should be sustained campaigns, not dependent on characters being in the right place |
| War end territory transfer | Winner gains border tiles from loser proportional to battle wins | Gives wars real stakes; uses existing `TransferTerritory` infrastructure |
| Settlement siege | Siege triggered when campaign battle target has population < siege_conquest_threshold | Re-uses existing `ResolveRaid` conquest path; just triggers it from the campaign phase |

---

## Scope

**In scope:**
- `always_record_types` additions: TerritoryExpanded, TerritoryLost, SettlementConquered
- Territory expansion rate tuning (growth_per_year 2→4)
- Territory-adjacency border tension (scan border tile pairs, not just city distances)
- Annual abstract war campaign: one raid attempt per active war per year
- War end: border tile transfer from loser to winner scaled by battle advantage
- New config keys in `[war]` section of `sim_config.toml`
- Tests for each epic

**Out of scope:**
- Named generals or war commanders (Spotlight, M5)
- Treaty negotiation UI (M5 God Mode)
- Siege engines or specific weapon systems (V2)
- Full diplomatic war-justification chain

---

## Epic 4.2.1 — Territory Event Visibility (quick win)

### Story 4.2.1.1 — always_record_types and growth rate

In `config/sim_config.toml`:
- Add `"TerritoryExpanded"`, `"TerritoryLost"`, `"SettlementConquered"` to `always_record_types`
- Change `territory_growth_per_year = 2` → `territory_growth_per_year = 4`
  (With slower disasters now, settlements can sustain larger territories; 4 tiles/year still
  takes 15-20 years to reach max territory)

**Test:** Run a 10-year sim with 2 settlements; assert at least 1 `TerritoryExpanded` event
appears in the history log (not filtered).

---

## Epic 4.2.2 — Territory-Adjacency Border Tension

**Goal:** When territory tiles of two civs share an edge, border tension accrues between them
— independent of how far their city tiles are apart.

### Story 4.2.2.1 — Border tile scan

Add `RunTerritoryBorderTension(world, pending)` called from `RunAnnualDiplomacy`
immediately after `RunBorderTension`. (Or integrate into `RunBorderTension` as a second pass.)

```csharp
// For each tile in TerritoryMap:
//   For each of its 4 neighbours:
//     If neighbour is in TerritoryMap AND neighbour's owning civ != this tile's owning civ:
//       Accrue border tension between the two civs:
//         civA.BorderTension[civB] += cfg.War.TerritoryTensionPerAdjacentPair
//         civB.BorderTension[civA] += cfg.War.TerritoryTensionPerAdjacentPair
//       (cap to avoid double-counting: only count from the tile with lower CivId numeric value)
```

The existing `RunBorderTension` already handles the war declaration check after tension
accumulates — no need to duplicate that logic. This phase just adds another tension source.

New config key: `territory_tension_per_adjacent_pair = 0.015` (in `[war]` section).
At this rate, 10 adjacent tile pairs builds `0.15` tension/year; reaching `tension_war_threshold = 1.0` takes ~7 years of sustained territorial contact.

**Test:**
- Two civs with 0 settlements but 5 adjacent territory tile pairs: after `RunTerritoryBorderTension`,
  both have `0.075` border tension from each other.
- Two civs whose territories do NOT touch: no territory-derived tension.
- Existing settlement-proximity tension still accrues (no regression).

---

## Epic 4.2.3 — Annual War Campaign

**Goal:** Active wars automatically generate one battle attempt per year, abstracting away
the character-proximity bottleneck.

### Story 4.2.3.1 — Campaign tick

Add `RunWarCampaigns(world, pending, rng)` to `CivTracker.War.cs`, called from
`RunAnnualDiplomacy` after `RunBorderTension`.

```
For each civ A:
  For each enemy civ B in A.WarsAgainst (only process each pair once — skip if A.Id > B.Id):
    Find target: the settlement of B nearest to any settlement of A.
    If no target (civ B has no settlements): war ends by collapse (already handled elsewhere).
    Find attacker: the character in A with highest Combat skill.
    If no attacker: skip (use a default stub attacker for the abstract campaign).

    Roll campaign battle outcome:
      attackerStr = attacker.Skills.Combat (or 0.5f if no attacker)
      defenderStr = 0.3f + (target.Health / max_health) * 0.5f
      battleRoll  = rng.NextFloat()
      attackerWins = battleRoll < attackerStr / (attackerStr + defenderStr)

      if attackerWins:
        target.Health -= campaign_battle_damage  (new config key, default 15)
        A.WarBattleWins[B.Id]++
        emit BattleOccurred (attacker=ruler or best combatant, settlement=target)
      else:
        B.WarBattleWins[A.Id]++  (defender wins — attacker repulsed)
        emit BattleOccurred (same, with raidOutcome="repulsed")

      if target.Health <= 0 → trigger conquest path (existing ResolveRaid conquest block)
```

New fields on `Civilization`: `Dictionary<CivId, int> WarBattleWins` (tracks wins vs each enemy,
reset when war ends — used by Epic 4.2.4).

New config keys (`[war]` section):
```toml
campaign_battle_damage          = 15    # health damage per campaign battle
campaign_battle_base_strength   = 0.5   # strength used when no character attacker found
```

**Test:**
- Two civs at war, civ A has ruler with 0.8 combat: after 10 campaign ticks,
  `BattleOccurred` events ≥ 1 in history.
- Settlement health decreases over war years.
- War that reaches settlement health ≤ 0 fires `SettlementConquered`.

### Story 4.2.3.2 — `WarBattleWins` serialization

Add `WarBattleWins` to `CivDto` in `WorldStateDto.cs` and round-trip in `WorldStateMapper.cs`.
Add to existing `SaveLoad_ProducesIdenticalState` assertion.

---

## Epic 4.2.4 — War End Territory Outcome

**Goal:** When `EndWarBetween` fires, the winning side gains some border territory from the loser.

### Story 4.2.4.1 — Border tile transfer on peace

Modify `EndWarBetween` (in `CivTracker.Diplomacy.cs`) to perform a territory transfer:

```csharp
int aWins = ca.WarBattleWins.GetValueOrDefault(civB, 0);
int bWins = cb.WarBattleWins.GetValueOrDefault(civA, 0);
int advantage = aWins - bWins;

if (advantage > 0)
{
    // A won more battles — transfer border tiles from B to A
    int tilesToTransfer = Math.Min(advantage * cfg.War.TilesPerBattleWin, cfg.War.MaxTilesTransferredPerWar);
    TransferBorderTiles(civA, civB, tilesToTransfer, world);
}
else if (advantage < 0)
{
    int tilesToTransfer = Math.Min(-advantage * cfg.War.TilesPerBattleWin, cfg.War.MaxTilesTransferredPerWar);
    TransferBorderTiles(civB, civA, tilesToTransfer, world);
}

// Reset war battle wins
ca.WarBattleWins.Remove(civB);
cb.WarBattleWins.Remove(civA);
```

`TransferBorderTiles(winnerCivId, loserCivId, count, world)`:
```
Find all tiles in loserCiv.TerritoryMap that are adjacent to winner's territory.
Sort by distance to loser's capital (closest to loser capital = most painful to lose).
Transfer up to `count` of them: remove from loser's CityTerritories + TerritoryMap,
add to nearest winner city's CityTerritories + TerritoryMap.
Emit TerritoryLost for loser (count=transferred, reason="war_outcome").
Emit TerritoryExpanded for winner (count=transferred).
```

New config keys:
```toml
tiles_per_battle_win            = 2     # tiles transferred per net battle victory
max_tiles_transferred_per_war   = 12    # cap; prevents one decisive war from reshaping the world
```

**Test:**
- Civ A wins 4 battles, Civ B wins 1 → net advantage 3 → 6 tiles transferred from B to A on peace.
- All transferred tiles appear in A's TerritoryMap and removed from B's.
- `TerritoryLost` and `TerritoryExpanded` events appear in history.
- Net 0 battles (tie) → no territory transfer.

---

## Epic 4.2.5 — Integration Tests

### Story 4.2.5.1 — Territory conflict integration test

Run a 50-year sim with 2 civs placed 20 tiles apart (settlements just outside
`war_proximity_radius` = 15, so no settlement-proximity tension). Assert:

- After 15 years: territory of both civs has expanded (TerritoryExpanded events in log).
- After 25 years: if territories are adjacent, border tension has accrued.
- After 50 years: at least 1 war declared (territory tension triggered it).
- After that war ends: territory changed hands (WarBattleWins != 0 during war).

### Story 4.2.5.2 — War campaign integration test

Run a 30-year sim with 2 civs already at war (forced via `StartWarBetween`). Assert:

- Within 5 years: at least 3 `BattleOccurred` events (campaign ticks fired).
- `EndWarBetween` fires with non-zero `WarBattleWins` on at least one side.
- Territory changes after war ends.

---

## File Checklist

| File | Change |
|------|--------|
| `config/sim_config.toml` | Add always_record_types entries; bump growth rate; add `[war]` config keys |
| `WorldEngine.Sim/Civilizations/Civilization.cs` | Add `WarBattleWins` dict |
| `WorldEngine.Sim/Civilizations/CivTracker.Diplomacy.cs` | Add `RunTerritoryBorderTension`, modify `EndWarBetween` for territory transfer, add `TransferBorderTiles` |
| `WorldEngine.Sim/Civilizations/CivTracker.War.cs` | Add `RunWarCampaigns` |
| `WorldEngine.Sim/Config/SimConfig.cs` / `CharacterSimConfig.cs` | Add `[war]` config section or extend existing |
| `WorldEngine.Sim/Persistence/WorldStateDto.cs` | Add `WarBattleWins` to CivDto |
| `WorldEngine.Sim/Persistence/WorldStateMapper.cs` | Round-trip WarBattleWins |
| `WorldEngine.Tests/TerritoryWarTests.cs` | All tests from epics 4.2.1–4.2.5 |

---

## Risk Notes

- **O(TerritoryMap) border scan:** The territory tile border scan is O(tiles_claimed) per tick. With 20 civs × 50 tiles each = 1000 territory tiles, and 4 neighbors each = 4000 checks, this is trivial.
- **Double-counting prevention:** Only count each adjacent pair once by requiring the tile's owning civ's Id numeric value to be the lower one before accruing tension.
- **`TransferBorderTiles` and orphaned improvements:** When a tile is transferred, remove any improvements on that tile (or leave them — improvements belong to the tile, not the civ). For now, leave improvements in place (new owner inherits the farm/mine).
- **Campaign battle and character HP:** `RunWarCampaigns` does NOT reduce character HP — abstract campaigns are civ-level, not character-level. Named characters participate in war via the existing `ResolveRaid` character path.
