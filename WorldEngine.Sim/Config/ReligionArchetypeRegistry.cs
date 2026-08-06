namespace WorldEngine.Sim.Config;

/// <summary>
/// Loaded set of all religion archetype configs. Accessible via world.SimConfig.ReligionArchetypes.
/// Provides personality-affinity-weighted archetype selection for religion founding (M15 15.0).
/// </summary>
public sealed class ReligionArchetypeRegistry
{
    private readonly IReadOnlyList<ReligionArchetypeConfig> _all;

    public static readonly ReligionArchetypeRegistry Empty = new(Array.Empty<ReligionArchetypeConfig>());

    public ReligionArchetypeRegistry(IReadOnlyList<ReligionArchetypeConfig> all) => _all = all;

    public IReadOnlyList<ReligionArchetypeConfig> All => _all;

    /// <summary>
    /// Picks the archetype whose affinity biases best match the founder's PersonalityVector
    /// (highest weighted dot product). Deterministic given the same personality — the RNG lives in
    /// which deity name / name template gets rolled within the chosen archetype, not in the pick
    /// itself, so a founder's convictions consistently produce the "right kind" of religion.
    /// </summary>
    public ReligionArchetypeConfig? SelectForFounder(
        float compassion, float aggression, float curiosity, float wonder, float ambition, float stability)
    {
        ReligionArchetypeConfig? best = null;
        float bestScore = float.NegativeInfinity;
        foreach (var a in _all)
        {
            float score = a.AffinityCompassion * compassion
                        + a.AffinityAggression * aggression
                        + a.AffinityCuriosity  * curiosity
                        + a.AffinityWonder     * wonder
                        + a.AffinityAmbition   * ambition
                        + a.AffinityStability  * stability;
            if (score > bestScore)
            {
                bestScore = score;
                best = a;
            }
        }
        return best;
    }
}
