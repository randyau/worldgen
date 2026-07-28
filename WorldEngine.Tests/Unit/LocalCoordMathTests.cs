using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;

namespace WorldEngine.Tests.Unit;

public class LocalCoordMathTests
{
    private const int LocalTilesPerWorldTileEdge = 1000;
    private const int ChunkSizeTiles = 40;
    private const int WorldTileWidth = 10;

    [Fact]
    public void ToAbsolute_FromAbsolute_RoundTrips()
    {
        var chunk = new ChunkCoord(new TileCoord(3, 2), 5, 7);
        var local = new LocalTileCoord(11, 22);

        var (absX, absY) = LocalCoordMath.ToAbsolute(chunk, local, LocalTilesPerWorldTileEdge, ChunkSizeTiles);
        var (roundTripChunk, roundTripLocal) = LocalCoordMath.FromAbsolute(
            absX, absY, LocalTilesPerWorldTileEdge, ChunkSizeTiles, WorldTileWidth);

        roundTripChunk.Should().Be(chunk);
        roundTripLocal.Should().Be(local);
    }

    [Fact]
    public void ToAbsolute_OriginOfTileZeroIsZero()
    {
        var chunk = new ChunkCoord(new TileCoord(0, 0), 0, 0);
        var local = new LocalTileCoord(0, 0);

        var (absX, absY) = LocalCoordMath.ToAbsolute(chunk, local, LocalTilesPerWorldTileEdge, ChunkSizeTiles);

        absX.Should().Be(0);
        absY.Should().Be(0);
    }

    [Fact]
    public void FromAbsolute_WrapsXCylindricallyAtWorldEdge()
    {
        long worldLocalWidth = (long)WorldTileWidth * LocalTilesPerWorldTileEdge;

        var (chunkAtEdge, localAtEdge) = LocalCoordMath.FromAbsolute(
            worldLocalWidth, 5, LocalTilesPerWorldTileEdge, ChunkSizeTiles, WorldTileWidth);
        var (chunkAtOrigin, localAtOrigin) = LocalCoordMath.FromAbsolute(
            0, 5, LocalTilesPerWorldTileEdge, ChunkSizeTiles, WorldTileWidth);

        chunkAtEdge.Should().Be(chunkAtOrigin, "wrapping one full world width in X must land back on the same cell");
        localAtEdge.Should().Be(localAtOrigin);
    }

    [Fact]
    public void ChunkCoord_Normalize_RollsOverflowIntoNeighboringWorldTileX()
    {
        var chunk = new ChunkCoord(new TileCoord(0, 3), 25, 0); // one chunk past this tile's east edge
        int chunksPerEdge = LocalTilesPerWorldTileEdge / ChunkSizeTiles; // 25

        var normalized = chunk.Normalize(chunksPerEdge, WorldTileWidth, 20);

        normalized.WorldTile.Should().Be(new TileCoord(1, 3));
        normalized.ChunkX.Should().Be(0);
        normalized.ChunkY.Should().Be(0);
    }

    [Fact]
    public void ChunkCoord_Normalize_WrapsWorldTileXCylindrically()
    {
        var chunk = new ChunkCoord(new TileCoord(WorldTileWidth - 1, 3), 25, 0);
        int chunksPerEdge = LocalTilesPerWorldTileEdge / ChunkSizeTiles;

        var normalized = chunk.Normalize(chunksPerEdge, WorldTileWidth, 20);

        normalized.WorldTile.Should().Be(new TileCoord(0, 3), "the world tile grid wraps in X like TileCoord.Wrap");
    }

    [Fact]
    public void ChunkCoord_Normalize_ClampsWorldTileYAtPole()
    {
        var chunk = new ChunkCoord(new TileCoord(2, 0), 0, -1);
        int chunksPerEdge = LocalTilesPerWorldTileEdge / ChunkSizeTiles;

        var normalized = chunk.Normalize(chunksPerEdge, WorldTileWidth, 20);

        normalized.WorldTile.Y.Should().Be(0, "the world does not wrap vertically, so Y clamps at the pole");
    }

    [Fact]
    public void ChunkCoord_Normalize_AlreadyInRangeIsUnchanged()
    {
        var chunk = new ChunkCoord(new TileCoord(4, 4), 3, 10);
        int chunksPerEdge = LocalTilesPerWorldTileEdge / ChunkSizeTiles;

        var normalized = chunk.Normalize(chunksPerEdge, WorldTileWidth, 20);

        normalized.Should().Be(chunk);
    }
}
