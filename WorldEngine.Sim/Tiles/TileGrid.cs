using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Tiles;

public sealed class TileGrid
{
    private readonly TileChunk?[,] _chunks;

    public int TileWidth { get; }
    public int TileHeight { get; }
    private int ChunkCountX { get; }
    private int ChunkCountY { get; }

    public TileGrid(int tileWidth, int tileHeight)
    {
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        ChunkCountX = (tileWidth + TileChunk.Size - 1) / TileChunk.Size;
        ChunkCountY = (tileHeight + TileChunk.Size - 1) / TileChunk.Size;
        _chunks = new TileChunk?[ChunkCountX, ChunkCountY];
    }

    /// <summary>Get tile. Applies East-West cylinder wrapping; North-South is clamped.</summary>
    public TileData GetTile(TileCoord coord)
    {
        var (cx, cy, lx, ly) = Decompose(coord);
        return _chunks[cx, cy]?.GetTile(lx, ly) ?? default;
    }

    /// <summary>Set tile. Applies East-West cylinder wrapping; creates chunk lazily.</summary>
    public void SetTile(TileCoord coord, TileData tile)
    {
        var (cx, cy, lx, ly) = Decompose(coord);
        _chunks[cx, cy] ??= new TileChunk();
        _chunks[cx, cy]!.SetTile(lx, ly, tile);
    }

    /// <summary>Linear index into parallel arrays (e.g. SeasonalProfiles). y * TileWidth + x.</summary>
    public int FlatIndex(TileCoord coord)
    {
        int x = ((coord.X % TileWidth) + TileWidth) % TileWidth;
        int y = Math.Clamp(coord.Y, 0, TileHeight - 1);
        return y * TileWidth + x;
    }

    public IEnumerable<TileChunk> AllChunks()
    {
        foreach (var chunk in _chunks)
            if (chunk is not null) yield return chunk;
    }

    public IEnumerable<TileChunk> AllDirtyChunks()
    {
        foreach (var chunk in _chunks)
            if (chunk is { IsDirty: true }) yield return chunk;
    }

    private (int cx, int cy, int lx, int ly) Decompose(TileCoord coord)
    {
        int x = ((coord.X % TileWidth) + TileWidth) % TileWidth;
        int y = Math.Clamp(coord.Y, 0, TileHeight - 1);
        return (x / TileChunk.Size, y / TileChunk.Size, x % TileChunk.Size, y % TileChunk.Size);
    }
}
