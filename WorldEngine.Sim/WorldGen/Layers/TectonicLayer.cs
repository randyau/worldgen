using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Generates tectonic plate assignments, fault lines, and volcanic zones.
/// Algorithm: Poisson disc plate center sampling → cylinder-aware Voronoi → subduction detection.
/// </summary>
public sealed class TectonicLayer : IWorldGenLayer<TectonicResult>
{
    // Salt offsets for distinct WorldRng calls within this layer
    private const int SaltPlateType   = 0;
    private const int SaltPoisson     = 1;
    private const int SaltDeposit     = 2;

    public TectonicResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        int seed = ctx.Config.Seed ^ LayerSeeds.Tectonic;
        int w = ctx.TileWidth, h = ctx.TileHeight;
        int plateCount = ctx.SimConfig.WorldGen.Tectonics.PlateCount;
        float minSepFraction = ctx.SimConfig.WorldGen.Tectonics.MinPlateSeparationFraction;
        float contFraction = ctx.SimConfig.WorldGen.Tectonics.ContinentalPlateFraction;

        float minSep = minSepFraction * Math.Min(w, h);

        // --- Step 1: Poisson disc sampling for plate centers ---
        var centers = SamplePlateCenters(seed, w, h, plateCount, minSep);

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.2f);

        // --- Step 2: Assign plate types (continental / oceanic) ---
        // Use a shuffle-based assignment to guarantee exactly round(plateCount * fraction) continental
        // plates, so that tile fraction stays close to the configured value regardless of RNG seed.
        int continentalCount = (int)Math.Round(plateCount * contFraction);
        bool[] isContinental = new bool[plateCount];
        int[] plateOrder = Enumerable.Range(0, plateCount)
            .OrderBy(p => WorldRng.FloatAt(seed, 0, p, 0, SaltPlateType))
            .ToArray();
        for (int i = 0; i < continentalCount; i++)
            isContinental[plateOrder[i]] = true;

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.4f);

        // --- Step 3: Voronoi assignment (cylinder-aware nearest center) ---
        var result = new TectonicResult(ctx.TileCount);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int nearest = NearestCenter(x, y, centers, w);
                int idx = ctx.IndexOf(x, y);
                result.PlateId[idx]           = (byte)nearest;
                result.IsContinentalTile[idx] = isContinental[nearest];
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.6f);

        // --- Step 4: Fault line detection (4-neighbor check) ---
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                byte pid = result.PlateId[idx];

                bool fault =
                    (y > 0     && result.PlateId[ctx.IndexOf(x, y - 1)]        != pid) ||
                    (y < h - 1 && result.PlateId[ctx.IndexOf(x, y + 1)]        != pid) ||
                    (result.PlateId[ctx.IndexOf((x + 1) % w, y)]               != pid) ||
                    (result.PlateId[ctx.IndexOf((x - 1 + w) % w, y)]           != pid);

                result.IsFaultLine[idx] = fault;
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.8f);

        // --- Steps 5 & 6: Volcanic zones and deposit potential ---
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (!result.IsFaultLine[idx]) continue;

                bool tileIsOceanic = !result.IsContinentalTile[idx];
                byte pid = result.PlateId[idx];

                // Check 4 neighbors for subduction (oceanic tile adjacent to continental different plate)
                int[] nx = { (x + 1) % w, (x - 1 + w) % w, x, x };
                int[] ny = { y, y, y > 0 ? y - 1 : y, y < h - 1 ? y + 1 : y };

                bool hasContNeighbor = false;
                for (int n = 0; n < 4; n++)
                {
                    int nIdx = ctx.IndexOf(nx[n], ny[n]);
                    if (result.PlateId[nIdx] != pid && result.IsContinentalTile[nIdx])
                    {
                        hasContNeighbor = true;
                        break;
                    }
                }

                // Volcanic: oceanic side of a subduction boundary
                if (tileIsOceanic && hasContNeighbor)
                    result.IsVolcanic[idx] = true;

                // Deposit potential: continental fault tiles
                if (result.IsContinentalTile[idx])
                {
                    result.DepositPotential[idx] =
                        WorldRng.FloatAt(seed, 0, x, y, SaltDeposit);
                }
            }
        }

        progress?.Report(1.0f);
        return result;
    }

    /// <summary>
    /// Perturbed-grid plate center placement. Divides the world into a grid of roughly
    /// plateCount cells and places one center per cell with a random offset. This gives
    /// more uniform Voronoi cell sizes than pure Poisson disc at low plate counts, which
    /// is needed for the continental fraction test to be reliable within ±10%.
    /// </summary>
    private static (int X, int Y)[] SamplePlateCenters(
        int seed, int w, int h, int plateCount, float minSep)
    {
        // Compute grid dimensions proportional to world aspect ratio
        float aspect = (float)w / h;
        int gridW = Math.Max(1, (int)Math.Round(Math.Sqrt(plateCount * aspect)));
        int gridH = Math.Max(1, (int)Math.Ceiling((double)plateCount / gridW));

        float cellW = (float)w / gridW;
        float cellH = (float)h / gridH;
        // Perturbation radius: 35% of the smaller cell dimension
        float maxOffset = Math.Min(cellW, cellH) * 0.35f;

        var centers = new List<(int X, int Y)>(plateCount);
        for (int gy = 0; gy < gridH && centers.Count < plateCount; gy++)
        {
            for (int gx = 0; gx < gridW && centers.Count < plateCount; gx++)
            {
                float cx = (gx + 0.5f) * cellW;
                float cy = (gy + 0.5f) * cellH;

                // Independent offsets for X and Y using different salts
                float ox = (WorldRng.FloatAt(seed, 0, gx, gy, SaltPoisson)     - 0.5f) * 2f * maxOffset;
                float oy = (WorldRng.FloatAt(seed, 0, gx, gy, SaltPoisson + 1) - 0.5f) * 2f * maxOffset;

                int x = ((int)(cx + ox) % w + w) % w;
                int y = Math.Clamp((int)(cy + oy), 0, h - 1);
                centers.Add((x, y));
            }
        }

        return centers.Take(plateCount).ToArray();
    }

    private static float CylDistSq(int ax, int ay, int bx, int by, int w)
    {
        int dxRaw = Math.Abs(ax - bx);
        int dx = Math.Min(dxRaw, w - dxRaw);
        int dy = ay - by;
        return dx * dx + dy * dy;
    }

    private static int NearestCenter(int x, int y, (int X, int Y)[] centers, int w)
    {
        int nearest = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < centers.Length; i++)
        {
            float d = CylDistSq(x, y, centers[i].X, centers[i].Y, w);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = i;
            }
        }
        return nearest;
    }
}
