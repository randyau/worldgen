using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.WorldGen;

/// <summary>Per-tile biome classification and fertility produced by BiomeLayer.</summary>
public sealed class BiomeResult
{
    /// <summary>Biome type for each tile.</summary>
    public BiomeType[] Biomes { get; }

    /// <summary>Fertility byte (0–255) derived from biome and climate.</summary>
    public byte[] Fertility { get; }

    public BiomeResult(int tileCount)
    {
        Biomes    = new BiomeType[tileCount];
        Fertility = new byte[tileCount];
    }
}
