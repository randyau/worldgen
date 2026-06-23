namespace WorldEngine.Sim.Core;

/// <summary>Stable identifier for a civilization. Assigned at founding, never reused.</summary>
public readonly record struct CivId(int Value)
{
    public static readonly CivId None = new(0);
    public bool IsValid => Value > 0;
    public override string ToString() => $"Civ#{Value}";
}
