using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles.LocalScale;

namespace WorldEngine.UI.Rendering;

// MAP: M11 11.7 — draws already-generated LocalChunks via a LocalCamera2D. Same solid-pixel-rect
// approach as TileMapRenderer, colored by biome (OverlayRenderer.GetBiomeColor, shared palette)
// with simple elevation shading and a river tint — local view has no overlay-type switcher yet.
public sealed class LocalTileMapRenderer(GraphicsDevice gd, LocalCamera2D camera)
{
    private readonly Texture2D _pixel = CreatePixel(gd);

    public void Draw(SpriteBatch sb, IReadOnlyDictionary<ChunkCoord, LocalChunk> chunks, int chunkSizeTiles, int viewportW, int viewportH)
    {
        var (minX, minY, maxX, maxY) = camera.GetVisibleTileBounds(viewportW, viewportH);

        foreach (var (coord, chunk) in chunks)
        {
            int originX = coord.ChunkX * chunkSizeTiles;
            int originY = coord.ChunkY * chunkSizeTiles;
            if (originX + chunkSizeTiles < minX || originX > maxX) continue;
            if (originY + chunkSizeTiles < minY || originY > maxY) continue;

            foreach (var (local, tile) in chunk.AllTiles())
            {
                int wx = originX + local.X;
                int wy = originY + local.Y;
                if (wx < minX || wx > maxX || wy < minY || wy > maxY) continue;

                var screenPos = camera.LocalTileToScreen(wx, wy);
                int x0 = (int)MathF.Round(screenPos.X);
                int y0 = (int)MathF.Round(screenPos.Y);
                int x1 = (int)MathF.Round(screenPos.X + camera.Zoom);
                int y1 = (int)MathF.Round(screenPos.Y + camera.Zoom);
                var rect = new Rectangle(x0, y0, x1 - x0, y1 - y0);

                var color = OverlayRenderer.GetBiomeColor((BiomeType)tile.BiomeType);

                // Slope-based relief shading: contrasts neighbor elevation deltas rather than the
                // cell's own absolute elevation, so even a few bytes of noise (NoiseAmplitude is a
                // small physical constant, not a visual-contrast knob) reads as visible relief
                // instead of a flat wash.
                float shade = 1f + Slope(chunk, local) * 0.6f;
                color = new Color(
                    (byte)Math.Clamp(color.R * shade, 0, 255),
                    (byte)Math.Clamp(color.G * shade, 0, 255),
                    (byte)Math.Clamp(color.B * shade, 0, 255));

                if (((LocalTileFlags)tile.Flags & LocalTileFlags.River) != 0)
                    color = new Color(40, 90, 200);

                sb.Draw(_pixel, rect, color);

                var decoColor = DecorationColor((LocalDecorationType)tile.DecorationType);
                if (decoColor is { } dc)
                {
                    int inset = Math.Max(1, (int)MathF.Round(camera.Zoom * 0.2f));
                    var decoRect = new Rectangle(rect.X + inset, rect.Y + inset,
                        Math.Max(1, rect.Width - inset * 2), Math.Max(1, rect.Height - inset * 2));
                    sb.Draw(_pixel, decoRect, dc);
                }
            }
        }
    }

    /// <summary>
    /// Normalized (roughly [-1,1]) elevation gradient toward the cell's south-east neighbors,
    /// edge-clamped. A cell higher than its neighbors reads brighter ("catching light"); lower
    /// reads darker — reveals relief from small elevation deltas that an absolute-elevation
    /// greyscale would render as visually flat.
    /// </summary>
    private static float Slope(LocalChunk chunk, LocalTileCoord local)
    {
        int size = chunk.Size;
        var here  = chunk.GetTile(local);
        var right = chunk.GetTile(new LocalTileCoord((byte)Math.Min(local.X + 1, size - 1), local.Y));
        var down  = chunk.GetTile(new LocalTileCoord(local.X, (byte)Math.Min(local.Y + 1, size - 1)));

        int dRight = here.Elevation - right.Elevation;
        int dDown  = here.Elevation - down.Elevation;
        return Math.Clamp((dRight + dDown) / 16f, -1f, 1f);
    }

    private static Color? DecorationColor(LocalDecorationType deco) => deco switch
    {
        LocalDecorationType.TreeStand       => new Color(20, 90, 35),
        LocalDecorationType.RockOutcropping => new Color(120, 115, 110),
        LocalDecorationType.Shrub           => new Color(110, 140, 60),
        LocalDecorationType.Wetland         => new Color(50, 95, 90),
        LocalDecorationType.SandDune        => new Color(215, 190, 130),
        _ => null,
    };

    public void Dispose() => _pixel.Dispose();

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}
