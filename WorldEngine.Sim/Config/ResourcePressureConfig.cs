namespace WorldEngine.Sim.Config;

public sealed class ResourcePressureConfig
{
    public float ShortageThreshold     { get; set; } = 0.6f;  // food ratio below this = shortage
    public float CrisisThreshold       { get; set; } = 0.3f;  // food ratio below this = crisis → flee goals
    public float AcquireGoalIntensity  { get; set; } = 0.7f;
    public float FleeGoalIntensity     { get; set; } = 0.5f;
    public int   StrainEventCooldown   { get; set; } = 8;     // ticks between SettlementStraining events
    public float PopulationCapPerTile  { get; set; } = 100f;  // population count that equals "full demand" for one tile
    // Minimum effective moisture fraction used in food calculation, regardless of season.
    // Prevents winter moisture crashes from zeroing out food supply for well-established settlements.
    // 0.25 = even in the driest winter, 25% of base moisture is assumed available (root storage, wells, etc.)
    public float FoodMoistureFloor     { get; set; } = 0.25f;

    // ─── Food stores ─────────────────────────────────────────────────────────
    // Fraction of per-tick surplus accumulated into stores each tick (rest is consumed immediately).
    public float StoreAccumulateRate   { get; set; } = 0.4f;
    // Max store depth in "seasons of food supply" per 1000 population.
    // e.g. 2.0 at pop 500 → max 1.0 season stored; at pop 2000 → max 4.0 seasons.
    public float StoreMaxSeasonsPerKPop { get; set; } = 2.0f;
    // Hard floor on max store size so even tiny settlements can hold something.
    public float StoreMinSeasons       { get; set; } = 0.5f;
    // Fraction of stores that spoil per tick (decay toward 0 even without draws).
    public float StoreSpoilageRate     { get; set; } = 0.002f;
    // Fraction of stores destroyed per point of raid damage (granaries burn).
    public float StoreRaidDestructionPerDamage { get; set; } = 0.008f;
}
