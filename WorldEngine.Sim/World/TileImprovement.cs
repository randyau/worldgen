using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.World;

public enum ImprovementType { Farm, Mine, LoggingCamp, Pasture, Fishery }

public sealed record TileImprovement(
    ImprovementType Type,
    TileCoord       CityTile,   // which city built/owns this
    int             BuiltYear,
    EntityId        BuilderId); // character who built it (for event attribution)
