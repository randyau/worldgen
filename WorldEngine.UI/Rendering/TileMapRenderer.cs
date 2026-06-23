using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;

namespace WorldEngine.UI.Rendering;

public sealed class TileMapRenderer(GraphicsDevice gd, Camera2D camera)
{
    private readonly Texture2D _pixel = CreatePixel(gd);

    // Must match Game1.SidebarWidth — tiles must not render into the sidebar area
    private const int SidebarWidth = 360;

    public void Draw(SpriteBatch sb, WorldSnapshot snapshot)
    {
        bool drawBorder = camera.Zoom > 4f;
        int tw = snapshot.WorldTileWidth, th = snapshot.WorldTileHeight;

        // Compute visible tile range — clip right edge to map area, excluding sidebar
        int screenW   = sb.GraphicsDevice.Viewport.Width;
        int screenH   = sb.GraphicsDevice.Viewport.Height;
        int mapWidth  = screenW - SidebarWidth;
        var tl = camera.ScreenToTile(Vector2.Zero);
        var br = camera.ScreenToTile(new Vector2(mapWidth, screenH));  // stop at sidebar boundary
        int minX = tl.X - 1;
        int minY = Math.Max(0, tl.Y - 1);
        int maxX = br.X + 1;
        int maxY = Math.Min(th - 1, br.Y + 1);

        for (int ty = minY; ty <= maxY; ty++)
        {
            for (int tx = minX; tx <= maxX; tx++)
            {
                int wx  = ((tx % tw) + tw) % tw;
                int idx = ty * tw + wx;
                if ((uint)idx >= (uint)snapshot.AllTiles.Length) continue;

                var coord     = new TileCoord(wx, ty);
                var tile      = snapshot.AllTiles[idx];
                var screenPos = camera.TileToScreen(coord);

                // Round both edges independently so tiles pack without gaps.
                int x0 = (int)MathF.Round(screenPos.X);
                int y0 = (int)MathF.Round(screenPos.Y);
                int x1 = (int)MathF.Round(screenPos.X + camera.Zoom);
                int y1 = (int)MathF.Round(screenPos.Y + camera.Zoom);
                var rect = new Rectangle(x0, y0, x1 - x0, y1 - y0);

                sb.Draw(_pixel, rect, OverlayRenderer.GetColor(tile, snapshot.ActiveOverlay));

                if (drawBorder)
                {
                    var borderColor = Color.Black * 0.3f;
                    sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), borderColor);
                    sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), borderColor);
                }
            }
        }

        // Draw beast markers — a small dot centered on each occupied tile
        if (camera.Zoom >= 4f)
            DrawBeastMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw character markers — small blue square at zoom ≥ 2
        if (camera.Zoom >= 2f)
            DrawCharacterMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw ruin markers — always visible regardless of zoom
        DrawRuinMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw settlement markers — always visible regardless of zoom
        DrawSettlementMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

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

    private void DrawBeastMarkers(
        SpriteBatch sb, WorldSnapshot snapshot,
        int tw, int th, int minX, int maxX, int minY, int maxY)
    {
        // Build a tile → beast presence lookup from EntitySnapshots
        // Legendary (or mythological) = gold dot; normal = dark-red dot
        var legendary = new HashSet<TileCoord>();
        var normal    = new HashSet<TileCoord>();

        foreach (var kvp in snapshot.EntitySnapshots)
        {
            var snap = kvp.Value;
            if (!snap.IsAlive) continue;
            if (snap.Kind != EntityKind.LegendaryBeast) continue;
            if (snap.IsLegendary) legendary.Add(snap.Location);
            else normal.Add(snap.Location);
        }

        int dotSize = Math.Max(2, (int)(camera.Zoom * 0.3f));
        int half    = dotSize / 2;

        void DrawDot(TileCoord coord, Color color)
        {
            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom / 2f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom / 2f);
            sb.Draw(_pixel, new Rectangle(cx - half, cy - half, dotSize, dotSize), color);
        }

        var goldColor    = Color.Gold;
        var darkRedColor = new Color(160, 30, 30);

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx  = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (legendary.Contains(coord)) DrawDot(coord, goldColor);
            else if (normal.Contains(coord)) DrawDot(coord, darkRedColor);
        }
    }

    private void DrawCharacterMarkers(
        SpriteBatch sb, WorldSnapshot snapshot,
        int tw, int th, int minX, int maxX, int minY, int maxY)
    {
        var charTiles = new HashSet<TileCoord>();
        foreach (var kvp in snapshot.EntitySnapshots)
        {
            var snap = kvp.Value;
            if (!snap.IsAlive) continue;
            if (snap.Kind == EntityKind.Tier1Character)
                charTiles.Add(snap.Location);
        }

        int dotSize = Math.Max(2, (int)(camera.Zoom * 0.25f));
        int half    = dotSize / 2;
        var blue    = new Color(60, 100, 220);

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!charTiles.Contains(coord)) continue;
            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.2f); // top of tile
            sb.Draw(_pixel, new Rectangle(cx - half, cy - half, dotSize, dotSize), blue);
        }
    }

    private void DrawSettlementMarkers(
        SpriteBatch sb, WorldSnapshot snapshot,
        int tw, int th, int minX, int maxX, int minY, int maxY)
    {
        if (snapshot.Settlements.Count == 0) return;

        // Scale marker with zoom but keep it readable at all zoom levels
        int markerSize = Math.Max(3, (int)(camera.Zoom * 0.4f));
        int half       = markerSize / 2;

        // White fill with dark border — stands out on any biome color
        var fill   = Color.White;
        var border = new Color(30, 20, 10);

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!snapshot.Settlements.TryGetValue(coord, out var s)) continue;

            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.5f);

            // Tint red if settlement is in poor health
            if (s.Health < 40) fill = new Color(220, 80, 60);
            else if (s.Health < 70) fill = new Color(230, 200, 80);
            else fill = Color.White;

            // Border then fill (border drawn as slightly larger rect)
            sb.Draw(_pixel, new Rectangle(cx - half - 1, cy - half - 1, markerSize + 2, markerSize + 2), border);
            sb.Draw(_pixel, new Rectangle(cx - half, cy - half, markerSize, markerSize), fill);
        }
    }

    private void DrawRuinMarkers(
        SpriteBatch sb, WorldSnapshot snapshot, int tw, int th,
        int minX, int maxX, int minY, int maxY)
    {
        if (snapshot.Ruins.Count == 0) return;

        int markerSize = Math.Max(2, (int)(camera.Zoom * 0.25f));
        int half       = markerSize / 2;
        var ruinColor  = new Color(110, 80, 50); // muted brown

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            // Don't show ruin marker when an active settlement is present
            if (!snapshot.Ruins.ContainsKey(coord) || snapshot.Settlements.ContainsKey(coord)) continue;

            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.5f);
            sb.Draw(_pixel, new Rectangle(cx - half, cy - half, markerSize, markerSize), ruinColor);
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
