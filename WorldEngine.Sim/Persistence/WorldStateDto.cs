using System.Text.Json.Serialization;

namespace WorldEngine.Sim.Persistence;

// ─── JSON source-gen context ──────────────────────────────────────────────────
[JsonSerializable(typeof(WorldStateDto))]
[JsonSerializable(typeof(MetaDto))]
[JsonSerializable(typeof(CivContactDto))]
[JsonSerializable(typeof(PendingEmissaryDto))]
internal partial class WorldStateSerializerContext : JsonSerializerContext { }

// ─── Meta ─────────────────────────────────────────────────────────────────────
/// <summary>Lightweight save metadata written to meta.json. Checked on load for version compat.</summary>
public sealed record MetaDto(
    string FormatVersion,
    int    Seed,
    int    WidthKm,
    int    HeightKm,
    int    TileWidthKm,
    int    SavedYear,
    long   SavedTick);

// ─── Root state ───────────────────────────────────────────────────────────────
/// <summary>Full serializable mirror of WorldState. Saved to state.bin as UTF-8 JSON.</summary>
public sealed record WorldStateDto(
    // World config (needed to regenerate TileGrid on load)
    int  Seed,
    int  WidthKm,
    int  HeightKm,
    int  TileWidthKm,

    // Time
    int  CurrentYear,
    int  CurrentSeason,     // (int)Season
    long CurrentTick,

    // Environmental drift
    float GlobalTemperatureAnomaly,
    float SecularTemperatureAnomaly,
    float CurrentSeaLevel,
    float GlobalPrecipitationMultiplier,
    float StormCorridorNormalizedLat,
    float StormCorridorHalfWidth,
    float MonsoonIntensityMultiplier,
    float VolcanicActivityMultiplier,

    // Civ id counter
    int NextCivId,

    // ── Collections ──────────────────────────────────────────────────────────
    // Dictionary keys that are TileCoord use "x,y" format
    // Dictionary keys that are CivId use the int value as string
    Dictionary<string, List<ResourceDepositDto>>    ResourceRegistry,
    Dictionary<string, List<ActiveDisasterDto>>     ActiveTileDisasters,
    List<ActiveDroughtDto>                          ActiveDroughts,
    List<CivilizationDto>                           Civilizations,
    Dictionary<string, SettlementStubDto>           Settlements,
    Dictionary<string, RuinRecordDto>               Ruins,
    Dictionary<string, string>                      TerritoryMap,
    Dictionary<string, TileImprovementDto>          ImprovementMap,
    List<EntityDto>                                 Entities,
    List<RelationshipEdgeDto>                       Relationships,
    Dictionary<string, int>                         NameOrdinals,
    List<long>                                      ActiveFounders,
    List<BeastEmergenceEntryDto>                    BeastEmergenceSchedule,
    // M12 Organization layer (see docs/phases/m12_organization_model.md)
    List<OrganizationDto>                           Organizations,
    int                                              NextOrganizationId,
    // DECISION: renamed from WatchedCharacterId now that Watch is polymorphic (Character/Beast/...).
    // This is a local dev save format with no shipped compatibility contract, so the field is
    // renamed/retyped directly rather than adding a migration shim for old saves.
    long?                                           WatchedEntityId,
    int?                                            WatchedEntityKind,
    List<PendingEmissaryDto>                        PendingEmissaries
);

// ─── Environment ──────────────────────────────────────────────────────────────
public sealed record ResourceDepositDto(string DepositType, byte Quality, byte Depth);

public sealed record ActiveDisasterDto(
    int   DisasterType,   // (int)DisasterType
    float Intensity,
    int   TicksRemaining,
    long  OriginEventId);

public sealed record ActiveDroughtDto(
    int   LatitudeBandIndex,
    int   AffectedBiome,   // (int)BiomeType
    float Intensity,
    int   SeasonsRemaining,
    long  OriginEventId);

// ─── Civilization ─────────────────────────────────────────────────────────────
public sealed record CivilizationDto(
    int    Id,
    string Name,
    long   FounderId,
    long   RulerId,
    int?   OrgId,            // M12: links to Organizations[i].Id, see docs/phases/m12_organization_model.md
    string CapitalTile,     // "x,y"
    int    FoundedYear,
    bool   IsCollapsed,
    int    CollapseYear,
    int    LastSettlementFoundedYear,
    int    SettlementCount,
    int    ColonyCount,
    int    TotalPopulation,
    int    SuccessionCrisisEndYear,
    int    RulerCount,
    int    TotalWarsInitiated,
    int    TotalSuccessions,
    int    TotalSettlementsFounded,
    int    NearCollapseCount,
    int    TotalScholarDiscoveries,
    List<long>                       Members,
    Dictionary<string, float>        BorderTension,
    Dictionary<string, int>          WarsAgainst,
    Dictionary<string, int>          PeaceTreaties,
    Dictionary<string, int>          WarHistory,
    List<string>                     CulturalTraits,
    Dictionary<string, List<string>> CityTerritories,
    CulturalProfileDto?              CulturalProfile,
    // M4 Phase 1 — civ awareness
    Dictionary<string, CivContactDto> KnownCivs,
    Dictionary<string, int>           ActiveEmissaryCountByTarget,
    // M4 Phase 2 — war campaigns
    Dictionary<string, int>           WarBattleWins);

// ─── Organization (M12) ────────────────────────────────────────────────────────
public sealed record MembershipDto(long CharacterId, int Role, float Loyalty);

public sealed record OrganizationDto(
    int    Id,
    int    Kind,        // (int)OrganizationKind
    string Name,
    long   LeaderId,
    int    FoundedYear,
    int    SuccessionCrisisEndYear,
    List<MembershipDto>     Members,
    Dictionary<string, int> WarsAgainst,      // CivId (or future org id) → year declared
    Dictionary<string, float> BorderTension,
    Dictionary<string, int> PeaceTreaties,
    List<int>                Allies);

public sealed record CulturalProfileDto(
    string   AncestryId,
    string   ArchitecturalStyle,
    string   SettlementDescriptor,
    string[] ArtisticTraditions,
    string[] ActiveTraits,
    string   DominantBiome);

// ─── Settlement & Ruins ───────────────────────────────────────────────────────
public sealed record SettlementStubDto(
    long   FounderId,
    int    CivId,
    string Tile,
    int    FoundedYear,
    int    Population,
    int    Health,
    string Name,
    float  PopulationF,
    int    LastCrystalThresh,
    float  FoodPressureRatio,
    float  WaterPressureRatio,
    int    LastStrainEventTick,
    Dictionary<string, float>? ResourceLedger,
    float  FertilityMultiplier,
    int    ConqueredYear,
    int    ConqueredFromCivId,
    Dictionary<string, float>? ResourceStores,
    int    CarryingCapacity,
    bool   IsColony,
    bool   IsInfected,
    int    InfectedSinceYear,
    float  SmoothedCapacity = 0f,   // EMA of food-derived capacity; 0 triggers re-init from raw on first tick
    float  Unrest = 0f,             // S2: settlement unrest scalar (0=content, 1=rebellious)
    string? Specialization = null,  // M9 9.2: non-vital resource key this settlement specializes in
    float  SpecializationStrength = 0f); // M9 9.2: EMA confidence [0,1] in Specialization

public sealed record RuinRecordDto(
    string Tile,
    string SettlementName,
    int    OriginalCivId,
    int    DestroyedYear,
    string Cause,
    int    TimesSettled);

// ─── Tile improvement ─────────────────────────────────────────────────────────
public sealed record TileImprovementDto(
    int    ImprovementType,
    string CityTile,
    int    BuiltYear,
    long   BuilderId);

// ─── Entities ─────────────────────────────────────────────────────────────────
/// <summary>Discriminated union for all entity kinds.</summary>
public sealed record EntityDto(
    string           Kind,   // "tier1" | "tier2" | "beast"
    Tier1EntityDto?  Tier1,
    Tier2EntityDto?  Tier2,
    BeastEntityDto?  Beast);

public sealed record Tier1EntityDto(
    long   Id,
    int    LocationX,
    int    LocationY,
    bool   IsAlive,
    int    Health,
    int    MaxHealth,
    int    AgeSeason,
    int    MaxAgeSeason,
    // Fixed-layout float arrays (layout documented in WorldStateMapper)
    float[] Personality,
    float[] Aptitude,
    float[] Skills,
    float[] Needs,
    IdentityDataDto   Identity,
    List<GoalDataDto> Goals,
    bool   IsInfected,
    int    InfectedSinceYear,
    float  Wellbeing,
    int    TicksInCurrentTile,
    int    LastCreateCompletedTick,
    int    LastArtworkYear,
    int    LastReligionFoundedYear,
    // Local-scale position foundation (M11 11.6) — null until a future milestone populates it.
    string? LocalChunkKey = null,
    string? LocalPositionKey = null);

public sealed record Tier2EntityDto(
    long   Id,
    int    LocationX,
    int    LocationY,
    bool   IsAlive,
    int    Health,
    int    MaxHealth,
    int    AgeSeason,
    int    MaxAgeSeason,
    string Name,
    float[] Personality6,
    float[] Needs4,
    LivelihoodDataDto Livelihood,
    int    LastNotableWorkTick,
    bool   HasMasterwork);

public sealed record BeastEntityDto(
    long   Id,
    int    LocationX,
    int    LocationY,
    int    HomeTileX,
    int    HomeTileY,
    bool   IsAlive,
    int    Health,
    int    MaxHealth,
    int    AgeSeason,
    int    MaxAgeSeason,
    string SpeciesId,
    string Name,
    bool   IsLegendary,
    int    Strength,
    int    Speed,
    float  Aggression,
    int    TerritoryRadius,
    string[] Abilities,
    float  FoodNeed,
    float  SafetyNeed,
    float  FoodDepletion,
    float  FoodFromHunt,
    float  FoodFromGraze,
    float  ReproductionChance,
    int    ReproductionMinAge,
    float  ReproductionFoodThreshold,
    bool   Hibernates,
    bool   PrefersCompany);

// ─── Character sub-types ──────────────────────────────────────────────────────
public sealed record IdentityDataDto(
    string Name,
    string Epithet,
    string AncestryId,
    long?  MotherId,
    long?  FatherId,
    int    CivId,
    int    BirthYear,
    int    BirthSeason,
    int    NameOrdinal,
    int    RulerOrdinal);

public sealed record GoalDataDto(
    int     Type,
    int     Object,
    long?   TargetEntityId,
    string? TargetTile,
    float   Priority,
    float   Progress,
    bool    IsComplete,
    int     StaleSince,
    float   Intensity,
    int     FormedTick,
    string? ResourceTag);

public sealed record LivelihoodDataDto(
    int    Role,
    long?  EmployerId,
    string SettlementTile,
    float  IncomeLevel);

// ─── Relationships ────────────────────────────────────────────────────────────
public sealed record RelationshipEdgeDto(
    long  From,
    long  To,
    float Trust,
    float Fear,
    float Debt,
    int   Flags);

// ─── Beast emergence ──────────────────────────────────────────────────────────
public sealed record BeastEmergenceEntryDto(int EmergenceYear, string SpeciesId);

// ─── M4 Phase 1 — Emissary system ────────────────────────────────────────────

public sealed record CivContactDto(
    int    KnownCivId,
    int    YearFirstContact,
    int    YearLastContact,
    int    BestSource,       // (int)CivContactSource
    string CapitalTile,      // "x,y"
    float  Confidence);

public sealed record PendingEmissaryDto(
    int   FromCiv,
    int   ToCiv,
    int   Purpose,           // (int)EmissaryPurpose
    int   DepartedYear,
    int   ArrivalYear,
    float SurvivalChance);
