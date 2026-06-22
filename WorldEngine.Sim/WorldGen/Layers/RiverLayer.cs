namespace WorldEngine.Sim.WorldGen.Layers;

/// <summary>
/// Computes drainage networks using D8 flow direction + Priority Flood sink filling
/// (Barnes 2014 algorithm), then accumulates flow to identify rivers and lakes.
/// Cylinder-aware throughout (X wraps, Y clamped).
/// </summary>
public sealed class RiverLayer : IWorldGenLayer<RiverResult>
{
    // D8 neighbor offsets (8 directions)
    private static readonly (int dx, int dy)[] D8 =
    {
        (-1,-1),(0,-1),(1,-1),
        (-1, 0),       (1, 0),
        (-1, 1),(0, 1),(1, 1)
    };

    public RiverResult Generate(
        WorldGenContext ctx,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        var elev  = ctx.Elevation!;
        var ocean = ctx.Ocean!;
        var cfg   = ctx.SimConfig.WorldGen.Rivers;
        int w = ctx.TileWidth, h = ctx.TileHeight, n = ctx.TileCount;

        // Work with a mutable elevation copy for sink-filling
        float[] fillElev = new float[n];
        for (int i = 0; i < n; i++)
            fillElev[i] = elev.Elevation[i];

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.1f);

        // --- Step 1: Priority Flood sink filling (Barnes 2014) ---
        // Basin tracking: each sink gets a basin ID and tile count
        int[] basinId   = new int[n];
        int[] basinSize = Array.Empty<int>(); // built after flood
        Array.Fill(basinId, -1);

        var isLake = PriorityFloodFill(fillElev, ocean.IsOcean, w, h, ctx, cfg.MinLakeBasinTiles);

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.4f);

        // --- Step 2: D8 flow direction (using filled elevation) ---
        // flowDir[i] = flat index of the tile this tile drains into (-1 if ocean or no lower neighbor)
        int[] flowDir = ComputeFlowDirection(fillElev, ocean.IsOcean, isLake, w, h, ctx);

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.6f);

        // --- Step 3: Flow accumulation via topological sort ---
        int[] flowAcc = ComputeFlowAccumulation(flowDir, ocean.IsOcean, n);

        ct.ThrowIfCancellationRequested();
        progress?.Report(0.8f);

        // --- Step 4: Mark rivers, lakes, and POI candidates ---
        var result = new RiverResult(n);
        int riverThreshold = cfg.FlowAccumulationThreshold;
        int majorThreshold = cfg.MajorRiverThreshold;

        for (int i = 0; i < n; i++)
        {
            if (ocean.IsOcean[i]) continue;
            result.IsLake[i]          = isLake[i];
            result.FlowAccumulation[i] = flowAcc[i];

            if (flowAcc[i] >= riverThreshold)
                result.HasRiver[i] = true;
            if (flowAcc[i] >= majorThreshold)
                result.IsPOICandidate[i] = true;
        }

        progress?.Report(1.0f);
        return result;
    }

    /// <summary>
    /// Priority Flood: fills inland sinks so water has a path to the ocean.
    /// Sinks with basin size &lt; minLakeBasinTiles are force-filled (raised).
    /// Sinks with basin size ≥ threshold are kept as lakes.
    /// Returns a bool[] marking lake tiles.
    /// </summary>
    private static bool[] PriorityFloodFill(
        float[] elev, bool[] isOcean, int w, int h,
        WorldGenContext ctx, int minLakeBasinTiles)
    {
        int n = elev.Length;
        bool[] processed = new bool[n];
        bool[] isLake    = new bool[n];

        // Priority queue: (elevation, tile_index)
        var pq = new PriorityQueue<int, float>(n / 4);

        // Seed the queue with all ocean tiles and boundary tiles
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (isOcean[idx] || y == 0 || y == h - 1)
                {
                    pq.Enqueue(idx, elev[idx]);
                    processed[idx] = true;
                }
            }
        }

        while (pq.Count > 0)
        {
            pq.TryDequeue(out int idx, out float _);
            int x = idx % w, y = idx / w;

            foreach (var (dx, dy) in D8)
            {
                int nx = ((x + dx) % w + w) % w;
                int ny = Math.Clamp(y + dy, 0, h - 1);
                int nIdx = ctx.IndexOf(nx, ny);

                if (processed[nIdx]) continue;
                processed[nIdx] = true;

                // If neighbor is lower than current path, raise it (fill the sink)
                if (elev[nIdx] < elev[idx])
                    elev[nIdx] = elev[idx];

                pq.Enqueue(nIdx, elev[nIdx]);
            }
        }

        // Identify filled sinks (where elevation was raised) as potential lakes
        // DECISION: comparing filled vs original not tracked — instead detect flat areas
        // surrounded by higher terrain using a flood-fill approach.
        // For simplicity in M1: tiles where all D8 neighbors have equal or higher filled elevation
        // and tile is land = flat spot = potential lake. Then threshold by connected component size.
        bool[] flatCandidate = new bool[n];
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (isOcean[idx]) continue;

                bool allHigherOrEqual = true;
                foreach (var (dx, dy) in D8)
                {
                    int nx = ((x + dx) % w + w) % w;
                    int ny = Math.Clamp(y + dy, 0, h - 1);
                    int nIdx = ctx.IndexOf(nx, ny);
                    if (!isOcean[nIdx] && elev[nIdx] < elev[idx])
                    {
                        allHigherOrEqual = false;
                        break;
                    }
                }
                flatCandidate[idx] = allHigherOrEqual;
            }
        }

        // Connected component labeling of flat candidates → determine basin size
        int[] component = new int[n];
        Array.Fill(component, -1);
        var componentSizes = new List<int>();
        var queue = new Queue<int>();

        for (int start = 0; start < n; start++)
        {
            if (!flatCandidate[start] || component[start] >= 0) continue;

            int compId = componentSizes.Count;
            componentSizes.Add(0);
            queue.Clear();
            queue.Enqueue(start);
            component[start] = compId;

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                componentSizes[compId]++;
                int cx = cur % w, cy = cur / w;

                foreach (var (dx, dy) in D8)
                {
                    int nx = ((cx + dx) % w + w) % w;
                    int ny = Math.Clamp(cy + dy, 0, h - 1);
                    int nIdx = ctx.IndexOf(nx, ny);
                    if (flatCandidate[nIdx] && component[nIdx] < 0)
                    {
                        component[nIdx] = compId;
                        queue.Enqueue(nIdx);
                    }
                }
            }
        }

        // Mark large enough basins as lakes
        for (int i = 0; i < n; i++)
        {
            if (component[i] >= 0 && componentSizes[component[i]] >= minLakeBasinTiles)
                isLake[i] = true;
        }

        return isLake;
    }

    /// <summary>
    /// D8 flow direction: each land tile drains to its lowest D8 neighbor.
    /// Returns flat indices of drain targets (-1 for ocean tiles and tiles with no lower neighbor).
    /// </summary>
    private static int[] ComputeFlowDirection(
        float[] fillElev, bool[] isOcean, bool[] isLake,
        int w, int h, WorldGenContext ctx)
    {
        int n = fillElev.Length;
        int[] flowDir = new int[n];
        Array.Fill(flowDir, -1);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = ctx.IndexOf(x, y);
                if (isOcean[idx] || isLake[idx]) continue;

                float minElev = fillElev[idx];
                int bestNeighbor = -1;

                foreach (var (dx, dy) in D8)
                {
                    int nx = ((x + dx) % w + w) % w;
                    int ny = Math.Clamp(y + dy, 0, h - 1);
                    int nIdx = ctx.IndexOf(nx, ny);

                    if (fillElev[nIdx] < minElev)
                    {
                        minElev = fillElev[nIdx];
                        bestNeighbor = nIdx;
                    }
                    else if (isOcean[nIdx] || isLake[nIdx])
                    {
                        // Terminate at ocean/lake regardless of elevation comparison
                        bestNeighbor = nIdx;
                        minElev = float.MinValue; // ensure this neighbor wins
                        break;
                    }
                }

                flowDir[idx] = bestNeighbor;
            }
        }

        return flowDir;
    }

    /// <summary>
    /// Accumulates flow upstream-to-downstream via topological sort (Kahn's algorithm).
    /// Each land tile starts with flow = 1. Each tile adds its flow to its downstream neighbor.
    /// </summary>
    private static int[] ComputeFlowAccumulation(int[] flowDir, bool[] isOcean, int n)
    {
        int[] inDegree = new int[n];
        int[] flowAcc  = new int[n];
        Array.Fill(flowAcc, 1);

        // Count in-degrees (how many tiles drain INTO each tile)
        for (int i = 0; i < n; i++)
        {
            if (isOcean[i]) continue;
            if (flowDir[i] >= 0)
                inDegree[flowDir[i]]++;
        }

        // Start with tiles that have no upstream contributors
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (!isOcean[i] && inDegree[i] == 0)
                queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int downstream = flowDir[cur];
            if (downstream < 0 || isOcean[downstream]) continue;

            flowAcc[downstream] += flowAcc[cur];
            inDegree[downstream]--;
            if (inDegree[downstream] == 0)
                queue.Enqueue(downstream);
        }

        return flowAcc;
    }
}
