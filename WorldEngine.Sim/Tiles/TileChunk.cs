using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Tiles;

public sealed class TileChunk
{
    public const int Size = 16;
    private readonly TileData[] _tiles = new TileData[Size * Size];

    public ChunkSummaryFlags SummaryFlags { get; set; }
    public bool IsDirty { get; private set; }

    public ref TileData GetTileRef(int localX, int localY) =>
        ref _tiles[localY * Size + localX];

    public TileData GetTile(int localX, int localY) =>
        _tiles[localY * Size + localX];

    public void SetTile(int localX, int localY, TileData tile)
    {
        _tiles[localY * Size + localX] = tile;
        IsDirty = true;
        AccumulateSummaryFlags(tile);
    }

    public void ClearDirty() => IsDirty = false;

    public IEnumerable<(TileCoord, TileData)> AllTiles(int chunkX, int chunkY)
    {
        int baseX = chunkX * Size;
        int baseY = chunkY * Size;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                yield return (new TileCoord(baseX + x, baseY + y), _tiles[y * Size + x]);
    }

    private void AccumulateSummaryFlags(TileData tile)
    {
        if ((tile.StaticFlags & TileStaticFlags.IsVolcanic) != 0)
            SummaryFlags |= ChunkSummaryFlags.HasVolcanicTile;
        if ((tile.StaticFlags & TileStaticFlags.IsFaultLine) != 0)
            SummaryFlags |= ChunkSummaryFlags.HasFaultLineTile;
        if ((tile.StaticFlags & TileStaticFlags.HasRiver) != 0)
            SummaryFlags |= ChunkSummaryFlags.HasRiverTile;
        var biome = (Core.BiomeType)tile.BiomeType;
        if (biome is Core.BiomeType.BorealForest or Core.BiomeType.TemperateForest or Core.BiomeType.TropicalRainforest)
            SummaryFlags |= ChunkSummaryFlags.HasForestTile;
        if ((tile.DynFlags & TileDynFlags.HasActiveDisaster) != 0)
            SummaryFlags |= ChunkSummaryFlags.HasActiveDisaster;
    }
}
