using System.Collections.Generic;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;

namespace WorldEngine.Sim.World;

/// <summary>
/// Snapshot entry for one territory tile: which city owns it and which civ that city belongs to.
/// Keyed by tile coord in WorldSnapshot.TerritoryMap.
/// </summary>
public sealed record TerritorySnapshot(
    TileCoord CityTile,
    long      CivId);

/// <summary>
/// Snapshot of a tile improvement: type, owning city, year built, and builder.
/// Keyed by tile coord in WorldSnapshot.ImprovementMap.
/// </summary>
public sealed record ImprovementSnapshot(
    string    ImprovementType,
    TileCoord CityTile,
    int       BuiltYear,
    long      BuilderId);

/// <summary>
/// Immutable snapshot of a single artifact for UI display.
/// Owner description is pre-formatted at snapshot time so the UI needs no lookups.
/// OwnerCharacterId: character entity id when owned by a character; 0 otherwise.
/// OwnerSettlementTile: settlement tile when held at a settlement; TileCoord(-1,-1) otherwise.
/// </summary>
public sealed record ArtifactSnapshot(
    long      Id,
    string    Name,
    string    Category,
    string    Origin,
    float     Quality,
    int       CreatedYear,
    string    CreatorName,
    string    OwnerDesc,
    bool      IsDestroyed,
    long      OwnerCharacterId,
    TileCoord OwnerSettlementTile);

/// <summary>Immutable settlement info for UI display.</summary>
public sealed record SettlementSnapshot(
    TileCoord Coord,
    string    Name,
    string    CivName,
    int       Population,
    int       Health,
    int       FoundedYear,
    IReadOnlyDictionary<string, float>? ResourceLedger = null,
    int       ConqueredYear      = 0,
    int       ConqueredFromCivId = 0,
    IReadOnlyDictionary<string, float>? ResourceStores = null,
    // M14 14.5 — economic ledger UI: per-money-equivalent-commodity LocalScarcityMultiplier
    // (PricingService.LocalScarcityMultiplier), keyed by EconomyConfig.MoneyEquivalentCommodities.
    // Precious-commodity *reserves* themselves are already derivable from ResourceStores above —
    // no separate field needed for that half.
    IReadOnlyDictionary<string, float>? LocalScarcityMultipliers = null);

/// <summary>
/// M14 14.5 — economic ledger UI: a snapshot of one Organization's treasury/membership for display.
/// Not limited to Guilds despite the name's origin — Kind lets the ledger panel also list civ
/// treasuries (relevant to TreasuryInsolvent/economic-ruin), reusing one snapshot shape for both
/// rather than inventing a parallel CivTreasurySnapshot.
/// </summary>
public sealed record GuildSnapshot(
    long      Id,
    string    Name,
    string    Kind,
    float     Treasury,
    TileCoord? HomeSettlementCoord,
    int       MemberCount,
    int       RecentTradeEventCount);

/// <summary>
/// Immutable snapshot of a single active goal for the watch panel.
/// Carries just what the UI needs to display goals.
/// </summary>
public sealed record GoalWatchEntry(string Description, float Priority);

/// <summary>
/// Live snapshot of a watched Tier1Character for the Watch panel (M3 Phase 3.4).
/// Populated by SnapshotBuilder when WorldState.WatchedEntityId is set to a Tier1Character.
/// </summary>
public sealed record CharacterWatchSnapshot(
    EntityId   Id,
    string     Name,
    string     Epithet,
    string     CivName,
    TileCoord  Location,
    string     BiomeName,
    int        AgeSeasons,
    float      Wellbeing,
    NeedsVector   Needs,
    PersonalityVector Personality,
    IReadOnlyList<GoalWatchEntry> Goals);

/// <summary>
/// Live vitals snapshot for a watched entity that isn't a Tier1Character (Tier2Character,
/// LegendaryBeast, and any future watchable kind) — no needs/goals/personality data exists for
/// these, so this is deliberately a thinner card than CharacterWatchSnapshot, not a subset of it.
/// Populated by SnapshotBuilder when WatchedEntityId is set on WorldState to a non-Tier1 entity.
/// </summary>
public sealed record BasicWatchSnapshot(
    EntityId   Id,
    EntityKind Kind,
    string     Name,
    string     SpeciesId,   // beasts.toml id for beasts; empty for characters
    bool       IsLegendary,
    TileCoord  Location,
    string     BiomeName,
    int        AgeSeasons,
    float      HealthFraction,
    float      FoodFraction);

/// <summary>
/// Immutable projection of world state for the UI. Created after each tick.
/// UI thread reads this every frame — never touches WorldState directly.
/// </summary>
public sealed record WorldSnapshot(
    // Time
    int CurrentYear,
    Season CurrentSeason,
    SimSpeed CurrentSpeed,
    bool IsPaused,
    long TicksPerSecond,

    // Map — flat array indexed by (y * WorldTileWidth + x); X wraps, Y clamps
    TileDisplayData[] AllTiles,
    OverlayType ActiveOverlay,
    int WorldTileWidth,
    int WorldTileHeight,

    // Event log
    IReadOnlyList<SimEvent> RecentEvents,

    // Tile inspector (null if no tile selected)
    TileInspectorData? InspectedTile,

    // Entities — flat lookup by EntityId; used by inspector and map renderer
    IReadOnlyDictionary<EntityId, EntitySnapshot> EntitySnapshots,

    // Settlements — keyed by tile coord; used by inspector and map renderer
    IReadOnlyDictionary<TileCoord, SettlementSnapshot> Settlements,

    // Ruins — keyed by tile coord; displayed in inspector and map renderer
    IReadOnlyDictionary<TileCoord, RuinRecord> Ruins,

    // Territory and improvements (M3 Phase 3.0)
    // TerritoryMap: tile → (owning city tile, civ id). Absent = unclaimed.
    IReadOnlyDictionary<TileCoord, TerritorySnapshot> TerritoryMap,
    // ImprovementMap: tile → improvement snapshot. Absent = no improvement.
    IReadOnlyDictionary<TileCoord, ImprovementSnapshot> ImprovementMap,

    // World-level drift parameters for UI status display
    float GlobalTemperatureAnomaly,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat,

    // Watch panel (M3 Phase 3.4) — exactly one of these is non-null at a time (or neither, if
    // nothing is watched), depending on the watched entity's kind. Tier1Character gets the rich
    // needs/goals/personality card; everything else (Tier2Character, LegendaryBeast, ...) gets
    // the thinner vitals-only card.
    CharacterWatchSnapshot? WatchedCharacter = null,
    BasicWatchSnapshot?     WatchedBasic     = null,

    // Save state (M3 Phase 3.6) — used by UI to show "Saving..." overlay
    bool IsSaving     = false,
    long LastSaveTick = -1,

    // Artifact system (M5) — all artifacts known to the world at snapshot time
    // Includes destroyed artifacts so the UI can display historical context if needed.
    IReadOnlyList<ArtifactSnapshot>? Artifacts = null,

    // Spotlight (M7+) — set when the player is controlling a character
    EntityId?  SpotlightCharacterId = null,
    TileCoord? SpotlightMoveTarget  = null,

    // Economy (M14 14.5) — world-level per-capita price index (decision 8) and every Guild/Civ
    // Organization's treasury for the read-only economic ledger panel. Personal Wealth and
    // settlement-level precious-commodity data are already carried on EntitySnapshot/
    // SettlementSnapshot above — this is just the two things with no other home in the snapshot.
    float GlobalPriceIndex = 1f,
    IReadOnlyList<GuildSnapshot>? Guilds = null
);
