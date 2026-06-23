namespace WorldEngine.Sim.Entities.Characters;

/// <summary>4-need subset for Tier 2 characters. Simpler than the 7-need Tier 1 model.</summary>
public record struct NeedsVector4(float Food, float Safety, float Belonging, float Status)
{
    public const float UrgentThreshold = 0.2f;

    public static NeedsVector4 Default => new(0.8f, 0.8f, 0.6f, 0.4f);

    public bool AnyUrgent =>
        Food < UrgentThreshold || Safety < UrgentThreshold
        || Belonging < UrgentThreshold || Status < UrgentThreshold;
}
