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
        int w = ctx.TileWidth, h = ctx.TileHeight, n = ctx.TileCount;

        var result = new ClimateResult(n);

        // --- Step 1: Base temperature from latitude + lapse rate ---
        ComputeBaseTemperature(elev, ocean, cfg, w, h, ctx, result.BaseTemperature);
        progress?.Report(0.2f);
        ct.ThrowIfCancellationRequested();

        // --- Step 2: Base moisture via two-band wind sweep ---
        ComputeBaseMoisture(elev, ocean, cfg, w, h, ctx, result.BaseMoisture);
        progress?.Report(0.5f);
        ct.ThrowIfCancellationRequested();

        // --- Step 3: Storm corridor + monsoon zone flags ---
        MarkZoneFlags(cfg, w, h, ctx, result);
        progress?.Report(0.7f);
        ct.ThrowIfCancellationRequested();

        // --- Step 4: Per-tile seasonal profiles ---
        ComputeSeasonalProfiles(ocean, cfg, w, h, ctx, result);
        progress?.Report(1.0f);

        return result;
    }

    private static void ComputeBaseTemperature(
        ElevationResult elev, OceanResult ocean, ClimateConfig cfg,
        int w, int h, WorldGenContext ctx, byte[] temp)
    {
        // Lapse rate: −6°C per 1000m, scaled to byte. World height ≈ 255 → ~128 max elevation m.
        // DECISION: byte 255 ≈ 2550m altitude → max lapse reduction ≈ 15°C → ~0.25 fraction.
        const float lapseScale = 0.25f;

        for (int y = 0; y < h; y++)
        {
            // Normalised latitude: 0 = south pole, 1 = north pole
            // Equator is at 0.5. Cosine is hottest at equator.
            float normLat = (float)y / (h - 1);
            // Temperature fraction from latitude [0,1]: cos(|lat-0.5| * π) maps 1.0 at equator to 0 at poles
            float latFrac = MathF.Cos(MathF.Abs(normLat - 0.5f) * MathF.PI);
            latFrac = Math.Clamp(latFrac, 0f, 1f);

            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                float elevation01 = elev.Elevation[idx] / 255f;
                float lapseFrac   = elevation01 * lapseScale;

                float t = Math.Clamp(latFrac - lapseFrac, 0f, 1f);
                temp[idx] = (byte)(t * 255f);
            }
        }
    }

    private static void ComputeBaseMoisture(
        ElevationResult elev, OceanResult ocean, ClimateConfig cfg,
        int w, int h, WorldGenContext ctx, byte[] moisture)
    {
        float[] raw = new float[ctx.TileCount];

        // --- Tropical band: East-to-West sweep (trade winds blow westward) ---
        // Each tile receives moisture from its EAST neighbor (wind comes from the east).
        // Sweep each row from right to left (x = w-1 → 0, then wrap).
        float tropHalf   = cfg.TropicalBandHalfWidth;
        float rainShadow = cfg.RainShadowLossFraction;
        byte  mtThresh   = cfg.MountainElevationThreshold;
        float decay      = cfg.MoistureCarryDecay;

        for (int y = 0; y < h; y++)
        {
            float normLat = (float)y / (h - 1);
            bool inTropical = MathF.Abs(normLat - 0.5f) < tropHalf;
            if (!inTropical) continue;

            // Coastal/ocean tiles seed moisture; inland accumulates with rain shadow
            // Sweep East→West (trade winds blow westward, moisture carried westward)
            float carry = 0f;
            for (int pass = 0; pass < w; pass++)
            {
                int x = (w - 1 - pass % w + w) % w;
                int idx = ctx.IndexOf(x, y);

                if (ocean.IsOcean[idx])
                {
                    carry = 0.7f + 0.3f * (1f - elev.Elevation[idx] / 255f);
                }
                else
                {
                    if (elev.Elevation[idx] >= mtThresh)
                        carry *= (1f - rainShadow);

                    raw[idx] = Math.Max(raw[idx], carry);
                    carry *= decay;
                }
            }
        }

        // --- Mid-latitude + polar: West-to-East sweep (westerlies blow eastward) ---
        for (int y = 0; y < h; y++)
        {
            float normLat = (float)y / (h - 1);
            bool inTropical = MathF.Abs(normLat - 0.5f) < tropHalf;
            if (inTropical) continue;

            float carry = 0f;
            for (int pass = 0; pass < w; pass++)
            {
                int x = pass % w;
                int idx = ctx.IndexOf(x, y);

                if (ocean.IsOcean[idx])
                {
                    carry = 0.6f + 0.3f * (1f - elev.Elevation[idx] / 255f);
                }
                else
                {
                    if (elev.Elevation[idx] >= mtThresh)
                        carry *= (1f - rainShadow);

                    raw[idx] = Math.Max(raw[idx], carry);
                    carry *= decay;
                }
            }
        }

        // Ensure land tiles with no moisture get a small baseline (prevents zero-moisture islands)
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
        int w, int h, WorldGenContext ctx, ClimateResult result)
    {
        float tropHalf  = cfg.TropicalBandHalfWidth;
        float stormLat  = cfg.StormCorridorNormalizedLat;
        float stormHalf = cfg.StormCorridorHalfWidth;

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

                if (inTropical)
                {
                    // Tropical: small temperature variance, high moisture in summer
                    tSpring = 2; tSummer = 4; tAutumn = 2; tWinter = -2;
                    mSpring = 10; mSummer = 30; mAutumn = 15; mWinter = 5;
                }
                else if (inPolar)
                {
                    // Polar: extreme winter cold, cool summer
                    tSpring = 5; tSummer = 15; tAutumn = -5; tWinter = -30;
                    mSpring = 3; mSummer = 8; mAutumn = 3; mWinter = -5;
                }
                else
                {
                    // Temperate: warm summer, cold winter
                    tSpring = 5; tSummer = 15; tAutumn = 0; tWinter = -15;
                    mSpring = 5; mSummer = -5; mAutumn = 10; mWinter = -5;
                }

                // Storm corridor gets autumn moisture bonus
                if (inStorm)
                {
                    mAutumn += 20;
                    mSummer += 5;
                }

                result.SeasonalProfiles[idx] = new SeasonalProfile
                {
                    TempDeltaSpring   = tSpring,
                    TempDeltaSummer   = tSummer,
                    TempDeltaAutumn   = tAutumn,
                    TempDeltaWinter   = tWinter,
                    MoistureDeltaSpring = mSpring,
                    MoistureDeltaSummer = mSummer,
                    MoistureDeltaAutumn = mAutumn,
                    MoistureDeltaWinter = mWinter,
                };
            }
        }
    }
}
