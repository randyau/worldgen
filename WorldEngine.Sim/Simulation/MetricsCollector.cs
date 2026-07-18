using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Persistence;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Samples world state once per in-game year and writes a row to the <c>yearly_metrics</c>
/// table in world.db. Runs on the sim thread only; all reads are direct WorldState accesses —
/// no LINQ over tiles, no DB reads, no cross-thread calls.
///
/// Called by <see cref="PhaseRunner"/> at the annual tick boundary, after all phases have run
/// for that year, when <c>[sim_loop] metrics_enabled = true</c> in config.
///
/// Columns that cannot be computed cheaply without restructuring phases are omitted and
/// annotated with // DECISION comments below.
/// </summary>
public static class MetricsCollector
{
    /// <summary>
    /// Collects all metrics for the completed year and writes them to the DB.
    /// Must be called exactly once per year on the sim thread.
    /// </summary>
    public static void Sample(WorldState world, MetricsAccumulator acc, EventStore store)
    {
        var civs        = world.Civilizations;
        var settlements = world.Settlements;

        // ── Population ────────────────────────────────────────────────────────
        int worldPop      = 0;
        int activeCivs    = 0;
        int collapsedCivs = 0;
        int settTotal     = settlements.Count;
        int settInShortage  = 0;
        int settInCrisis    = 0;
        int activeDiseases  = 0;

        double foodRatioSum = 0.0;
        float  foodRatioMin = 1.0f;

        double unrestSum = 0.0;

        foreach (var s in settlements.Values)
        {
            worldPop      += s.Population;
            unrestSum     += s.Unrest;
            float fr       = s.FoodPressureRatio;
            foodRatioSum  += fr;
            if (fr < foodRatioMin) foodRatioMin = fr;

            // DECISION: "shortage" = food ratio below 1.0 (supply < demand);
            // "crisis" = ratio below 0.5 (severe deficit). These thresholds
            // match the SettlementStraining heuristic in ResourcePressurePhase.
            if (fr < 1.0f) settInShortage++;
            if (fr < 0.5f) settInCrisis++;

            if (s.IsInfected) activeDiseases++;
        }

        float meanFoodRatio = settTotal > 0
            ? (float)(foodRatioSum / settTotal)
            : 1.0f;

        // ── Civilizations ─────────────────────────────────────────────────────
        int warsActive           = 0;
        int maxCitiesPerCiv      = 0;
        int totalCitiesAllCivs   = 0;

        foreach (var civ in civs.Values)
        {
            if (civ.IsCollapsed) collapsedCivs++;
            else
            {
                activeCivs++;
                int civCities = civ.SettlementCount + civ.ColonyCount;
                totalCitiesAllCivs += civCities;
                if (civCities > maxCitiesPerCiv) maxCitiesPerCiv = civCities;
            }

            // Count each war pair once: only count from the civ that has a higher
            // numeric ID to avoid double-counting.
            foreach (var (enemy, _) in civ.WarsAgainst)
                if (civ.Id.Value > enemy.Value)
                    warsActive++;
        }

        float meanCitiesPerCiv = activeCivs > 0 ? (float)totalCitiesAllCivs / activeCivs : 0f;

        // ── Territorial contact (S4; consumed by D5 war tuning) ──────────────
        // Count distinct civ pairs whose territory tiles share an edge. One scan of the
        // territory map per year — O(territory tiles × 2 neighbor checks).
        var borderPairs = new HashSet<(int, int)>();
        {
            int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
            foreach (var (tile, cityTile) in world.TerritoryMap)
            {
                if (!settlements.TryGetValue(cityTile, out var ownerStub)) continue;
                int ownerCiv = ownerStub.CivId.Value;

                // Only east + south neighbors — each adjacency examined exactly once.
                for (int i = 0; i < 2; i++)
                {
                    int nx = i == 0 ? ((tile.X + 1) % w + w) % w : tile.X;
                    int ny = i == 0 ? tile.Y : tile.Y + 1;
                    if (ny >= h) continue;
                    var nCoord = new Core.TileCoord(nx, ny);
                    if (!world.TerritoryMap.TryGetValue(nCoord, out var nCity)) continue;
                    if (!settlements.TryGetValue(nCity, out var nStub)) continue;
                    int nCiv = nStub.CivId.Value;
                    if (nCiv == ownerCiv) continue;
                    borderPairs.Add(ownerCiv < nCiv ? (ownerCiv, nCiv) : (nCiv, ownerCiv));
                }
            }
        }

        // ── Characters ────────────────────────────────────────────────────────
        int tier1Count      = world.Entities.Characters.Count;
        int tier2Count      = world.Entities.Tier2Chars.Count;
        int goalsFormedYtd  = acc.GoalsFormedYtd;
        int goalsResolvedYtd = acc.GoalsResolvedYtd;

        float meanWellbeing = 0f;
        if (tier1Count > 0)
        {
            float wbSum = 0f;
            foreach (var c in world.Entities.Characters)
                wbSum += c.Wellbeing;
            meanWellbeing = wbSum / tier1Count;
        }

        // ── Wars YTD from accumulator ─────────────────────────────────────────
        int warsDeclaredYtd      = acc.WarsDeclaredYtd;
        int warsEndedTruceYtd    = acc.WarsEndedTruceYtd;
        int warsEndedConquestYtd = acc.WarsEndedConquestYtd;

        // ── Settlement YTD changes from accumulator ──────────────────────────
        int settFoundedYtd   = acc.SettlementsFoundedYtd;
        int settAbandonedYtd = acc.SettlementsAbandonedYtd;
        int settConqueredYtd = acc.SettlementsConqueredYtd;

        store.WriteMetricsRow(new YearlyMetricsRow(
            year:                    world.CurrentYear,
            worldPopulation:         worldPop,
            activeCivs:              activeCivs,
            collapsedCivs:           collapsedCivs,
            settlementsTotal:        settTotal,
            settlementsFoundedYtd:   settFoundedYtd,
            settlementsAbandonedYtd: settAbandonedYtd,
            settlementsConqueredYtd: settConqueredYtd,
            deathsStarvation:        acc.DeathsStarvationYtd,
            deathsDisease:           acc.DeathsDiseaseYtd,
            deathsWar:               acc.DeathsWarYtd,
            deathsOther:             acc.DeathsOtherYtd,
            meanFoodRatio:           meanFoodRatio,
            minFoodRatio:            settTotal > 0 ? foodRatioMin : 1.0f,
            settlementsInShortage:   settInShortage,
            settlementsInCrisis:     settInCrisis,
            activeDiseases:          activeDiseases,
            warsActive:              warsActive,
            warsDeclaredYtd:         warsDeclaredYtd,
            warsEndedTruceYtd:       warsEndedTruceYtd,
            warsEndedConquestYtd:    warsEndedConquestYtd,
            tier1Count:              tier1Count,
            tier2Count:              tier2Count,
            goalsFormedYtd:          goalsFormedYtd,
            goalsResolvedYtd:        goalsResolvedYtd,
            meanWellbeing:           meanWellbeing,
            maxCitiesPerCivActual:   maxCitiesPerCiv,
            meanCitiesPerCiv:        meanCitiesPerCiv,
            secessionsYtd:           acc.SecessionsYtd,
            meanUnrest:              settTotal > 0 ? (float)(unrestSum / settTotal) : 0f,
            civBorderPairs:          borderPairs.Count));

        // Reset YTD counters for the next year
        acc.ResetYtd();
    }
}

/// <summary>
/// Accumulates year-to-date counters on the sim thread between annual samples.
/// Updated by PhaseRunner as events are classified (before batching/gating).
/// Uses simple int fields — no locking needed (sim thread only).
/// </summary>
public sealed class MetricsAccumulator
{
    public int SettlementsFoundedYtd   { get; set; }
    public int SettlementsAbandonedYtd { get; set; }
    public int SettlementsConqueredYtd { get; set; }
    public int WarsDeclaredYtd         { get; set; }
    public int WarsEndedTruceYtd       { get; set; }
    public int WarsEndedConquestYtd    { get; set; }
    public int GoalsFormedYtd          { get; set; }
    public int GoalsResolvedYtd        { get; set; }
    /// <summary>Count of CivSplintered events this year (S2 splinter mechanic).</summary>
    public int SecessionsYtd           { get; set; }

    // Death-by-cause counters — incremented from CharacterDeathPayload.Cause string.
    // Cause strings are set in CharacterBehaviorPhase.KillCharacter; this is the
    // authoritative mapping from cause string to metric bucket.
    public int DeathsStarvationYtd { get; set; }
    public int DeathsDiseaseYtd    { get; set; }
    public int DeathsWarYtd        { get; set; }
    public int DeathsOtherYtd      { get; set; }

    /// <summary>
    /// Extracts the "Cause" value from a CharacterDeathPayload JSON string and increments
    /// the appropriate death-cause counter.  Uses string search rather than JSON deserialization
    /// to avoid reflection on the internal payload record type.
    /// Cause strings defined in CharacterBehaviorPhase.KillCharacter:
    ///   "starvation"   → DeathsStarvation
    ///   "disease"      → DeathsDisease
    ///   "war", "violence", "killed by …" → DeathsWar
    ///   "old age", "wounds", anything else → DeathsOther
    /// </summary>
    public void IncrementDeathCauseFromJson(string payloadJson)
    {
        // Locate "Cause":"<value>" in the JSON — fast path, avoids full parse.
        const string marker = "\"Cause\":\"";
        int start = payloadJson.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { DeathsOtherYtd++; return; }
        start += marker.Length;
        int end = payloadJson.IndexOf('"', start);
        if (end <= start) { DeathsOtherYtd++; return; }

        var cause = payloadJson.AsSpan(start, end - start);
        if (cause.Equals("starvation", StringComparison.OrdinalIgnoreCase))
            DeathsStarvationYtd++;
        else if (cause.Equals("disease", StringComparison.OrdinalIgnoreCase))
            DeathsDiseaseYtd++;
        else if (cause.Equals("war", StringComparison.OrdinalIgnoreCase)
              || cause.Equals("violence", StringComparison.OrdinalIgnoreCase)
              || cause.StartsWith("killed by", StringComparison.OrdinalIgnoreCase))
            DeathsWarYtd++;
        else
            DeathsOtherYtd++;  // "old age", "wounds", and anything else
    }

    /// <summary>Resets all YTD fields to zero. Called by MetricsCollector after sampling.</summary>
    public void ResetYtd()
    {
        SettlementsFoundedYtd   = 0;
        SettlementsAbandonedYtd = 0;
        SettlementsConqueredYtd = 0;
        WarsDeclaredYtd         = 0;
        WarsEndedTruceYtd       = 0;
        WarsEndedConquestYtd    = 0;
        GoalsFormedYtd          = 0;
        GoalsResolvedYtd        = 0;
        SecessionsYtd           = 0;
        DeathsStarvationYtd     = 0;
        DeathsDiseaseYtd        = 0;
        DeathsWarYtd            = 0;
        DeathsOtherYtd          = 0;
    }
}

