namespace WorldEngine.Sim.Entities.Characters;

/// <summary>Simplified 6-trait personality for Tier 2 characters.</summary>
public readonly record struct PersonalityVector6(
    float Ambition, float Loyalty, float Diligence,
    float Sociability, float Cunning, float Rationality)
{
    public static PersonalityVector6 Default =>
        new(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
}
