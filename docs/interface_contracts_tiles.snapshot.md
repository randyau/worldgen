<!-- AUTO-GENERATED — do not edit. Run: python3 scripts/gen-interface-contracts.py -->
<!-- Generated: 2026-07-19T18:46:38Z -->

# Interface Contracts Snapshot — tiles

## TileData
**File:** `WorldEngine.Sim/Tiles/TileData.cs:7`  
**Kind:** `struct`

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TileData  // exactly 14 bytes — asserted at startup
{
    public byte Elevation;         // 0-255, scaled
    public byte Fertility;         // 0-255, scaled
    public byte BaseTemperature;   // 0-255, scaled (genesis climate)
    public byte BaseMoisture;      // 0-255, scaled (genesis climate)
    public byte MagicIntensity;    // 0-255, scaled
    public byte BiomeType;         // cast to BiomeType enum
    public byte PlateId;           // 0-255 tectonic plate assignment
    public TileStaticFlags StaticFlags;   // ushort, 16 bits

    public byte CurrentMoisture;   // 0-255, updated each seasonal tick
    public TileDynFlags DynFlags;  // byte, 8 bits
    public byte RoadLevel;         // 0=none; populated in M2+
    public ushort CivControl;      // 0=unclaimed; populated in M2+

}
```

<!-- content-hash: 650c9b9183040160 -->
