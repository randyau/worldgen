namespace WorldEngine.Sim.Config;

/// <summary>
/// One authored religion archetype, loaded from config/religions.toml. When a character founds a
/// religion (M15 15.0), the archetype whose personality affinity best matches the founder's
/// PersonalityVector is selected; a deity name and name template are then rolled from its pools
/// to produce the religion's display name.
/// </summary>
public sealed class ReligionArchetypeConfig
{
    public string   Id          { get; set; } = "";
    public string   DisplayName { get; set; } = "";

    /// <summary>Deity names to roll from when this archetype is selected.</summary>
    public string[] DeityNames    { get; set; } = [];
    /// <summary>Authored doctrine flavor lines — narrative content, not mechanically read yet.</summary>
    public string[] Tenets        { get; set; } = [];
    /// <summary>Name templates with a "{deity}" placeholder, e.g. "The Circle of {deity}".</summary>
    public string[] NameTemplates { get; set; } = [];

    // Personality-affinity biases — the archetype whose weighted dot product with the founder's
    // PersonalityVector is highest gets selected. Only the traits that meaningfully differentiate
    // archetypes are scored; unlisted traits default to 0 (no pull either way).
    public float AffinityCompassion { get; set; } = 0f;
    public float AffinityAggression { get; set; } = 0f;
    public float AffinityCuriosity  { get; set; } = 0f;
    public float AffinityWonder     { get; set; } = 0f;
    public float AffinityAmbition   { get; set; } = 0f;
    public float AffinityStability  { get; set; } = 0f;

    /// <summary>
    /// -1 (tolerant/syncretic — spreads gently, never suppresses rivals) .. +1 (militant/exclusive
    /// — pressures/persecutes rivals within its home civ more, but has weaker cross-civ appeal).
    /// Consumed starting M15 15.1 (conversion behavior) and 15.3 (persecution intensity) — see
    /// docs/phases/m15_religion_deepened.md "Long-run balance constraints" for the sink/source
    /// reasoning this axis exists to serve.
    /// </summary>
    public float Zealotry { get; set; } = 0f;
}
