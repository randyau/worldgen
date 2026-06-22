# Phase 2 — Epic 1.2: Tile World Data Structures
**Status:** NOT STARTED  
**Requires:** Phase 1 complete  
**Reads required:** `docs/interface_contracts.md` (TileData, all flag enums, IWorldStateReadOnly)

---

## Goal
Define the in-memory tile representation and all its supporting types. No generation logic yet — just the data structures that world gen will populate and the sim will read.

---

## Story 1.2.1 — TileData + Flag Enums

**Files to create:**
```
WorldEngine.Sim/Tiles/TileData.cs           # 14-byte struct
WorldEngine.Sim/Tiles/TileStaticFlags.cs    # ushort, 9 bits
WorldEngine.Sim/Tiles/TileDynFlags.cs       # byte, 1 bit
WorldEngine.Sim/Tiles/BiomeType.cs          # enum (16 values)
WorldEngine.Sim/Tiles/ChunkSummaryFlags.cs  # byte, 5 bits
WorldEngine.Sim/Tiles/SeasonalProfile.cs    # 8-byte struct
```

**TileData must be exactly 14 bytes.** Use `[StructLayout(LayoutKind.Sequential, Pack = 1)]`. Add a startup assertion:
```csharp
static TileData() => System.Diagnostics.Debug.Assert(
    Marshal.SizeOf<TileData>() == 14, "TileData size invariant broken");
```

See `docs/interface_contracts.md` for exact field definitions and all enum values. Do not define fields from memory — read the contracts doc.

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/TileDataTests.cs`):
```
TileData_SizeIsExactly14Bytes                  # Marshal.SizeOf<TileData>() == 14
TileData_DefaultIsAllZero                      # new TileData() all zero
TileStaticFlags_OrthogonalBits                 # no two flags share a bit
TileDynFlags_OrthogonalBits                    # no two flags share a bit
TileStaticFlags_IsUshort                       # sizeof(TileStaticFlags) == 2
TileDynFlags_IsByte                            # sizeof(TileDynFlags) == 1
SeasonalProfile_SizeIsExactly8Bytes            # Marshal.SizeOf<SeasonalProfile>() == 8
BiomeType_HasExactly16Values                   # Enum.GetValues count == 16
ChunkSummaryFlags_OrthogonalBits               # no two flags share a bit
```

**Done when:** Tests pass. Struct sizes locked.

---

## Story 1.2.2 — TileChunk + TileGrid

**Files to create:**
```
WorldEngine.Sim/Tiles/TileChunk.cs    # 16×16 TileData array + ChunkSummaryFlags
WorldEngine.Sim/Tiles/TileGrid.cs     # chunk array, dirty tracking, coordinate lookup
```

**TileChunk:**
```csharp
public sealed class TileChunk
{
    public const int Size = 16;
    private readonly TileData[] _tiles = new TileData[Size * Size];
    public ChunkSummaryFlags SummaryFlags { get; set; }
    public bool IsDirty { get; private set; }

    public ref TileData GetTileRef(int localX, int localY) { ... }
    public TileData GetTile(int localX, int localY) { ... }
    public void SetTile(int localX, int localY, TileData tile) { IsDirty = true; ... }
    public void ClearDirty() => IsDirty = false;
    public IEnumerable<(TileCoord, TileData)> AllTiles(int chunkX, int chunkY) { ... }
}
```

**TileGrid:**
- Stores `TileChunk?[,]` — null chunks for uncreated (initially all ocean) tiles
- `GetTile(TileCoord)`: wraps X, clamps Y, null chunk returns default `TileData`
- `SetTile(TileCoord, TileData)`: wraps X, creates chunk lazily if null
- `FlatIndex(TileCoord)`: returns `y * TileWidth + x` for parallel array indexing
- `AllChunks()`: yields all non-null chunks
- `AllDirtyChunks()`: yields only dirty chunks

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/TileGridTests.cs`):
```
TileGrid_EastWestWrapping                      # GetTile(x=-1) same as GetTile(x=Width-1)
TileGrid_EastWestWrappingHighEnd               # GetTile(x=Width) same as GetTile(x=0)
TileGrid_NorthSouthClampedNotWrapped           # GetTile(y=-1) same as GetTile(y=0)
TileGrid_NullChunkReturnsDefault               # uninit tile returns TileData.default
TileGrid_SetTileCreatesChunk                   # SetTile on null chunk creates it
TileGrid_DirtyFlagSetAfterWrite                # chunk.IsDirty after SetTile
TileGrid_DirtyFlagClearsAfterClearDirty        # IsDirty=false after ClearDirty
TileGrid_FlatIndexIsConsistent                 # FlatIndex(0,0) == 0, FlatIndex(1,0) == 1
TileGrid_FlatIndexForSeasonalProfiles          # FlatIndex(x,y) * SeasonalProfile size is in-bounds
TileChunk_SummaryFlagsUpdatedOnSet             # setting a volcanic tile updates HasVolcanicTile
```

**Done when:** Tests pass.

---

## Story 1.2.3 — Border Manifests

**Files to create:**
```
WorldEngine.Sim/World/BorderManifestSample.cs  # 64-sample edge encoding
WorldEngine.Sim/World/BorderManifest.cs        # 4-edge manifest per tile
WorldEngine.Sim/World/BorderManifestStore.cs   # write manifests.bin, stub load
```

**BorderManifestSample:**
```csharp
public struct BorderManifestSample  // ~5 bytes per sample × 64 × 4 = 1,280 bytes per tile
{
    public byte Elevation;
    public byte Moisture;
    public byte HasRiverCrossing;    // 1 if a river crosses here
    public byte HasRoadCrossing;     // 1 if a road crosses here (M2+)
    public byte FlowVolume;          // river flow volume if HasRiverCrossing
}
```

**BorderManifestStore:**
- `WriteToFile(string path, IEnumerable<(TileCoord, BorderManifest)>)` — write all manifests to binary file
- `LoadFromFile(string path)` — stub: throws `NotImplementedException("M4 feature")` with a comment
- File format: `[TileCount:int][TileCoord.X:int][TileCoord.Y:int][BorderManifest bytes]...`

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/BorderManifestTests.cs`):
```
BorderManifest_Has4Edges                       # North, South, East, West
BorderManifest_EachEdgeHas64Samples            # BorderManifest.North.Length == 64
BorderManifest_WriteReadRoundTrip              # write 10 manifests, read back, same values
BorderManifestStore_LoadThrowsNotImplemented   # confirms stub behavior
```

**Done when:** Tests pass.

---

## Story 1.2.4 — IWorldStateReadOnly + Registry Types

**Files to create:**
```
WorldEngine.Sim/World/IWorldStateReadOnly.cs   # from docs/interface_contracts.md exactly
WorldEngine.Sim/World/ResourceDeposit.cs       # sealed record
WorldEngine.Sim/World/ActiveDisaster.cs        # sealed record + DisasterType enum
WorldEngine.Sim/World/ActiveDrought.cs         # sealed record
WorldEngine.Sim/Core/Season.cs                 # enum
WorldEngine.Sim/Core/SimPhase.cs               # enum
WorldEngine.Sim/Core/EntityKind.cs             # enum (stub, M2)
WorldEngine.Sim/Core/SimSpeed.cs               # enum
WorldEngine.Sim/Core/EventTier.cs              # enum
WorldEngine.Sim/Core/VerbClass.cs              # enum
WorldEngine.Sim/Core/PopulationImpact.cs       # enum
WorldEngine.Sim/Core/OverlayType.cs            # enum
```

**IWorldStateReadOnly:** Copy exactly from `docs/interface_contracts.md`. Do not modify signatures.

**WRITE TESTS FIRST** (`WorldEngine.Tests/Unit/RegistryTypeTests.cs`):
```
ResourceDeposit_ValueEquality                  # two records with same fields are equal
ResourceDeposit_SupportsListStacking           # List<ResourceDeposit> can hold 2+ items same tile
ActiveDisaster_ImmutableRecord                 # with-expression creates new instance
ActiveDisaster_TicksRemainingNegativeOneValid  # -1 is valid (indefinite)
ActiveDrought_MatchesByLatitudeBandAndBiome    # verify equality fields
DisasterType_HasFourValues                     # Wildfire, Flood, VolcanicAsh, SeismicDamage
```

**Done when:** Tests pass. All enum files compile. IWorldStateReadOnly matches `docs/interface_contracts.md` exactly.

---

## Phase 2 Done Criteria

- `dotnet build` — 0 warnings
- `dotnet test` — all Phase 2 tests pass
- `TileData` is exactly 14 bytes (assertion fires on startup)
- `SeasonalProfile` is exactly 8 bytes
- `TileGrid` East-West wrapping verified by test
- `IWorldStateReadOnly` matches interface_contracts.md exactly
- Phase 3 (world gen) can start without Phase 2 blockers
