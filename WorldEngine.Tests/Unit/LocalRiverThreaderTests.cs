using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

public class LocalRiverThreaderTests
{
    private static LocalGenConfig MakeConfig() => new()
    {
        ChunkSizeTiles = 40,
        LocalTilesPerWorldTileEdge = 1000,
        EdgeBlendBandTiles = 100,
        NoiseFrequency = 0.05f,
        NoiseOctaves = 3,
        NoiseAmplitude = 0f, // isolate river carving from elevation-detail noise
        RiverChannelDepth = 20,
        RiverSourceWidthTiles = 15f,
    };

    private static BorderManifest EmptyManifest() => new();

    private static void StampCrossing(BorderManifestSample[] samples, int lo, int hi, byte flowVolume)
    {
        for (int i = lo; i <= hi; i++)
        {
            samples[i].HasRiverCrossing = 1;
            samples[i].FlowVolume = flowVolume;
        }
    }

    private static LocalChunk FlatChunk(ChunkCoord coord, LocalGenConfig config, byte elevation)
    {
        var chunk = new LocalChunk(coord, config.ChunkSizeTiles);
        foreach (var (c, _) in chunk.AllTiles())
            chunk.SetTile(c, new LocalTileData { Elevation = elevation, BiomeType = 1 });
        return chunk;
    }

    [Fact]
    public void Thread_NoRiverFlag_NoOp()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(2, 2), 3, 3);
        var parent = new TileData { Elevation = 100, StaticFlags = TileStaticFlags.None };
        var manifest = EmptyManifest();
        StampCrossing(manifest.West, 10, 20, 200); // present but tile has no HasRiver flag

        var chunk = FlatChunk(coord, config, 100);
        LocalRiverThreader.Thread(chunk, coord, parent, manifest, config);

        foreach (var (_, tile) in chunk.AllTiles())
            tile.Flags.Should().Be(0);
    }

    [Fact]
    public void Thread_NoCrossingsOnAnyEdge_NoOp()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(2, 2), 3, 3);
        var parent = new TileData { Elevation = 100, StaticFlags = TileStaticFlags.HasRiver };
        var manifest = EmptyManifest(); // no edge has a HasRiverCrossing run

        var chunk = FlatChunk(coord, config, 100);
        LocalRiverThreader.Thread(chunk, coord, parent, manifest, config);

        foreach (var (_, tile) in chunk.AllTiles())
            tile.Flags.Should().Be(0);
    }

    [Fact]
    public void Thread_SingleBoundaryAnchor_CarvesChannelAtTheAnchor()
    {
        var config = MakeConfig();
        // West edge, centered (samples 30..33), so posAlongEdge ~ 0.5 -> anchor row near the
        // chunk's vertical center for chunk row 12 (of 25 chunks per 1000-tile edge).
        var coord = new ChunkCoord(new TileCoord(2, 2), 0, config.ChunksPerWorldTileEdge / 2);
        var parent = new TileData { Elevation = 150, StaticFlags = TileStaticFlags.HasRiver };
        var manifest = EmptyManifest();
        StampCrossing(manifest.West, 30, 33, 200);

        var chunk = FlatChunk(coord, config, 150);
        LocalRiverThreader.Thread(chunk, coord, parent, manifest, config);

        // The chunk's first column (touching the tile's west edge) at the anchor's local row must
        // be carved: local (0,0) is the chunk's absolute-Y start, which lines up with the anchor.
        var carved = chunk.AllTiles().Where(t => t.Tile.Flags != 0).ToList();
        carved.Should().NotBeEmpty();
        carved.Should().OnlyContain(t => t.Tile.Elevation == 130); // 150 - RiverChannelDepth(20)
    }

    [Fact]
    public void Thread_TwoBoundaryAnchors_ConnectsThem()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(2, 2), config.ChunksPerWorldTileEdge / 2, config.ChunksPerWorldTileEdge / 2);
        var parent = new TileData { Elevation = 150, StaticFlags = TileStaticFlags.HasRiver };
        var manifest = EmptyManifest();
        StampCrossing(manifest.West, 30, 33, 200); // ~ mid-height
        StampCrossing(manifest.East, 30, 33, 200); // ~ mid-height, same row -> roughly horizontal channel

        var chunk = FlatChunk(coord, config, 150);
        LocalRiverThreader.Thread(chunk, coord, parent, manifest, config);

        chunk.AllTiles().Should().Contain(t => t.Tile.Flags != 0);
    }

    [Fact]
    public void Thread_IsDeterministic_SameInputsProduceSameOutput()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(1, 1), 5, 5);
        var parent = new TileData { Elevation = 120, StaticFlags = TileStaticFlags.HasRiver };
        var manifest = EmptyManifest();
        StampCrossing(manifest.North, 10, 14, 180);
        StampCrossing(manifest.South, 40, 44, 180);

        var chunk1 = FlatChunk(coord, config, 120);
        var chunk2 = FlatChunk(coord, config, 120);
        LocalRiverThreader.Thread(chunk1, coord, parent, manifest, config);
        LocalRiverThreader.Thread(chunk2, coord, parent, manifest, config);

        foreach (var (c, tile) in chunk1.AllTiles())
        {
            var other = chunk2.GetTile(c);
            other.Flags.Should().Be(tile.Flags);
            other.Elevation.Should().Be(tile.Elevation);
        }
    }

    [Fact]
    public void Thread_RecoveredAnchor_MatchesAcrossSharedEdge()
    {
        // BorderManifestBuilder.ApplyCrossing stamps both sides of a shared edge from the same
        // RiverCrossing record, so both tiles' manifests carry byte-identical marked-sample runs
        // on their shared edge. This is the property LocalRiverThreader's anchor recovery relies
        // on for cross-tile continuity — verify it holds independent of BorderManifestBuilder by
        // constructing the identical stamp directly, the same way the two edges would come out.
        var manifestA = new BorderManifest();
        var manifestB = new BorderManifest();
        StampCrossing(manifestA.East, 12, 15, 210);
        StampCrossing(manifestB.West, 12, 15, 210);

        var config = MakeConfig();
        int n = config.LocalTilesPerWorldTileEdge;
        var anchorsA = new List<(double X, double Y, double WidthTiles)>();
        var anchorsB = new List<(double X, double Y, double WidthTiles)>();

        LocalRiverThreader.TryAddAnchor(manifestA.East, EdgeDirection.East, new TileCoord(0, 4), n, anchorsA);
        LocalRiverThreader.TryAddAnchor(manifestB.West, EdgeDirection.West, new TileCoord(1, 4), n, anchorsB);

        anchorsA.Should().HaveCount(1);
        anchorsB.Should().HaveCount(1);
        anchorsA[0].WidthTiles.Should().Be(anchorsB[0].WidthTiles);
        // A's east boundary sits at tileOriginX_A + (n-1); B's west boundary sits at
        // tileOriginX_B + 0 = tileOriginX_A + n — adjacent-but-distinct by construction (same
        // one-unit offset LocalTerrainAmplifier's noise term has across this same seam).
        (anchorsB[0].X - anchorsA[0].X).Should().Be(1);
        anchorsA[0].Y.Should().Be(anchorsB[0].Y);
    }

    [Fact]
    public void Thread_BoundaryColumns_AgreeAtCrossingCenterRow()
    {
        var config = MakeConfig();
        int chunksPerEdge = config.ChunksPerWorldTileEdge;

        var tileA = new TileCoord(0, 4);
        var tileB = new TileCoord(1, 4);
        var parentA = new TileData { Elevation = 150, StaticFlags = TileStaticFlags.HasRiver };
        var parentB = new TileData { Elevation = 150, StaticFlags = TileStaticFlags.HasRiver };

        var manifestA = EmptyManifest();
        var manifestB = EmptyManifest();
        StampCrossing(manifestA.East, 30, 33, 210); // posAlongEdge ~ 0.5 -> row 500 of 1000
        StampCrossing(manifestB.West, 30, 33, 210); // identical stamp, mirroring BorderManifestBuilder

        var chunkCoordA = new ChunkCoord(tileA, chunksPerEdge - 1, chunksPerEdge / 2);
        var chunkCoordB = new ChunkCoord(tileB, 0, chunksPerEdge / 2);

        var chunkA = FlatChunk(chunkCoordA, config, 150);
        var chunkB = FlatChunk(chunkCoordB, config, 150);
        LocalRiverThreader.Thread(chunkA, chunkCoordA, parentA, manifestA, config);
        LocalRiverThreader.Thread(chunkB, chunkCoordB, parentB, manifestB, config);

        // The crossing's own anchor row (local Y within this mid-height chunk) must be carved on
        // both sides of the boundary — the point itself is shared exactly (dist=0 from the anchor
        // to itself), even though the two channels approach it from different interior directions.
        int n = config.LocalTilesPerWorldTileEdge;
        double posAlongEdge = ((30 + 33) / 2.0 + 0.5) / BorderManifest.SampleCount;
        long anchorY = (long)tileA.Y * n + (long)(posAlongEdge * n);
        int localRowInChunk = (int)(anchorY - (long)tileA.Y * n - (long)chunkCoordA.ChunkY * config.ChunkSizeTiles);

        var edgeOfA = chunkA.GetTile(new LocalTileCoord((byte)(config.ChunkSizeTiles - 1), (byte)localRowInChunk));
        var edgeOfB = chunkB.GetTile(new LocalTileCoord(0, (byte)localRowInChunk));

        (edgeOfA.Flags != 0).Should().BeTrue();
        (edgeOfB.Flags != 0).Should().BeTrue();
    }
}
