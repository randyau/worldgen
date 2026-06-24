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

    // ─── Resource stores ─────────────────────────────────────────────────────
    // Fraction of per-tick surplus that goes into stores (rest consumed immediately).
    public float StoreAccumulateRate            { get; set; } = 0.4f;

    // Vital resources (food/water): max store depth in "seasons of supply" scaled with population.
    public float StoreMaxSeasonsPerKPop         { get; set; } = 2.0f;
    public float StoreMinSeasons                { get; set; } = 0.5f;

    // Per-resource spoilage rates (fraction lost per tick regardless of draws).
    public float FoodSpoilageRate               { get; set; } = 0.002f;  // ~500 ticks to fully spoil
    public float WaterSpoilageRate              { get; set; } = 0.010f;  // cisterns evaporate faster
    public float WealthSpoilageRate             { get; set; } = 0.0001f; // gold/gems essentially permanent
    public float StockpileSpoilageRate          { get; set; } = 0.0005f; // iron/timber decay slowly

    // Wealth resources accumulate at a fraction of the raw ledger supply per tick.
    // For non-vital resources (minerals, gold, timber), the ledger value is absolute supply;
    // this rate controls how fast it banks into persistent stores.
    public float WealthAccumulateRate           { get; set; } = 0.2f;

    // Fraction of ALL stores destroyed per point of raid damage (granaries/vaults burn).
    public float StoreRaidDestructionPerDamage  { get; set; } = 0.008f;
}
