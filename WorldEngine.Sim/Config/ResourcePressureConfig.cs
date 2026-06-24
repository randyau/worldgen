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
}
