using System.Runtime.InteropServices;

namespace WorldEngine.Sim.Tiles;

/// <summary>8-byte per-tile seasonal climate deltas (temperature + moisture delta per season).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SeasonalProfile  // 8 bytes
{
    public sbyte TempDeltaSpring, TempDeltaSummer, TempDeltaAutumn, TempDeltaWinter;
    public sbyte MoistureDeltaSpring, MoistureDeltaSummer, MoistureDeltaAutumn, MoistureDeltaWinter;

    static SeasonalProfile() => System.Diagnostics.Debug.Assert(
        Marshal.SizeOf<SeasonalProfile>() == 8, "SeasonalProfile size invariant broken");
}
