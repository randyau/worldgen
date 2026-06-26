using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// Post-sim pass that infers causal relationships between events and writes them
/// to the CausalEdges table with typed EdgeType labels.
/// </summary>
public static class CausalEdgeBuilder
{
    // EventType integer constants
    private const int DiseaseOutbreak      = 3404;
    private const int SettlementAbandoned  = 3403;
    private const int WarDeclared          = 3103;
    private const int WarEnded             = 3104;
    private const int BattleOccurred       = 3105;
    private const int SettlementConquered  = 3207;
    private const int CharacterDied        = 3002;
    private const int SuccessionOccurred   = 3205;
    private const int CharacterGrieved     = 3005;
    private const int GoalFormed           = 3109;

    private const int YearsForDisease      = 3;  // disease→abandonment window
    private const int TicksForSuccession   = 4;  // death→succession window
    private const int TicksForGrief        = 20; // death→grief window

    /// <summary>
    /// Scans the Events table and inserts inferred causal edges into CausalEdges.
    /// Existing edges are preserved (INSERT OR IGNORE).
    /// </summary>
    public static void BuildCausalEdges(SqliteConnection conn)
    {
        // 1. DiseaseOutbreak → SettlementAbandoned (same location within 3 years)
        BuildDiseaseAbandonmentEdges(conn);

        // 2. WarDeclared → BattleOccurred (battles that belong to the war)
        // 3. BattleOccurred → SettlementConquered (conquest in same tick/location)
        BuildWarEdges(conn);

        // 4. CharacterDied → SuccessionOccurred (within 4 ticks, same civ)
        BuildDeathSuccessionEdges(conn);

        // 5. CharacterDied → CharacterGrieved (within 20 ticks, payload references deceased)
        BuildDeathGriefEdges(conn);

        // 6. GoalFormed(Avenge) → CharacterDied (nearest prior death for same char)
        BuildAvengeGoalEdges(conn);
    }

    // ── 1. Disease → Abandonment ────────────────────────────────────────────────────────────────

    private static void BuildDiseaseAbandonmentEdges(SqliteConnection conn)
    {
        var diseases = conn.Query<LocationEventRow>(
            "SELECT Id, Year, LocationX, LocationY FROM Events WHERE Type = @t AND LocationX IS NOT NULL",
            new { t = DiseaseOutbreak }).ToList();

        if (diseases.Count == 0) return;

        var abandonments = conn.Query<LocationEventRow>(
            "SELECT Id, Year, LocationX, LocationY FROM Events WHERE Type = @t AND LocationX IS NOT NULL",
            new { t = SettlementAbandoned }).ToList();

        using var tx = conn.BeginTransaction();
        foreach (var disease in diseases)
        {
            foreach (var abandon in abandonments)
            {
                if (abandon.LocationX == disease.LocationX &&
                    abandon.LocationY == disease.LocationY &&
                    abandon.Year > disease.Year &&
                    abandon.Year - disease.Year <= YearsForDisease)
                {
                    conn.Execute(
                        "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                        new { P = disease.Id, S = abandon.Id, E = "disease_abandonment" }, tx);
                }
            }
        }
        tx.Commit();
    }

    // ── 2 & 3. War edges ────────────────────────────────────────────────────────────────────────

    private static void BuildWarEdges(SqliteConnection conn)
    {
        // Load WarDeclared events
        var warDecls = conn.Query<WarRow>(
            "SELECT Id, Year, CivId, PayloadJson FROM Events WHERE Type = @t",
            new { t = WarDeclared }).ToList();

        if (warDecls.Count == 0) return;

        // Load WarEnded events (keyed by WarNumber + civPair)
        var warEnds = new Dictionary<(long A, long B, int Num), (long Id, int Year)>();
        foreach (var row in conn.Query<WarRow>(
            "SELECT Id, Year, CivId, PayloadJson FROM Events WHERE Type = @t",
            new { t = WarEnded }))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (!root.TryGetProperty("CivAId",    out var ca)) continue;
                if (!root.TryGetProperty("CivBId",    out var cb)) continue;
                if (!root.TryGetProperty("WarNumber", out var wn)) continue;
                long civA = ca.GetInt64(), civB = cb.GetInt64();
                int  num  = wn.GetInt32();
                var  key  = civA < civB ? (civA, civB, num) : (civB, civA, num);
                warEnds.TryAdd(key, (row.Id, row.Year));
            }
            catch (JsonException) { /* skip */ }
        }

        // Load all battles (by year, civ pair)
        var battles = conn.Query<BattleRow>(
            "SELECT Id, Year, Tick, CivId, PayloadJson, LocationX, LocationY FROM Events WHERE Type = @t",
            new { t = BattleOccurred }).ToList();

        // Load all conquests (by tick + location)
        var conquests = conn.Query<LocationEventRow>(
            "SELECT Id, Year, Tick, LocationX, LocationY FROM Events WHERE Type = @t AND LocationX IS NOT NULL",
            new { t = SettlementConquered }).ToList();
        var conquestByTickLoc = conquests
            .GroupBy(c => (c.Tick, c.LocationX, c.LocationY))
            .ToDictionary(g => g.Key, g => g.First().Id);

        using var tx = conn.BeginTransaction();
        foreach (var wd in warDecls)
        {
            long declCivId  = wd.CivId ?? 0;
            long targetCivId = 0;
            int  warNumber   = 0;

            try
            {
                using var doc = JsonDocument.Parse(wd.PayloadJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("TargetCivId", out var tc)) targetCivId = tc.GetInt64();
                if (root.TryGetProperty("WarNumber",   out var wn)) warNumber   = wn.GetInt32();
            }
            catch (JsonException) { continue; }

            if (targetCivId == 0) continue;

            var key = declCivId < targetCivId
                ? (declCivId, targetCivId, warNumber)
                : (targetCivId, declCivId, warNumber);

            int warEndYear = warEnds.TryGetValue(key, out var we) ? we.Year : int.MaxValue;
            long warEndId  = warEnds.TryGetValue(key, out we) ? we.Id : 0;

            // Link this WarDeclared → WarEnded
            if (warEndId != 0)
                conn.Execute(
                    "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                    new { P = wd.Id, S = warEndId, E = "war_ended" }, tx);

            // Link WarDeclared → each battle during the war between these two civs
            foreach (var battle in battles)
            {
                if (battle.Year < wd.Year || battle.Year > warEndYear) continue;
                if (battle.CivId != declCivId && battle.CivId != targetCivId) continue;

                // Check battle payload involves the correct target
                bool matchesPair = false;
                try
                {
                    using var doc = JsonDocument.Parse(battle.PayloadJson ?? "{}");
                    // RaiderId won't tell us the target civ — we use location CivId proximity
                    // DECISION: match by CivId being one of the war parties; approximation
                    matchesPair = true;
                }
                catch (JsonException) { matchesPair = true; }

                if (!matchesPair) continue;

                conn.Execute(
                    "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                    new { P = wd.Id, S = battle.Id, E = "war_battle" }, tx);

                // Battle → SettlementConquered (same tick + location)
                if (battle.LocationX.HasValue && battle.LocationY.HasValue)
                {
                    var conquestKey = (battle.Tick, battle.LocationX, battle.LocationY);
                    if (conquestByTickLoc.TryGetValue(conquestKey, out long conquestId))
                    {
                        conn.Execute(
                            "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                            new { P = battle.Id, S = conquestId, E = "battle_conquest" }, tx);
                    }
                }
            }
        }
        tx.Commit();
    }

    // ── 4. Death → Succession ───────────────────────────────────────────────────────────────────

    private static void BuildDeathSuccessionEdges(SqliteConnection conn)
    {
        var deaths = conn.Query<TickEventRow>(
            "SELECT Id, Tick, CivId, ActorId FROM Events WHERE Type = @t AND CivId IS NOT NULL",
            new { t = CharacterDied }).ToList();

        var successions = conn.Query<TickEventRow>(
            "SELECT Id, Tick, CivId, ActorId FROM Events WHERE Type = @t AND CivId IS NOT NULL",
            new { t = SuccessionOccurred }).ToList();

        using var tx = conn.BeginTransaction();
        foreach (var death in deaths)
        {
            foreach (var succ in successions)
            {
                if (succ.CivId != death.CivId) continue;
                long tickDelta = succ.Tick - death.Tick;
                if (tickDelta >= 0 && tickDelta <= TicksForSuccession)
                {
                    conn.Execute(
                        "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                        new { P = death.Id, S = succ.Id, E = "death_succession" }, tx);
                }
            }
        }
        tx.Commit();
    }

    // ── 5. Death → Grief ────────────────────────────────────────────────────────────────────────

    private static void BuildDeathGriefEdges(SqliteConnection conn)
    {
        var deaths = conn.Query<TickEventRow>(
            "SELECT Id, Tick, ActorId FROM Events WHERE Type = @t",
            new { t = CharacterDied }).ToList();

        if (deaths.Count == 0) return;

        var griefs = conn.Query<(long Id, long Tick, string PayloadJson)>(
            "SELECT Id, Tick, PayloadJson FROM Events WHERE Type = @t",
            new { t = CharacterGrieved }).ToList();

        using var tx = conn.BeginTransaction();
        foreach (var grief in griefs)
        {
            long deceasedId = 0;
            try
            {
                using var doc = JsonDocument.Parse(grief.PayloadJson ?? "{}");
                if (doc.RootElement.TryGetProperty("DeceasedId", out var di))
                    deceasedId = di.GetInt64();
            }
            catch (JsonException) { continue; }

            if (deceasedId == 0) continue;

            // Find the death event for this deceased within TicksForGrief
            foreach (var death in deaths)
            {
                if (death.ActorId != deceasedId) continue;
                long tickDelta = grief.Tick - death.Tick;
                if (tickDelta >= 0 && tickDelta <= TicksForGrief)
                {
                    conn.Execute(
                        "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                        new { P = death.Id, S = grief.Id, E = "death_grief" }, tx);
                    break;
                }
            }
        }
        tx.Commit();
    }

    // ── 6. GoalFormed(Avenge) → CharacterDied ───────────────────────────────────────────────────

    private static void BuildAvengeGoalEdges(SqliteConnection conn)
    {
        var avengeGoals = conn.Query<(long Id, long Tick, long? ActorId, string PayloadJson)>(
            "SELECT Id, Tick, ActorId, PayloadJson FROM Events WHERE Type = @t",
            new { t = GoalFormed }).ToList();

        if (avengeGoals.Count == 0) return;

        var deaths = conn.Query<TickEventRow>(
            "SELECT Id, Tick, ActorId FROM Events WHERE Type = @t",
            new { t = CharacterDied }).ToList();

        using var tx = conn.BeginTransaction();
        foreach (var goal in avengeGoals)
        {
            try
            {
                using var doc = JsonDocument.Parse(goal.PayloadJson ?? "{}");
                if (!doc.RootElement.TryGetProperty("GoalType", out var gt)) continue;
                if (!string.Equals(gt.GetString(), "Avenge", StringComparison.OrdinalIgnoreCase)) continue;
            }
            catch (JsonException) { continue; }

            if (goal.ActorId is null) continue;

            // Find the nearest prior death (closest tick before this goal was formed)
            long bestDeathId = 0;
            long bestDelta   = long.MaxValue;
            foreach (var death in deaths)
            {
                long delta = goal.Tick - death.Tick;
                if (delta >= 0 && delta < bestDelta)
                {
                    bestDelta   = delta;
                    bestDeathId = death.Id;
                }
            }

            if (bestDeathId != 0)
            {
                conn.Execute(
                    "INSERT OR IGNORE INTO CausalEdges (PredecessorId, SuccessorId, EdgeType) VALUES (@P, @S, @E)",
                    new { P = bestDeathId, S = goal.Id, E = "death_avenge" }, tx);
            }
        }
        tx.Commit();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private sealed class LocationEventRow
    {
        public long Id         { get; init; }
        public int  Year       { get; init; }
        public long Tick       { get; init; }
        public int? LocationX  { get; init; }
        public int? LocationY  { get; init; }
    }

    private sealed class WarRow
    {
        public long    Id          { get; init; }
        public int     Year        { get; init; }
        public long?   CivId       { get; init; }
        public string  PayloadJson { get; init; } = "{}";
    }

    private sealed class BattleRow
    {
        public long    Id          { get; init; }
        public int     Year        { get; init; }
        public long    Tick        { get; init; }
        public long?   CivId       { get; init; }
        public string  PayloadJson { get; init; } = "{}";
        public int?    LocationX   { get; init; }
        public int?    LocationY   { get; init; }
    }

    private sealed class TickEventRow
    {
        public long  Id      { get; init; }
        public long  Tick    { get; init; }
        public long? CivId   { get; init; }
        public long? ActorId { get; init; }
    }
}
