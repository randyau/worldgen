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

    // Civilization-born character generation
    public int   CivBirthMinPop         { get; set; } = 30;   // settlement needs this many people
    public float CivBirthChancePerSeason { get; set; } = 0.02f; // ~1 birth per 50 seasons at min pop

    // Territorial aggression — aggressive founders develop negative trust with foreign visitors
    public float TerritorialAggressionMin { get; set; } = 0.55f; // aggression threshold to apply pressure
    public float TerritorialTrustDrain    { get; set; } = 0.025f; // trust lost per tick; ~-0.1 in 4 ticks (1 season)

    // Beast encounters — predators on the same tile can attack characters
    public float BeastEncounterAggressionMin { get; set; } = 0.4f;  // beasts below this are passive
    public float BeastEncounterChance        { get; set; } = 0.15f; // probability of attack per shared tick
    public float BeastDamageMultiplier       { get; set; } = 0.3f;  // beast.Strength × this = damage to char

    // Wanderlust — travel urge that builds the longer a character stays in one place
    public int   WanderlustMaxTicks          { get; set; } = 8;   // full bonus after 2 seasons stationary
    public float WanderlustBonus             { get; set; } = 0.4f; // added to travel score at max wanderlust
    // Role dampeners: multiply the wanderlust bonus before applying
    public float WanderlustFounderMultiplier { get; set; } = 0.15f; // settlement founders (rulers/kings) rarely leave
    public float WanderlustMemberMultiplier  { get; set; } = 0.5f;  // civ members wander occasionally
    // Curiosity floor: even Curiosity=0 chars get this fraction of their role's wanderlust
    public float WanderlustCuriosityFloor    { get; set; } = 0.3f;

    // Ancestry / cultural trust drains (passive per-tick; applied when chars of different civs share a tile)
    public float PersonalityMismatchDrainRate { get; set; } = 0.003f; // drain × |stability diff|
    public float CulturalDistanceDrainRate    { get; set; } = 0.002f; // drain × cultural_distance (0–1)

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
