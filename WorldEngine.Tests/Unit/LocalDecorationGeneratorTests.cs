using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

public class LocalDecorationGeneratorTests
{
    private static LocalGenConfig MakeConfig() => new()
    {
        ChunkSizeTiles = 40,
        LocalTilesPerWorldTileEdge = 1000,
        DecorationClusterFrequency = 0.015f,
        DecorationClusterThreshold = 0.15f,
        DecorationSparseFrequency = 0.25f,
        DecorationSparseThreshold = 0.55f,
    };

    private static LocalChunk FlatChunk(ChunkCoord coord, LocalGenConfig config, byte biome, byte flags = 0)
    {
        var chunk = new LocalChunk(coord, config.ChunkSizeTiles);
        foreach (var (c, _) in chunk.AllTiles())
            chunk.SetTile(c, new LocalTileData { Elevation = 100, BiomeType = biome, Flags = flags });
        return chunk;
    }

    [Fact]
    public void Generate_SameInputs_ProducesSameDecorations()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(4, 4), 2, 2);

        var chunkA = FlatChunk(coord, config, (byte)BiomeType.TemperateForest);
        var chunkB = FlatChunk(coord, config, (byte)BiomeType.TemperateForest);

        LocalDecorationGenerator.Generate(chunkA, coord, BiomeType.TemperateForest, worldSeed: 12345, config);
        LocalDecorationGenerator.Generate(chunkB, coord, BiomeType.TemperateForest, worldSeed: 12345, config);

        foreach (var (c, tileA) in chunkA.AllTiles())
        {
            var tileB = chunkB.GetTile(c);
            tileA.DecorationType.Should().Be(tileB.DecorationType);
        }
    }

    [Fact]
    public void Generate_ForestBiome_ProducesSomeTreeStandsAndSomeBareCells()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(1, 1), 0, 0);
        var chunk = FlatChunk(coord, config, (byte)BiomeType.TemperateForest);

        LocalDecorationGenerator.Generate(chunk, coord, BiomeType.TemperateForest, worldSeed: 999, config);

        var decorations = chunk.AllTiles().Select(t => (LocalDecorationType)t.Tile.DecorationType).ToList();
        decorations.Should().Contain(LocalDecorationType.TreeStand, "forest clusters should place at least one tree stand across a 40x40 chunk");
        decorations.Should().Contain(LocalDecorationType.None, "decoration should be sparse/patchy, not covering every cell");
    }

    [Fact]
    public void Generate_WaterBiome_NeverDecorates()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(5, 5), 1, 1);
        var chunk = FlatChunk(coord, config, (byte)BiomeType.Ocean);

        LocalDecorationGenerator.Generate(chunk, coord, BiomeType.Ocean, worldSeed: 42, config);

        foreach (var (_, tile) in chunk.AllTiles())
            tile.DecorationType.Should().Be((byte)LocalDecorationType.None);
    }

    [Fact]
    public void Generate_RiverCells_NeverDecorated()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(2, 2), 3, 3);
        var chunk = FlatChunk(coord, config, (byte)BiomeType.TemperateForest, flags: (byte)LocalTileFlags.River);

        LocalDecorationGenerator.Generate(chunk, coord, BiomeType.TemperateForest, worldSeed: 7, config);

        foreach (var (_, tile) in chunk.AllTiles())
            tile.DecorationType.Should().Be((byte)LocalDecorationType.None);
    }
}
