using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using WorldEngine.Sim.WorldGen;

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

        if (isAnnualTick)
        {
            RunAnnualDrift(world, pending);
            RunAnnualSeaLevel(world, pending);
            RunAnnualResourceDynamics(world);
            VolcanicMultiplierDecay(world);
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
                cfg.MonsoonIntensityMultiplier + world.GlobalTemperatureAnomaly * 0.01f,
                0.5f, 3.0f);
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
                float latScale = 1.0f + MathF.Abs(normalizedLat - 0.5f) * 1.4f;
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
                        EventType.BiomeShifted,
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

                // Post-fire boost: tile that just cleared a wildfire (no active disaster, was forest)
                bool justClearedFire = !tile.DynFlags.HasFlag(TileDynFlags.HasActiveDisaster)
                    && biome is BiomeType.TemperateForest or BiomeType.TropicalRainforest or BiomeType.BorealForest
                    && fertility < 100; // low fertility suggests recent damage
                if (justClearedFire)
                    fertility = Math.Min(255, fertility + cfg.PostFireFertilityBoost);

                if ((byte)fertility != tile.Fertility)
                {
                    tile.Fertility = (byte)fertility;
                    world.TileGrid.SetTile(coord, tile);
                }
            }
        }
    }
}
