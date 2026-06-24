using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.World;

/// <summary>
/// Constructs a WorldSnapshot from WorldState. Called by the sim thread at the end of each tick.
/// Only touches WorldState — never called from the UI thread.
/// </summary>
public sealed class SnapshotBuilder
{
    public WorldSnapshot Build(
        WorldState world,
        OverlayType activeOverlay,
        SimSpeed currentSpeed,
        bool paused,
        long ticksPerSecond,
        IReadOnlyList<SimEvent> recentEvents)
    {
        var entitySnapshots = BuildEntitySnapshots(world);
        var tiles           = BuildAllTiles(world, entitySnapshots);
        var inspected       = world.InspectedTile.HasValue
            ? BuildInspectorData(world, world.InspectedTile.Value)
            : null;
        var settlements     = BuildSettlementSnapshots(world);

        return new WorldSnapshot(
            CurrentYear:                 world.CurrentYear,
            CurrentSeason:               world.CurrentSeason,
            CurrentSpeed:                currentSpeed,
            IsPaused:                    paused,
            TicksPerSecond:              ticksPerSecond,
            AllTiles:                    tiles,
            ActiveOverlay:               activeOverlay,
            WorldTileWidth:              world.TileGrid.TileWidth,
            WorldTileHeight:             world.TileGrid.TileHeight,
            RecentEvents:                recentEvents,
            InspectedTile:               inspected,
            EntitySnapshots:             entitySnapshots,
            Settlements:                 settlements,
            Ruins:                       world.Ruins,
            GlobalTemperatureAnomaly:    world.GlobalTemperatureAnomaly,
            GlobalPrecipitationMultiplier: world.GlobalPrecipitationMultiplier,
            StormCorridorNormalizedLat:  world.StormCorridorNormalizedLat
        );
    }

    private static IReadOnlyDictionary<TileCoord, SettlementSnapshot> BuildSettlementSnapshots(
        WorldState world)
    {
        var dict = new Dictionary<TileCoord, SettlementSnapshot>(world.Settlements.Count);
        foreach (var (coord, stub) in world.Settlements)
        {
            string civName = world.Civilizations.TryGetValue(stub.CivId, out var civ)
                ? civ.Name : "Unknown";
            dict[coord] = new SettlementSnapshot(
                Coord:              coord,
                Name:               stub.Name,
                CivName:            civName,
                Population:         stub.Population,
                Health:             stub.Health,
                FoundedYear:        stub.FoundedYear,
                ResourceLedger:     stub.ResourceLedger,
                ConqueredYear:      stub.ConqueredYear,
                ConqueredFromCivId: stub.ConqueredFromCivId,
                FoodStores:         stub.FoodStores);
        }
        return dict;
    }

    private static IReadOnlyDictionary<EntityId, EntitySnapshot> BuildEntitySnapshots(WorldState world)
    {
        var dict = new Dictionary<EntityId, EntitySnapshot>(world.Entities.Count);
        foreach (var (id, entity) in world.Entities.All)
        {
            var snap = entity.ToSnapshot();
            if (entity is Entities.Characters.Tier1Character c && c.Identity.CivId.IsValid
                && world.Civilizations.TryGetValue(c.Identity.CivId, out var civ))
                snap = snap with { CivName = civ.Name };
            dict[id] = snap;
        }
        return dict;
    }

    private static TileDisplayData[] BuildAllTiles(
        WorldState world,
        IReadOnlyDictionary<EntityId, EntitySnapshot> entitySnapshots)
    {
        // Build a reverse lookup: coord → entity IDs, from the live snapshot we just built
        var byCoord = new Dictionary<TileCoord, List<EntityId>>();
        foreach (var snap in entitySnapshots.Values)
        {
            if (!byCoord.TryGetValue(snap.Location, out var list))
                byCoord[snap.Location] = list = new List<EntityId>();
            list.Add(snap.Id);
        }

        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var result = new TileDisplayData[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var ids = byCoord.TryGetValue(coord, out var list)
                    ? list.ToArray()
                    : Array.Empty<EntityId>();
                result[y * w + x] = BuildTileDisplayData(world, coord, ids);
            }
        return result;
    }

    private static TileDisplayData BuildTileDisplayData(
        WorldState world, TileCoord coord, EntityId[] entitiesPresent)
    {
        var tile = world.TileGrid.GetTile(coord);
        int idx = world.TileGrid.FlatIndex(coord);

        byte effectiveTemp = ComputeEffectiveTemperature(world, coord, tile, idx);
        bool hasDisaster = world.ActiveTileDisasters.ContainsKey(coord);
        bool hasRuin     = world.Ruins.ContainsKey(coord);

        return new TileDisplayData(
            Biome:               (BiomeType)tile.BiomeType,
            Elevation:           tile.Elevation,
            EffectiveTemperature: effectiveTemp,
            CurrentMoisture:     tile.CurrentMoisture,
            MagicIntensity:      tile.MagicIntensity,
            Fertility:           tile.Fertility,
            StaticFlags:         tile.StaticFlags,
            DynFlags:            tile.DynFlags,
            HasActiveDisaster:   hasDisaster,
            HasRuin:             hasRuin,
            EntitiesPresent:     entitiesPresent
        );
    }

    private static byte ComputeEffectiveTemperature(
        WorldState world, TileCoord coord, TileData tile, int idx)
    {
        // Pattern #9 from docs/snippets/patterns.md
        int h = world.TileGrid.TileHeight;
        float normalizedLat = coord.Y / (float)h;
        float latitudeScale = 1.0f + MathF.Abs(normalizedLat - 0.5f) * 1.4f;

        int seasonalDelta = world.SeasonalProfiles.Length > idx
            ? GetSeasonalTempDelta(world.SeasonalProfiles[idx], world.CurrentSeason)
            : 0;

        float anomalyContrib = world.GlobalTemperatureAnomaly * latitudeScale;
        int raw = tile.BaseTemperature + seasonalDelta + (int)anomalyContrib;
        return (byte)Math.Clamp(raw, 0, 255);
    }

    private static int GetSeasonalTempDelta(SeasonalProfile profile, Season season) =>
        season switch
        {
            Season.Spring => profile.TempDeltaSpring,
            Season.Summer => profile.TempDeltaSummer,
            Season.Autumn => profile.TempDeltaAutumn,
            Season.Winter => profile.TempDeltaWinter,
            _             => 0
        };

    private static TileInspectorData BuildInspectorData(WorldState world, TileCoord coord)
    {
        var tile = world.TileGrid.GetTile(coord);
        int idx = world.TileGrid.FlatIndex(coord);
        int h = world.TileGrid.TileHeight;

        var profile = world.SeasonalProfiles.Length > idx
            ? world.SeasonalProfiles[idx]
            : default;

        float normalizedLat = coord.Y / (float)h;
        float latitudeScale = 1.0f + MathF.Abs(normalizedLat - 0.5f) * 1.4f;

        float seasonTempDelta = GetSeasonalTempDelta(profile, world.CurrentSeason);
        float effectiveTemp   = tile.BaseTemperature + seasonTempDelta
                              + world.GlobalTemperatureAnomaly * latitudeScale;

        int seasonMoistDelta = GetSeasonalMoistDelta(profile, world.CurrentSeason);
        float baseMoist = tile.CurrentMoisture + seasonMoistDelta;
        float stormBonus = tile.StaticFlags.HasFlag(TileStaticFlags.IsStormCorridor)
            ? world.SimConfig.Climate.StormCorridorMoistureBonus
            : 1.0f;
        float effectiveMoist = baseMoist * world.GlobalPrecipitationMultiplier * stormBonus;

        var deposits = world.ResourceRegistry.TryGetValue(coord, out var deps)
            ? (IReadOnlyList<ResourceDeposit>)deps
            : Array.Empty<ResourceDeposit>();

        var disasters = world.ActiveTileDisasters.TryGetValue(coord, out var dis)
            ? (IReadOnlyList<ActiveDisaster>)dis
            : Array.Empty<ActiveDisaster>();

        var tileHeight = world.TileGrid.TileHeight;
        int latBand = coord.Y / Math.Max(1, tileHeight / 4);
        var biome = (BiomeType)tile.BiomeType;

        var drought = world.ActiveDroughts
            .FirstOrDefault(d => d.LatitudeBandIndex == latBand && d.AffectedBiome == biome);
        bool inDrought = drought is not null;

        return new TileInspectorData(
            Coord:                coord,
            RawTile:              tile,
            SeasonalProfile:      profile,
            EffectiveTemperature: effectiveTemp,
            CurrentMoistureF:     Math.Clamp(effectiveMoist, 0f, 255f),
            Deposits:             deposits,
            Disasters:            disasters,
            IsInActiveDrought:    inDrought,
            DroughtOriginEventId: inDrought ? drought!.OriginEventId : null
        );
    }

    private static int GetSeasonalMoistDelta(SeasonalProfile profile, Season season) =>
        season switch
        {
            Season.Spring => profile.MoistureDeltaSpring,
            Season.Summer => profile.MoistureDeltaSummer,
            Season.Autumn => profile.MoistureDeltaAutumn,
            Season.Winter => profile.MoistureDeltaWinter,
            _             => 0
        };
}
