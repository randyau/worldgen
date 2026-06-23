namespace WorldEngine.Sim.Config;

public sealed class CharacterSimConfig
{
    public int   InitialCount           { get; set; } = 20;
    public int   MaxAgeSeasonsMin       { get; set; } = 80;
    public int   MaxAgeSeasonsMax       { get; set; } = 200;
    public int   MaxHealth              { get; set; } = 100;

    // Needs decay per season (all 0.0–1.0 range)
    public float NeedsDecaySafety       { get; set; } = 0.05f;
    public float NeedsDecayFood         { get; set; } = 0.08f;
    public float NeedsDecayShelter      { get; set; } = 0.04f;
    public float NeedsDecayBelonging    { get; set; } = 0.03f;
    public float NeedsDecayStatus       { get; set; } = 0.03f;
    public float NeedsDecayPurpose      { get; set; } = 0.04f;
    public float NeedsDecaySpiritual    { get; set; } = 0.02f;

    // Utility weights
    public float NeedsWeight            { get; set; } = 0.5f;
    public float GoalsWeight            { get; set; } = 0.3f;
    public float PersonalityWeight      { get; set; } = 0.2f;

    // Softmax temperature (scales with Curiosity trait)
    public float SoftmaxTempMin         { get; set; } = 0.5f;
    public float SoftmaxTempMax         { get; set; } = 2.0f;

    // Misc
    public int   PerceptionRadius       { get; set; } = 3;
    public int   HealthPerSeasonHeal    { get; set; } = 5;
    public int   CombatDamageBase       { get; set; } = 20;
    public int   MinFertilityToSettle   { get; set; } = 100;

    // Tier 2 tuning
    public int   Tier2PerPopulation     { get; set; } = 10;  // one Tier2 per this many pop
    public int   Tier2MaxAgeSeasonsMin  { get; set; } = 60;
    public int   Tier2MaxAgeSeasonsMax  { get; set; } = 120;
    public float Tier2CrystalChance     { get; set; } = 0.001f;  // per season
    public float Tier2NeedsDecayFood    { get; set; } = 0.06f;
    public float Tier2NeedsDecaySafety  { get; set; } = 0.04f;
    public float Tier2NeedsDecayBelonging { get; set; } = 0.03f;
    public float Tier2NeedsDecayStatus   { get; set; } = 0.04f;
}
