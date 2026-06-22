namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Per-layer seed constants XOR'd with worldSeed when initializing FastNoiseLite.
/// All values must be unique — LayerSeeds_AllValuesAreUnique test enforces this.
/// </summary>
public static class LayerSeeds
{
    public const int Tectonic  = 0x1A2B3C;
    public const int Elevation = 0x2B3C4D;
    public const int Ocean     = 0x3C4D5E;
    public const int River     = 0x4D5E6F;
    public const int Magic     = 0x5E6F7A;
    public const int Climate   = 0x6F7A8B;
    public const int Biome     = 0x7A8B9C;
    public const int Resource  = 0x8B9CAD;
    public const int Poi       = 0x9CADBE;
}
