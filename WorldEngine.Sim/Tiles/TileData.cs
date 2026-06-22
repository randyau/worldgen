using System.Runtime.InteropServices;

namespace WorldEngine.Sim.Tiles;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TileData  // exactly 14 bytes — asserted at startup
{
    // Static — set at world gen, never mutated during sim
    public byte Elevation;         // 0-255, scaled
    public byte Fertility;         // 0-255, scaled
    public byte BaseTemperature;   // 0-255, scaled (genesis climate)
    public byte BaseMoisture;      // 0-255, scaled (genesis climate)
    public byte MagicIntensity;    // 0-255, scaled
    public byte BiomeType;         // cast to BiomeType enum
    public byte PlateId;           // 0-255 tectonic plate assignment
    public TileStaticFlags StaticFlags;   // ushort, 16 bits

    // Dynamic — mutated during sim
    public byte CurrentMoisture;   // 0-255, updated each seasonal tick
    public TileDynFlags DynFlags;  // byte, 8 bits
    public byte RoadLevel;         // 0=none; populated in M2+
    public ushort CivControl;      // 0=unclaimed; populated in M2+

    static TileData() => System.Diagnostics.Debug.Assert(
        Marshal.SizeOf<TileData>() == 14, "TileData size invariant broken");
}
