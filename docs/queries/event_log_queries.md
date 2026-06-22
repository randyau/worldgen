# Event Log Exploration Queries
**Purpose:** SQL queries for validating that the simulation is generating coherent history.  
**Database:** `world.db` (SQLite) — open with any SQLite tool (DB Browser for SQLite, sqlite3 CLI, DBeaver, etc.)  
**When to use:** After running the sim for any significant period to verify the event log is functioning correctly.

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
-- Which disaster types have fired?
SELECT Type, COUNT(*) as occurrences, 
       MIN(Year) as first_occurrence,
       MAX(Year) as last_occurrence
FROM Events 
WHERE Type IN ('VolcanicEruption', 'EarthquakeOccurred', 'FloodOccurred', 
               'DroughtBegan', 'DroughtEnded', 'WildfireOccurred', 'SeaLevelChanged',
               'BiomeChanged', 'ClimateShifted')
GROUP BY Type
ORDER BY occurrences DESC;

-- Disaster frequency: roughly how often does each disaster type fire?
-- Compare to sim_config.toml probabilities
SELECT Type, COUNT(*) as total,
       (SELECT MAX(Year) FROM Events) as sim_years,
       ROUND(COUNT(*) * 1.0 / (SELECT MAX(Year) FROM Events), 3) as per_year
FROM Events 
WHERE Type IN ('VolcanicEruption', 'EarthquakeOccurred', 'FloodOccurred', 
               'WildfireOccurred', 'SeaLevelChanged')
GROUP BY Type;

-- Sea level change events over time
SELECT Year, Season, substr(PayloadJson, 1, 200) as payload
FROM Events 
WHERE Type = 'SeaLevelChanged'
ORDER BY Year;

-- Biome change events (are biomes drifting over time?)
SELECT Year, LocationX, LocationY, substr(PayloadJson, 1, 200) as payload
FROM Events 
WHERE Type = 'BiomeChanged'
ORDER BY Year
LIMIT 50;

-- Climate shift events
SELECT Year, COUNT(*) as shifts_this_year
FROM Events 
WHERE Type = 'ClimateShifted'
GROUP BY Year
ORDER BY Year;

-- Most disaster-prone locations
SELECT LocationX, LocationY, COUNT(*) as disaster_count
FROM Events 
WHERE Type IN ('VolcanicEruption', 'EarthquakeOccurred', 'FloodOccurred', 
               'WildfireOccurred')
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
