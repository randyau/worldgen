namespace WorldEngine.Sim.Config;

/// <summary>
/// All data for one ancestry, loaded from config/ancestries.toml.
/// Personality and aptitude fields are bias offsets added to the Gaussian mean (base 0.5).
/// </summary>
public sealed class AncestryConfig
{
    public string   Id          { get; set; } = "human";
    public string   DisplayName { get; set; } = "Human";

    public int MinLifespanSeasons { get; set; } = 60;
    public int MaxLifespanSeasons { get; set; } = 200;

    // Personality biases — added to the Gaussian mean.
    // +0.2 shifts average from 0.5 → 0.7; individual noise (stddev ≈ 0.2) stays equal to max bias.
    public float BiasAmbition    { get; set; } = 0f;
    public float BiasGreed       { get; set; } = 0f;
    public float BiasAggression  { get; set; } = 0f;
    public float BiasCompassion  { get; set; } = 0f;
    public float BiasCuriosity   { get; set; } = 0f;
    public float BiasCreativity  { get; set; } = 0f;
    public float BiasRationality { get; set; } = 0f;
    public float BiasWonder      { get; set; } = 0f;
    public float BiasLoyalty     { get; set; } = 0f;
    public float BiasSociability { get; set; } = 0f;
    public float BiasHonesty     { get; set; } = 0f;
    public float BiasStability   { get; set; } = 0f;

    // Aptitude biases — additive modifier on starting AptitudeVector (clamped to [0,1])
    public float BiasDiligence     { get; set; } = 0f;
    public float BiasFocus         { get; set; } = 0f;
    public float BiasPerfectionism  { get; set; } = 0f;
    public float BiasComposure     { get; set; } = 0f;
    public float BiasAcuity        { get; set; } = 0f;
    public float BiasIngenuity     { get; set; } = 0f;

    // Spawn probability weight per biome (snake_case BiomeType name → weight).
    // Missing biome → weight 0. Weights are relative; normalized across all ancestries at that biome.
    public Dictionary<string, float> SpawnWeights { get; set; } = new();

    // Default trust modifier applied ONCE at first interaction with each other ancestry.
    // Keyed by other ancestry ID.
    public Dictionary<string, float> FirstMeetingTrust { get; set; } = new();

    // Ongoing passive trust drain per shared-tile tick with each other ancestry (0.0–1.0 cultural distance).
    // drain/tick = CulturalDistance × CulturalDistanceDrainRate (config)
    public Dictionary<string, float> CulturalDistance { get; set; } = new();

    // Names — populated from first_names / epithets arrays in ancestries.toml
    public string[] FirstNames { get; set; } = [];
    public string[] Epithets   { get; set; } = [];

    // V2 mechanical hooks — ignored until implemented
    public string[] PhysicalTags { get; set; } = [];
}
