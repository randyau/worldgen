namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// A Size×Size grid of local terrain. Always derivable from (WorldSeed, ChunkCoord, parent
/// TileData, border manifests) — never itself persisted; see
/// docs/phases/m11_local_scale_generation.md "regenerate base terrain on demand".
/// </summary>
public sealed class LocalChunk
{
    public ChunkCoord Coord { get; }
    public int Size { get; }

    private readonly LocalTileData[] _tiles;

    public LocalChunk(ChunkCoord coord, int size)
    {
        Coord = coord;
        Size = size;
        _tiles = new LocalTileData[size * size];
    }

    public ref LocalTileData GetTileRef(LocalTileCoord c) => ref _tiles[c.Y * Size + c.X];

    public LocalTileData GetTile(LocalTileCoord c) => _tiles[c.Y * Size + c.X];

    public void SetTile(LocalTileCoord c, LocalTileData tile) => _tiles[c.Y * Size + c.X] = tile;

    public IEnumerable<(LocalTileCoord Coord, LocalTileData Tile)> AllTiles()
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                yield return (new LocalTileCoord((byte)x, (byte)y), _tiles[y * Size + x]);
    }
}
