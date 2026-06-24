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
    int        BirthSeason,
    int        NameOrdinal  = 0,   // 0 = first bearer; 1 = II, 2 = III, etc.
    int        RulerOrdinal = 0);  // Nth ruler of their civ (0 = founder / not yet a ruler)
