namespace WorldEngine.Sim.Core;

/// <summary>Stable identifier for an Organization (civilization/guild/religion/family). Assigned at founding, never reused.</summary>
public readonly record struct OrganizationId(int Value)
{
    public static readonly OrganizationId None = new(0);
    public bool IsValid => Value > 0;
    public override string ToString() => $"Org#{Value}";
}
