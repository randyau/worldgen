using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.Rendering;

public sealed class TileMapRenderer(GraphicsDevice gd, Camera2D camera)
{
    private readonly Texture2D _pixel = CreatePixel(gd);

    public void Draw(SpriteBatch sb, WorldSnapshot snapshot)
    {
        bool drawBorder = camera.Zoom > 4f;

        foreach (var (coord, tile) in snapshot.VisibleTiles)
        {
            var screenPos = camera.TileToScreen(coord);
            // Round both edges independently so tiles pack without gaps.
            // At non-integer zoom, each tile is either floor(zoom) or ceil(zoom) px wide,
            // distributed evenly — prevents the irregular moiré from double-flooring.
            int x0 = (int)MathF.Round(screenPos.X);
            int y0 = (int)MathF.Round(screenPos.Y);
            int x1 = (int)MathF.Round(screenPos.X + camera.Zoom);
            int y1 = (int)MathF.Round(screenPos.Y + camera.Zoom);
            var rect = new Rectangle(x0, y0, x1 - x0, y1 - y0);

            var color = OverlayRenderer.GetColor(tile, snapshot.ActiveOverlay);
            sb.Draw(_pixel, rect, color);

            if (drawBorder)
            {
                var borderColor = Color.Black * 0.3f;
                // Top edge
                sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), borderColor);
                // Left edge
                sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), borderColor);
            }
        }

        // Draw selection highlight over the inspected tile
        if (snapshot.InspectedTile?.Coord is TileCoord sel)
        {
            var pos = camera.TileToScreen(sel);
            int x0s = (int)MathF.Round(pos.X);
            int y0s = (int)MathF.Round(pos.Y);
            int x1s = (int)MathF.Round(pos.X + camera.Zoom);
            int y1s = (int)MathF.Round(pos.Y + camera.Zoom);
            int w = x1s - x0s, h = y1s - y0s;
            var hi = Color.Yellow;
            const int B = 2;
            sb.Draw(_pixel, new Rectangle(x0s,      y0s,      w, B),    hi); // top
            sb.Draw(_pixel, new Rectangle(x0s,      y1s - B,  w, B),    hi); // bottom
            sb.Draw(_pixel, new Rectangle(x0s,      y0s,      B, h),    hi); // left
            sb.Draw(_pixel, new Rectangle(x1s - B,  y0s,      B, h),    hi); // right
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
