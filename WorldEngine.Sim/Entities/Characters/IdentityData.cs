using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

public sealed record IdentityData(
    string     Name,
    string     Epithet,
    string     AncestryId,
    EntityId?  MotherId,
    EntityId?  FatherId,
    CivId      CivId,       // mutable via WithCivId — record copy-with
    int        BirthYear,
    int        BirthSeason);
