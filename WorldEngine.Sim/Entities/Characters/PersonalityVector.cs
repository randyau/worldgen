namespace WorldEngine.Sim.Entities.Characters;

/// <summary>12 stable personality traits. Set at character generation, never change.</summary>
public readonly record struct PersonalityVector(
    // Drive
    float Ambition,    float Greed,      float Aggression, float Compassion,
    // Mind
    float Curiosity,   float Creativity, float Rationality, float Wonder,
    // Social
    float Loyalty,     float Sociability, float Honesty,    float Stability)
{
    public static PersonalityVector Default => new(0.5f, 0.5f, 0.5f, 0.5f,
                                                   0.5f, 0.5f, 0.5f, 0.5f,
                                                   0.5f, 0.5f, 0.5f, 0.5f);
}
