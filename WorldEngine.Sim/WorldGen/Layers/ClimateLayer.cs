using WorldEngine.Sim.Config;
using WorldEngine.Sim.Tiles;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates base temperature and moisture for every tile.
///
/// Temperature: latitude cosine curve + elevation lapse rate.
/// Moisture: two-band wind sweep —
///   Tropical band: East-to-West sweep (trade winds blow toward equator).
///   Mid-lat + polar: West-to-East sweep (westerlies).
///   Rain shadow: leeward tiles of mountain lose RainShadowLossFraction moisture.
/// Also sets storm corridor flag and computes per-tile SeasonalProfiles.
/// </summary>
public sealed class ClimateLayer : IWorldGenLayer<ClimateResult>
{
    public ClimateResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var elev  = ctx.Elevation!;
        var ocean = ctx.Ocean!;
        var cfg   = ctx.SimConfig.Climate;
        int seed  = ctx.Config.Seed ^ LayerSeeds.Climate;
        int w = ctx.TileWidth, h = ctx.TileHeight, n = ctx.TileCount;

        var result = new ClimateResult(n);

        // --- Step 0: Maritime influence map ---
        // BFS distance from ocean+lake tiles, converted to exponential decay [0,1].
        // Used by temperature (continental amplification) and moisture (lake recharge).
        float[] maritime = ComputeMaritimeInfluence(ocean, ctx.River, w, h, ctx, cfg.ContinentalRadiusTiles);

        // --- Step 1: Base temperature from latitude + lapse rate ---
        FastNoiseLite? tempNoise = null;
        if (cfg.TemperatureNoiseScale > 0f)
        {
            tempNoise = new FastNoiseLite(seed ^ 0x7A4B3C);
            tempNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            tempNoise.SetFrequency(cfg.TemperatureNoiseFrequency);
        }
        ComputeBaseTemperature(elev, ocean, cfg, w, h, ctx, result.BaseTemperature, tempNoise, maritime);
        progress?.Report(0.2f);
        ct.ThrowIfCancellationRequested();

        // --- Step 2: Base moisture via two-band wind sweep ---
        ComputeBaseMoisture(elev, ocean, ctx.River, cfg, w, h, ctx, result.BaseMoisture);

        // Apply moisture noise after sweep to break horizontal banding.
        // Moisture from wind sweeps is constant across each latitude row; noise
        // introduces east-west variation between regions.
        if (cfg.MoistureNoiseScale > 0f)
        {
            var moistNoise = new FastNoiseLite(seed ^ 0x3D9A17);
            moistNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            moistNoise.SetFrequency(cfg.MoistureNoiseFrequency);
            float mScale = cfg.MoistureNoiseScale;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = ctx.IndexOf(x, y);
                    if (ocean.IsOcean[idx]) continue; // don't perturb ocean tiles
                    int m = result.BaseMoisture[idx] + (int)(moistNoise.GetNoise(x, y) * mScale);
                    result.BaseMoisture[idx] = (byte)Math.Clamp(m, 0, 255);
                }
            }
        }

        // Enforce a minimum BaseMoisture on all land tiles AFTER noise.
        // The 5% floor in ComputeBaseMoisture runs before noise, so large negative noise
        // values can push floor tiles to 0. BaseMoisture=0 is degenerate: the food formula
        // (fertility × moisture) and the seasonal relative floor both collapse to zero.
        // A small guaranteed minimum keeps marginal tiles non-degenerate.
        const byte MinLandBaseMoisture = 10;
        for (int i = 0; i < n; i++)
        {
            if (!ocean.IsOcean[i] && result.BaseMoisture[i] < MinLandBaseMoisture)
                result.BaseMoisture[i] = MinLandBaseMoisture;
        }

        progress?.Report(0.5f);
        ct.ThrowIfCancellationRequested();

        // --- Step 3: Storm corridor + monsoon zone flags ---
        MarkZoneFlags(cfg, w, h, ctx, result);
        progress?.Report(0.7f);
        ct.ThrowIfCancellationRequested();

        // --- Step 4: Per-tile seasonal profiles ---
        // maritime[] passed so coastal vs. continental tiles get different seasonal shapes.
        ComputeSeasonalProfiles(ocean, cfg, w, h, ctx, result, maritime);
        progress?.Report(1.0f);

        return result;
    }

    private static void ComputeBaseTemperature(
        ElevationResult elev, OceanResult ocean, ClimateConfig cfg,
        int w, int h, WorldGenContext ctx, byte[] temp, FastNoiseLite? noise,
        float[] maritime)
    {
        const float lapseScale = 0.25f;
        float noiseScale  = cfg.TemperatureNoiseScale;
        float contAmp     = cfg.ContinentalAmplification;

        for (int y = 0; y < h; y++)
        {
            float normLat = (float)y / (h - 1);
            float latFrac = MathF.Cos(MathF.Abs(normLat - 0.5f) * MathF.PI);
            latFrac = Math.Clamp(latFrac, 0f, 1f);

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                float elevation01 = elev.Elevation[idx] / 255f;
                float lapseFrac   = elevation01 * lapseScale;

                float noiseT = noise != null ? noise.GetNoise(x, y) * noiseScale : 0f;

                // Continental/maritime effect: coasts are moderated (pulled toward mean);
                // deep interiors are amplified (pushed away from mean). Same latitude can
                // produce temperate forest on the coast and desert inland.
                // Formula: (1 - 2*maritime) maps [0,1] → [+1,-1]:
                //   maritime=0 (interior) → +contAmp * deviation (warmer in tropics, colder at poles)
                //   maritime=1 (coast)    → -contAmp * deviation (cooler in tropics, warmer at poles)
                float contMod = contAmp > 0f
                    ? (1f - 2f * maritime[idx]) * contAmp * (latFrac - 0.5f)
                    : 0f;

                float t = Math.Clamp(latFrac - lapseFrac + noiseT + contMod, 0f, 1f);
                temp[idx] = (byte)(t * 255f);
            }
        }
    }

    private static bool InTropical(int y, int h, float tropHalf)
        => MathF.Abs((float)y / (h - 1) - 0.5f) < tropHalf;

    /// <summary>
    /// BFS distance from ocean and lake tiles, converted to exponential decay.
    /// Returns 1.0 at water, decaying to ~0 at distance >> radius tiles.
    /// If radius is 0 or no config, returns all-zeros (continental effect disabled).
    /// </summary>
    private static float[] ComputeMaritimeInfluence(
        OceanResult ocean, RiverResult? river,
        int w, int h, WorldGenContext ctx, float radiusTiles)
    {
        var influence = new float[ctx.TileCount];
        if (radiusTiles <= 0f) return influence; // disabled

        var dist = new float[ctx.TileCount];
        Array.Fill(dist, float.MaxValue);
        var queue = new Queue<int>(ctx.TileCount / 4);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (ocean.IsOcean[idx] || (river?.IsLake[idx] ?? false))
                {
                    dist[idx] = 0f;
                    queue.Enqueue(idx);
                }
            }
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            float next = dist[idx] + 1f;
            int x = idx % w, y = idx / w;

            void Relax(int ni)
            {
                if (next < dist[ni]) { dist[ni] = next; queue.Enqueue(ni); }
            }

            if (x > 0)     Relax(idx - 1);
            if (x < w - 1) Relax(idx + 1);
            if (x == 0)    Relax(idx + w - 1); // cylinder wrap
            if (x == w-1)  Relax(idx - w + 1);
            if (y > 0)     Relax(idx - w);
            if (y < h - 1) Relax(idx + w);
        }

        float scale = 1f / radiusTiles;
        for (int i = 0; i < ctx.TileCount; i++)
            influence[i] = dist[i] == float.MaxValue ? 0f : MathF.Exp(-dist[i] * scale);

        return influence;
    }

    private static void ComputeBaseMoisture(
        ElevationResult elev, OceanResult ocean, RiverResult? river, ClimateConfig cfg,
        int w, int h, WorldGenContext ctx, byte[] moisture)
    {
        float[] raw       = new float[ctx.TileCount];
        float tropHalf    = cfg.TropicalBandHalfWidth;
        float rainShadow  = cfg.RainShadowLossFraction;
        byte  mtThresh    = cfg.MountainElevationThreshold;
        float decay       = cfg.MoistureCarryDecay;
        float angle       = cfg.MoistureAngleBlend;
        float lakeRecharge = cfg.LakeMoistureRecharge;
        float riverBonus   = cfg.RiverMoistureBonus;

        // carry[y] holds how much moisture row y is transporting at the current column.
        // Processing column-by-column (rather than row-by-row) lets carry bleed N/S
        // between adjacent rows at each step, creating diagonal/angled flow.
        var carry = new float[h];
        var bleed = new float[h];

        // --- Tropical band: East→West column sweep ---
        for (int pass = 0; pass < w; pass++)
        {
            int x = ((w - 1 - pass) % w + w) % w;

            for (int y = 0; y < h; y++)
            {
                if (!InTropical(y, h, tropHalf)) continue;
                int idx = ctx.IndexOf(x, y);
                if (ocean.IsOcean[idx])
                {
                    carry[y] = 0.7f + 0.3f * (1f - elev.Elevation[idx] / 255f);
                }
                else
                {
                    // Lakes recharge the carry (inland moisture sources).
                    // Rivers add a smaller bonus — moisture evaporates from flowing water.
                    if (lakeRecharge > 0f && (river?.IsLake[idx] ?? false))
                        carry[y] = Math.Max(carry[y], lakeRecharge);
                    else if (riverBonus > 0f && (river?.HasRiver[idx] ?? false))
                        carry[y] = Math.Min(1f, carry[y] + riverBonus);

                    if (elev.Elevation[idx] >= mtThresh) carry[y] *= (1f - rainShadow);
                    raw[idx] = Math.Max(raw[idx], carry[y]);
                    carry[y] *= decay;
                }
            }

            // N-S bleed: carry leaks to adjacent tropical rows each column step.
            // This is the "angle" — moisture drifts diagonally rather than purely west.
            if (angle > 0f)
            {
                for (int y = 0; y < h; y++)
                {
                    if (!InTropical(y, h, tropHalf)) { bleed[y] = 0f; continue; }
                    float n = (y > 0     && InTropical(y - 1, h, tropHalf)) ? carry[y - 1] : carry[y];
                    float s = (y < h - 1 && InTropical(y + 1, h, tropHalf)) ? carry[y + 1] : carry[y];
                    bleed[y] = carry[y] * (1f - 2f * angle) + n * angle + s * angle;
                }
                (carry, bleed) = (bleed, carry);
            }
        }

        Array.Clear(carry, 0, h);

        // --- Mid-latitude + polar: West→East column sweep ---
        for (int pass = 0; pass < w; pass++)
        {
            int x = pass % w;

            for (int y = 0; y < h; y++)
            {
                if (InTropical(y, h, tropHalf)) continue;
                int idx = ctx.IndexOf(x, y);
                if (ocean.IsOcean[idx])
                {
                    carry[y] = 0.6f + 0.3f * (1f - elev.Elevation[idx] / 255f);
                }
                else
                {
                    if (lakeRecharge > 0f && (river?.IsLake[idx] ?? false))
                        carry[y] = Math.Max(carry[y], lakeRecharge);
                    else if (riverBonus > 0f && (river?.HasRiver[idx] ?? false))
                        carry[y] = Math.Min(1f, carry[y] + riverBonus);

                    if (elev.Elevation[idx] >= mtThresh) carry[y] *= (1f - rainShadow);
                    raw[idx] = Math.Max(raw[idx], carry[y]);
                    carry[y] *= decay;
                }
            }

            if (angle > 0f)
            {
                for (int y = 0; y < h; y++)
                {
                    if (InTropical(y, h, tropHalf)) { bleed[y] = 0f; continue; }
                    float n = (y > 0     && !InTropical(y - 1, h, tropHalf)) ? carry[y - 1] : carry[y];
                    float s = (y < h - 1 && !InTropical(y + 1, h, tropHalf)) ? carry[y + 1] : carry[y];
                    bleed[y] = carry[y] * (1f - 2f * angle) + n * angle + s * angle;
                }
                (carry, bleed) = (bleed, carry);
            }
        }

        // Baseline for tiles that received no moisture
        for (int i = 0; i < ctx.TileCount; i++)
        {
            if (!ocean.IsOcean[i] && raw[i] < 0.05f)
                raw[i] = 0.05f;
        }

        for (int i = 0; i < ctx.TileCount; i++)
            moisture[i] = (byte)Math.Clamp((int)(raw[i] * 255f), 0, 255);
    }

    private static void MarkZoneFlags(
        ClimateConfig cfg, int w, int h, WorldGenContext ctx, ClimateResult result)
    {
        float stormLat  = cfg.StormCorridorNormalizedLat;
        float stormHalf = cfg.StormCorridorHalfWidth;
        byte  monsoonThresh = cfg.MonsoonMoistureThreshold;
        float tropHalf  = cfg.TropicalBandHalfWidth;

        for (int y = 0; y < h; y++)
        {
            float normLat = (float)y / (h - 1);
            bool inStorm    = MathF.Abs(normLat - stormLat) < stormHalf;
            bool inTropical = MathF.Abs(normLat - 0.5f) < tropHalf;

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (inStorm) result.IsStormCorridor[idx] = true;
                if (inTropical && result.BaseMoisture[idx] >= monsoonThresh)
                    result.IsMonsoonTile[idx] = true;
            }
        }
    }

    private static void ComputeSeasonalProfiles(
        OceanResult ocean, ClimateConfig cfg,
        int w, int h, WorldGenContext ctx, ClimateResult result,
        float[] maritime)
    {
        float tropHalf   = cfg.TropicalBandHalfWidth;
        float stormLat   = cfg.StormCorridorNormalizedLat;
        float stormHalf  = cfg.StormCorridorHalfWidth;
        float contThresh = cfg.ContinentalSeasonalThreshold;    // maritime < this → continental
        float maritThresh = cfg.MaritimeSeasonalThreshold;      // maritime > this → maritime

        for (int y = 0; y < h; y++)
        {
            float normLat   = (float)y / (h - 1);
            bool inTropical = MathF.Abs(normLat - 0.5f) < tropHalf;
            bool inPolar    = MathF.Abs(normLat - 0.5f) > 0.4f;
            bool inStorm    = MathF.Abs(normLat - stormLat) < stormHalf;

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (ocean.IsOcean[idx]) continue;

                sbyte tSpring, tSummer, tAutumn, tWinter;
                sbyte mSpring, mSummer, mAutumn, mWinter;

                float mar = maritime[idx];

                if (inTropical)
                {
                    // Tropical: small temperature variance (~5-10°C annual range);
                    // distinct wet/dry seasons dominate over temperature swings.
                    // ±5-10 units ≈ ±1.5-3°C — correct for tropical regions.
                    tSpring = 5; tSummer = 10; tAutumn = 5; tWinter = -5;
                    mSpring = 10; mSummer = 30; mAutumn = 15; mWinter = 5;
                }
                else if (inPolar)
                {
                    // Polar: brutal winter, brief but warm summer.
                    // Real annual range: 40-50°C → ~130-160 units, but we cap at
                    // sbyte range (-128..127) and choose values that keep tile temps
                    // non-negative in practice (base polar ≈ 10-50, winter clamps at 0).
                    // Maritime polar (fjords, Iceland): moderated by ocean, less extreme
                    // Continental polar (Siberia): extreme swings, drier winter
                    if (mar > maritThresh)
                    {
                        tSpring = 8; tSummer = 28; tAutumn = -5; tWinter = -35;
                        mSpring = 5; mSummer = 8; mAutumn = 5; mWinter = 0;
                    }
                    else if (mar < contThresh)
                    {
                        tSpring = 10; tSummer = 35; tAutumn = -8; tWinter = -48;
                        mSpring = 3; mSummer = 12; mAutumn = 0; mWinter = -12;
                    }
                    else
                    {
                        tSpring = 8; tSummer = 30; tAutumn = -5; tWinter = -42;
                        mSpring = 4; mSummer = 10; mAutumn = 3; mWinter = -8;
                    }
                }
                else
                {
                    // Temperate zone: maritime/continental distinction drives both
                    // temperature amplitude and moisture seasonality.
                    // Real annual range: maritime 10-20°C (32-65 units), continental 30-50°C (97-161 units).

                    if (mar > maritThresh)
                    {
                        // Maritime temperate (Atlantic coasts, Pacific NW, western Europe):
                        // moderate temperature swings, dry summers, wet autumns/winters.
                        // ~15-20°C real range → 48-65 units; split asymmetrically (summer milder than winter).
                        tSpring = 8; tSummer = 20; tAutumn = 5; tWinter = -18;
                        mSpring = 5; mSummer = -10; mAutumn = 15; mWinter = 5;
                    }
                    else if (mar < contThresh)
                    {
                        // Continental interior (Great Plains, Central Asia, eastern Europe):
                        // large temperature swings, summer convective rains, cold dry winters.
                        // ~35-45°C real range → 113-145 units; split +38/-38 to stay symmetric.
                        tSpring = 10; tSummer = 38; tAutumn = -5; tWinter = -38;
                        mSpring = 10; mSummer = 15; mAutumn = 0; mWinter = -20;
                    }
                    else
                    {
                        // Semi-maritime transition zone (central France, Midwest US fringes):
                        // intermediate swings, modest moisture variation.
                        // ~25-30°C real range → 80-97 units.
                        tSpring = 8; tSummer = 28; tAutumn = 2; tWinter = -25;
                        mSpring = 5; mSummer = 0; mAutumn = 10; mWinter = -8;
                    }
                }

                // Storm corridor gets autumn moisture bonus regardless of zone
                if (inStorm)
                {
                    mAutumn += 20;
                    mSummer += 5;
                }

                result.SeasonalProfiles[idx] = new SeasonalProfile
                {
                    TempDeltaSpring     = tSpring,
                    TempDeltaSummer     = tSummer,
                    TempDeltaAutumn     = tAutumn,
                    TempDeltaWinter     = tWinter,
                    MoistureDeltaSpring = mSpring,
                    MoistureDeltaSummer = mSummer,
                    MoistureDeltaAutumn = mAutumn,
                    MoistureDeltaWinter = mWinter,
                };
            }
        }
    }
}
