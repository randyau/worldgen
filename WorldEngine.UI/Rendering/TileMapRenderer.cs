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
            var rect = new Rectangle(
                (int)screenPos.X, (int)screenPos.Y,
                (int)camera.Zoom, (int)camera.Zoom);

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
    }

    public void Dispose() => _pixel.Dispose();

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}
