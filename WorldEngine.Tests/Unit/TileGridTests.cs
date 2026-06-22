using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Tests.Unit;

public class TileGridTests
{
    [Fact]
    public void TileGrid_EastWestWrapping()
    {
        var grid = new TileGrid(32, 32);
        var tileAt31 = new TileData { Elevation = 77 };

        grid.SetTile(new TileCoord(31, 5), tileAt31);

        var retrieved = grid.GetTile(new TileCoord(-1, 5));
        retrieved.Elevation.Should().Be(77);
    }

    [Fact]
    public void TileGrid_EastWestWrappingHighEnd()
    {
        var grid = new TileGrid(32, 32);
        var tileAt0 = new TileData { Elevation = 42 };

        grid.SetTile(new TileCoord(0, 5), tileAt0);

        var retrieved = grid.GetTile(new TileCoord(32, 5));
        retrieved.Elevation.Should().Be(42);
    }

    [Fact]
    public void TileGrid_NorthSouthClampedNotWrapped()
    {
        var grid = new TileGrid(32, 32);
        var tileAt0 = new TileData { Elevation = 123 };

        grid.SetTile(new TileCoord(0, 0), tileAt0);

        var retrievedAtNegative = grid.GetTile(new TileCoord(0, -1));
        retrievedAtNegative.Elevation.Should().Be(123);

        var tileAtMax = new TileData { Elevation = 88 };
        grid.SetTile(new TileCoord(0, 31), tileAtMax);

        var retrievedBeyondMax = grid.GetTile(new TileCoord(0, 32));
        retrievedBeyondMax.Elevation.Should().Be(88);
    }

    [Fact]
    public void TileGrid_NullChunkReturnsDefault()
    {
        var grid = new TileGrid(32, 32);

        var tile = grid.GetTile(new TileCoord(5, 5));

        tile.Elevation.Should().Be(0);
        tile.Fertility.Should().Be(0);
        tile.StaticFlags.Should().Be(TileStaticFlags.None);
    }

    [Fact]
    public void TileGrid_SetTileCreatesChunk()
    {
        var grid = new TileGrid(32, 32);
        var tileData = new TileData { Elevation = 55, Fertility = 200 };

        grid.SetTile(new TileCoord(5, 7), tileData);

        var retrieved = grid.GetTile(new TileCoord(5, 7));
        retrieved.Elevation.Should().Be(55);
        retrieved.Fertility.Should().Be(200);
    }

    [Fact]
    public void TileGrid_DirtyFlagSetAfterWrite()
    {
        var grid = new TileGrid(32, 32);
        var tileData = new TileData { Elevation = 99 };

        grid.SetTile(new TileCoord(10, 10), tileData);

        var dirtyChunks = grid.AllDirtyChunks().ToList();
        dirtyChunks.Should().NotBeEmpty();
    }

    [Fact]
    public void TileGrid_DirtyFlagClearsAfterClearDirty()
    {
        var grid = new TileGrid(32, 32);
        var tileData = new TileData { Elevation = 77 };

        grid.SetTile(new TileCoord(10, 10), tileData);

        var dirtyChunksBefore = grid.AllDirtyChunks().ToList();
        dirtyChunksBefore.Should().NotBeEmpty();

        foreach (var chunk in dirtyChunksBefore)
        {
            chunk.ClearDirty();
        }

        var dirtyChunksAfter = grid.AllDirtyChunks().ToList();
        dirtyChunksAfter.Should().BeEmpty();
    }

    [Fact]
    public void TileGrid_FlatIndexIsConsistent()
    {
        var grid = new TileGrid(32, 32);

        grid.FlatIndex(new TileCoord(0, 0)).Should().Be(0);
        grid.FlatIndex(new TileCoord(1, 0)).Should().Be(1);
        grid.FlatIndex(new TileCoord(0, 1)).Should().Be(32);
    }

    [Fact]
    public void TileGrid_FlatIndexForSeasonalProfiles()
    {
        var grid = new TileGrid(4, 4);

        var indices = new HashSet<int>();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var idx = grid.FlatIndex(new TileCoord(x, y));
                indices.Add(idx);
                idx.Should().BeGreaterThanOrEqualTo(0);
                idx.Should().BeLessThan(16);
            }
        }

        indices.Should().HaveCount(16);
    }

    [Fact]
    public void TileChunk_SummaryFlagsUpdatedOnSet()
    {
        var chunk = new TileChunk();
        var volcanicTile = new TileData { StaticFlags = TileStaticFlags.IsVolcanic };

        chunk.SetTile(0, 0, volcanicTile);

        chunk.SummaryFlags.Should().HaveFlag(ChunkSummaryFlags.HasVolcanicTile);
    }
}
