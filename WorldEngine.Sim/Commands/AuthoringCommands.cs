using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Artifacts;

namespace WorldEngine.Sim.Commands;

/// <summary>
/// Player-authored God Mode commands. Each represents a single intentional act
/// that bypasses normal simulation probability and is stamped IsGodMode = true
/// in the resulting SimEvent. All fields are value-type only (no callbacks/delegates).
/// </summary>

/// <summary>Drop a new artifact at a tile. Null Name triggers auto-generation.</summary>
public sealed record AuthorPlaceArtifact(
    TileCoord Coord,
    ArtifactCategory Category,
    string? Name = null) : ICommand;

/// <summary>Trigger a disaster of the specified type at a tile.</summary>
public sealed record AuthorTriggerDisaster(
    TileCoord Coord,
    DisasterType Type) : ICommand;

/// <summary>Spawn a new Tier 1 character at a land tile. Null AncestryId uses biome default.</summary>
public sealed record AuthorSpawnCharacter(
    TileCoord Coord,
    string? AncestryId = null) : ICommand;

/// <summary>Apply a one-shot nudge to an existing living character.</summary>
public sealed record AuthorNudgeCharacter(
    EntityId CharacterId,
    CharacterNudge Nudge) : ICommand;

/// <summary>Available nudges for AuthorNudgeCharacter.</summary>
public enum CharacterNudge
{
    RaiseMorale,   // boost Wellbeing need toward satisfied
    LowerMorale,   // push Wellbeing need toward urgent
    SetWander,     // clear goals; character enters aimless wander mode
    SetSettle,     // push FoundCity goal to high priority
}
