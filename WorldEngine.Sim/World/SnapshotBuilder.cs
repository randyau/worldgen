using WorldEngine.Sim.Core;
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
        ViewportRect viewport,
        OverlayType activeOverlay,
        SimSpeed currentSpeed,
        bool paused,
        long ticksPerSecond,
        IReadOnlyList<SimEvent> recentEvents)
    {
        var tiles = BuildVisibleTiles(world, viewport);
        var inspected = world.InspectedTile.HasValue
            ? BuildInspectorData(world, world.InspectedTile.Value)
            : null;

        return new WorldSnapshot(
            CurrentYear:                 world.CurrentYear,
            CurrentSeason:               world.CurrentSeason,
            CurrentSpeed:                currentSpeed,
            IsPaused:                    paused,
            TicksPerSecond:              ticksPerSecond,
            VisibleTiles:                tiles,
            ActiveOverlay:               activeOverlay,
            WorldTileWidth:              world.TileGrid.TileWidth,
            WorldTileHeight:             world.TileGrid.TileHeight,
            RecentEvents:                recentEvents,
            InspectedTile:               inspected,
            GlobalTemperatureAnomaly:    world.GlobalTemperatureAnomaly,
            GlobalPrecipitationMultiplier: world.GlobalPrecipitationMultiplier,
            StormCorridorNormalizedLat:  world.StormCorridorNormalizedLat
        );
    }

    private static IReadOnlyDictionary<TileCoord, TileDisplayData> BuildVisibleTiles(
        WorldState world, ViewportRect viewport)
    {
        var result = new Dictionary<TileCoord, TileDisplayData>(viewport.Width * viewport.Height);
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        for (int y = viewport.Y; y < viewport.Y + viewport.Height; y++)
        {
            int clampedY = Math.Clamp(y, 0, h - 1);
            for (int x = viewport.X; x < viewport.X + viewport.Width; x++)
            {
                int wrappedX = ((x % w) + w) % w;
                var coord = new TileCoord(wrappedX, clampedY);
                result[coord] = BuildTileDisplayData(world, coord);
            }
        }

        return result;
    }

    private static TileDisplayData BuildTileDisplayData(WorldState world, TileCoord coord)
    {
        var tile = world.TileGrid.GetTile(coord);
        int idx = world.TileGrid.FlatIndex(coord);

        byte effectiveTemp = ComputeEffectiveTemperature(world, coord, tile, idx);
        bool hasDisaster = world.ActiveTileDisasters.ContainsKey(coord);

        return new TileDisplayData(
            Biome:               (BiomeType)tile.BiomeType,
            Elevation:           tile.Elevation,
            EffectiveTemperature: effectiveTemp,
            CurrentMoisture:     tile.CurrentMoisture,
            MagicIntensity:      tile.MagicIntensity,
            Fertility:           tile.Fertility,
            StaticFlags:         tile.StaticFlags,
            DynFlags:            tile.DynFlags,
            HasActiveDisaster:   hasDisaster
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
