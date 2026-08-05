using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Immutable identity record for a character: name, epithet, ancestry, and birth/death metadata.
/// Civ/org affiliation lives on Tier1Character.Memberships (M12 12.2), not here — see
/// docs/phases/m12_organization_model.md.
/// </summary>
public sealed record IdentityData(
    string     Name,
    string     Epithet,
    string     AncestryId,
    EntityId?  MotherId,
    EntityId?  FatherId,
    int        BirthYear,
    int        BirthSeason,
    int        NameOrdinal  = 0,   // 0 = first bearer; 1 = II, 2 = III, etc.
    int        RulerOrdinal = 0,   // Nth ruler of their civ (0 = founder / not yet a ruler)
    string     Surname      = "");// family/house/clan name (M15.x namespace expansion); "" = none generated
