using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Artifacts;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// The complete mutable world state. Owned by the sim thread — never accessed from the UI thread.
/// The UI reads WorldSnapshot via StateCache.
/// </summary>
public sealed class WorldState : IWorldStateReadOnly
{
    // === IDENTITY ===
    public WorldConfig Config { get; }
    public SimConfig SimConfig { get; }
    public int WorldSeed => Config.Seed;

    // === TILE DATA ===
    public TileGrid TileGrid { get; }

    /// <summary>
    /// Seasonal climate deltas parallel to TileGrid.
    /// Populated during world gen assembly (1.3.8). Never mutated after world gen.
    /// </summary>
    public SeasonalProfile[] SeasonalProfiles { get; }

    // === REGISTRIES ===
    public Dictionary<TileCoord, List<ResourceDeposit>> ResourceRegistry { get; }

    // IWorldStateReadOnly view (covariant wrapper built lazily — same data, no copy)
    IReadOnlyDictionary<TileCoord, IReadOnlyList<ResourceDeposit>> IWorldStateReadOnly.ResourceDeposits
        => _resourceDepositsView ??= new ResourceDepositsView(ResourceRegistry);
    private ResourceDepositsView? _resourceDepositsView;
    public Dictionary<TileCoord, List<ActiveDisaster>> ActiveTileDisasters { get; } = new();
    public List<ActiveDrought> ActiveDroughts { get; } = new();
    public EntityRegistry Entities { get; } = new();

    /// <summary>Deferred mythological beast emergence schedule. Processed annually.</summary>
    public List<(int EmergenceYear, string SpeciesId)> BeastEmergenceSchedule { get; } = new();

    /// <summary>
    /// Emissaries currently in transit. Resolved annually in CivTracker.RunEmissaryResolution
    /// when their ArrivalYear equals CurrentYear. Canonical list — ActiveEmissaryCountByTarget
    /// on each Civilization is the per-civ index for cap checks.
    /// </summary>
    public List<PendingEmissary> PendingEmissaries { get; } = new();

    // === CIVILIZATION / CHARACTER STATE ===
    public Dictionary<CivId, Civilization>        Civilizations   { get; } = new();
    public Dictionary<TileCoord, SettlementStub>  Settlements     { get; } = new();
    public Dictionary<TileCoord, RuinRecord>      Ruins           { get; } = new();
    /// <summary>Tile → owning city tile. Unclaimed tiles are absent. City tiles own themselves.</summary>
    public Dictionary<TileCoord, TileCoord>       TerritoryMap    { get; } = new();
    /// <summary>Tile → improvement. One improvement per tile maximum.</summary>
    public Dictionary<TileCoord, TileImprovement> ImprovementMap  { get; } = new();
    /// <summary>All legendary artifacts ever created in this world, keyed by artifact id.</summary>
    public Dictionary<ArtifactId, Artifact>        Artifacts       { get; } = new();
    public RelationshipGraph                       Relationships   { get; } = new();
    public int NextCivId { get; set; } = 1;

    /// <summary>
    /// Tracks how many characters have ever had each given name.
    /// Used to assign ordinal suffixes: first "Caelen" = ordinal 0 (no suffix), second = 1 (II), etc.
    /// </summary>
    public Dictionary<string, int> NameOrdinals { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a character's name and returns the ordinal to assign (0 = first bearer, 1 = second, etc.).
    /// Call once at spawn time; ordinal 0 means "no suffix needed" for display purposes.
    /// </summary>
    public int ClaimNameOrdinal(string name)
    {
        NameOrdinals.TryGetValue(name, out int count);
        NameOrdinals[name] = count + 1;
        return count;
    }

    /// <summary>
    /// EntityIds of characters who founded a currently-live settlement.
    /// Updated by CivTracker on establish/abandon/destroy — allows O(1) isFounder checks
    /// without scanning all Settlements per character per tick.
    /// </summary>
    private readonly HashSet<EntityId> _activeFounders = new();
    public IReadOnlySet<EntityId> ActiveFounders => _activeFounders;
    public void AddActiveFounder(EntityId id)    => _activeFounders.Add(id);
    public void RemoveActiveFounder(EntityId id) => _activeFounders.Remove(id);

    // === TIME ===
    public int CurrentYear { get; internal set; } = 1;
    public Season CurrentSeason { get; internal set; } = Season.Spring;
    public long CurrentTick { get; internal set; }

    // === DRIFT PARAMETERS (genesis = zero/defaults) ===
    /// <summary>
    /// Monotonic secular warming/cooling trend, accumulated from AnnualTempDriftRate and clamped
    /// to [-MaxCoolingAnomaly, MaxWarmingAnomaly]. Tracked separately from GlobalTemperatureAnomaly
    /// because the secular trend saturates at the clamp within decades — the multi-century cyclical
    /// term layered on top in RunAnnualDrift keeps climate moving for the rest of a long run.
    /// </summary>
    public float SecularTemperatureAnomaly { get; internal set; }
    public float GlobalTemperatureAnomaly { get; internal set; }
    public float CurrentSeaLevel { get; internal set; }
    public float GlobalPrecipitationMultiplier { get; internal set; } = 1.0f;

    /// <summary>Normalized lat of storm corridor center (0=south, 1=north). May drift.</summary>
    public float StormCorridorNormalizedLat { get; internal set; }

    /// <summary>Width of the storm corridor band (normalized). May drift.</summary>
    public float StormCorridorHalfWidth { get; internal set; }

    /// <summary>Monsoon season moisture multiplier. Set from config in the constructor below.</summary>
    public float MonsoonIntensityMultiplier { get; internal set; }

    /// <summary>Global scaling of volcanic event probability. Starts at 1.0.</summary>
    public float VolcanicActivityMultiplier { get; internal set; } = 1.0f;

    // === UI INSPECTOR ===
    /// <summary>Set by SetInspectedTile command. Null means no tile is selected.</summary>
    public TileCoord? InspectedTile { get; internal set; }

    /// <summary>Entity being watched in the Watch panel. Null means no watch active. Single slot —
    /// watching a new entity replaces whatever was watched before, whatever its kind.</summary>
    public EntityId? WatchedEntityId { get; internal set; }

    /// <summary>Kind of the watched entity (Tier1Character, Tier2Character, LegendaryBeast, ...),
    /// resolved from the registry when the watch target is set. Meaningless when WatchedEntityId is null.</summary>
    public EntityKind WatchedEntityKind { get; internal set; }

    // === SPOTLIGHT (M7+) ===
    /// <summary>Character currently under spotlight player control. Null means no spotlight active.</summary>
    public EntityId?        SpotlightCharacterId { get; internal set; }

    /// <summary>Current player intent for the spotlit character. Null when spotlight is inactive.</summary>
    public SpotlightIntent? SpotlightIntent      { get; internal set; }

    // IWorldStateReadOnly spotlight projection — entity logic reads these; never reads SpotlightIntent directly
    TileCoord? IWorldStateReadOnly.SpotlightMoveTarget => SpotlightIntent?.MoveTarget;
    GoalType?  IWorldStateReadOnly.SpotlightGoalIntent => SpotlightIntent?.GoalIntent;

    // === SAVE STATE ===
    /// <summary>True while an auto-save or manual save is running on the background Task.</summary>
    public volatile bool IsSaving;

    /// <summary>Tick at which the last successful save completed. -1 if never saved.</summary>
    public long LastSaveTick { get; internal set; } = -1;

    public WorldState(
        WorldConfig config,
        SimConfig simConfig,
        TileGrid tileGrid,
        SeasonalProfile[] seasonalProfiles,
        Dictionary<TileCoord, List<ResourceDeposit>> resourceRegistry,
        float stormCorridorNormalizedLat)
    {
        Config                    = config;
        SimConfig                 = simConfig;
        TileGrid                  = tileGrid;
        SeasonalProfiles          = seasonalProfiles;
        ResourceRegistry          = resourceRegistry;
        StormCorridorNormalizedLat = stormCorridorNormalizedLat;
        StormCorridorHalfWidth    = simConfig.Climate.StormCorridorHalfWidth;
        MonsoonIntensityMultiplier = simConfig.Climate.MonsoonIntensityMultiplier;
    }

    // === IWorldStateReadOnly ===

    /// <summary>Returns tile data with East-West cylinder wrapping applied.</summary>
    public TileData GetTile(TileCoord coord)
    {
        int w = TileGrid.TileWidth;
        int wrappedX = ((coord.X % w) + w) % w;
        int clampedY = Math.Clamp(coord.Y, 0, TileGrid.TileHeight - 1);
        return TileGrid.GetTile(new TileCoord(wrappedX, clampedY));
    }

    public bool IsLand(TileCoord coord) =>
        (BiomeType)GetTile(coord).BiomeType is not BiomeType.Ocean and not BiomeType.CoastalWater;

    // DECISION: shallow-ocean is computed on demand from adjacency rather than a stored
    // TileStaticFlags bit baked in during worldgen. Land/ocean assignment never changes after
    // worldgen (see WorldState.IsLand's own basis), so there's no staleness risk, and this keeps
    // M11 sea-voyage logic from needing a worldgen-layer change just to classify tiles.
    //
    // DECISION (found while building the 11.1 route search): strict 1-tile adjacency only
    // classifies the immediate coastal fringe as shallow, which makes any strait wider than ~2
    // tiles unbridgeable regardless of MaxVoyageTiles (the BFS can only step onto shallow tiles,
    // and a strait's middle tiles aren't adjacent to either shore). Widened to a small radius so
    // real narrow-strait crossings (a handful of tiles) are actually reachable, while a radius
    // this small still excludes open ocean far from any coast — the whole point of the
    // classification. Not config-exposed: this is a geometric definition of "shallow," not a
    // gameplay tuning knob (same treatment as the fixed 1-tile IsCoastal adjacency elsewhere).
    private const int ShallowOceanRadius = 2;

    public bool IsShallowOcean(TileCoord coord)
    {
        if (IsLand(coord)) return false;
        int w = TileGrid.TileWidth, h = TileGrid.TileHeight;
        for (int dy = -ShallowOceanRadius; dy <= ShallowOceanRadius; dy++)
        {
            int ny = Math.Clamp(coord.Y + dy, 0, h - 1);
            for (int dx = -ShallowOceanRadius; dx <= ShallowOceanRadius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = ((coord.X + dx) % w + w) % w;
                if (IsLand(new TileCoord(nx, ny))) return true;
            }
        }
        return false;
    }

    // Lazy, computed once and cached — land/ocean assignment is fixed after worldgen (see IsLand),
    // so a landmass ID never goes stale. Used by M11 sea-voyage route search to tell "still my
    // landmass" apart from "a genuine far shore reached across water."
    private int[]? _landmassIds;

    public int GetLandmassId(TileCoord coord)
    {
        EnsureLandmassIds();
        int w = TileGrid.TileWidth;
        int x = ((coord.X % w) + w) % w;
        int y = Math.Clamp(coord.Y, 0, TileGrid.TileHeight - 1);
        return _landmassIds![y * w + x];
    }

    private void EnsureLandmassIds()
    {
        if (_landmassIds != null) return;
        int w = TileGrid.TileWidth, h = TileGrid.TileHeight;
        var ids = new int[w * h];
        Array.Fill(ids, -1);
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };
        int nextId = 0;
        var queue = new Queue<TileCoord>();

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int startIdx = y * w + x;
            if (ids[startIdx] != -1) continue;
            var start = new TileCoord(x, y);
            if (!IsLand(start)) continue;

            ids[startIdx] = nextId;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nx = ((cur.X + dx[i]) % w + w) % w;
                    int ny = Math.Clamp(cur.Y + dy[i], 0, h - 1);
                    int nIdx = ny * w + nx;
                    if (ids[nIdx] != -1) continue;
                    if (!IsLand(new TileCoord(nx, ny))) continue;
                    ids[nIdx] = nextId;
                    queue.Enqueue(new TileCoord(nx, ny));
                }
            }
            nextId++;
        }
        _landmassIds = ids;
    }

    public IEnumerable<TileCoord> GetTilesInRadius(TileCoord center, int radius)
    {
        int w = TileGrid.TileWidth, h = TileGrid.TileHeight;
        for (int dy = -radius; dy <= radius; dy++)
        {
            int y = Math.Clamp(center.Y + dy, 0, h - 1);
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;
                int x = ((center.X + dx) % w + w) % w;
                yield return new TileCoord(x, y);
            }
        }
    }

    // Permanent geometry cache: tile coords + IsLand filter for a given (center, radius).
    // World grid and biome assignments never change after worldgen, so these results are immutable.
    private readonly Dictionary<(int X, int Y, int R), TileCoord[]> _landTileCache = new();

    public TileCoord[] GetCachedLandTilesInRadius(TileCoord center, int radius)
    {
        var key = (center.X, center.Y, radius);
        if (!_landTileCache.TryGetValue(key, out var tiles))
        {
            tiles = [.. GetTilesInRadius(center, radius).Where(IsLand)];
            _landTileCache[key] = tiles;
        }
        return tiles;
    }

    public float GetRandomFloat(EntityId entityId, int salt = 0) =>
        WorldRng.FloatAt(WorldSeed, CurrentTick, (int)(entityId.Value & 0xFFFF), (int)(entityId.Value >> 32), salt);

    public int GetRandomInt(EntityId entityId, int min, int max, int salt = 0) =>
        min + (int)(GetRandomFloat(entityId, salt) * (max - min));

    // === IWorldStateReadOnly — entity access ===

    public IEntity? GetEntity(EntityId id) => Entities.Get(id);

    public IEnumerable<IEntity> GetEntitiesAt(TileCoord coord) => Entities.GetAt(coord);

    public IEnumerable<IEntity> GetEntitiesInRadius(TileCoord center, int radius) =>
        Entities.GetInRadius(center, radius);

    // === IWorldStateReadOnly — civilization / character ===

    IReadOnlyDictionary<TileCoord, SettlementStub>  IWorldStateReadOnly.Settlements   => Settlements;
    IReadOnlyDictionary<TileCoord, RuinRecord>      IWorldStateReadOnly.Ruins         => Ruins;
    IReadOnlyDictionary<TileCoord, TileCoord>       IWorldStateReadOnly.TerritoryMap  => TerritoryMap;
    IReadOnlyDictionary<TileCoord, TileImprovement> IWorldStateReadOnly.ImprovementMap => ImprovementMap;
    IReadOnlyDictionary<ArtifactId, Artifact>       IWorldStateReadOnly.Artifacts     => Artifacts;

    public RelationshipEdge? GetRelationship(EntityId a, EntityId b) =>
        Relationships.Get(a, b);

    public int CountAlliances(EntityId id) => Relationships.CountAlliances(id);
    public int CountRivals(EntityId id)    => Relationships.CountRivals(id);

    public Civilization? GetCivilization(CivId civId) =>
        Civilizations.TryGetValue(civId, out var civ) ? civ : null;
}

/// <summary>
/// Lightweight read-only adapter that presents Dictionary{TileCoord, List{ResourceDeposit}}
/// as IReadOnlyDictionary{TileCoord, IReadOnlyList{ResourceDeposit}} without copying data.
/// </summary>
internal sealed class ResourceDepositsView(Dictionary<TileCoord, List<ResourceDeposit>> inner)
    : IReadOnlyDictionary<TileCoord, IReadOnlyList<ResourceDeposit>>
{
    public IReadOnlyList<ResourceDeposit> this[TileCoord key] => inner[key];
    public IEnumerable<TileCoord> Keys   => inner.Keys;
    public IEnumerable<IReadOnlyList<ResourceDeposit>> Values => inner.Values;
    public int Count => inner.Count;
    public bool ContainsKey(TileCoord key) => inner.ContainsKey(key);
    public bool TryGetValue(TileCoord key, out IReadOnlyList<ResourceDeposit> value)
    {
        if (inner.TryGetValue(key, out var list)) { value = list; return true; }
        value = Array.Empty<ResourceDeposit>(); return false;
    }
    public IEnumerator<KeyValuePair<TileCoord, IReadOnlyList<ResourceDeposit>>> GetEnumerator()
        => inner.Select(kv => new KeyValuePair<TileCoord, IReadOnlyList<ResourceDeposit>>(kv.Key, kv.Value))
                .GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
