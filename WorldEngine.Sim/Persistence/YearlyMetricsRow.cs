namespace WorldEngine.Sim.Persistence;

/// <summary>
/// One row of the <c>yearly_metrics</c> table. Mutable class with settable properties
/// so Dapper can materialize it from SQLite query results (SQLite returns INTEGER as Int64
/// and REAL as Double; Dapper requires a matching default constructor for property-based
/// deserialization).
/// Written by <see cref="WorldEngine.Sim.Simulation.MetricsCollector"/> once per in-game year.
/// </summary>
public sealed class YearlyMetricsRow
{
    // ── Constructor for creation (sim thread writes) ──────────────────────────
    public YearlyMetricsRow() { } // required for Dapper deserialization

    public YearlyMetricsRow(
        int   year,
        int   worldPopulation,
        int   activeCivs,
        int   collapsedCivs,
        int   settlementsTotal,
        int   settlementsFoundedYtd,
        int   settlementsAbandonedYtd,
        int   settlementsConqueredYtd,
        int   deathsStarvation,
        int   deathsDisease,
        int   deathsWar,
        int   deathsOther,
        float meanFoodRatio,
        float minFoodRatio,
        int   settlementsInShortage,
        int   settlementsInCrisis,
        int   activeDiseases,
        int   warsActive,
        int   warsDeclaredYtd,
        int   warsEndedTruceYtd,
        int   warsEndedConquestYtd,
        int   tier1Count,
        int   tier2Count,
        int   goalsFormedYtd,
        int   goalsResolvedYtd,
        float meanWellbeing)
    {
        Year                   = year;
        WorldPopulation        = worldPopulation;
        ActiveCivs             = activeCivs;
        CollapsedCivs          = collapsedCivs;
        SettlementsTotal       = settlementsTotal;
        SettlementsFoundedYtd  = settlementsFoundedYtd;
        SettlementsAbandonedYtd = settlementsAbandonedYtd;
        SettlementsConqueredYtd = settlementsConqueredYtd;
        DeathsStarvation       = deathsStarvation;
        DeathsDisease          = deathsDisease;
        DeathsWar              = deathsWar;
        DeathsOther            = deathsOther;
        MeanFoodRatio          = meanFoodRatio;
        MinFoodRatio           = minFoodRatio;
        SettlementsInShortage  = settlementsInShortage;
        SettlementsInCrisis    = settlementsInCrisis;
        ActiveDiseases         = activeDiseases;
        WarsActive             = warsActive;
        WarsDeclaredYtd        = warsDeclaredYtd;
        WarsEndedTruceYtd      = warsEndedTruceYtd;
        WarsEndedConquestYtd   = warsEndedConquestYtd;
        Tier1Count             = tier1Count;
        Tier2Count             = tier2Count;
        GoalsFormedYtd         = goalsFormedYtd;
        GoalsResolvedYtd       = goalsResolvedYtd;
        MeanWellbeing          = meanWellbeing;
    }

    // ── Properties (snake_case aliases handled by Dapper column mapping) ──────
    public int   Year                    { get; set; }
    public int   WorldPopulation         { get; set; }
    public int   ActiveCivs              { get; set; }
    public int   CollapsedCivs           { get; set; }
    public int   SettlementsTotal        { get; set; }
    public int   SettlementsFoundedYtd   { get; set; }
    public int   SettlementsAbandonedYtd { get; set; }
    public int   SettlementsConqueredYtd { get; set; }
    public int   DeathsStarvation        { get; set; }
    public int   DeathsDisease           { get; set; }
    public int   DeathsWar               { get; set; }
    public int   DeathsOther             { get; set; }
    public float MeanFoodRatio           { get; set; }
    public float MinFoodRatio            { get; set; }
    public int   SettlementsInShortage   { get; set; }
    public int   SettlementsInCrisis     { get; set; }
    public int   ActiveDiseases          { get; set; }
    public int   WarsActive              { get; set; }
    public int   WarsDeclaredYtd         { get; set; }
    public int   WarsEndedTruceYtd       { get; set; }
    public int   WarsEndedConquestYtd    { get; set; }
    public int   Tier1Count              { get; set; }
    public int   Tier2Count              { get; set; }
    public int   GoalsFormedYtd          { get; set; }
    public int   GoalsResolvedYtd        { get; set; }
    public float MeanWellbeing           { get; set; }
}
