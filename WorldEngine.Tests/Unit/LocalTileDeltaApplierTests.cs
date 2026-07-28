using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

public class LocalTileDeltaApplierTests
{
    private static LocalChunk FlatChunk(int size, byte elevation, byte biomeType)
    {
        var coord = new ChunkCoord(new TileCoord(0, 0), 0, 0);
        var chunk = new LocalChunk(coord, size);
        foreach (var (c, _) in chunk.AllTiles())
            chunk.SetTile(c, new LocalTileData { Elevation = elevation, BiomeType = biomeType });
        return chunk;
    }

    private static string Payload(byte? elevation = null, byte? biomeType = null, byte? decorationType = null, byte? flags = null) =>
        JsonSerializer.Serialize(new LocalTileDeltaPayload(elevation, biomeType, decorationType, flags));

    [Fact]
    public void Apply_NoDeltas_LeavesChunkUnchanged()
    {
        var chunk = FlatChunk(4, elevation: 100, biomeType: 2);
        LocalTileDeltaApplier.Apply(chunk, Array.Empty<LocalTileDelta>());

        foreach (var (_, tile) in chunk.AllTiles())
        {
            tile.Elevation.Should().Be(100);
            tile.BiomeType.Should().Be(2);
        }
    }

    [Fact]
    public void Apply_ElevationOverride_OnlyChangesElevation()
    {
        var chunk = FlatChunk(4, elevation: 100, biomeType: 2);
        var target = new LocalTileCoord(1, 1);
        var delta = new LocalTileDelta(chunk.Coord, target, LocalChangeType.CellOverride, Payload(elevation: 250));

        LocalTileDeltaApplier.Apply(chunk, new[] { delta });

        var tile = chunk.GetTile(target);
        tile.Elevation.Should().Be(250);
        tile.BiomeType.Should().Be(2, "the payload didn't set BiomeType, so it must be left untouched");
    }

    [Fact]
    public void Apply_AllFieldsOverride_SetsEveryField()
    {
        var chunk = FlatChunk(4, elevation: 100, biomeType: 2);
        var target = new LocalTileCoord(2, 3);
        var delta = new LocalTileDelta(
            chunk.Coord, target, LocalChangeType.CellOverride,
            Payload(elevation: 10, biomeType: 5, decorationType: 7, flags: 1));

        LocalTileDeltaApplier.Apply(chunk, new[] { delta });

        var tile = chunk.GetTile(target);
        tile.Elevation.Should().Be(10);
        tile.BiomeType.Should().Be(5);
        tile.DecorationType.Should().Be(7);
        tile.Flags.Should().Be(1);
    }

    [Fact]
    public void Apply_OnlyTouchesTargetCell()
    {
        var chunk = FlatChunk(4, elevation: 100, biomeType: 2);
        var target = new LocalTileCoord(0, 0);
        var delta = new LocalTileDelta(chunk.Coord, target, LocalChangeType.CellOverride, Payload(elevation: 250));

        LocalTileDeltaApplier.Apply(chunk, new[] { delta });

        foreach (var (c, tile) in chunk.AllTiles())
        {
            if (c.Equals(target)) continue;
            tile.Elevation.Should().Be(100);
        }
    }

    [Fact]
    public void Apply_MultipleDeltas_EachAppliesToItsOwnCell()
    {
        var chunk = FlatChunk(4, elevation: 100, biomeType: 2);
        var a = new LocalTileCoord(0, 0);
        var b = new LocalTileCoord(3, 3);
        var deltas = new[]
        {
            new LocalTileDelta(chunk.Coord, a, LocalChangeType.CellOverride, Payload(elevation: 10)),
            new LocalTileDelta(chunk.Coord, b, LocalChangeType.CellOverride, Payload(elevation: 20)),
        };

        LocalTileDeltaApplier.Apply(chunk, deltas);

        chunk.GetTile(a).Elevation.Should().Be(10);
        chunk.GetTile(b).Elevation.Should().Be(20);
    }

    [Fact]
    public void Apply_IsDeterministic_SameInputsProduceSameOutput()
    {
        var chunk1 = FlatChunk(4, elevation: 100, biomeType: 2);
        var chunk2 = FlatChunk(4, elevation: 100, biomeType: 2);
        var deltas = new[]
        {
            new LocalTileDelta(chunk1.Coord, new LocalTileCoord(1, 2), LocalChangeType.CellOverride, Payload(elevation: 77, flags: 1)),
        };

        LocalTileDeltaApplier.Apply(chunk1, deltas);
        LocalTileDeltaApplier.Apply(chunk2, deltas);

        foreach (var (c, tile) in chunk1.AllTiles())
        {
            var other = chunk2.GetTile(c);
            other.Elevation.Should().Be(tile.Elevation);
            other.BiomeType.Should().Be(tile.BiomeType);
            other.DecorationType.Should().Be(tile.DecorationType);
            other.Flags.Should().Be(tile.Flags);
        }
    }
}
