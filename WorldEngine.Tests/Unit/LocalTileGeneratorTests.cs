using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

public class LocalTileGeneratorTests
{
    [Fact]
    public void GenerateFlat_ProducesChunkSizedGrid_AllCellsMatchParentTile()
    {
        var config = new LocalGenConfig { ChunkSizeTiles = 40, LocalTilesPerWorldTileEdge = 1000 };
        var coord = new ChunkCoord(new TileCoord(2, 3), 0, 0);
        var parentTile = new TileData { Elevation = 123, BiomeType = 7 };

        var chunk = LocalTileGenerator.GenerateFlat(coord, parentTile, config);

        chunk.Size.Should().Be(config.ChunkSizeTiles);
        chunk.Coord.Should().Be(coord);

        foreach (var (_, tile) in chunk.AllTiles())
        {
            tile.Elevation.Should().Be(parentTile.Elevation);
            tile.BiomeType.Should().Be(parentTile.BiomeType);
        }
    }

    [Fact]
    public void GenerateFlat_TileCountMatchesSizeSquared()
    {
        var config = new LocalGenConfig { ChunkSizeTiles = 40, LocalTilesPerWorldTileEdge = 1000 };
        var chunk = LocalTileGenerator.GenerateFlat(
            new ChunkCoord(new TileCoord(0, 0), 0, 0), new TileData(), config);

        chunk.AllTiles().Count().Should().Be(config.ChunkSizeTiles * config.ChunkSizeTiles);
    }
}
