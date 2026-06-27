namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Immutable cultural snapshot for a civilization, derived from its founding ancestry
/// and any acquired cultural traits (Phase 3.2). Computed once at civ founding and
/// updated when new traits are acquired via CivTracker.BuildCulturalProfile.
/// </summary>
public sealed record CulturalProfile(
    string   AncestryId,
    string   ArchitecturalStyle,
    string   SettlementDescriptor,
    string[] ArtisticTraditions,
    string[] ActiveTraits,
    string   DominantBiome);
