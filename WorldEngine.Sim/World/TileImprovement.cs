using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

/// <summary>ImprovementType enum (Farm/Mine/etc.) and TileImprovement record for territory-based improvements (M3.0).</summary>
public enum ImprovementType { Farm, Mine, LoggingCamp, Pasture, Fishery }

public sealed record TileImprovement(
    ImprovementType Type,
    TileCoord       CityTile,   // which city built/owns this
    int             BuiltYear,
    EntityId        BuilderId); // character who built it (for event attribution)
