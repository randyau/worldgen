namespace WorldEngine.Sim.Config;

/// <summary>
/// Configuration for civ-level war mechanics: territory-driven tension,
/// annual campaign battles, and war-end territory transfers.
/// Loaded from the [war] section of sim_config.toml.
/// </summary>
public sealed class WarConfig
{
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

    /// <summary>Territory tiles transferred from loser to winner per net battle win at war end.</summary>
    public int TilesPerBattleWin { get; set; } = 2;

    /// <summary>Cap on tiles transferred in a single war outcome; prevents one decisive war from reshaping the world.</summary>
    public int MaxTilesTransferredPerWar { get; set; } = 12;
}
