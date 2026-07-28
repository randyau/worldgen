using WorldEngine.Sim.Config;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Deterministic local-chunk terrain generator: a pure function of (worldSeed, ChunkCoord, parent
/// TileData, parent BorderManifest, LocalGenConfig) — same inputs always produce the same chunk, so
/// nothing here is persisted (see docs/phases/m11_local_scale_generation.md). Elevation blends from
/// the parent tile's own byte value toward the shared BorderManifest edge sample within
/// EdgeBlendBandTiles of each world-tile edge, then adds FastNoiseLite detail sampled in absolute
/// local-tile coordinates so the detail layer is automatically continuous across chunk/tile
/// boundaries (same seed + same absolute coordinate = same noise value regardless of which chunk is
/// generating).
/// </summary>
public static class LocalTerrainAmplifier
{
    public static LocalChunk Amplify(
        ChunkCoord coord, TileData parentTile, BorderManifest manifest, int worldSeed, LocalGenConfig config)
    {
        var chunk = new LocalChunk(coord, config.ChunkSizeTiles);

        var noise = new FastNoiseLite(worldSeed ^ LayerSeeds.LocalTerrain);
        noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        noise.SetFrequency(config.NoiseFrequency);
        noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        noise.SetFractalOctaves(config.NoiseOctaves);

        int n = config.LocalTilesPerWorldTileEdge;
        int band = config.EdgeBlendBandTiles;

        for (byte ly = 0; ly < config.ChunkSizeTiles; ly++)
        {
            for (byte lx = 0; lx < config.ChunkSizeTiles; lx++)
            {
                var local = new LocalTileCoord(lx, ly);
                var (absX, absY) = LocalCoordMath.ToAbsolute(coord, local, n, config.ChunkSizeTiles);

                int withinTileX = (int)(absX - (long)coord.WorldTile.X * n);
                int withinTileY = (int)(absY - (long)coord.WorldTile.Y * n);

                byte macroElevation = BlendElevation(withinTileX, withinTileY, n, band, parentTile.Elevation, manifest);

                float noiseVal = noise.GetNoise(absX, absY); // [-1, 1]
                int amplified = macroElevation + (int)MathF.Round(noiseVal * config.NoiseAmplitude);

                chunk.SetTile(local, new LocalTileData
                {
                    Elevation = (byte)Math.Clamp(amplified, 0, 255),
                    BiomeType = parentTile.BiomeType,
                    DecorationType = 0,
                    Flags = 0,
                });
            }
        }

        return chunk;
    }

    /// <summary>
    /// Blends toward whichever edge (of the up to two axes) has the larger blend weight, breaking
    /// ties in favor of the X axis. Ties only occur when both axis weights are 1.0 — i.e. exactly on
    /// a world-tile edge — so the entire shared East/West boundary column resolves consistently to
    /// the (identical, per BorderManifestBuilder) East/West manifest sample on both adjacent tiles.
    /// The North/South boundary is continuous everywhere except its two extreme corner columns,
    /// which are also on an East/West boundary shared with a third (diagonal) tile — an inherent
    /// ambiguity with only two adjacent manifests to blend from, not solved here.
    /// </summary>
    private static byte BlendElevation(int wx, int wy, int n, int band, byte center, BorderManifest manifest)
    {
        float weightX = 0f;
        byte edgeValX = 0;
        int distWest = wx;
        int distEast = (n - 1) - wx;
        if (distWest <= distEast && distWest < band)
        {
            weightX = 1f - (float)distWest / band;
            edgeValX = SampleEdge(manifest.West, wy, n);
        }
        else if (distEast < band)
        {
            weightX = 1f - (float)distEast / band;
            edgeValX = SampleEdge(manifest.East, wy, n);
        }

        float weightY = 0f;
        byte edgeValY = 0;
        int distNorth = wy;
        int distSouth = (n - 1) - wy;
        if (distNorth <= distSouth && distNorth < band)
        {
            weightY = 1f - (float)distNorth / band;
            edgeValY = SampleEdge(manifest.North, wx, n);
        }
        else if (distSouth < band)
        {
            weightY = 1f - (float)distSouth / band;
            edgeValY = SampleEdge(manifest.South, wx, n);
        }

        if (weightX <= 0f && weightY <= 0f)
            return center;

        var (edgeVal, weight) = weightX >= weightY ? (edgeValX, weightX) : (edgeValY, weightY);
        float blended = center + (edgeVal - center) * weight;
        return (byte)Math.Clamp((int)MathF.Round(blended), 0, 255);
    }

    private static byte SampleEdge(BorderManifestSample[] samples, int posAlongEdge, int n)
    {
        int idx = Math.Clamp(posAlongEdge * BorderManifest.SampleCount / n, 0, BorderManifest.SampleCount - 1);
        return samples[idx].Elevation;
    }
}
