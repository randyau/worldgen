namespace WorldEngine.Sim.Core;

/// <summary>RNG salt constants for disaster phase; keeps disaster rolls reproducible and independent.</summary>
public static class DisasterSalts
{
    public const int Wildfire = 1;
    public const int WildfireSpread = 2;
    public const int Flood = 3;
    public const int VolcanicEruption = 4;
    public const int Earthquake = 5;
    public const int DroughtCheck = 6;
}
