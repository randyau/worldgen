namespace WorldEngine.Sim.Config;

/// <summary>
/// All constants governing the civ awareness and emissary system (M4.1).
/// Loaded from the [emissary] section of sim_config.toml.
/// </summary>
public sealed class EmissaryConfig
{
    // ─── Knowledge propagation ────────────────────────────────────────────────

    /// <summary>Tiles; civs with settlements within this range gain Rumor contact (wider than WarProximityRadius).</summary>
    public int   KnowledgeSpreadRadius       { get; set; } = 30;

    /// <summary>Confidence added per year when proximity rumor is active between two civs.</summary>
    public float RumorConfidenceGain         { get; set; } = 0.15f;

    /// <summary>Confidence added when a character cross-civ encounter seeds a WandererMet contact.</summary>
    public float EncounterConfidenceGain     { get; set; } = 0.35f;

    /// <summary>Confidence lost per year when no contact mechanism is active for a civ pair.</summary>
    public float ConfidenceDecayPerYear      { get; set; } = 0.05f;

    /// <summary>Annual probability that Civ A passes knowledge of Civ C to Civ B (per pair).</summary>
    public float RumorChainProbability       { get; set; } = 0.05f;

    /// <summary>Chained rumors arrive at this fraction of the source's confidence.</summary>
    public float RumorChainConfidenceFactor  { get; set; } = 0.5f;

    // ─── Emissary dispatch ────────────────────────────────────────────────────

    /// <summary>Ruler considers dispatching emissaries every this many years.</summary>
    public int   DispatchCheckYears                 { get; set; } = 5;

    /// <summary>Maximum simultaneous in-transit emissaries per civ.</summary>
    public int   MaxActiveEmissariesPerCiv          { get; set; } = 3;

    /// <summary>Tiles per year of emissary travel speed; affects arrival delay and mortality.</summary>
    public float EmissaryTravelSpeedTilesPerYear    { get; set; } = 8.0f;

    /// <summary>Minimum trust between rulers to send a Trade emissary.</summary>
    public float TradeDispatchMinTrust              { get; set; } = -0.1f;

    /// <summary>Minimum trust for a Diplomacy mission.</summary>
    public float DiplomacyDispatchMinTrust          { get; set; } = 0.1f;

    /// <summary>Maximum trust for a Spy mission (targets civs you don't trust well).</summary>
    public float SpyDispatchMaxTrust                { get; set; } = 0.2f;

    // ─── Emissary mortality ───────────────────────────────────────────────────

    /// <summary>Cumulative per-tile mortality rate; formula: clamp(1 - dist * DeathPerTile, MinSurvivalChance, 1.0).</summary>
    public float EmissaryDeathPerTile               { get; set; } = 0.008f;

    /// <summary>Floor survival probability; even a 200-tile journey has this chance of success.</summary>
    public float EmissaryMinSurvivalChance          { get; set; } = 0.2f;

    // ─── Emissary outcomes (on arrival) ──────────────────────────────────────

    /// <summary>Trust gain between rulers on successful Trade emissary arrival.</summary>
    public float TradeTrustGain                     { get; set; } = 0.08f;

    /// <summary>Both civs need at least this pop to meaningfully exchange trade goods.</summary>
    public int   TradeMinPopForGoods                { get; set; } = 50;

    /// <summary>Trust required after diplomatic emissary for AllianceFormed to trigger.</summary>
    public float DiplomacyAllianceMinTrust          { get; set; } = 0.25f;

    /// <summary>How much contact confidence improves from a successful Spy emissary.</summary>
    public float SpyConfidenceBoost                 { get; set; } = 0.4f;

    /// <summary>Awe modifier added to target-civ characters on successful Religious emissary.</summary>
    public float ReligiousSpreadAweBoost            { get; set; } = 0.3f;
}
