namespace WorldEngine.Sim.Entities.Characters;

/// <summary>6 stable aptitude traits. Set at character generation, never change.</summary>
public readonly record struct AptitudeVector(
    float Diligence, float Focus, float Perfectionism,
    float Composure, float Acuity, float Ingenuity)
{
    public static AptitudeVector Default => new(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
}
