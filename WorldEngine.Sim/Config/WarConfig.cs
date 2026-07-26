namespace WorldEngine.Sim.Config;

/// <summary>
/// All war-system configuration — consolidated from [character] + [war] in D5.
/// Loaded from the [war] section of sim_config.toml.
/// </summary>
public sealed class WarConfig
{
    // ─── War lifecycle ────────────────────────────────────────────────────────

    /// <summary>Wars auto-expire after this many years if not resolved by surrender or destruction.</summary>
    public int MaxWarDurationYears { get; set; } = 15;

    /// <summary>Hard cap on simultaneous active wars per civilization.</summary>
    public int MaxActiveWars { get; set; } = 2;

    /// <summary>
    /// After any war ends, neither side can declare war on the other for this many years.
    /// Prevents immediate re-declaration.
    /// </summary>
    public int PeaceCooldownYears { get; set; } = 10;

    /// <summary>
    /// Extra cooldown years per prior war between the same pair (war exhaustion).
    /// 1st war → 10 yr cooldown; 2nd → 15; 3rd → 20, etc.
    /// </summary>
    public int WarExhaustionYearsPerWar { get; set; } = 5;

    // ─── Raid ─────────────────────────────────────────────────────────────────

    /// <summary>Minimum HP damage dealt to a settlement per character-level raid.</summary>
    public int RaidDamageMin { get; set; } = 15;

    /// <summary>Maximum HP damage dealt to a settlement per character-level raid.</summary>
    public int RaidDamageMax { get; set; } = 40;

    // ─── War resolution ───────────────────────────────────────────────────────

    /// <summary>
    /// If a settlement's health is at or below this threshold at war expiry, the attacker
    /// can force a conquest rather than accepting a mere truce — models a completed siege.
    /// </summary>
    public int WarConquestHealthThreshold { get; set; } = 35;

    /// <summary>
    /// A civ whose total population falls below this threshold during a war sues for peace
    /// (surrender). The war ends immediately regardless of remaining duration.
    /// </summary>
    public int WarSurrenderPopThreshold { get; set; } = 5;

    // ─── Declaration thresholds ───────────────────────────────────────────────

    /// <summary>Minimum ruler Aggression to consider DeclareWar action or trigger tension-based war.</summary>
    public float WarAggressionThreshold { get; set; } = 0.5f;

    // ─── Border tension (civ-level trigger) ──────────────────────────────────

    /// <summary>
    /// Tile radius within which settlements accumulate tension toward neighbor civs.
    /// Larger radius means distant civs can still be on a collision course.
    /// </summary>
    public int WarProximityRadius { get; set; } = 40;

    /// <summary>
    /// Tension added per close settlement pair per year; multiplied by proximity (0–1)
    /// and the aggressor ruler's Aggression. Aggressive civs with many border settlements escalate fast.
    /// </summary>
    public float TensionAccrualPerPair { get; set; } = 0.12f;

    /// <summary>Fraction of tension lost each year when no proximate settlements exist.</summary>
    public float TensionDecayRate { get; set; } = 0.15f;

    /// <summary>
    /// When accumulated tension crosses this value AND the ruler's Aggression meets WarAggressionThreshold,
    /// war is declared without any personal character encounter.
    /// </summary>
    public float TensionWarThreshold { get; set; } = 1.0f;

    // ─── Campaign battles ─────────────────────────────────────────────────────

    /// <summary>
    /// Tension added per year for each pair of adjacent territory tiles owned by different civs.
    /// At 0.015 per pair: 10 touching tile pairs → 0.15 tension/year → war threshold (~1.0) in ~7 years.
    /// </summary>
    public float TerritoryTensionPerAdjacentPair { get; set; } = 0.015f;

    /// <summary>Health damage dealt to the target settlement per successful campaign battle.</summary>
    public int CampaignBattleDamage { get; set; } = 15;

    /// <summary>
    /// Attacker strength used in campaign battle rolls when no named character combatant is available.
    /// Range 0–1; same scale as Skills.Combat.
    /// </summary>
    public float CampaignBattleBaseStrength { get; set; } = 0.5f;

    /// <summary>
    /// bonus_military_strength (M9 9.1): additive to attacker/defender strength in campaign battle
    /// rolls, read from that side's capital settlement store. Capped so a single stockpile can't
    /// make battles a foregone conclusion.
    /// </summary>
    public float MilitaryStrengthBonusScale { get; set; } = 1.0f;
    public float MilitaryStrengthBonusCap   { get; set; } = 0.3f;

    /// <summary>Territory tiles transferred from loser to winner per net battle win at war end.</summary>
    public int TilesPerBattleWin { get; set; } = 2;

    /// <summary>Cap on tiles transferred in a single war outcome; prevents one decisive war from reshaping the world.</summary>
    public int MaxTilesTransferredPerWar { get; set; } = 12;

    // ─── Opportunistic war causes (D5) ───────────────────────────────────────

    /// <summary>
    /// Minimum ruler Aggression required for an opportunistic war declaration
    /// (SuccessionCrisis, WeakNeighbor, ResourceShortage triggers).
    /// Same threshold as the base WarAggressionThreshold but can be tuned independently.
    /// </summary>
    public float OpportunisticWarAggressionThreshold { get; set; } = 0.55f;

    /// <summary>
    /// Annual probability multiplier applied to tension accrual against a target civ that is
    /// in an active succession crisis. Simulates neighbors exploiting a power vacuum.
    /// Applies in addition to normal tension; at 2.0× a succession crisis doubles tension accrual.
    /// </summary>
    public float SuccessionCrisisWarTensionMult { get; set; } = 2.0f;

    /// <summary>
    /// Fraction of TensionWarThreshold at which a character (without border-tension data)
    /// considers a civ "hostile enough" to justify a personal war declaration.
    /// Lower values make personal encounters trigger war more easily.
    /// </summary>
    public float PersonalWarTensionFraction { get; set; } = 0.6f;

    /// <summary>
    /// A neighbor civ is considered "weak" if it has at least this fraction of its settlements
    /// currently infected OR in food shortage (FoodPressureRatio &lt; WarWeakNeighborFoodThreshold).
    /// Weak-neighbor status adds tension against that civ.
    /// </summary>
    public float WeakNeighborSettlementFraction { get; set; } = 0.4f;

    /// <summary>
    /// Food pressure ratio below which a settlement counts toward the weak-neighbor fraction.
    /// Matches the famine threshold by default.
    /// </summary>
    public float WarWeakNeighborFoodThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Extra annual tension accrued against a weak neighbor (infected/starving settlements).
    /// Added on top of normal proximity tension.
    /// </summary>
    public float WeakNeighborTensionBonus { get; set; } = 0.25f;

    /// <summary>
    /// A civ is considered "resource-shortage" for aggressor purposes when its aggressor
    /// settlement food ratio is below this threshold.
    /// Resource-starved civs are more likely to raid neighbors.
    /// </summary>
    public float ResourceShortageWarFoodThreshold { get; set; } = 0.75f;

    /// <summary>
    /// Extra annual tension accrued BY a resource-short aggressor (food ratio below threshold).
    /// Models desperation driving expansion: hungry civs are pushier.
    /// </summary>
    public float ResourceShortageTensionBonus { get; set; } = 0.20f;

    // ─── Diplomacy / peaceful coexistence ────────────────────────────────────

    /// <summary>
    /// Trust boost applied between the target's ruler and each proximate non-enemy civ ruler
    /// when a war is declared — seeds defensive coalitions against common aggressors.
    /// </summary>
    public float CoalitionTrustBonus { get; set; } = 0.20f;

    /// <summary>
    /// Population floor below which war probability is zero.
    /// Both the aggressor and target must exceed this floor.
    /// </summary>
    public int WarMinCivPop { get; set; } = 300;

    /// <summary>
    /// Population range over which war probability ramps from 0 to full.
    /// At WarMinCivPop the chance is 0; at WarMinCivPop + this value it is fully unlocked.
    /// The bottleneck civ (lower pop of the two) determines the ramp factor.
    /// </summary>
    public int WarPopRampRange { get; set; } = 700;
}
