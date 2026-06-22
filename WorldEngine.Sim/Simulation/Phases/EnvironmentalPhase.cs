using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;
using System.Linq;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 1 — Environmental: seasonal climate, annual drift, disaster system, resource dynamics,
/// sea level changes. Direct mutator — never called from UI thread.
/// </summary>
public sealed class EnvironmentalPhase
{
    private readonly SimConfig _cfg;

    public EnvironmentalPhase(SimConfig cfg) => _cfg = cfg;

    /// <summary>
    /// Entry point called by PhaseRunner each tick.
    /// isAnnualTick: true when the season just flipped to Spring (once per year).
    /// </summary>
    public List<PendingEvent> RunTick(WorldState world, List<PendingEvent> pending, bool isAnnualTick = false)
    {
        RunSeasonalClimate(world);
        VolcanicMultiplierDecay(world);
        RunDisasterTick(world, pending);

        if (isAnnualTick)
        {
            RunAnnualDrift(world, pending);
            RunAnnualSeaLevel(world, pending);
            RunAnnualResourceDynamics(world);
            RunDroughtsAnnual(world, pending);
        }

        return pending;
    }

    // =========================================================================
    // 1.5.1 — Seasonal Climate
    // =========================================================================

    private void RunSeasonalClimate(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var cfg = _cfg.Climate;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);

                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater)
                    continue;

                int idx = x + y * w;
                var profile = world.SeasonalProfiles[idx];

                int moistureDelta = GetSeasonalMoistDelta(profile, world.CurrentSeason);
                float moisture = (tile.BaseMoisture + moistureDelta) * world.GlobalPrecipitationMultiplier;

                // Storm corridor bonus: autumn only
                if (tile.StaticFlags.HasFlag(TileStaticFlags.IsStormCorridor)
                    && world.CurrentSeason == Season.Autumn)
                    moisture *= cfg.StormCorridorMoistureBonus;

                // Monsoon bonus: Summer only for tropical/monsoon tiles
                if (world.CurrentSeason == Season.Summer && IsMonsoonTile(tile, x, y, world, cfg))
                    moisture *= world.MonsoonIntensityMultiplier;

                tile.CurrentMoisture = (byte)Math.Clamp((int)moisture, 0, 255);
                world.TileGrid.SetTile(coord, tile);
            }
        }
    }

    private static bool IsMonsoonTile(TileData tile, int x, int y, WorldState world, ClimateConfig cfg)
    {
        // Tropical rainforest is always monsoon
        if ((BiomeType)tile.BiomeType == BiomeType.TropicalRainforest) return true;

        // High moisture + tropical band
        float normalizedLat = y / (float)world.TileGrid.TileHeight;
        bool inTropicalBand = MathF.Abs(normalizedLat - 0.5f) < cfg.TropicalBandHalfWidth;
        return tile.BaseMoisture > cfg.MonsoonMoistureThreshold && inTropicalBand;
    }

    private static int GetSeasonalMoistDelta(SeasonalProfile p, Season s) => s switch
    {
        Season.Spring => p.MoistureDeltaSpring,
        Season.Summer => p.MoistureDeltaSummer,
        Season.Autumn => p.MoistureDeltaAutumn,
        Season.Winter => p.MoistureDeltaWinter,
        _             => 0
    };

    // =========================================================================
    // 1.5.2 — Climate Drift (annual)
    // =========================================================================

    private void RunAnnualDrift(WorldState world, List<PendingEvent> pending)
    {
        var cfg = _cfg.Climate;

        // Temperature anomaly drift (only when drift rate > 0)
        if (cfg.AnnualTempDriftRate != 0f)
        {
            world.GlobalTemperatureAnomaly = Math.Clamp(
                world.GlobalTemperatureAnomaly + cfg.AnnualTempDriftRate,
                -cfg.MaxCoolingAnomaly,
                cfg.MaxWarmingAnomaly);

            // Storm corridor shift
            world.StormCorridorNormalizedLat = Math.Clamp(
                world.StormCorridorNormalizedLat + world.GlobalTemperatureAnomaly * cfg.StormCorridorShiftPerDegree,
                0.05f, 0.95f);

            // Monsoon multiplier varies with anomaly
            world.MonsoonIntensityMultiplier = Math.Clamp(
                cfg.MonsoonIntensityMultiplier + world.GlobalTemperatureAnomaly * cfg.MonsoonAnomalySensitivity,
                cfg.MonsoonMultiplierMin, cfg.MonsoonMultiplierMax);
        }

        // Biome reclassification always runs when anomaly is non-zero
        if (world.GlobalTemperatureAnomaly != 0f)
            RunBiomeReclassification(world, pending);
    }

    private void RunBiomeReclassification(WorldState world, List<PendingEvent> pending)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var cfg = _cfg;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);

                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;

                int idx = x + y * w;
                var profile = world.SeasonalProfiles[idx];

                // Compute effective temperature including anomaly
                float normalizedLat = y / (float)h;
                float latScale = 1.0f + MathF.Abs(normalizedLat - 0.5f) * cfg.Climate.LatTemperatureAnomalyScale;
                int effectiveTemp = (int)Math.Clamp(
                    tile.BaseTemperature + GetSeasonalTempDelta(profile, world.CurrentSeason)
                    + world.GlobalTemperatureAnomaly * latScale,
                    0, 255);

                var newBiome = BiomeClassifier.Classify(
                    (byte)effectiveTemp,
                    tile.CurrentMoisture,
                    tile.Elevation,
                    tile.StaticFlags,
                    cfg);

                if ((byte)newBiome != tile.BiomeType)
                {
                    byte oldBiome = tile.BiomeType;
                    tile.BiomeType = (byte)newBiome;
                    world.TileGrid.SetTile(coord, tile);

                    pending.Add(new PendingEvent(
                        EventType.BiomeChanged,
                        coord,
                        CauseEventId: null,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            From = (BiomeType)oldBiome,
                            To   = newBiome,
                            GlobalTemperatureAnomaly = world.GlobalTemperatureAnomaly
                        })));
                }
            }
        }
    }

    private static int GetSeasonalTempDelta(SeasonalProfile p, Season s) => s switch
    {
        Season.Spring => p.TempDeltaSpring,
        Season.Summer => p.TempDeltaSummer,
        Season.Autumn => p.TempDeltaAutumn,
        Season.Winter => p.TempDeltaWinter,
        _             => 0
    };

    // =========================================================================
    // 1.5.5 — Sea Level + VolcanicActivityMultiplier Decay (annual)
    // =========================================================================

    private void RunAnnualSeaLevel(WorldState world, List<PendingEvent> pending)
    {
        var cfg = _cfg.Climate;
        if (cfg.AnnualSeaLevelDriftRate == 0f) return;

        float tempFactor = 1.0f + Math.Abs(world.GlobalTemperatureAnomaly) * 0.1f;
        float delta = cfg.AnnualSeaLevelDriftRate * tempFactor;
        float previousLevel = world.CurrentSeaLevel;
        world.CurrentSeaLevel += delta;

        // Reclassify coastal tiles
        ReclassifyCoastalTiles(world);

        if (Math.Abs(world.CurrentSeaLevel - previousLevel) >= cfg.SeaLevelEventThreshold)
        {
            pending.Add(new PendingEvent(
                EventType.SeaLevelChanged,
                Location: null,
                CauseEventId: null,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    PreviousLevel = previousLevel,
                    NewLevel      = world.CurrentSeaLevel,
                    Delta         = delta
                })));
        }
    }

    private static void ReclassifyCoastalTiles(WorldState world)
    {
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        float seaLevelFraction = world.CurrentSeaLevel;
        // Determine new ocean threshold byte value by matching fraction to elevation distribution
        // Quick approximation: seaLevel fraction of 255 gives the threshold elevation
        byte seaLevelByte = (byte)(seaLevelFraction * 255f);

        // Two-pass: first mark ocean, then find new coasts
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);

                bool shouldBeOcean = tile.Elevation <= seaLevelByte;
                bool isOcean = (BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater;

                if (shouldBeOcean && !isOcean)
                {
                    tile.BiomeType = (byte)BiomeType.Ocean;
                    tile.StaticFlags &= ~TileStaticFlags.IsCoastal;
                    world.TileGrid.SetTile(coord, tile);
                }
                else if (!shouldBeOcean && isOcean)
                {
                    // Rising land — reclassification to previous biome is handled by BiomeClassifier
                    tile.StaticFlags &= ~TileStaticFlags.IsCoastal;
                    world.TileGrid.SetTile(coord, tile);
                }
            }
        }

        // Second pass: set IsCoastal on land tiles adjacent to ocean
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);
                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;

                bool hasOceanNeighbor = false;
                int[] nx = { (x + 1) % w, (x - 1 + w) % w, x, x };
                int[] ny = { y, y, Math.Max(0, y - 1), Math.Min(h - 1, y + 1) };
                for (int n = 0; n < 4; n++)
                {
                    var nb = world.TileGrid.GetTile(new TileCoord(nx[n], ny[n]));
                    if ((BiomeType)nb.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater)
                    { hasOceanNeighbor = true; break; }
                }

                bool wasCoastal = tile.StaticFlags.HasFlag(TileStaticFlags.IsCoastal);
                if (hasOceanNeighbor != wasCoastal)
                {
                    if (hasOceanNeighbor) tile.StaticFlags |= TileStaticFlags.IsCoastal;
                    else tile.StaticFlags &= ~TileStaticFlags.IsCoastal;
                    world.TileGrid.SetTile(coord, tile);
                }
            }
        }
    }

    private void VolcanicMultiplierDecay(WorldState world)
    {
        float rate = _cfg.Climate.VolcanicDecayRate;
        world.VolcanicActivityMultiplier = MathF.Max(1.0f,
            world.VolcanicActivityMultiplier + (1.0f - world.VolcanicActivityMultiplier) * rate);
    }

    // =========================================================================
    // 1.5.4 — Resource Dynamics (annual)
    // =========================================================================

    // =========================================================================
    // 1.5.3 — Natural Disaster System
    // =========================================================================

    private void RunDisasterTick(WorldState world, List<PendingEvent> pending)
    {
        RunVolcanicEruptions(world, pending);
        RunEarthquakes(world, pending);
        RunWildfires(world, pending);
        RunFloods(world, pending);
        TickDownActiveDisasters(world);
    }

    private void RunVolcanicEruptions(WorldState world, List<PendingEvent> pending)
    {
        var dcfg = _cfg.Disasters;
        foreach (var (cx, cy, chunk) in world.TileGrid.AllChunksWithCoords())
        {
            if (!chunk.SummaryFlags.HasFlag(ChunkSummaryFlags.HasVolcanicTile)) continue;
            foreach (var (coord, tile) in chunk.AllTiles(cx, cy))
            {
                if (!tile.StaticFlags.HasFlag(TileStaticFlags.IsVolcanic)) continue;
                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;
                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, coord.X, coord.Y, DisasterSalts.VolcanicEruption);
                float prob = dcfg.VolcanicEruptionProbabilityPerTick * world.VolcanicActivityMultiplier;
                if (roll >= prob) continue;
                AddDisaster(world, coord, new ActiveDisaster(DisasterType.VolcanicAsh, dcfg.VolcanicAshIntensity, -1, new EventId(0)));
                world.VolcanicActivityMultiplier = MathF.Min(
                    world.VolcanicActivityMultiplier + dcfg.VolcanicActivityBoost,
                    dcfg.VolcanicActivityMultiplierCap);
                pending.Add(new PendingEvent(EventType.VolcanicEruption, coord, null,
                    System.Text.Json.JsonSerializer.Serialize(new { Intensity = dcfg.VolcanicAshIntensity })));
            }
        }
    }

    private void RunEarthquakes(WorldState world, List<PendingEvent> pending)
    {
        var dcfg = _cfg.Disasters;
        foreach (var (cx, cy, chunk) in world.TileGrid.AllChunksWithCoords())
        {
            if (!chunk.SummaryFlags.HasFlag(ChunkSummaryFlags.HasFaultLineTile)) continue;
            foreach (var (coord, tile) in chunk.AllTiles(cx, cy))
            {
                if (!tile.StaticFlags.HasFlag(TileStaticFlags.IsFaultLine)) continue;
                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;
                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, coord.X, coord.Y, DisasterSalts.Earthquake);
                if (roll >= dcfg.EarthquakeProbabilityPerTick) continue;
                AddDisaster(world, coord, new ActiveDisaster(DisasterType.SeismicDamage, dcfg.EarthquakeIntensity, dcfg.EarthquakeDecayTicks, new EventId(0)));
                pending.Add(new PendingEvent(EventType.EarthquakeOccurred, coord, null,
                    System.Text.Json.JsonSerializer.Serialize(new { Intensity = dcfg.EarthquakeIntensity })));
            }
        }
    }

    private void RunWildfires(WorldState world, List<PendingEvent> pending)
    {
        if (world.CurrentSeason is not (Season.Summer or Season.Autumn)) return;
        var dcfg = _cfg.Disasters;

        // Ignition pass
        foreach (var (cx, cy, chunk) in world.TileGrid.AllChunksWithCoords())
        {
            if (!chunk.SummaryFlags.HasFlag(ChunkSummaryFlags.HasForestTile)) continue;
            foreach (var (coord, tile) in chunk.AllTiles(cx, cy))
            {
                if (!IsForestBiome((BiomeType)tile.BiomeType)) continue;
                if (HasActiveDisasterType(world, coord, DisasterType.Wildfire)) continue;

                float prob = dcfg.WildfireIgnitionProbabilityPerTick;
                if (tile.CurrentMoisture < dcfg.WildfireDryMoistureThreshold)
                    prob *= dcfg.WildfireIgnitionDryMultiplier;

                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, coord.X, coord.Y, DisasterSalts.Wildfire);
                if (roll >= prob) continue;
                AddDisaster(world, coord, new ActiveDisaster(DisasterType.Wildfire, dcfg.WildfireIntensity, dcfg.WildfireMaxTicks, new EventId(0)));
                pending.Add(new PendingEvent(EventType.WildfireOccurred, coord, null,
                    System.Text.Json.JsonSerializer.Serialize(new { Intensity = dcfg.WildfireIntensity })));
            }
        }

        // Spread pass — iterate all tiles that already have an active wildfire
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;
        var activeFires = world.ActiveTileDisasters
            .Where(kv => kv.Value.Any(d => d.Type == DisasterType.Wildfire))
            .Select(kv => kv.Key)
            .ToList();

        var spreadNeighbors = new TileCoord[4];
        foreach (var coord in activeFires)
        {
            spreadNeighbors[0] = new TileCoord(((coord.X + 1) % w + w) % w, coord.Y);
            spreadNeighbors[1] = new TileCoord(((coord.X - 1) % w + w) % w, coord.Y);
            spreadNeighbors[2] = new TileCoord(coord.X, Math.Clamp(coord.Y - 1, 0, h - 1));
            spreadNeighbors[3] = new TileCoord(coord.X, Math.Clamp(coord.Y + 1, 0, h - 1));
            foreach (var nb in spreadNeighbors)
            {
                var nbTile = world.TileGrid.GetTile(nb);
                if (!IsForestBiome((BiomeType)nbTile.BiomeType)) continue;
                if (HasActiveDisasterType(world, nb, DisasterType.Wildfire)) continue;

                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, nb.X, nb.Y, DisasterSalts.WildfireSpread);
                if (roll >= dcfg.WildfireSpreadProbabilityPerTick) continue;
                AddDisaster(world, nb, new ActiveDisaster(DisasterType.Wildfire, dcfg.WildfireIntensity, dcfg.WildfireMaxTicks, new EventId(0)));
                // No new PendingEvent for spread — shares root fire's causal chain
            }
        }
    }

    private void RunFloods(WorldState world, List<PendingEvent> pending)
    {
        if (world.CurrentSeason is not (Season.Spring or Season.Summer)) return;
        var dcfg = _cfg.Disasters;

        foreach (var (cx, cy, chunk) in world.TileGrid.AllChunksWithCoords())
        {
            if (!chunk.SummaryFlags.HasFlag(ChunkSummaryFlags.HasRiverTile)) continue;
            foreach (var (coord, tile) in chunk.AllTiles(cx, cy))
            {
                if (!tile.StaticFlags.HasFlag(TileStaticFlags.HasRiver)) continue;
                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;

                float prob = dcfg.FloodIgnitionProbabilityPerTick;
                if (tile.CurrentMoisture >= dcfg.FloodWetMoistureThreshold)
                    prob *= dcfg.FloodWetMultiplier;

                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentTick, coord.X, coord.Y, DisasterSalts.Flood);
                if (roll >= prob) continue;

                AddDisaster(world, coord, new ActiveDisaster(DisasterType.Flood, dcfg.FloodOriginIntensity, dcfg.FloodOriginTicks, new EventId(0)));
                pending.Add(new PendingEvent(EventType.FloodOccurred, coord, null,
                    System.Text.Json.JsonSerializer.Serialize(new { Intensity = dcfg.FloodOriginIntensity })));

                foreach (var nb in world.GetTilesInRadius(coord, dcfg.FloodSpreadRadius))
                {
                    if (nb == coord) continue;
                    var nbTile = world.TileGrid.GetTile(nb);
                    if ((BiomeType)nbTile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;
                    AddDisaster(world, nb, new ActiveDisaster(DisasterType.Flood, dcfg.FloodSpreadIntensity, dcfg.FloodSpreadTicks, new EventId(0)));
                }
            }
        }
    }

    private static void TickDownActiveDisasters(WorldState world)
    {
        var toRemove = new List<TileCoord>();

        foreach (var (coord, list) in world.ActiveTileDisasters)
        {
            bool wildfireExpired = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var d = list[i];
                if (d.TicksRemaining < 0) continue; // indefinite (e.g. volcanic ash deposit)
                if (d.TicksRemaining <= 1)
                {
                    if (d.Type == DisasterType.Wildfire) wildfireExpired = true;
                    list.RemoveAt(i);
                }
                else
                {
                    list[i] = d with { TicksRemaining = d.TicksRemaining - 1 };
                }
            }

            if (list.Count == 0)
            {
                toRemove.Add(coord);
                var tile = world.TileGrid.GetTile(coord);
                tile.DynFlags &= ~TileDynFlags.HasActiveDisaster;
                if (wildfireExpired && IsForestBiome((BiomeType)tile.BiomeType))
                    tile.DynFlags |= TileDynFlags.RecentlyBurned;
                world.TileGrid.SetTile(coord, tile);
            }
        }

        foreach (var coord in toRemove)
            world.ActiveTileDisasters.Remove(coord);
    }

    private void RunDroughtsAnnual(WorldState world, List<PendingEvent> pending)
    {
        var dcfg = _cfg.Disasters;

        // Tick down existing droughts
        for (int i = world.ActiveDroughts.Count - 1; i >= 0; i--)
        {
            var d = world.ActiveDroughts[i];
            if (d.SeasonsRemaining <= 1)
            {
                world.ActiveDroughts.RemoveAt(i);
                pending.Add(new PendingEvent(EventType.DroughtEnded, null, d.OriginEventId, "{}"));
            }
            else
            {
                world.ActiveDroughts[i] = d with { SeasonsRemaining = d.SeasonsRemaining - 1 };
            }
        }

        // Attempt new drought per (latBand, biome) pair — one roll per unique combination
        int h = world.TileGrid.TileHeight, w = world.TileGrid.TileWidth;
        var seen = new HashSet<(int, BiomeType)>();

        for (int y = 0; y < h; y++)
        {
            int latBand = y / Math.Max(1, h / 4);
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);
                var biome = (BiomeType)tile.BiomeType;
                if (biome is BiomeType.Ocean or BiomeType.CoastalWater) continue;

                if (!seen.Add((latBand, biome))) continue;
                if (world.ActiveDroughts.Any(d => d.LatitudeBandIndex == latBand && d.AffectedBiome == biome)) continue;

                float prob = dcfg.DroughtProbabilityPerYear;
                if (world.GlobalPrecipitationMultiplier < dcfg.DroughtPrecipitationThreshold)
                    prob *= dcfg.DroughtDroughtMultiplier;

                float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, latBand, (int)biome, DisasterSalts.DroughtCheck);
                if (roll >= prob) continue;

                int seasons = Math.Clamp(
                    dcfg.DroughtMinSeasons + (int)(roll / Math.Max(prob, 0.001f) * (dcfg.DroughtMaxSeasons - dcfg.DroughtMinSeasons)),
                    dcfg.DroughtMinSeasons, dcfg.DroughtMaxSeasons);
                world.ActiveDroughts.Add(new ActiveDrought(latBand, biome, 0.7f, seasons, new EventId(0)));
                pending.Add(new PendingEvent(EventType.DroughtBegan, null, null, "{}"));
            }
        }
    }

    private static void AddDisaster(WorldState world, TileCoord coord, ActiveDisaster disaster)
    {
        if (!world.ActiveTileDisasters.TryGetValue(coord, out var list))
            world.ActiveTileDisasters[coord] = list = new List<ActiveDisaster>();
        list.Add(disaster);

        var tile = world.TileGrid.GetTile(coord);
        tile.DynFlags |= TileDynFlags.HasActiveDisaster;
        world.TileGrid.SetTile(coord, tile);
    }

    private static bool HasActiveDisasterType(WorldState world, TileCoord coord, DisasterType type) =>
        world.ActiveTileDisasters.TryGetValue(coord, out var list) && list.Any(d => d.Type == type);

    private static bool IsForestBiome(BiomeType b) =>
        b is BiomeType.TemperateForest or BiomeType.TropicalRainforest or BiomeType.BorealForest;

    // =========================================================================
    // 1.5.4 — Resource Dynamics (annual)
    // =========================================================================

    private void RunAnnualResourceDynamics(WorldState world)
    {
        var cfg = _cfg.WorldGen.Resources;
        int w = world.TileGrid.TileWidth, h = world.TileGrid.TileHeight;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var coord = new TileCoord(x, y);
                var tile = world.TileGrid.GetTile(coord);

                if ((BiomeType)tile.BiomeType is BiomeType.Ocean or BiomeType.CoastalWater) continue;

                int latBand = y / Math.Max(1, h / 4);
                var biome = (BiomeType)tile.BiomeType;
                bool inDrought = world.ActiveDroughts
                    .Any(d => d.LatitudeBandIndex == latBand && d.AffectedBiome == biome);

                int fertility = tile.Fertility;

                if (inDrought)
                {
                    // Drought reduces fertility
                    fertility = Math.Max(0, fertility - cfg.DroughtFertilityPenaltyPerSeason);
                }
                else
                {
                    // Natural recovery — tiles below some baseline recover 1 per year
                    if (fertility < 200)
                        fertility = Math.Min(255, fertility + cfg.FertilityRecoveryPerYear);
                }

                // Post-fire boost: tile where wildfire just expired this tick
                if (tile.DynFlags.HasFlag(TileDynFlags.RecentlyBurned))
                {
                    fertility = Math.Min(255, fertility + cfg.PostFireFertilityBoost);
                    tile.DynFlags &= ~TileDynFlags.RecentlyBurned;
                }

                if ((byte)fertility != tile.Fertility)
                {
                    tile.Fertility = (byte)fertility;
                    world.TileGrid.SetTile(coord, tile);
                }
            }
        }
    }
}
