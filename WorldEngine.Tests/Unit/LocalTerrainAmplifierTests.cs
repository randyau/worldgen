using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

namespace WorldEngine.Tests.Unit;

public class LocalTerrainAmplifierTests
{
    private static LocalGenConfig MakeConfig() => new()
    {
        ChunkSizeTiles = 40,
        LocalTilesPerWorldTileEdge = 1000,
        EdgeBlendBandTiles = 100,
        NoiseFrequency = 0.05f,
        NoiseOctaves = 3,
        NoiseAmplitude = 6f,
    };

    private static BorderManifest FlatManifest(byte north, byte south, byte east, byte west)
    {
        var m = new BorderManifest();
        Fill(m.North, north);
        Fill(m.South, south);
        Fill(m.East, east);
        Fill(m.West, west);
        return m;
    }

    private static void Fill(BorderManifestSample[] samples, byte elevation)
    {
        for (int i = 0; i < samples.Length; i++)
            samples[i].Elevation = elevation;
    }

    [Fact]
    public void Amplify_ProducesChunkSizedGrid()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(2, 2), 3, 3);
        var parent = new TileData { Elevation = 100, BiomeType = 1 };
        var manifest = FlatManifest(100, 100, 100, 100);

        var chunk = LocalTerrainAmplifier.Amplify(coord, parent, manifest, worldSeed: 42, config);

        chunk.Size.Should().Be(config.ChunkSizeTiles);
        chunk.AllTiles().Count().Should().Be(config.ChunkSizeTiles * config.ChunkSizeTiles);
    }

    [Fact]
    public void Amplify_IsDeterministic_SameInputsProduceSameOutput()
    {
        var config = MakeConfig();
        var coord = new ChunkCoord(new TileCoord(1, 1), 2, 2);
        var parent = new TileData { Elevation = 80, BiomeType = 2 };
        var manifest = FlatManifest(80, 80, 80, 80);

        var chunk1 = LocalTerrainAmplifier.Amplify(coord, parent, manifest, worldSeed: 7, config);
        var chunk2 = LocalTerrainAmplifier.Amplify(coord, parent, manifest, worldSeed: 7, config);

        foreach (var (c, tile) in chunk1.AllTiles())
            chunk2.GetTile(c).Elevation.Should().Be(tile.Elevation);
    }

    [Fact]
    public void Amplify_InteriorCell_BlendsTowardCenterAwayFromEdgeBand()
    {
        var config = MakeConfig();
        // Manifest elevation deliberately far from parent's own elevation, so a leaked blend would
        // be obvious; noise amplitude keeps the result within a small band of the parent's value.
        var coord = new ChunkCoord(new TileCoord(5, 5), (config.ChunksPerWorldTileEdge - 1) / 2, (config.ChunksPerWorldTileEdge - 1) / 2);
        var parent = new TileData { Elevation = 128, BiomeType = 3 };
        var manifest = FlatManifest(255, 255, 255, 255);

        var chunk = LocalTerrainAmplifier.Amplify(coord, parent, manifest, worldSeed: 99, config);
        var centerTile = chunk.GetTile(new LocalTileCoord((byte)(config.ChunkSizeTiles / 2), (byte)(config.ChunkSizeTiles / 2)));

        centerTile.Elevation.Should().BeInRange(
            (byte)(parent.Elevation - config.NoiseAmplitude - 1),
            (byte)(parent.Elevation + config.NoiseAmplitude + 1));
    }

    [Fact]
    public void Amplify_EastWestBoundary_IsContinuousAcrossWorldTiles()
    {
        // Noise amplitude zeroed: the boundary columns sit at adjacent-but-distinct absolute
        // coordinates (999 vs 1000), so the continuous noise term differs slightly between them by
        // design — only the macro (manifest-blended) component is required to match exactly.
        var config = MakeConfig();
        config.NoiseAmplitude = 0f;
        int worldSeed = 12345;
        int chunksPerEdge = config.ChunksPerWorldTileEdge;

        var tileA = new TileCoord(0, 4);
        var tileB = new TileCoord(1, 4);
        var parentA = new TileData { Elevation = 60, BiomeType = 1 };
        var parentB = new TileData { Elevation = 200, BiomeType = 1 };

        byte sharedElevation = (byte)((parentA.Elevation + parentB.Elevation) / 2);
        // A's East edge and B's West edge are built from the same blend formula in
        // BorderManifestBuilder, so both sides carry the identical sample value.
        var manifestA = FlatManifest(north: parentA.Elevation, south: parentA.Elevation, east: sharedElevation, west: parentA.Elevation);
        var manifestB = FlatManifest(north: parentB.Elevation, south: parentB.Elevation, east: parentB.Elevation, west: sharedElevation);

        // Rightmost chunk of tile A (its East edge) vs leftmost chunk of tile B (its West edge).
        var chunkA = LocalTerrainAmplifier.Amplify(
            new ChunkCoord(tileA, chunksPerEdge - 1, 3), parentA, manifestA, worldSeed, config);
        var chunkB = LocalTerrainAmplifier.Amplify(
            new ChunkCoord(tileB, 0, 3), parentB, manifestB, worldSeed, config);

        for (byte y = 0; y < config.ChunkSizeTiles; y++)
        {
            byte edgeOfA = chunkA.GetTile(new LocalTileCoord((byte)(config.ChunkSizeTiles - 1), y)).Elevation;
            byte edgeOfB = chunkB.GetTile(new LocalTileCoord(0, y)).Elevation;
            edgeOfA.Should().Be(edgeOfB, $"row {y} must match across the A/B world-tile seam");
        }
    }

    [Fact]
    public void Amplify_NorthSouthBoundary_IsContinuousAwayFromCorners()
    {
        // Noise amplitude zeroed — see comment in the East/West continuity test above.
        var config = MakeConfig();
        config.NoiseAmplitude = 0f;
        int worldSeed = 54321;
        int chunksPerEdge = config.ChunksPerWorldTileEdge;

        var tileA = new TileCoord(4, 0);
        var tileC = new TileCoord(4, 1); // south neighbor of A

        var parentA = new TileData { Elevation = 70, BiomeType = 1 };
        var parentC = new TileData { Elevation = 190, BiomeType = 1 };

        byte sharedElevation = (byte)((parentA.Elevation + parentC.Elevation) / 2);
        var manifestA = FlatManifest(north: parentA.Elevation, south: sharedElevation, east: parentA.Elevation, west: parentA.Elevation);
        var manifestC = FlatManifest(north: sharedElevation, south: parentC.Elevation, east: parentC.Elevation, west: parentC.Elevation);

        // Bottommost chunk of tile A (its South edge) vs topmost chunk of tile C (its North edge),
        // using a chunk column away from the tile's east/west edges so we're not also in a corner band.
        int midChunk = chunksPerEdge / 2;
        var chunkA = LocalTerrainAmplifier.Amplify(
            new ChunkCoord(tileA, midChunk, chunksPerEdge - 1), parentA, manifestA, worldSeed, config);
        var chunkC = LocalTerrainAmplifier.Amplify(
            new ChunkCoord(tileC, midChunk, 0), parentC, manifestC, worldSeed, config);

        for (byte x = 0; x < config.ChunkSizeTiles; x++)
        {
            byte edgeOfA = chunkA.GetTile(new LocalTileCoord(x, (byte)(config.ChunkSizeTiles - 1))).Elevation;
            byte edgeOfC = chunkC.GetTile(new LocalTileCoord(x, 0)).Elevation;
            edgeOfA.Should().Be(edgeOfC, $"column {x} must match across the A/C world-tile seam");
        }
    }
}
