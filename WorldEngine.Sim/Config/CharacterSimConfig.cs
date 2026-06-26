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
    // Temperature scales shelter decay outside the comfort band.
    // At the extremes (polar cold, desert heat) decay is multiplied by (1 + ShelterTemperatureScale).
    // Inside the comfort band the multiplier is 1.0 (no extra pressure).
    // Keep scale ≤ 1.5 so settlements (which recover +0.10/tick) still net-positive in all climates.
    public byte  ShelterComfortTempLow  { get; set; } = 80;   // byte; below → cold pressure
    public byte  ShelterComfortTempHigh { get; set; } = 180;  // byte; above → heat pressure
    public float ShelterTemperatureScale { get; set; } = 0.8f; // max additional multiplier at extreme temp
    // Expansion movement scoring — tile score adjustments when character has an active Expansion goal
    // Home-civ settlement tiles get this penalty so expansion characters leave rather than orbit home
    public int   ExpansionHomePenalty     { get; set; } = 120;
    // Tiles outside every settlement's hinterland get this bonus — draws expansion chars toward open land
    public int   ExpansionEmptyTileBonus  { get; set; } = 80;
    // Additional bonus when an unclaimed tile is within this many tiles of a same-civ settlement;
    // encourages blob-shaped growth rather than linear tendrils
    public int   ExpansionCompactnessRadius { get; set; } = 8;
    public int   ExpansionCompactnessBonus  { get; set; } = 60;
    // Hard cap on live local settlements per civilization; expansion goals stop forming beyond this
    public int   MaxSettlementsPerCiv      { get; set; } = 20;

    // Colonization: separate goal type for long-range settlement founding
    // Minimum tile distance from any same-civ settlement for a tile to count as "frontier"
    public int   ColonyMinDistance         { get; set; } = 25;
    // Score bonus when a tile is beyond ColonyMinDistance from all same-civ settlements
    public int   ColonyFrontierBonus       { get; set; } = 120;
    // Ambition threshold to form a Colonize goal (higher than regular expansion)
    public float ColonizeAmbitionThreshold { get; set; } = 0.72f;
    // Hard cap on live colonies per civ; Colonize goals stop forming beyond this
    public int   MaxColoniesPerCiv         { get; set; } = 3;

    // City-State model (M3 Phase 3.0): ruler-delegated city founding
    // Total city cap per civ (settlements + colonies combined)
    public int   MaxCitiesPerCiv               { get; set; } = 8;
    // Minimum Ambition for a civ member to be selected as a city delegate
    public float CityFoundingAmbitionThreshold { get; set; } = 0.5f;

    // When shelter drops below this threshold, characters actively prefer tiles with natural cover
    public float ShelterSeekThreshold   { get; set; } = 0.35f;
    // Max tile-score bonus added per unit of BiomeShelterScore when shelter-seeking
    // (scaled by desperation: bonus × (1 - shelter), so critically low = full bonus)
    public int   ShelterSeekTileBonus   { get; set; } = 80;
    public float NeedsDecayBelonging    { get; set; } = 0.03f;
    public float NeedsDecayStatus       { get; set; } = 0.03f;
    public float NeedsDecayPurpose      { get; set; } = 0.04f;
    public float NeedsDecaySpiritual    { get; set; } = 0.02f;

    // Settlement presence passively restores social/identity needs — community provides recognition and belonging.
    // "Own civ" = same CivId as character; "foreign" = at any other settlement.
    public float BelongingOwnSettlementRecovery     { get; set; } = 0.03f;  // full community belonging
    public float BelongingForeignSettlementRecovery { get; set; } = 0.01f;  // partial: stranger in a crowd
    public float StatusOwnSettlementRecovery        { get; set; } = 0.03f;  // recognized by community peers
    public float PurposeOwnSettlementRecovery       { get; set; } = 0.02f;  // shared goals and work
    public float SpiritualSettlementRecovery        { get; set; } = 0.01f;  // any settlement: ritual, culture

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
    public int   MinFertilityToSettle    { get; set; } = 60;
    // Minimum base moisture (0-255) required to found a settlement.
    // Prevents founding on tiles that are climatically too dry to sustain a population.
    // Deposit-rich founding is still allowed below this threshold (miners live there anyway).
    public byte  MinBaseMoistureToSettle { get; set; } = 15;
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
    // Wars auto-expire after this many years if not resolved by surrender or truce.
    public int MaxWarDurationYears        { get; set; } = 4;
    // Hard cap on simultaneous active wars per civilization.
    public int MaxActiveWars              { get; set; } = 2;
    // After any war ends (expiry, surrender, or truce), neither side can declare war
    // on the other for this many years. Prevents immediate re-declaration.
    public int PeaceCooldownYears         { get; set; } = 10;
    // Additional cooldown years added per prior war between the same pair (war exhaustion).
    // 5 means: 1st war → 10 year cooldown, 2nd → 15, 3rd → 20, etc.
    public int WarExhaustionYearsPerWar   { get; set; } = 5;
    // Raid damage constants (moved from hardcoded to configurable)
    public int RaidDamageMin              { get; set; } = 15;
    public int RaidDamageMax              { get; set; } = 40;
    // If a settlement's health is at or below this at war expiry, it can be conquered
    // rather than returning to truce — models a siege that completes at war's end.
    public int WarConquestHealthThreshold { get; set; } = 35;
    // A civ whose total population falls below this threshold during a war sues for peace
    // (surrender). The war ends immediately regardless of duration.
    public int WarSurrenderPopThreshold   { get; set; } = 5;
    // Rivalry cap scales with Aggression: floor(base + Aggression × perAggression).
    // A war-hungry character can sustain more rivalries; a peaceful one almost none.
    public int   RivalryMaxBase           { get; set; } = 1;
    public int   RivalryMaxPerAggression  { get; set; } = 3;  // Aggression=1.0 → 1+3=4 rivals max
    // Trust must fall below this threshold before DeclareRivalry becomes available.
    // -0.1 was too easy to trigger; sustained hostility is required, not one bad encounter.
    public float RivalryTrustThreshold    { get; set; } = -0.4f;
    // Bond cap scales with Compassion: floor(base + Compassion × perCompassion).
    // A cold character bonds with at most 1 person; highly empathetic can hold 2-3.
    public int   BondMaxBase              { get; set; } = 1;
    public int   BondMaxPerCompassion     { get; set; } = 2;  // Compassion=1.0 → 1+2=3 bonds max

    // ─── Effective fertility multiplier for tiles already inside a same-civ settlement's hinterland.
    // 0.5 = "half the resources are claimed" — discourages but doesn't block high-fertility tiles.
    public float HinterlandDrainFactor       { get; set; } = 0.5f;
    // Base cooldown years between same-civ settlements; scales down with civ population
    public int   BaseFoundingCooldownYears   { get; set; } = 8;
    // Minimum cooldown years regardless of population (a civ can't expand instantly even if huge)
    public int   MinFoundingCooldownYears    { get; set; } = 2;
    // Population scale factor: cooldown halves when civ population reaches this value
    public int   FoundingCooldownPopScale    { get; set; } = 2000;

    // Civilization-born character generation
    public int   CivBirthMinPop         { get; set; } = 20;   // settlement needs this many people
    public float CivBirthChancePerSeason { get; set; } = 0.03f; // ~1 birth per 33 seasons at min pop

    // Territorial aggression — aggressive founders develop negative trust with foreign visitors
    public float TerritorialAggressionMin { get; set; } = 0.55f; // aggression threshold to apply pressure
    public float TerritorialTrustDrain    { get; set; } = 0.025f; // trust lost per tick; ~-0.1 in 4 ticks (1 season)

    // Beast encounters — predators on the same tile can attack characters
    public float BeastEncounterAggressionMin { get; set; } = 0.4f;  // beasts below this are passive
    public float BeastEncounterChance        { get; set; } = 0.15f; // probability of attack per shared tick
    public float BeastDamageMultiplier       { get; set; } = 0.3f;  // beast.Strength × this = damage to char
    public float CharCounterDamageMultiplier { get; set; } = 0.4f;  // c.Skills.Combat × MaxHealth × this = counter-damage to beast

    // Character disease — named characters can contract disease from infected settlements
    public float CharacterDiseaseExposureChance  { get; set; } = 0.10f; // annual chance of catching disease at infected settlement
    public int   CharacterDiseaseHealthDrain     { get; set; } = 15;    // HP lost per year while infected (suppresses healing)
    public float CharacterDiseaseRecoveryChance  { get; set; } = 0.30f; // annual natural recovery chance

    // Battle wounds — defenders fight back during settlement raids
    public float DefenderCounterDamageMultiplier { get; set; } = 0.25f; // defender.Combat × MaxHealth × this = damage to raider
    public float RaiderCharDamageMultiplier      { get; set; } = 0.20f; // raider.Combat × MaxHealth × this = damage to defender char

    // Wildlife defense — characters at raided tiles defend and can be wounded
    public float WildlifeCharInjuryFraction  { get; set; } = 0.12f; // fraction of MaxHealth lost when present at wildlife raid
    public float WildlifeCharDefenseReduction { get; set; } = 0.40f; // defender.Combat scales this reduction in pop damage (0=none, max=this)

    // ─── Civilisation floor ───────────────────────────────────────────────────
    // When active (non-collapsed) civ count falls below this value, the annual pass
    // rolls to spawn a new free-agent founder character on suitable unclaimed land.
    public int   CivFloorCount       { get; set; } = 5;
    // Annual probability per missing civ slot of spawning a replacement founder.
    // e.g. at 0.3 and 2 slots missing: each slot has a 30% chance → ~51% at least one spawns.
    public float CivFloorSpawnChance { get; set; } = 0.3f;
    // Minimum tile distance from any existing settlement for a floor-spawn tile.
    public int   CivFloorMinDist     { get; set; } = 20;

    // ─── Succession crisis ────────────────────────────────────────────────────
    // How many years distant settlements suffer increased decay after the founding ruler dies.
    public int   SuccessionCrisisYears     { get; set; } = 10;
    // Decay rate multiplier applied to settlements beyond SuccessionStableRadius during crisis.
    public float SuccessionCrisisDecayMult { get; set; } = 2.5f;
    // Settlements within this tile radius of the capital are insulated from succession crisis.
    public int   SuccessionStableRadius    { get; set; } = 15;

    // ─── Goal formation thresholds ────────────────────────────────────────────
    // Minimum personality trait value required to generate each goal type.
    // Tuning these shifts how common each goal is across the population.
    public float GoalAmbitionThreshold      { get; set; } = 0.55f; // Expansion goal
    public float GoalAggressionThreshold    { get; set; } = 0.6f;  // Dominance goal
    public float GoalSociabilityThreshold   { get; set; } = 0.5f;  // Alliance goal
    public float GoalCompassionThreshold    { get; set; } = 0.5f;  // Bond goal
    public float GoalIngenuityThreshold     { get; set; } = 0.55f; // Create goal
    public float GoalDiligenceThreshold     { get; set; } = 0.45f; // BuildImprovement goal
    // Avenge goal: triggered on ally death if aggression exceeds threshold and grief intensity is strong enough
    public float AvengeAggressionThreshold  { get; set; } = 0.6f;
    public float AvengeIntensityThreshold   { get; set; } = 0.5f;
    // Trust floor: minimum relationship trust before Bond goal considers a companion
    public float BondTrustThreshold         { get; set; } = 0.5f;
    // Seasons a goal can stall (progress < 10%) before being pruned
    public int   GoalStaleSeasonLimit       { get; set; } = 8;
    // Radii used when searching for nearby rival / neutral / companion targets
    public int   RivalSearchRadius          { get; set; } = 5;
    public int   AllianceSearchRadius       { get; set; } = 4;
    public int   BondSearchRadius           { get; set; } = 3;

    // ─── Wellbeing constants ──────────────────────────────────────────────────
    // Food need below this threshold drains wellbeing (full drain when food=0)
    public float WellbeingHungerThreshold   { get; set; } = 0.3f;
    // Fraction of grief intensity applied as immediate wellbeing shock on mourning a Bond target
    public float GriefWellbeingShock        { get; set; } = 0.4f;
    // Grief Intensity below this → grief goal auto-completes (grief resolved)
    public float GriefCompletionThreshold   { get; set; } = 0.05f;
    // Wellbeing gain rate multipliers for specific goal types (fraction of WellbeingGoalGainRate)
    public float WellbeingEndureMultiplier  { get; set; } = 0.5f;  // Endure: slow negative progress
    public float WellbeingSurviveMultiplier { get; set; } = 0.3f;  // Survive: urgent but temporary
    public float WellbeingFleeMultiplier    { get; set; } = 0.4f;  // Flee: flight stress

    // Minimum aggression score required to consider War action against a rival
    public float WarAggressionThreshold     { get; set; } = 0.5f;

    // ─── Border tension (civ-level war trigger) ───────────────────────────────
    // Settlements within this tile radius accumulate tension toward their neighbour civ each year.
    public int   WarProximityRadius         { get; set; } = 15;
    // Base tension accrued per close settlement pair per year; multiplied by proximity (0–1)
    // and the ruler's Aggression, so aggressive civs with many border settlements escalate fast.
    public float TensionAccrualPerPair      { get; set; } = 0.12f;
    // Fraction of tension lost each year when a civ pair has no proximate settlements.
    public float TensionDecayRate           { get; set; } = 0.15f;
    // When a civ's accumulated tension toward an enemy crosses this value AND the ruler is
    // aggressive enough (WarAggressionThreshold), war is declared without personal contact.
    public float TensionWarThreshold        { get; set; } = 1.0f;

    // ─── Wanderlust — travel urge that builds the longer a character stays in one place
    public int   WanderlustMaxTicks          { get; set; } = 8;   // full bonus after 2 seasons stationary
    public float WanderlustBonus             { get; set; } = 0.4f; // added to travel score at max wanderlust
    // Role dampeners: multiply the wanderlust bonus before applying
    public float WanderlustFounderMultiplier { get; set; } = 0.15f; // settlement founders (rulers/kings) rarely leave
    public float WanderlustMemberMultiplier  { get; set; } = 0.7f;  // civ members wander more to encourage expansion
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
    public int   Tier2MaxAgeSeasonsMin  { get; set; } = 600;   // ~38 years at 16 ticks/year
    public int   Tier2MaxAgeSeasonsMax  { get; set; } = 1200;  // ~75 years
    public float Tier2CrystalChance     { get; set; } = 0.001f;  // per season
    public float Tier2NeedsDecayFood    { get; set; } = 0.06f;
    public float Tier2NeedsDecaySafety  { get; set; } = 0.04f;
    public float Tier2NeedsDecayBelonging { get; set; } = 0.03f;
    public float Tier2NeedsDecayStatus   { get; set; } = 0.04f;

    // ─── Scholar discoveries ──────────────────────────────────────────────────
    // Per-tick discovery probability = this × Rationality
    public float ScholarDiscoveryChance       { get; set; } = 0.04f;
    // Amount added to the relevant ResourceStores bonus key on each discovery
    public float ScholarDiscoveryBonusAmount  { get; set; } = 0.05f;

    // ─── Physician settlement healing ─────────────────────────────────────────
    // Per-tick settlement health recovery (applied while settlement IsInfected) = this × Rationality
    public float PhysicianSettlementHealRate  { get; set; } = 0.5f;

    // ─── Tier2 creator notable/exceptional event pacing ───────────────────────
    // Minimum ticks between notable work events per character (applies to Scholar, Merchant,
    // Physician, and Artisan). Routine work happens silently; this gates when it becomes
    // noteworthy enough to enter the history log.
    public int   Tier2NotableCooldownTicks   { get; set; } = 32;   // 2 years between notable events
    // Per-tick probability that a notable work event is also flagged as exceptional
    // (would create an artifact once the artifact system is live). Expected ~once per lifetime.
    public float Tier2ExceptionalWorkChance  { get; set; } = 0.002f;

    // ─── Tier1 Create goal cooldown ───────────────────────────────────────────
    // Ticks after completing a Create goal before the character can start a new one.
    // Prevents back-to-back creative obsession; 80 ticks = 5 years at 16 ticks/year.
    public int   CreateGoalCooldownTicks     { get; set; } = 80;
}
