using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation;

/// <summary>
/// Validates God Mode authoring commands before they are resolved.
/// Returns (true, null) on success; (false, reason) when the command would corrupt invariants.
/// </summary>
internal static class AuthoringValidator
{
    internal static (bool Valid, string? Reason) ValidateCoord(TileCoord coord, WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        if (coord.X < 0 || coord.X >= w || coord.Y < 0 || coord.Y >= h)
            return (false, $"Coord {coord} is out of bounds ({w}x{h})");
        return (true, null);
    }

    internal static (bool Valid, string? Reason) ValidateLandTile(TileCoord coord, WorldState world)
    {
        var (valid, reason) = ValidateCoord(coord, world);
        if (!valid) return (false, reason);
        if (!world.IsLand(coord))
            return (false, $"Tile {coord} is not a land tile");
        return (true, null);
    }

    internal static (bool Valid, string? Reason) ValidateCharacterAlive(EntityId id, WorldState world)
    {
        var entity = world.GetEntity(id);
        if (entity is not Tier1Character ch)
            return (false, $"Entity {id.Value} is not a Tier1Character");
        if (!ch.IsAlive)
            return (false, $"Character {id.Value} ({ch.Identity.Name}) is not alive");
        return (true, null);
    }

    internal static (bool Valid, string? Reason) ValidateDisasterApplicable(
        TileCoord coord, DisasterType type, WorldState world)
    {
        var (valid, reason) = ValidateCoord(coord, world);
        if (!valid) return (false, reason);

        var tile = world.TileGrid.GetTile(coord);
        if (type == DisasterType.VolcanicAsh && !tile.StaticFlags.HasFlag(TileStaticFlags.IsVolcanic))
            return (false, $"Tile {coord} is not a volcanic tile — cannot trigger VolcanicAsh");

        return (true, null);
    }
}
