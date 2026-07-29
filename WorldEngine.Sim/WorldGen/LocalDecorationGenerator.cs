using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Deterministic sub-tile decoration generator: a pure function of (worldSeed, ChunkCoord,
/// biome, LocalGenConfig), same rationale as <see cref="LocalTerrainAmplifier"/> — no state is
/// persisted, the same inputs always reproduce the same cell. Populates
/// <see cref="LocalTileData"/>.DecorationType so a chunk isn't a flat wash of one biome color —
/// a low-frequency "cluster" noise places patchy regions of the biome's primary decoration (tree
/// stands, wetland patches), and a high-frequency "sparse" noise scatters an occasional secondary
/// feature (rock outcroppings, shrubs) elsewhere in the cell grid.
/// </summary>
/// <remarks>
/// Purely cosmetic today — nothing reads DecorationType for gameplay yet.
/// V2: local decoration → persistent object promotion. (ChunkCoord, LocalTileCoord) is already
/// the stable per-cell key <see cref="LocalTileDelta"/> uses; a future interaction command (e.g.
/// "mine the rock outcropping") would write a delta for that cell the first time it's touched —
/// no new identity scheme or persistence shape is needed, the delta overlay already is the
/// "this location now has tracked, permanent state" registry.
/// </remarks>
public static class LocalDecorationGenerator
{
    public static void Generate(LocalChunk chunk, ChunkCoord coord, BiomeType biome, int worldSeed, LocalGenConfig config)
    {
        var (primary, secondary) = DecorationsFor(biome);
        if (primary == LocalDecorationType.None && secondary == LocalDecorationType.None)
            return;

        var clusterNoise = new FastNoiseLite(worldSeed ^ LayerSeeds.LocalDecorationCluster);
        clusterNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        clusterNoise.SetFrequency(config.DecorationClusterFrequency);
        clusterNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        clusterNoise.SetFractalOctaves(2);

        var sparseNoise = new FastNoiseLite(worldSeed ^ LayerSeeds.LocalDecorationSparse);
        sparseNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        sparseNoise.SetFrequency(config.DecorationSparseFrequency);

        int n = config.LocalTilesPerWorldTileEdge;

        for (byte ly = 0; ly < chunk.Size; ly++)
        {
            for (byte lx = 0; lx < chunk.Size; lx++)
            {
                var local = new LocalTileCoord(lx, ly);
                ref var tile = ref chunk.GetTileRef(local);

                // Never decorate a river cell — carved channels stay clear water.
                if (((LocalTileFlags)tile.Flags & LocalTileFlags.River) != 0)
                    continue;

                var (absX, absY) = LocalCoordMath.ToAbsolute(coord, local, n, config.ChunkSizeTiles);

                if (primary != LocalDecorationType.None
                    && clusterNoise.GetNoise(absX, absY) > config.DecorationClusterThreshold)
                {
                    tile.DecorationType = (byte)primary;
                }
                else if (secondary != LocalDecorationType.None
                    && sparseNoise.GetNoise(absX, absY) > config.DecorationSparseThreshold)
                {
                    tile.DecorationType = (byte)secondary;
                }
            }
        }
    }

    /// <summary>Primary (clustered/common) and secondary (sparse/occasional) decoration for a biome.</summary>
    private static (LocalDecorationType Primary, LocalDecorationType Secondary) DecorationsFor(BiomeType biome) => biome switch
    {
        BiomeType.BorealForest or BiomeType.TemperateForest or BiomeType.TropicalRainforest
            => (LocalDecorationType.TreeStand, LocalDecorationType.RockOutcropping),
        BiomeType.Grassland or BiomeType.Plains or BiomeType.Savanna
            => (LocalDecorationType.Shrub, LocalDecorationType.RockOutcropping),
        BiomeType.Desert
            => (LocalDecorationType.SandDune, LocalDecorationType.RockOutcropping),
        BiomeType.Swamp
            => (LocalDecorationType.Wetland, LocalDecorationType.Shrub),
        BiomeType.Tundra
            => (LocalDecorationType.Shrub, LocalDecorationType.RockOutcropping),
        BiomeType.Hills
            => (LocalDecorationType.RockOutcropping, LocalDecorationType.TreeStand),
        BiomeType.Mountain or BiomeType.HighMountain or BiomeType.Volcanic
            => (LocalDecorationType.RockOutcropping, LocalDecorationType.None),
        // Ocean/CoastalWater/Beach: no land decoration.
        _ => (LocalDecorationType.None, LocalDecorationType.None),
    };
}
