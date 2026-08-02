# Event Log Exploration Queries
**Purpose:** SQL queries for validating that the simulation is generating coherent history.  
**Database:** `world.db` (SQLite) — open with any SQLite tool (DB Browser for SQLite, sqlite3 CLI, DBeaver, etc.)  
**When to use:** After running the sim for any significant period to verify the event log is functioning correctly.

---

## EventType Integer Reference

The `Type` column stores integer values. Use these constants in WHERE clauses:

<!-- GENERATED:enums — DO NOT EDIT BELOW THIS LINE; run python3 scripts/gen-enum-tables.py -->
```
-- Environmental (1000–1099)
  1001 = VolcanicEruption      1002 = EarthquakeOccurred  
  1003 = WildfireOccurred      1004 = FloodOccurred       
  1005 = DroughtBegan          1006 = DroughtEnded        
  1007 = SeaLevelChanged       1008 = BiomeChanged        
  1009 = ClimateShifted        1010 = ResourceRecovered   

-- Beast events (2000–2099)
  2001 = BeastSpawned         2002 = BeastAwakened      
  2003 = BeastDied            2004 = BeastSlain         
  2005 = BeastReproduced      2006 = BeastEncountered   
  2007 = BeastAttackedChar  

-- Character lifecycle (3000–3099)
  3001 = CharacterBorn           3002 = CharacterDied         
  3003 = CharacterMarried        3004 = CharacterExiled       
  3005 = CharacterGrieved        3006 = CharacterFlourishing  
  3007 = CharacterSpiraling    

-- Character actions (3100–3199)
  3101 = AllianceFormed            3102 = AllianceBroken          
  3103 = WarDeclared               3104 = WarEnded                
  3105 = BattleOccurred            3106 = RivalryFormed           
  3107 = Negotiated                3108 = ArtworkCreated          
  3109 = GoalFormed                3110 = GoalResolved            
  3111 = DebtIncurred              3112 = DebtForgiven            
  3113 = RivalryPlacated           3114 = CharacterDefected       
  3115 = RivalsReconciled          3116 = RivalryEscalatedToFeud  
  3117 = CharacterEstranged        3118 = OathBroken              

-- Civilization/Settlement (3200–3299)
  3201 = CivilizationFounded      3202 = CivilizationCollapsed  
  3203 = SettlementFounded        3204 = SettlementDestroyed    
  3205 = SuccessionOccurred       3206 = SettlementStraining    
  3207 = SettlementConquered      3208 = TerritoryExpanded      
  3209 = TerritoryLost            3210 = ImprovementBuilt       
  3211 = CivTraitAcquired         3212 = CivSplintered          

-- Population events (3400–3499)
  3401 = SettlementGrew         3402 = SettlementShrank     
  3403 = SettlementAbandoned    3404 = DiseaseOutbreak      
  3405 = DiseaseRecovered       3406 = WildlifeRaid         
  3407 = SuccessionCrisis     

-- Tier 2 character events (3300–3399)
  3301 = AppointedToRole           3302 = DismissedFromRole       
  3303 = MerchantTradeCompleted    3304 = ScholarDiscovery        
  3305 = PhysicianHealed           3306 = CharacterCrystallized   
  3307 = ArtisanCrafted          

-- Artifact (6000–6999)
  6001 = ArtifactCreated        6002 = ArtifactDestroyed    
  6003 = ArtifactTransferred  

-- Artifacts/Religion (4000–4999)
  4003 = ReligionFounded    4004 = ReligionExtinct  

-- God Mode (9000+)
  9001 = GodModeDisasterTriggered     9002 = GodModeEntitySpawned       
  9003 = GodModeCharacterCreated      9004 = GodModeArtifactPlaced      
  9005 = GodModeCivilizationForced    9006 = GodModeCharacterNudged     

-- Emissary/Diplomatic (5000–5999)
  5001 = EmissaryDispatched          5002 = EmissaryLost              
  5003 = ReligiousEmissaryArrived    5004 = CivIntelGathered          
  5101 = SeaVoyageEmbarked           5102 = SeaVoyageCompleted        

-- VerbClass integers
0 = Creation    1 = Destruction    2 = Transformation    3 = Transfer    4 = Conflict    5 = Maintenance    6 = Interaction

-- TierInvolvement integers
0 = Background    1 = Character    2 = Regional    3 = Headline

-- PopulationImpact integers
0 = None    1 = Minor    2 = Moderate    3 = Major    4 = Catastrophic

-- Season integers
0 = Spring    1 = Summer    2 = Autumn    3 = Winter
```
<!-- GENERATED:enums END -->

---

## Quick Health Checks

Run these first to verify the event system is working at all.

```sql
-- Total event count
SELECT COUNT(*) as total_events FROM Events;

-- Events by year (are events being generated continuously?)
SELECT Year, COUNT(*) as events_per_year 
FROM Events 
GROUP BY Year 
ORDER BY Year;

-- Most recent 20 events
SELECT Id, Type, Year, Season, TierInvolvement, VerbClass, LocationX, LocationY
FROM Events 
ORDER BY Id DESC 
LIMIT 20;

-- Are there any Headline events?
SELECT * FROM Events 
WHERE TierInvolvement = 3  -- 3 = Headline
ORDER BY Year;
```

---

## Event Type Distribution

Use these to identify noise categories and tune the event gate.

```sql
-- Event type distribution (most common first)
-- Use this to identify candidate types for suppressed_types in sim_config.toml
SELECT Type, COUNT(*) as count,
       ROUND(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM Events), 2) as percentage
FROM Events 
GROUP BY Type 
ORDER BY count DESC;

-- Event type distribution by tier
SELECT Type, TierInvolvement, COUNT(*) as count
FROM Events 
GROUP BY Type, TierInvolvement
ORDER BY TierInvolvement DESC, count DESC;

-- Verb class distribution
SELECT VerbClass, COUNT(*) as count,
       ROUND(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM Events), 2) as percentage
FROM Events 
GROUP BY VerbClass 
ORDER BY count DESC;

-- How many events are at each tier?
SELECT 
    CASE TierInvolvement 
        WHEN 0 THEN 'Background'
        WHEN 1 THEN 'Character'
        WHEN 2 THEN 'Regional'
        WHEN 3 THEN 'Headline'
    END as tier_name,
    COUNT(*) as count,
    ROUND(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM Events), 2) as percentage
FROM Events 
GROUP BY TierInvolvement 
ORDER BY TierInvolvement DESC;
```

---

## Temporal Analysis

Use these to check that events are distributed sensibly across time.

```sql
-- Events per decade (is history evenly distributed?)
SELECT (Year / 10) * 10 as decade, COUNT(*) as events
FROM Events 
GROUP BY decade 
ORDER BY decade;

-- Headline events timeline
SELECT Year, Season, Type, LocationX, LocationY, 
       substr(PayloadJson, 1, 100) as payload_preview
FROM Events 
WHERE TierInvolvement = 3
ORDER BY Year, Season;

-- Events by season (are seasonal patterns visible?)
SELECT 
    CASE Season WHEN 0 THEN 'Spring' WHEN 1 THEN 'Summer' 
                WHEN 2 THEN 'Autumn' WHEN 3 THEN 'Winter' END as season_name,
    COUNT(*) as count
FROM Events 
GROUP BY Season;

-- Disaster frequency over time (check for reasonable disaster rates)
SELECT (Year / 100) * 100 as century, Type, COUNT(*) as count
FROM Events 
WHERE VerbClass = 1  -- 1 = Destruction
GROUP BY century, Type 
ORDER BY century, count DESC;
```

---

## Spatial Analysis

Use these to check that events are geographically distributed.

```sql
-- Events by location (are events spread across the world?)
SELECT LocationX, LocationY, COUNT(*) as events
FROM Events 
WHERE LocationX IS NOT NULL
GROUP BY LocationX, LocationY
ORDER BY events DESC
LIMIT 50;

-- Which regions have the most activity?
-- Bucketing into 10-tile regions
SELECT (LocationX / 10) * 10 as region_x, 
       (LocationY / 10) * 10 as region_y,
       COUNT(*) as events
FROM Events 
WHERE LocationX IS NOT NULL
GROUP BY region_x, region_y
ORDER BY events DESC
LIMIT 20;

-- Are there any events with no location (world-spanning events)?
SELECT Type, COUNT(*) as count
FROM Events 
WHERE LocationX IS NULL
GROUP BY Type
ORDER BY count DESC;
```

---

## Causal Graph Validation

Use these to verify the causal graph is being built correctly.

```sql
-- Are causal edges being created?
SELECT COUNT(*) as total_edges FROM CausalEdges;

-- Average causal predecessors per event
SELECT AVG(pred_count) as avg_predecessors
FROM (
    SELECT SuccessorId, COUNT(*) as pred_count 
    FROM CausalEdges 
    GROUP BY SuccessorId
);

-- Events with the most causal successors (these are the pivotal moments)
SELECT e.Id, e.Type, e.Year, e.Season, e.TierInvolvement, COUNT(ce.SuccessorId) as successor_count
FROM Events e
JOIN CausalEdges ce ON e.Id = ce.PredecessorId
GROUP BY e.Id
ORDER BY successor_count DESC
LIMIT 10;

-- Causal chain depth: how deep do chains go?
WITH RECURSIVE depth AS (
    SELECT SuccessorId as event_id, 1 as d
    FROM CausalEdges
    WHERE PredecessorId NOT IN (SELECT SuccessorId FROM CausalEdges)  -- root events
    UNION ALL
    SELECT ce.SuccessorId, d.d + 1
    FROM CausalEdges ce
    JOIN depth d ON ce.PredecessorId = d.event_id
    WHERE d.d < 20  -- safety limit
)
SELECT MAX(d) as max_chain_depth, AVG(d) as avg_chain_depth FROM depth;

-- Walk a specific causal chain (replace @eventId with an actual event ID)
-- Run this to verify "tell me what led to this" works conceptually
WITH RECURSIVE chain AS (
    SELECT e.*, 0 as depth
    FROM Events e
    WHERE e.Id = 42  -- replace with actual event ID
    UNION ALL
    SELECT e.*, chain.depth + 1
    FROM Events e
    JOIN CausalEdges ce ON e.Id = ce.PredecessorId
    JOIN chain ON ce.SuccessorId = chain.Id
    WHERE chain.depth < 10
)
SELECT Id, Type, Year, Season, TierInvolvement, depth
FROM chain 
ORDER BY depth DESC, Year;
```

---

## Significance Classification Validation

Use these to verify the significance classifier is working correctly.

```sql
-- IsFirstOfKind distribution
SELECT IsFirstOfKind, COUNT(*) as count
FROM Events 
GROUP BY IsFirstOfKind;

-- Do IsFirstOfKind events trend toward higher tiers? (They should)
SELECT IsFirstOfKind, TierInvolvement, COUNT(*) as count
FROM Events 
GROUP BY IsFirstOfKind, TierInvolvement
ORDER BY IsFirstOfKind, TierInvolvement;

-- PopulationImpact distribution
SELECT 
    CASE PopulationImpact 
        WHEN 0 THEN 'None' WHEN 1 THEN 'Minor' WHEN 2 THEN 'Moderate'
        WHEN 3 THEN 'Major' WHEN 4 THEN 'Catastrophic'
    END as impact_name,
    COUNT(*) as count
FROM Events 
GROUP BY PopulationImpact 
ORDER BY PopulationImpact;

-- Are Catastrophic population events always Headline? (They should be)
SELECT PopulationImpact, TierInvolvement, COUNT(*) as count
FROM Events 
WHERE PopulationImpact >= 3
GROUP BY PopulationImpact, TierInvolvement;

-- God Mode events (verify they're all being recorded)
SELECT Type, COUNT(*) as count
FROM Events 
WHERE IsGodMode = 1
GROUP BY Type;
```

---

## Environmental Simulation Validation (Milestone 1)

Use these specifically during Milestone 1 to verify the environmental sim is working.

```sql
-- Which disaster types have fired? (Type stored as integer — see reference table above)
SELECT Type,
       CASE Type
           WHEN 1001 THEN 'VolcanicEruption'   WHEN 1002 THEN 'EarthquakeOccurred'
           WHEN 1003 THEN 'WildfireOccurred'    WHEN 1004 THEN 'FloodOccurred'
           WHEN 1005 THEN 'DroughtBegan'        WHEN 1006 THEN 'DroughtEnded'
           WHEN 1007 THEN 'SeaLevelChanged'     WHEN 1008 THEN 'BiomeChanged'
           WHEN 1009 THEN 'ClimateShifted'      WHEN 1010 THEN 'ResourceRecovered'
       END as type_name,
       COUNT(*) as occurrences, 
       MIN(Year) as first_occurrence,
       MAX(Year) as last_occurrence
FROM Events 
WHERE Type BETWEEN 1001 AND 1010
GROUP BY Type
ORDER BY occurrences DESC;

-- Disaster frequency: roughly how often does each disaster type fire?
-- Compare to sim_config.toml probabilities
SELECT Type, COUNT(*) as total,
       (SELECT MAX(Year) FROM Events) as sim_years,
       ROUND(COUNT(*) * 1.0 / (SELECT MAX(Year) FROM Events), 3) as per_year
FROM Events 
WHERE Type IN (1001, 1002, 1003, 1004, 1007)  -- volcanic, earthquake, wildfire, flood, sea level
GROUP BY Type;

-- Sea level change events over time
SELECT Year, Season, substr(PayloadJson, 1, 200) as payload
FROM Events 
WHERE Type = 1007  -- SeaLevelChanged
ORDER BY Year;

-- Biome change events (are biomes drifting over time?)
SELECT Year, LocationX, LocationY, substr(PayloadJson, 1, 200) as payload
FROM Events 
WHERE Type = 1008  -- BiomeChanged
ORDER BY Year
LIMIT 50;

-- Climate shift events
SELECT Year, COUNT(*) as shifts_this_year
FROM Events 
WHERE Type = 1009  -- ClimateShifted
GROUP BY Year
ORDER BY Year;

-- Most disaster-prone locations
SELECT LocationX, LocationY, COUNT(*) as disaster_count
FROM Events 
WHERE Type IN (1001, 1002, 1003, 1004)  -- volcanic, earthquake, wildfire, flood
  AND LocationX IS NOT NULL
GROUP BY LocationX, LocationY
ORDER BY disaster_count DESC
LIMIT 20;
```

---

## Performance Monitoring

Use these to monitor database growth and query performance.

```sql
-- Database statistics
SELECT 
    (SELECT COUNT(*) FROM Events) as total_events,
    (SELECT COUNT(*) FROM CausalEdges) as total_causal_edges,
    (SELECT page_count * page_size FROM pragma_page_count(), pragma_page_size()) as db_size_bytes;

-- Events per tick (average) — detect if event generation is too prolific
SELECT AVG(events_per_tick) as avg_events_per_tick
FROM (
    SELECT Tick, COUNT(*) as events_per_tick 
    FROM Events 
    GROUP BY Tick
);

-- Identify the most event-heavy ticks (potential performance issues)
SELECT Tick, Year, Season, COUNT(*) as event_count
FROM Events 
GROUP BY Tick 
ORDER BY event_count DESC 
LIMIT 10;

-- Check index usage (run EXPLAIN QUERY PLAN on your most common queries)
-- Example:
EXPLAIN QUERY PLAN 
SELECT * FROM Events WHERE TierInvolvement = 3 ORDER BY Year;
```

---

## Sample History Narratives

Use these to generate human-readable summaries for quick sanity checking.

```sql
-- A year in history: all events from a specific year formatted readably
SELECT 
    CASE Season WHEN 0 THEN 'Spring' WHEN 1 THEN 'Summer' 
                WHEN 2 THEN 'Autumn' WHEN 3 THEN 'Winter' END as season,
    CASE TierInvolvement WHEN 3 THEN '[HEADLINE]' WHEN 2 THEN '[Regional]' 
                         WHEN 1 THEN '[Character]' ELSE '[Background]' END as tier,
    Type,
    CASE WHEN LocationX IS NOT NULL THEN '(' || LocationX || ',' || LocationY || ')' 
         ELSE 'world' END as location
FROM Events 
WHERE Year = 250  -- replace with a year you want to inspect
ORDER BY Season, TierInvolvement DESC;

-- The most significant events in history (potential "headline moments")
SELECT Year, Season, Type, LocationX, LocationY,
       CASE TierInvolvement WHEN 3 THEN 'HEADLINE' WHEN 2 THEN 'Regional' END as tier,
       substr(PayloadJson, 1, 150) as summary
FROM Events 
WHERE TierInvolvement >= 2
ORDER BY TierInvolvement DESC, Year
LIMIT 50;

-- What happened in a specific region over time?
-- Replace 180, 200, 120, 140 with actual tile coordinate ranges
SELECT Year, Season, Type, TierInvolvement, substr(PayloadJson, 1, 100) as payload
FROM Events 
WHERE LocationX BETWEEN 180 AND 200
  AND LocationY BETWEEN 120 AND 140
ORDER BY Year, Season;
```

---

## Tuning the Event Gate

The goal is a database that is:
- Large enough to tell interesting stories
- Small enough to stay performant
- Free from obvious noise that adds no narrative value

### Suggested Tuning Process

1. Run sim for 500 years with default (permissive) gate
2. Run the event type distribution query
3. Identify types in the top 10 most common that:
   - Never appear in causal chains
   - Are always TierInvolvement = Background
   - Never become IsFirstOfKind = true
   - Have PayloadJson with no interesting content
4. Add those types to `suppressed_types` in `sim_config.toml`
5. Run again and check the distribution changed as expected
6. Repeat until comfortable

### Target Distribution (approximate)
A well-tuned gate should produce roughly:
- Headline events: ~5-20 per century
- Regional events: ~50-200 per century  
- Character events: ~500-2000 per century (if character system is active)
- Background events: as few as possible given the gate settings

These are rough targets, not hard requirements. The right number is whatever produces history that feels rich but not overwhelming when browsed.

---

## Common Issues and Diagnostics

```sql
-- Issue: No events being generated
-- Check: Is Phase 7 running?
SELECT MIN(Tick) as first_tick, MAX(Tick) as last_tick, COUNT(DISTINCT Tick) as ticks_with_events
FROM Events;

-- Issue: Events all have TierInvolvement = 0 (Background)
-- Check: Are the classifier rules being applied?
SELECT Type, COUNT(*) as count
FROM Events 
WHERE TierInvolvement > 0
GROUP BY Type
ORDER BY count DESC;

-- Issue: CausalEdges table is empty
-- Check: Is Phase 7's causal edge insertion running?
SELECT COUNT(*) FROM CausalEdges;

-- Issue: PayloadJson is empty or null
-- Check: Are event payloads being populated?
SELECT Type, COUNT(*) as total, 
       SUM(CASE WHEN PayloadJson = '{}' OR PayloadJson = '' THEN 1 ELSE 0 END) as empty_payload
FROM Events 
GROUP BY Type
HAVING empty_payload > 0;

-- Issue: Database growing too fast
-- Check: Which event types are generating the most data?
SELECT Type, COUNT(*) as count, 
       SUM(LENGTH(PayloadJson)) as total_payload_bytes,
       AVG(LENGTH(PayloadJson)) as avg_payload_bytes
FROM Events 
GROUP BY Type
ORDER BY total_payload_bytes DESC
LIMIT 20;
```

---

## Artifact Queries (M5)

Use these to explore the artifact system. The `ArtifactCreated` (6001) and `ArtifactDestroyed` (6002)
event types carry artifact data in `PayloadJson`. The payload for `ArtifactCreated` contains at minimum:

```json
{ "ArtifactId": 123, "Name": "Blade of Caelen", "Category": "Weapon",
  "Quality": 0.95, "CreatorName": "Caelen the Unyielding", "CreatedYear": 41 }
```

`ArtifactTransferred` events (if emitted by the artifact system) carry `ArtifactId`, `FromOwner`,
and `ToOwner` fields and form the lineage chain.

### Artifact creation timeline

```sql
-- All artifacts created, in order
SELECT Year, Season,
       json_extract(PayloadJson, '$.ArtifactId')   as artifact_id,
       json_extract(PayloadJson, '$.Name')          as name,
       json_extract(PayloadJson, '$.Category')      as category,
       json_extract(PayloadJson, '$.Quality')       as quality,
       json_extract(PayloadJson, '$.CreatorName')   as creator,
       LocationX, LocationY
FROM Events
WHERE Type = 6001   -- ArtifactCreated
ORDER BY Year, Season;

-- How many artifacts have been created per century?
SELECT (Year / 100) * 100 as century, COUNT(*) as artifacts_created
FROM Events
WHERE Type = 6001
GROUP BY century
ORDER BY century;

-- Masterworks only (quality >= 0.9)
SELECT Year,
       json_extract(PayloadJson, '$.Name')        as name,
       json_extract(PayloadJson, '$.CreatorName') as creator,
       ROUND(json_extract(PayloadJson, '$.Quality'), 3) as quality
FROM Events
WHERE Type = 6001
  AND CAST(json_extract(PayloadJson, '$.Quality') as REAL) >= 0.9
ORDER BY quality DESC;
```

### Transfer / lineage chain

Reconstruct the full ownership history of a specific artifact by walking
`ArtifactCreated` and all subsequent `ArtifactTransferred` events sharing the same `ArtifactId`.

```sql
-- Full lineage for a specific artifact (replace 123 with the target ArtifactId)
SELECT Year, Season, Type,
       CASE Type
           WHEN 6001 THEN 'Created'
           WHEN 6002 THEN 'Destroyed'
           ELSE 'Transferred'
       END as event_kind,
       json_extract(PayloadJson, '$.Name')      as name,
       json_extract(PayloadJson, '$.FromOwner') as from_owner,
       json_extract(PayloadJson, '$.ToOwner')   as to_owner
FROM Events
WHERE json_extract(PayloadJson, '$.ArtifactId') = 123
ORDER BY Year, Season, Id;

-- How many times has each artifact changed hands?
SELECT json_extract(PayloadJson, '$.ArtifactId') as artifact_id,
       COUNT(*) as transfer_count
FROM Events
WHERE Type NOT IN (6001, 6002)   -- exclude creation/destruction; count only transfers
  AND json_extract(PayloadJson, '$.ArtifactId') IS NOT NULL
GROUP BY artifact_id
ORDER BY transfer_count DESC
LIMIT 20;

-- Most-travelled artifacts (created in one region, last seen elsewhere)
-- Requires ArtifactCreated + location of the last transfer event
SELECT
    c.Year as created_year,
    json_extract(c.PayloadJson, '$.Name') as name,
    c.LocationX as origin_x, c.LocationY as origin_y,
    t.LocationX as last_x,  t.LocationY as last_y,
    ABS(c.LocationX - t.LocationX) + ABS(c.LocationY - t.LocationY) as manhattan_dist
FROM Events c
JOIN (
    SELECT json_extract(PayloadJson, '$.ArtifactId') as aid,
           MAX(Id) as last_event_id
    FROM Events
    WHERE json_extract(PayloadJson, '$.ArtifactId') IS NOT NULL
    GROUP BY aid
) latest ON json_extract(c.PayloadJson, '$.ArtifactId') = latest.aid
JOIN Events t ON t.Id = latest.last_event_id
WHERE c.Type = 6001
ORDER BY manhattan_dist DESC
LIMIT 10;
```

### Most-coveted / highest-quality artifacts

```sql
-- Top 20 highest-quality artifacts ever created
SELECT Year,
       json_extract(PayloadJson, '$.ArtifactId')   as id,
       json_extract(PayloadJson, '$.Name')          as name,
       json_extract(PayloadJson, '$.Category')      as category,
       json_extract(PayloadJson, '$.CreatorName')   as creator,
       ROUND(json_extract(PayloadJson, '$.Quality'), 3) as quality
FROM Events
WHERE Type = 6001
ORDER BY quality DESC
LIMIT 20;

-- Artifacts that were eventually destroyed (lost/legendary items)
SELECT c.Year as created_year,
       json_extract(c.PayloadJson, '$.Name')    as name,
       json_extract(c.PayloadJson, '$.Quality') as quality,
       d.Year as destroyed_year
FROM Events c
JOIN Events d ON json_extract(d.PayloadJson, '$.ArtifactId')
              = json_extract(c.PayloadJson, '$.ArtifactId')
WHERE c.Type = 6001
  AND d.Type = 6002
ORDER BY (d.Year - c.Year) DESC;   -- longest-lived at top

-- Average quality by category
SELECT json_extract(PayloadJson, '$.Category') as category,
       COUNT(*)                                 as count,
       ROUND(AVG(CAST(json_extract(PayloadJson, '$.Quality') as REAL)), 3) as avg_quality,
       ROUND(MAX(CAST(json_extract(PayloadJson, '$.Quality') as REAL)), 3) as max_quality
FROM Events
WHERE Type = 6001
GROUP BY category
ORDER BY avg_quality DESC;
```
