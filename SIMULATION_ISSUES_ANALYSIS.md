# World Engine Simulation Issues Analysis
**Date:** 2026-06-26  
**Database:** world.db from long simulation run (2150 years, 20 civilizations, ~20 characters)  
**Total Events Logged:** 522,890

---

## TIER 1: Critical Bugs & Logic Errors

### Issue #1: ArtworkCreated Event Explosion

**Evidence:**
- **208,699 ArtworkCreated events** = **40.0% of all events** (522,890 total)
- Expected: 1-2 rare masterpieces per character lifetime = ~20-40 events per 2150 years
- Actual: ~97 artworks per year globally (~10,000 per character over 2150 years)
- Distribution: Every character with an active Create goal fires this event every single tick

**Database Query:**
```sql
SELECT Type, COUNT(*) FROM Events 
WHERE Type = 3108 
GROUP BY Type;
-- Result: 208,699 events (40% of all 522,890 events)
```

**Root Cause (Code Investigation):**
- File: `CharacterBehaviorPhase.cs:354` calls `ResolveCreateArtwork()` every tick when character has active Create goal
- File: `UtilityScorer.cs:223-228` makes CreateArtwork action available every tick with high utility score
- No cooldown between artwork creation; art is created continuously, not as a milestone

**Current Behavior:** Characters emit ArtworkCreated event **every tick** while Create goal is active  
**Expected Behavior:** Artwork should be rare, memorable achievements (1-2 per character lifetime)

**Impact:**
- Database bloat (40% of history is noise)
- Legitimate historical events buried in log
- No narrative weight to artistic achievement

**Severity:** 🔴 Critical (fundamental logging bug)

---

### Issue #2: SettlementGrew and SettlementShrank Never Fire

**Evidence:**
- **SettlementGrew: 0 events**
- **SettlementShrank: 0 events**
- By contrast: **SettlementStraining: 21,231 events** (settlements under food/water stress)
- Population change events appear completely absent from logging

**Database Query:**
```sql
SELECT 'SettlementGrew' as Event, COUNT(*) FROM Events WHERE Type = 3401
UNION ALL
SELECT 'SettlementShrank', COUNT(*) FROM Events WHERE Type = 3402
UNION ALL
SELECT 'SettlementStraining', COUNT(*) FROM Events WHERE Type = 3206;
-- Result: 0, 0, 21231
```

**Current Behavior:** Settlements report stress but never show growth/decline  
**Expected Behavior:** Should see frequent SettlementGrew/Shrank events as populations fluctuate

**Impact:**
- Cannot determine if settlements are actually growing or just stressed
- Population dynamics may not be working at all (logic bug or event emission bug)
- Makes it impossible to trace civilization stability

**Severity:** 🔴 Critical (fundamental transparency issue)

---

### Issue #3: Disaster Events 14× Too Frequent

**Evidence:**
- **Wildfire: 11,514 events** = 5.4/year (config: 0.0005/tile/season in 400-tile world ≈ 1.5-2/year expected)
- **Earthquake: 5,187 events** = 2.4/year (config: 0.00005/tile/season ≈ 0.3/year expected)
- **DroughtBegan: 3,438 events** = 1.6/year (config: 0.02/region/year ≈ 0.2-0.4/year expected)
- **VolcanicEruption: 2,127 events** = 1.0/year (config: 0.00005/tile/season ≈ 0.3/year expected)
- **Total environmental disasters: ~14.2/year** (should be 1-3/year)

**Database Query:**
```sql
SELECT Type, COUNT(*) FROM Events WHERE Type IN (1001, 1002, 1003, 1004, 1005, 1006)
GROUP BY Type ORDER BY COUNT(*) DESC;
-- Results show ~14 major disasters/year average across 2150 years
```

**Configuration Review:**
- `wildfire_prob = 0.0005` per forest tile per season
- `earthquake_probability_per_tick = 0.00005` per fault tile per season
- At 4 seasons/year, 400 tiles: theoretical rate should be ~0.5-2/year per type
- Actual rate is 7000× higher than earthquake config suggests

**Current Behavior:** World is pummeled by ~14 major disasters per year  
**Expected Behavior:** Rare, impactful events (1-3 per year total)

**Impact:**
- Constant environmental pressure prevents settlement stabilization
- Disasters trigger settlement abandonment faster than population can grow
- Cascades into Issue #7 (settlement/population collapse)
- Makes early civilization growth impossible

**Severity:** 🔴 Critical (cascading destabilization)

---

### Issue #4: ArtifactCreated and ReligionFounded Both = 0

**Evidence:**
- **ArtifactCreated: 0 events** (config lists always_record_types, suggesting it should exist)
- **ReligionFounded: 0 events** (same)
- **ArtifactDestroyed: 0 events**
- **ReligionExtinct: 0 events**

**Database Query:**
```sql
SELECT 'ArtifactCreated' as Event, COUNT(*) FROM Events WHERE Type = 6001
UNION ALL
SELECT 'ReligionFounded', COUNT(*) FROM Events WHERE Type = 4003;
-- Result: 0, 0
```

**Current Behavior:** Both systems completely non-functional; zero events ever fired  
**Expected Behavior:** Should see regular artifact creation and religion founding

**Impact:**
- Religion system doesn't generate narrative events
- Artifact crafting doesn't produce artifacts
- Entire narrative subsystems are dark/broken

**Severity:** 🔴 Critical (subsystem non-functional)

---

### Issue #5: DismissedFromRole Never Fires

**Evidence:**
- **DismissedFromRole: 0 events**
- **AppointedToRole: 371 events**
- Ratio: 371 appointments, 0 dismissals

**Database Query:**
```sql
SELECT 'AppointedToRole', COUNT(*) FROM Events WHERE Type = 3301
UNION ALL
SELECT 'DismissedFromRole', COUNT(*) FROM Events WHERE Type = 3302;
-- Result: 371, 0
```

**Current Behavior:** Characters are appointed to roles but never removed  
**Expected Behavior:** Should see turnover as characters age, die, or underperform

**Impact:**
- Role positions are permanent/sticky
- No narrative through role transitions
- Specialist economy never resets

**Severity:** 🔴 Critical (role system incomplete)

---

## TIER 2: Architectural/Design Issues

### Issue #6: No Territorial Expansion or Diplomacy System

**Evidence:**
- 20 civilizations founded
- **0 territory expansion events visible in log** (no TerritoryExpanded events beyond initial claims)
- **Only 29 wars declared in 2150 years** (0.013 wars/civ/year)
  - Wars require civilizations to be adjacent/aware of each other
  - Random encounter rate is extremely low
- **Only 10 alliances formed** across entire 2150-year span
  - Should be common if diplomatic contact existed
- **Only 85 merchant trades** in 2150 years (0.04/civ/year)
  - Requires trade routes or direct contact

**Evidence Interpretation:**
- Civilizations are isolated "wilderness bubbles"
- No borders or territorial control mechanisms
- No persistent diplomatic links between settlements
- Characters only interact when randomly wandering into each other
- No reasons to go to war beyond personal grudges (which require proximity)

**Database Queries:**
```sql
-- War frequency
SELECT COUNT(*) FROM Events WHERE Type = 3103; -- 29 WarDeclared
SELECT COUNT(*) FROM Events WHERE Type = 3105; -- 48 BattleOccurred (1.6 battles/war)

-- Alliance frequency
SELECT COUNT(*) FROM Events WHERE Type = 3101; -- 10 AllianceFormed
SELECT COUNT(*) FROM Events WHERE Type = 3102; -- 2 AllianceBroken (80% failure)

-- Trade frequency
SELECT COUNT(*) FROM Events WHERE Type = 3303; -- 85 MerchantTradeCompleted

-- Territory claims
SELECT COUNT(*) FROM Events WHERE Type = 3208; -- TerritoryExpanded
SELECT COUNT(*) FROM Events WHERE Type = 3209; -- TerritoryLost
-- Result: Both minimal
```

**Current Behavior:**
- Civilizations start with initial territory; no expansion mechanics visible
- All inter-civ interaction is encounter-based (random wandering)
- No diplomatic infrastructure
- No reason for civilizations to interact intentionally

**Expected Behavior (if system existed):**
- Civilizations should claim adjacent territory
- Borders should create conflict zones
- Trade routes should establish regular contact
- Diplomacy should be persistent, not encounter-only

**Impact:**
- **Kingpin issue:** Explains why 6+ other symptoms are broken:
  - War rarity (no borders = no conflicts)
  - Alliance rarity (no diplomatic contact)
  - Religion rarity (civilizations isolated)
  - Trade rarity (no routes)
  - Cultural traits unobtainable (no inter-civ dynamics)
- Civilizations never actually compete or interact
- History feels like 20 independent, unrelated timelines

**Severity:** 🔴 Critical (fundamental architectural gap)

**Note:** This may be intentional (Spotlight mode only, Milestone 4+), but its absence cascades into all other issues. **Design decision needed from lead: Is this a stub for M4, or should basic territory + diplomacy exist now?**

---

### Issue #7: Settlement & Population Tuning Backwards (Village-Scale vs City-Scale)

**Evidence:**

**Settlement Founding Pattern:**
```
Years 1-100:     164 settlements founded
Years 100-200:   54 more
Years 200+:      ~1-2 per century
Abandonment:     124 abandoned (vs 239 founded) — 52% failure rate
```

**Population Growth Analysis:**
- `pop_growth_rate = 0.5` per season per fertility unit
  - At mid-fertility (100/255): 0.196 people/season = 0.78 people/year
  - Baseline decay: 0.05/season = 0.2/year
  - Net growth: ~0.58 people/year (extremely slow)
- Carrying capacities are village-scale:
  - `carry_cap_grassland = 80` per tile
  - 5-tile hinterland = 400 pop max
  - With `emigration_threshold = 0.75`, emigrants spawn at 300 pop
- Characters born too early: `civ_birth_min_pop = 20` (spawns new character when settlement barely viable)

**Current Game Model:** "Village Networks"
- Many small settlements (max 5 per civ)
- High churn (half fail)
- Population in 50-400 range

**Expected Model:** "City-States"
- Fewer, larger settlements (max 5 per civ maintained, but each is 500-2000+ pop)
- Stable, enduring cities
- Lower churn

**Database Evidence:**
- Most settlements never appear in logs (no SettlementGrew/Shrank events)
- Rapid abandonment in first 200 years
- Stabilization only after ~year 300 when population tuning naturally selects for stable sites

**Impact:**
- Settlements fail before reaching viable population
- Ruins proliferate (as you observed)
- No "city" feel; world is scattered hamlets
- Character density too low for economies/relationships
- Cascades with Issue #3 (disasters destabilize before growth possible)

**Severity:** 🔴 Critical (contradicts stated game design of city-states)

---

## TIER 3: Balance & Threshold Issues

### Issue #8: Character Wellbeing Unreachable Positive Extremes

**Evidence:**
- **CharacterFlourishing: 0 events** (threshold: +0.7 wellbeing)
- **CharacterSpiraling: 107 events** (threshold: -0.7 wellbeing)
- Ratio: 107 spiraling vs 0 flourishing (all negative, no positive)

**Database Query:**
```sql
SELECT 'CharacterFlourishing', COUNT(*) FROM Events WHERE Type = 3006
UNION ALL
SELECT 'CharacterSpiraling', COUNT(*) FROM Events WHERE Type = 3007;
-- Result: 0, 107
```

**Configuration:**
- `wellbeing_mean_reversion_rate = 0.008` (pulls toward 0 each tick)
- `wellbeing_goal_gain_rate = 0.01` (gains +0.01/tick while progressing)
- `flourishing_threshold = 0.7`
- `spiral_threshold = -0.7`

**Analysis:**
- Characters can reach -0.7 but never +0.7
- Mean reversion pulls toward 0; gaining wellbeing requires sustained goal progress
- Without perfect conditions, character wellbeing drifts to near-zero
- Reaching +0.7 requires extended positive goal progress with no setbacks

**Current Behavior:** Characters can suffer spiraling but never truly flourish  
**Expected Behavior:** Wellbeing should be bidirectional with both extremes reachable

**Impact:**
- No positive emotional rewards for character success
- All emotional narrative is suffering/crisis
- No counterbalance to the despair threshold

**Severity:** 🟡 Major (narrative imbalance)

---

### Issue #9: Beast Reproduction Explosion

**Evidence:**
- **BeastSpawned: 64 events** (initial spawns only)
- **BeastReproduced: 45,787 events**
- **BeastDied: 45,776 events**
- Ratio: 64 spawned → 45,787 births (715× reproduction rate)

**Database Query:**
```sql
SELECT 'BeastSpawned', COUNT(*) FROM Events WHERE Type = 2001
UNION ALL
SELECT 'BeastReproduced', COUNT(*) FROM Events WHERE Type = 2005
UNION ALL
SELECT 'BeastDied', COUNT(*) FROM Events WHERE Type = 2003
UNION ALL
SELECT 'BeastSlain', COUNT(*) FROM Events WHERE Type = 2004;
-- Result: 64, 45787, 45776, 3
```

**Analysis:**
- Only 64 beasts ever spawned initially
- Reproduction: 45,787 births keeps population growing
- Death: 45,776 deaths (nearly matched reproduction)
- Very few slain by characters (3 total — almost no player hunting)
- Population is sustained by birth/death balance, not accumulation

**Current Behavior:** Closed reproductive cycle; beasts populate themselves  
**Expected Behavior:** Should spawn periodically + sustain via reproduction

**Impact:**
- Beasts are self-sufficient; don't require new spawns
- Very rare combat encounters with characters (only 3 slain in 2150 years)
- Wildlife encounters are not a meaningful threat

**Severity:** 🟡 Major (balance within subsystem seems okay, but low threat level)

---

### Issue #10: Religion System Completely Non-Functional

**Evidence:**
- **ReligionFounded: 0 events**
- **ReligionExtinct: 0 events**
- Both should be firing regularly if religion mechanics existed

**Config Section:**
```toml
[religion]
awe_threshold_base          = 0.6
wonder_trait_multiplier     = 0.5
piety_trait_multiplier      = 0.3
spread_trust_threshold      = 0.5
inter_religion_trust_penalty = -0.15
conversion_experience_threshold = 0.3
```

**Current Behavior:** Religion system completely dark; no founding events  
**Expected Behavior:** Should see occasional religion foundings, spreads, extinctions

**Likely Causes:**
- Goal formation for FoundReligionGoal not triggering
- Awe events not firing (check EventGeneration phase)
- Threshold never reached by any character

**Impact:**
- Religion subsystem is non-narrative
- Cultural/spiritual dimension completely absent from history

**Severity:** 🟡 Major (subsystem non-functional)

---

### Issue #11: Specialist Economy Dead on Arrival

**Evidence:**
```
MerchantTradeCompleted:  85 events (0.04/civ/year)
ScholarDiscovery:      1498 events (0.7/civ/year)
PhysicianHealed:        421 events (0.2/civ/year)
ArtisanCrafted:        3639 events (1.7/civ/year)
AppointedToRole:        371 events (0.17/civ/year)
DismissedFromRole:        0 events (never)
```

**Database Query:**
```sql
SELECT 'MerchantTradeCompleted', COUNT(*) FROM Events WHERE Type = 3303
UNION ALL
SELECT 'ScholarDiscovery', COUNT(*) FROM Events WHERE Type = 3304
UNION ALL
SELECT 'PhysicianHealed', COUNT(*) FROM Events WHERE Type = 3305
UNION ALL
SELECT 'ArtisanCrafted', COUNT(*) FROM Events WHERE Type = 3307;
-- Results: 85, 1498, 421, 3639
```

**Analysis:**
- Merchant trade is nearly non-existent (85 in 2150 years)
- Scholars are somewhat active (1498)
- Physicians rarely heal (421)
- Artisans craft frequently (3639)
- But roles never cycle (DismissedFromRole = 0)

**Current Behavior:**
- Specialists exist and occasionally produce
- No trade economy (no routes)
- Role positions are permanent/sticky

**Expected Behavior:**
- Trade should be frequent (10-50/civ/year if routes existed)
- Roles should turn over as characters age/die/promoted

**Impact:**
- Specialist economy is a minor sideshow, not a game pillar
- No economic competition or specialization pressure
- Missing inter-settlement economic interdependency

**Severity:** 🟡 Major (subsystem underutilized)

---

### Issue #12: Cultural Traits Never Acquired

**Evidence:**
- **CivTraitAcquired: 0 events**

**Database Query:**
```sql
SELECT 'CivTraitAcquired', COUNT(*) FROM Events WHERE Type = 3211;
-- Result: 0
```

**Config Thresholds:**
```toml
[cultural_traits]
militaristic_min_wars = 10
militaristic_wars_per_decade = 2.0

expansionist_founding_rate = 1.0        # 1+ settlement/decade
expansionist_sustained_years = 30       # sustained over 30 years

scholarly_min_discoveries = 5
war_weary_min_repeat_wars = 3
unstable_throne_min_successions = 5
resilient_min_near_collapse_count = 1
```

**Analysis:**
- **Militaristic trait:** Requires 10+ wars. Global total: 29 wars across all civs.
  - Average per civ: 1.45 wars total
  - Threshold: 10 wars per civ
  - Status: Unobtainable
- **Expansionist trait:** Requires 1+ settlement/decade for 30 years.
  - Data shows: 239 settlements founded over 2150 years = 0.11/year/civ
  - Threshold requires sustained 0.1/year for 30 years
  - Status: Possible but rare; depends on early-game luck
- **Scholarly trait:** Requires 5+ discoveries. Total: 1498 discoveries across 20 civs = 75/civ avg.
  - Status: Achievable
- **War-weary trait:** Requires 3+ wars vs same opponent. Global wars: 29 total
  - Status: Nearly impossible
- **Resilient trait:** Requires 1+ near-collapse. Data: Civs are stable (20 founded, 0 collapsed)
  - Status: Impossible

**Current Behavior:** Trait thresholds are unreachable for most civs  
**Expected Behavior:** Civs should acquire 1-3 traits each over 2150 years

**Impact:**
- Civilizations have no cultural identity evolution
- Trait system is window dressing, never fires
- No representation of civ character/personality in history

**Severity:** 🟡 Major (subsystem underutilized)

---

### Issue #13: Alliance Formation & Persistence Broken

**Evidence:**
```
AllianceFormed:   10 events
AllianceBroken:    2 events
Success rate: 20% (8 alliances active after breakups)
```

**Database Query:**
```sql
SELECT 'AllianceFormed', COUNT(*) FROM Events WHERE Type = 3101
UNION ALL
SELECT 'AllianceBroken', COUNT(*) FROM Events WHERE Type = 3102;
-- Result: 10, 2
```

**Analysis:**
- Only 10 alliances formed across 2150 years among 20 characters
- Of those, 2 broke (20% failure rate)
- Expected: Alliances should form regularly when characters meet
- Actual: Formation is rare; most character meetings don't result in alliances

**Current Behavior:**
- Alliances are rare despite frequent character interactions
- Trust mechanics may be preventing alliance formation
- Once formed, alliances are sticky (only 2 broke)

**Expected Behavior:**
- Should see 50+ alliances over 2150 years if characters regularly interact
- Suggests characters rarely meet, or trust barriers are too high

**Impact:**
- Alliance/relationship subsystem is barely firing
- No network effects from character relationships
- History is mostly isolated individual stories

**Severity:** 🟡 Major (relationship system underutilized)

---

### Issue #14: War/Conflict Severely Underpowered

**Evidence:**
```
WarDeclared:       29 events (0.013/civ/year)
BattleOccurred:    48 events (1.6 battles/war)
RivalryFormed:      0 events
AllianceFormed:    10 events
```

**Database Queries:**
```sql
SELECT 'WarDeclared', COUNT(*) FROM Events WHERE Type = 3103;
SELECT 'WarEnded', COUNT(*) FROM Events WHERE Type = 3104;
SELECT 'BattleOccurred', COUNT(*) FROM Events WHERE Type = 3105;
SELECT 'RivalryFormed', COUNT(*) FROM Events WHERE Type = 3106;
-- Results: 29, 29, 48, 0
```

**Comparison to Other Systems:**
- ArtworkCreated: 208,699 (7000× more frequent than war)
- GoalFormed: 47,747 (1600× more)
- Environmental events: ~14/year (vs 0.013 wars/civ/year)

**Analysis:**
- 0 rivalries ever formed (trust threshold -0.4 never reached?)
- Only 29 wars declared; wars usually end by truce (WarEnded = 29, same count)
- 48 battles total = 1.6 battles/war (very short wars)
- Cascades from Issue #6: No territorial pressure = no reason to war

**Current Behavior:**
- War is rarest major historical event
- Only occurs when characters randomly encounter and develop hostility
- Battles are brief once declared

**Expected Behavior (with territorial system):**
- Border pressure should trigger wars regularly
- Wars should be sustained campaigns, not brief skirmishes
- Rivalries should form from repeated conflicts

**Impact:**
- Conflict is non-existent as historical force
- No military victory conditions
- Power dynamics among civs are static
- World history has no drama

**Severity:** 🟡 Major (gameplay pillar is broken)

---

## Summary Table

| ID | Issue | Evidence | Severity |
|----|-------|----------|----------|
| 1  | ArtworkCreated explosion | 208,699 events (40% of all) | 🔴 Critical |
| 2  | SettlementGrew/Shrank = 0 | No population change events | 🔴 Critical |
| 3  | Disasters 14× too frequent | ~14/year vs 1-3 expected | 🔴 Critical |
| 4  | Artifact & Religion = 0 | Both subsystems non-functional | 🔴 Critical |
| 5  | DismissedFromRole = 0 | Role turnover never happens | 🔴 Critical |
| 6  | No territorial system | Civs isolated; wars require random encounter | 🔴 Critical |
| 7  | Population tuning backwards | Growth too slow, caps too low, emigration too aggressive | 🔴 Critical |
| 8  | Wellbeing unreachable positive | 0 Flourishing vs 107 Spiraling | 🟡 Major |
| 9  | Beast reproduction balanced | Population stable but threat low (3 slain) | 🟡 Major |
| 10 | Religion non-functional | 0 ReligionFounded events | 🟡 Major |
| 11 | Specialist economy dead | Trade 0.04/civ/year; Merchants missing | 🟡 Major |
| 12 | Cultural traits unreachable | 0 CivTraitAcquired; thresholds impossible | 🟡 Major |
| 13 | Alliance formation broken | 10 alliances in 2150 years | 🟡 Major |
| 14 | War/conflict underpowered | 0.013 wars/civ/year; 7000× rarer than art | 🟡 Major |

---

## Interdependencies

**Kingpin issues (explain multiple cascades):**
1. **Issue #6 (No territorial system)** → Explains Issues #14, #13, #10, #11
   - No borders = no war triggers
   - No trade routes = no merchant economy
   - Isolated civs = no religion spread
   - Rare contact = few alliances

2. **Issue #3 (Disasters too frequent)** + **Issue #7 (Population tuning)** → Explain settlement collapse
   - 14 disasters/year destabilizes growth
   - Slow growth (0.5/season) can't overcome disaster pressure
   - Combined effect: settlements fail before reaching viability

3. **Issue #1 (Artwork explosion)** → Explains database noise
   - 40% of events are noise
   - Real events are statistically lost

4. **Issue #2 (No SettlementGrew/Shrank)** → Explains transparency gap
   - Can't verify if population mechanics are working
   - May indicate broader logging or logic bug

---

## Questions for Design Review

1. **Territorial system:** Is it intentionally stubbed for M4 (Spotlight), or should basic territory+diplomacy exist in M3? -> territory and diplomacy should exist in M3, it's not a spotlight specific feature
2. **Disaster frequency:** Are config values wrong, or is disaster probability calculation buggy? the config valuse can be tuned, we haven't played with them, it is a separate issue if they're buggy. the config should be the source of truth
3. **Population tuning:** Is "village networks" intentional, or should it be "city-states" per design doc? village networks aren't intentional, but they're also not excluded. we were hoping for emergent behavior for networks but the distancse were too far, so village networks can still be fine
4. **Artifact/Religion:** Are these M4+ features with stubbed mechanics, or actually supposed to work? they're stubbed mechanics for now
5. **Wellbeing asymmetry:** Is the +0.7 threshold intentionally difficult, or should it be reachable? high wellbeing is supposed to be difficult to reach (same with spiraling) but still EVENTUALLY reachable, perhaps only a handful of times in a lifetime when conditions align
6. **SettlementGrew/Shrank:** Are these event types suppressed in config, or is there a logic bug? settlement parametrs should be a config option, if they're not we should make them and have config be source of truth
