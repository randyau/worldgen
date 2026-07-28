using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.Tiles.LocalScale;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Carves a river channel through an already-generated <see cref="LocalChunk"/> (post-process
/// pass after <see cref="LocalTerrainAmplifier.Amplify"/>), connecting the tile's river boundary
/// crossing(s) recovered from its <see cref="BorderManifest"/>. Pure function of (ChunkCoord,
/// parent TileData, parent BorderManifest, LocalGenConfig) — deterministic and not persisted,
/// same rationale as <see cref="LocalTerrainAmplifier"/>.
///
/// A crossing's position/width is recovered directly from the manifest edge's
/// <see cref="BorderManifestSample.HasRiverCrossing"/> run, not from the raw
/// <see cref="RiverCrossing"/> record — <see cref="BorderManifestBuilder"/> stamps both sides of a
/// shared edge from the exact same crossing, so both adjacent tiles recover byte-identical
/// position/width, matching per the phase requirement without needing to re-derive or share state.
/// </summary>
public static class LocalRiverThreader
{
    public static void Thread(
        LocalChunk chunk, ChunkCoord coord, TileData parentTile, BorderManifest manifest, LocalGenConfig config)
    {
        if ((parentTile.StaticFlags & TileStaticFlags.HasRiver) == 0)
            return;

        int n = config.LocalTilesPerWorldTileEdge;
        var anchors = new List<(double X, double Y, double WidthTiles)>();
        TryAddAnchor(manifest.North, EdgeDirection.North, coord.WorldTile, n, anchors);
        TryAddAnchor(manifest.South, EdgeDirection.South, coord.WorldTile, n, anchors);
        TryAddAnchor(manifest.East,  EdgeDirection.East,  coord.WorldTile, n, anchors);
        TryAddAnchor(manifest.West,  EdgeDirection.West,  coord.WorldTile, n, anchors);

        // DECISION: a tile's river touches its boundary at zero (interior-only segment, e.g. a
        // lake-fed source) or 3+ (rare multi-branch) edges are not fully modeled this phase — zero
        // is a no-op (nothing to anchor a path to), 3+ connects only the first two found (N/S/E/W
        // scan order), matching the "simplest that passes the continuity requirement" approach 11.3
        // used for its own edge-case corners.
        if (anchors.Count == 0)
            return;

        (double X, double Y, double WidthTiles) start, end;
        if (anchors.Count == 1)
        {
            start = anchors[0];
            double centerX = (double)coord.WorldTile.X * n + n / 2.0;
            double centerY = (double)coord.WorldTile.Y * n + n / 2.0;
            end = (centerX, centerY, config.RiverSourceWidthTiles);
        }
        else
        {
            start = anchors[0];
            end = anchors[1];
        }

        for (byte ly = 0; ly < chunk.Size; ly++)
        {
            for (byte lx = 0; lx < chunk.Size; lx++)
            {
                var local = new LocalTileCoord(lx, ly);
                var (absX, absY) = LocalCoordMath.ToAbsolute(coord, local, n, config.ChunkSizeTiles);

                double t = ProjectOntoSegment(absX, absY, start.X, start.Y, end.X, end.Y);
                double segX = start.X + (end.X - start.X) * t;
                double segY = start.Y + (end.Y - start.Y) * t;
                double dist = Math.Sqrt((absX - segX) * (absX - segX) + (absY - segY) * (absY - segY));
                double width = start.WidthTiles + (end.WidthTiles - start.WidthTiles) * t;

                if (dist <= width / 2.0)
                {
                    ref var tile = ref chunk.GetTileRef(local);
                    tile.Flags = (byte)(tile.Flags | (byte)LocalTileFlags.River);
                    tile.Elevation = (byte)Math.Max(0, tile.Elevation - config.RiverChannelDepth);
                }
            }
        }
    }

    /// <summary>
    /// Recovers a river crossing's position/width from the contiguous run of
    /// HasRiverCrossing-marked samples on one manifest edge, if any, as an absolute-local-tile
    /// anchor point. Returns nothing (adds no anchor) if the edge carries no crossing.
    /// </summary>
    /// <remarks>Internal (not private) so continuity tests can verify anchor recovery in isolation.</remarks>
    internal static void TryAddAnchor(
        BorderManifestSample[] samples, EdgeDirection edge, TileCoord worldTile, int n,
        List<(double X, double Y, double WidthTiles)> anchors)
    {
        int lo = -1, hi = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            if (samples[i].HasRiverCrossing == 0) continue;
            if (lo < 0) lo = i;
            hi = i;
        }
        if (lo < 0) return;

        double posAlongEdge = ((lo + hi) / 2.0 + 0.5) / BorderManifest.SampleCount;
        double widthFraction = (hi - lo + 1) / (double)BorderManifest.SampleCount;
        double widthTiles = widthFraction * n;

        long tileOriginX = (long)worldTile.X * n;
        long tileOriginY = (long)worldTile.Y * n;

        (double X, double Y) point = edge switch
        {
            EdgeDirection.North => (tileOriginX + posAlongEdge * n, tileOriginY),
            EdgeDirection.South => (tileOriginX + posAlongEdge * n, tileOriginY + n - 1),
            EdgeDirection.West  => (tileOriginX, tileOriginY + posAlongEdge * n),
            EdgeDirection.East  => (tileOriginX + n - 1, tileOriginY + posAlongEdge * n),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };

        anchors.Add((point.X, point.Y, widthTiles));
    }

    private static double ProjectOntoSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double abx = bx - ax, aby = by - ay;
        double lenSq = abx * abx + aby * aby;
        if (lenSq < 1e-9) return 0.0;
        double t = ((px - ax) * abx + (py - ay) * aby) / lenSq;
        return Math.Clamp(t, 0.0, 1.0);
    }
}
