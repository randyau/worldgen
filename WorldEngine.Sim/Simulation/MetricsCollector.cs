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

        foreach (var s in settlements.Values)
        {
            worldPop      += s.Population;
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
        int warsActive = 0;

        foreach (var civ in civs.Values)
        {
            if (civ.IsCollapsed) collapsedCivs++;
            else                 activeCivs++;

            // Count each war pair once: only count from the civ that has a higher
            // numeric ID to avoid double-counting.
            foreach (var (enemy, _) in civ.WarsAgainst)
                if (civ.Id.Value > enemy.Value)
                    warsActive++;
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

        // ── Deaths YTD — DECISION ─────────────────────────────────────────────
        // Deaths-by-cause (starvation, disease, war, other) are not currently
        // tracked as discrete fields on WorldState or accumulated per tick.
        // CharacterDied events carry a cause string in PayloadJson, but reading
        // the event log per year would require a DB query on the sim thread
        // (expensive, couples metrics to the gated event log).
        // Omitted: deaths_starvation, deaths_disease, deaths_war, deaths_other.
        // To add: accumulate death counts by cause in MetricsAccumulator when
        // CharacterBehaviorPhase emits CharacterDied pending events.

        store.WriteMetricsRow(new YearlyMetricsRow(
            year:                    world.CurrentYear,
            worldPopulation:         worldPop,
            activeCivs:              activeCivs,
            collapsedCivs:           collapsedCivs,
            settlementsTotal:        settTotal,
            settlementsFoundedYtd:   settFoundedYtd,
            settlementsAbandonedYtd: settAbandonedYtd,
            settlementsConqueredYtd: settConqueredYtd,
            deathsStarvation:        0, // DECISION: see above — not tracked
            deathsDisease:           0, // DECISION: see above — not tracked
            deathsWar:               0, // DECISION: see above — not tracked
            deathsOther:             0, // DECISION: see above — not tracked
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
            meanWellbeing:           meanWellbeing));

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
    }
}

