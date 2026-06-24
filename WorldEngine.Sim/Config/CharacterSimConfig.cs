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
    public int   MinFertilityToSettle    { get; set; } = 100;
    // Characters will also settle low-fertility tiles that have deposits above this threshold
    public float DepositSettleThreshold  { get; set; } = 0.5f;
    // How much deposit value boosts the EstablishSettlement success probability multiplier
    public float DepositScoreMultiplier  { get; set; } = 0.5f;
    // How much route-position bonus boosts the EstablishSettlement success probability multiplier
    public float RouteScoreMultiplier    { get; set; } = 0.3f;
    // ─── Alliance system ───────────────────────────────────────────────────────
    // Base + Sociability-scaled max alliances a character can hold (cross-civ only)
    public int   AllianceMaxBase             { get; set; } = 2;
    public int   AllianceMaxPerSociability   { get; set; } = 3;  // +floor(Sociability × this)
    // Trust floor below which an alliance dissolves on the annual check
    public float AllianceTrustFloor          { get; set; } = 0.1f;
    // Trust drain applied to the attacker's allies when war is declared
    public float AllianceWarTrustDrain       { get; set; } = 0.4f;
    // Trust drain on A's relationship with C when B (A's new ally) is allied with C (A's rival)
    public float EnemyOfAllyTrustDrain       { get; set; } = 0.15f;
    // Intensity of the Protect goal seeded on allies of a civ under attack
    public float AllyProtectGoalIntensity    { get; set; } = 0.6f;
    // Intensity of the Acquire(Food) goal seeded on allied chars when a settlement strains
    public float AllyDisasterAidIntensity    { get; set; } = 0.3f;

    // ─── Ruins ────────────────────────────────────────────────────────────────
    // Hard cooldown: a ruined tile cannot be settled at all for this many years after destruction.
    // Deposits cannot override this — the site is simply too dangerous/cursed to settle immediately.
    public int   RuinCooldownYears        { get; set; } = 10;
    // Score penalty (0–1) applied to EstablishSettlement after the hard cooldown expires
    public float RuinFoundingPenalty      { get; set; } = 0.4f;
    // Years for the penalty to halve (exponential decay); 0 = penalty never decays
    public float RuinDecayHalfLifeYears   { get; set; } = 50f;

    // ─── War ──────────────────────────────────────────────────────────────────
    // Wars auto-expire after this many years if not renewed by a battle/raid.
    // Prevents permanent inter-civ hostility and the rivalry accumulation it causes.
    public int MaxWarDurationYears        { get; set; } = 10;

    // ─── Effective fertility multiplier for tiles already inside a same-civ settlement's hinterland.
    // 0.5 = "half the resources are claimed" — discourages but doesn't block high-fertility tiles.
    public float HinterlandDrainFactor       { get; set; } = 0.5f;
    // Base cooldown years between same-civ settlements; scales down with civ population
    public int   BaseFoundingCooldownYears   { get; set; } = 20;
    // Minimum cooldown years regardless of population (a civ can't expand instantly even if huge)
    public int   MinFoundingCooldownYears    { get; set; } = 4;
    // Population scale factor: cooldown halves when civ population reaches this value
    public int   FoundingCooldownPopScale    { get; set; } = 2000;

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

    // Wellbeing — emotional state dynamics
    public float WellbeingGoalGainRate      { get; set; } = 0.01f;   // per tick when goal is progressing
    public float WellbeingCompanionBoost    { get; set; } = 0.005f;  // per tick co-located with Bond target
    public float WellbeingHungerDrain       { get; set; } = 0.02f;   // max drain when food = 0
    public float WellbeingMeanReversionRate { get; set; } = 0.005f;  // pull toward 0 each tick
    public float FlourishingThreshold       { get; set; } = 0.7f;    // Wellbeing ≥ this → Flourishing
    public float SpiralThreshold            { get; set; } = -0.7f;   // Wellbeing ≤ this → Spiraling
    public float DistressedSocialSuppression { get; set; } = 0.4f;   // social action score multiplier when Wellbeing < -0.3
    public float GriefDrainRate             { get; set; } = 0.015f;  // Wellbeing drain per tick per Grieve goal
    public float GriefDecayRate             { get; set; } = 0.002f;  // grief Intensity decay per tick

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
