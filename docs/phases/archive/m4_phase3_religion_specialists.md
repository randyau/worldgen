# Phase 4.3 — Religion Founding & Specialist Replacement

**Milestone:** 4 — World Dynamics
**Status:** COMPLETE — 2026-06-27
**Goal:** Fix two major non-functional subsystems: (1) `ReligionFounded = 0` because no goal
or trigger exists; (2) `DismissedFromRole = 0` because the crystallization gate is never
re-opened after a specialist dies, so dead specialists are never replaced.

---

## Background

After M4 Phase 1 & 2, war and trade are much more active. Two subsystems remain dark:

**Religion:**
- `ReligionFounded: 0` in the 2150-year reference run
- The `Spiritual` need exists on Tier1 characters and decays/recovers, but no goal or
  trigger connects high spirituality to founding an event. `ReligionFounded` event type exists
  (4003) and `ReligiousEmissaryArrived` boosts Spiritual, but nothing reads that boost.

**Specialist replacement:**
- `AppointedToRole: 371` vs `DismissedFromRole: 0`
- Tier2 specialists live 38–75 sim-years; they should die many times in a 2150-year run.
- Root cause: `PopulationDynamicsPhase.TryCrystallize` uses `LastCrystalThresh` to skip
  roles already crystallized. When a specialist dies, `LastCrystalThresh` is not reset, so
  the population threshold is "spent" and no replacement is ever spawned.
- Fix: check "is a living specialist of this role present?" instead of "has this threshold
  been crossed?". If population is above threshold AND no living specialist exists → spawn.

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Religion model | Events only, no persistent Religion object | Simplest; Religion objects are a V2 feature |
| FoundReligion trigger | Spiritual ≥ threshold AND Piety skill ≥ threshold AND Wonder trait ≥ threshold | All three together make it rare but achievable |
| FoundReligion goal progression | Auto-advances +progress_per_year annually while Spiritual stays high | No dedicated "Pray" action needed; founding takes 1-3 years |
| Multiple religions per civ | Allowed | History has many religious movements; no cap |
| Specialist replacement trigger | Living-specialist check per (tile, role) pair | Cleaner than resetting LastCrystalThresh; handles multiple roles independently |
| LastCrystalThresh retention | Keep as-is | Still useful to track the high-water population mark |

---

## Config (new keys in `sim_config.toml`)

```toml
[religion]
spiritual_founding_threshold    = 0.75   # Spiritual need level to trigger FoundReligion goal
piety_founding_threshold        = 0.50   # Piety skill floor to qualify as a founder
wonder_founding_threshold       = 0.60   # Wonder personality trait floor
religion_founding_progress_per_year = 0.35  # progress added annually; ~3 years to complete
religion_founding_cooldown_years    = 50    # per character: min years between foundings
```

---

## Scope

**In scope:**
- `GoalType.FoundReligion` added to GoalData.cs
- Annual trigger in `CharacterBehaviorPhase`: character with high Spiritual + Piety + Wonder
  → form FoundReligion goal if none active
- Annual progression: advance FoundReligion goal progress each year Spiritual stays above
  threshold; abandon goal if Spiritual drops below threshold
- Goal completion: emit `ReligionFounded` event, grant +Purpose and +Status to founder
- `ReligionFounded` added to `always_record_types` in sim_config.toml
- Specialist replacement: living-specialist check in `PopulationDynamicsPhase.TryCrystallize`
- New `[religion]` config section in `sim_config.toml` and `ReligionConfig` record
- Tests for each epic

**Out of scope:**
- Religion as a persistent tracked object (V2)
- Religion spread mechanics beyond emissary boost (V2)
- Conversion events (V2)
- Named religions (V2)

---

## Epic 4.3.1 — FoundReligion Goal

### Story 4.3.1.1 — GoalType.FoundReligion

Add `FoundReligion` to `GoalType` enum in `GoalData.cs`.

### Story 4.3.1.2 — Annual trigger in CharacterBehaviorPhase

In `CharacterBehaviorPhase.Execute`, in the annual block (alongside `ProcessAnnualDisease`),
add a call to `TryFormFoundReligionGoal(c, world, tick, pending)`:

```csharp
// Annual check per character
private void TryFormFoundReligionGoal(
    Tier1Character c, WorldState world, long tick, List<PendingEvent> pending)
{
    var cfg = world.SimConfig.Religion;

    // Must have high Spiritual, Piety skill, and Wonder personality
    if (c.Needs.Spiritual    < cfg.SpiritualFoundingThreshold) return;
    if (c.Skills.Piety       < cfg.PietyFoundingThreshold)     return;
    if (c.Personality.Wonder < cfg.WonderFoundingThreshold)    return;

    // Cooldown: don't found again too soon
    if (c.LastReligionFoundedYear > 0
        && world.CurrentYear - c.LastReligionFoundedYear < cfg.ReligionFoundingCooldownYears)
        return;

    // Already has an active FoundReligion goal
    if (c.Goals.Any(g => g.Type == GoalType.FoundReligion && !g.IsComplete)) return;

    c.Goals.Add(new GoalData
    {
        Type      = GoalType.FoundReligion,
        Priority  = 0.8f,
        Intensity = 0.9f,
        Progress  = 0f,
        FormedTick = (int)tick,
    });
}
```

**Test:** Character with Spiritual=0.8, Piety=0.6, Wonder=0.7 → FoundReligion goal forms on annual tick.

### Story 4.3.1.3 — Annual progression and completion

In the same annual block, advance active FoundReligion goals:

```csharp
private void AdvanceFoundReligionGoal(
    Tier1Character c, WorldState world, List<PendingEvent> pending)
{
    var goal = c.Goals.FirstOrDefault(g => g.Type == GoalType.FoundReligion && !g.IsComplete);
    if (goal is null) return;

    var cfg = world.SimConfig.Religion;

    // Abandon if Spiritual dropped
    if (c.Needs.Spiritual < cfg.SpiritualFoundingThreshold - 0.1f)
    {
        goal.IsComplete = true;  // marks for pruning
        return;
    }

    goal.Progress = Math.Min(1f, goal.Progress + cfg.ReligionFoundingProgressPerYear);
    if (goal.Progress < 1f) return;

    // Completion
    goal.IsComplete = true;
    c.LastReligionFoundedYear = world.CurrentYear;

    c.Needs = c.Needs with
    {
        Purpose  = Math.Min(1f, c.Needs.Purpose  + 0.25f),
        Spiritual = Math.Min(1f, c.Needs.Spiritual + 0.15f),
        Status   = Math.Min(1f, c.Needs.Status    + 0.20f),
    };

    var payload = JsonSerializer.Serialize(new ReligionFoundedPayload(
        c.Id.Value, c.Identity.Name, world.CurrentYear,
        c.Location.X, c.Location.Y));
    pending.Add(new PendingEvent(EventType.ReligionFounded, c.Location, null, payload,
        new[] { c.Id.Value },
        ActorId: c.Id.Value, ActorName: c.Identity.Name,
        CivId: c.Identity.CivId.Value));
}
```

Add `LastReligionFoundedYear` (int, default -999) to `Tier1Character`.

**Test:** FoundReligion goal at 0.35/year progress fires `ReligionFounded` after 3 annual ticks.

### Story 4.3.1.4 — Event payload + config

- Add `ReligionFoundedPayload` record to `EventPayloads.cs`
- Add `ReligionFounded` to `always_record_types` in `sim_config.toml`
- Add `ReligionConfig` record and `[religion]` section to toml
- Wire `Religion` property into `SimConfig`
- Add `LastReligionFoundedYear` to `WorldStateDto.EntityDto` and serializer context

---

## Epic 4.3.2 — Specialist Replacement

### Story 4.3.2.1 — Living-specialist check in TryCrystallize

In `PopulationDynamicsPhase.TryCrystallize`, replace the `threshold <= currentThresh` skip
with a per-role alive check:

```csharp
// Build set of (tile, role) for living specialists — call once per settlement, pass in
bool HasLivingSpecialist(TileCoord tile, Tier2Role role) =>
    world.Entities.Tier2Chars.Any(t =>
        t.IsAlive && t.Livelihood.SettlementTile == tile && t.Livelihood.Role == role);

foreach (var (threshold, role) in thresholds)
{
    if (pop < threshold) break;             // not big enough for this role
    if (HasLivingSpecialist(tile, role)) continue;  // already filled
    
    // Spawn replacement (same code as before)
    ...
    currentThresh = Math.Max(currentThresh, threshold);
}
```

`LastCrystalThresh` continues to track high-water mark (used for UI or future logic), but
no longer gates replacement.

**Test:**
- Settlement at pop=250, Artisan spawned, Artisan dies → next TryCrystallize spawns replacement
- Settlement at pop=250 with living Artisan → no duplicate spawned
- Settlement at pop=150 (below threshold=200) → no spawn even after Artisan death

---

## Epic 4.3.3 — Integration & Tuning

### Story 4.3.3.1 — Religion integration test

Run a short sim with a character whose personality traits and Piety skill guarantee the
thresholds are met after several years. Assert:
- FoundReligion goal forms
- ReligionFounded event fires within 5 years after goal forms

### Story 4.3.3.2 — Replacement integration test

Build a settlement at pop > crystal_pop_artisan, spawn+kill an Artisan, run population
phase again. Assert a new Artisan is spawned.

---

## File Checklist

| File | Change |
|------|--------|
| `WorldEngine.Sim/Entities/Characters/GoalData.cs` | Add `GoalType.FoundReligion` |
| `WorldEngine.Sim/Entities/Characters/Tier1Character.cs` | Add `LastReligionFoundedYear` property |
| `WorldEngine.Sim/Simulation/Phases/CharacterBehaviorPhase.cs` | Annual trigger + progression for FoundReligion |
| `WorldEngine.Sim/Config/ReligionConfig.cs` | New config record |
| `WorldEngine.Sim/Config/SimConfig.cs` | Add `Religion` property |
| `config/sim_config.toml` | Add `[religion]` section, `ReligionFounded` to always_record_types |
| `WorldEngine.Sim/Events/EventPayloads.cs` | Add `ReligionFoundedPayload` |
| `WorldEngine.Sim/Simulation/Phases/PopulationDynamicsPhase.cs` | Living-specialist check in TryCrystallize |
| `WorldEngine.Sim/Persistence/WorldStateDto.cs` | Add `LastReligionFoundedYear` to EntityDto |
| `WorldEngine.Sim/Persistence/WorldStateMapper.cs` | Map new field |
| `WorldEngine.Tests/ReligionSpecialistTests.cs` | All tests from epics 4.3.1–4.3.3 |
