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

                float shade = 0.7f + tile.Elevation / 255f * 0.5f;
                color = new Color(
                    (byte)Math.Clamp(color.R * shade, 0, 255),
                    (byte)Math.Clamp(color.G * shade, 0, 255),
                    (byte)Math.Clamp(color.B * shade, 0, 255));

                if (((LocalTileFlags)tile.Flags & LocalTileFlags.River) != 0)
                    color = new Color(40, 90, 200);

                sb.Draw(_pixel, rect, color);
            }
        }
    }

    public void Dispose() => _pixel.Dispose();

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}
