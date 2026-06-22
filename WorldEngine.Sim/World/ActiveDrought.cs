using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>
/// A drought affecting all tiles in a (LatitudeBand, Biome) region.
/// Membership is computed at runtime: ActiveDroughts.Any(d => tile matches d).
/// No per-tile registry entry — the region can contain thousands of tiles.
/// </summary>
public sealed record ActiveDrought(
    int LatitudeBandIndex,
    BiomeType AffectedBiome,
    float Intensity,        // 0.0–1.0
    int SeasonsRemaining,
    EventId OriginEventId
);
