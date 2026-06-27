namespace WorldEngine.Sim.Config;

/// <summary>
/// Thresholds that govern when a civilization acquires a permanent cultural trait.
/// All constants loaded from [cultural_traits] section in sim_config.toml.
/// </summary>
public sealed class CulturalTraitsConfig
{
    // ─── Militaristic ─────────────────────────────────────────────────────────
    /// <summary>Minimum total wars ever initiated for the Militaristic trait.</summary>
    public int   MilitaristicMinWars       { get; set; } = 3;   // reduced from 10; avg civ gets 1.45 wars
    /// <summary>Minimum wars-per-decade average for the Militaristic trait.</summary>
    public float MilitaristicWarsPerDecade { get; set; } = 2f;

    // ─── Expansionist ─────────────────────────────────────────────────────────
    /// <summary>Minimum settlement founding rate (per 10 years) for Expansionist.</summary>
    public float ExpansionistFoundingRate    { get; set; } = 1f;
    /// <summary>Minimum years the civ must have exceeded the founding rate to qualify.</summary>
    public int   ExpansionistSustainedYears  { get; set; } = 20;  // reduced from 30

    // ─── WarWeary ─────────────────────────────────────────────────────────────
    /// <summary>Minimum times a single enemy must appear in WarHistory (repeated wars) to trigger WarWeary.</summary>
    public int   WarWearyMinRepeatWars { get; set; } = 2;  // reduced from 3; same-enemy wars are rare

    // ─── Resilient ────────────────────────────────────────────────────────────
    /// <summary>Minimum near-collapse episodes (TotalPopulation below threshold) to earn Resilient.</summary>
    public int   ResilientMinNearCollapseCount     { get; set; } = 1;
    /// <summary>Population threshold below which an episode counts as a near-collapse.</summary>
    public int   ResilientNearCollapsePopThreshold { get; set; } = 50;  // raised from 20; pop of 20 never reached while alive

    // ─── Scholarly ────────────────────────────────────────────────────────────
    /// <summary>Minimum total ScholarDiscovery events by civ members to earn Scholarly.</summary>
    public int   ScholarlyMinDiscoveries { get; set; } = 5;

    // ─── UnstableThrone ───────────────────────────────────────────────────────
    /// <summary>Minimum successions within UnstableThroneYears to earn UnstableThrone.</summary>
    public int   UnstableThroneMinSuccessions { get; set; } = 5;
    /// <summary>Rolling window in years for counting successions for UnstableThrone.</summary>
    public int   UnstableThroneYears          { get; set; } = 50;
}
