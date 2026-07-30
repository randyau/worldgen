using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.World;
using WorldEngine.UI.UI.Theme;

namespace WorldEngine.UI.Rendering;

// MAP: Draws tiles + entity/settlement/ruin markers; M3.4: territory civ-color tint + improvement icons.
public sealed class TileMapRenderer(GraphicsDevice gd, Camera2D camera)
{
    private readonly Texture2D _pixel = CreatePixel(gd);

    // Must match Game1.SidebarWidth — tiles must not render into the sidebar area
    private const int SidebarWidth = 360;

    public void Draw(SpriteBatch sb, WorldSnapshot snapshot)
    {
        bool drawBorder    = camera.Zoom > 4f;
        bool territoryMode = snapshot.ActiveOverlay == OverlayType.Territory;
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

                // Territory overlay: apply semi-transparent civ-color tint + border outline
                if (territoryMode && snapshot.TerritoryMap.TryGetValue(coord, out var terr))
                {
                    var civColor = UiTheme.CivColor(terr.CivId);
                    // City tile itself gets full brightness tint; other territory gets alpha tint
                    bool isCity = snapshot.Settlements.ContainsKey(coord);
                    float alpha = isCity ? 0.65f : 0.35f;
                    sb.Draw(_pixel, rect, civColor * alpha);

                    // Draw civ-colored border on edges that touch a different civ or unclaimed land.
                    // Border width scales with zoom so it stays visually consistent.
                    int b = Math.Max(1, (int)MathF.Round(camera.Zoom * 0.12f));
                    var border = civColor * 0.92f;

                    int lx = ((wx - 1) % tw + tw) % tw;
                    int rx = (wx + 1) % tw;
                    if (!snapshot.TerritoryMap.TryGetValue(new TileCoord(lx, ty),  out var lt) || lt.CivId != terr.CivId)
                        sb.Draw(_pixel, new Rectangle(rect.X,          rect.Y, b, rect.Height), border);
                    if (!snapshot.TerritoryMap.TryGetValue(new TileCoord(rx, ty),  out var rt) || rt.CivId != terr.CivId)
                        sb.Draw(_pixel, new Rectangle(rect.Right - b,  rect.Y, b, rect.Height), border);
                    if (ty == 0 || !snapshot.TerritoryMap.TryGetValue(new TileCoord(wx, ty - 1), out var tt) || tt.CivId != terr.CivId)
                        sb.Draw(_pixel, new Rectangle(rect.X, rect.Y,           rect.Width, b), border);
                    if (ty == th - 1 || !snapshot.TerritoryMap.TryGetValue(new TileCoord(wx, ty + 1), out var bt) || bt.CivId != terr.CivId)
                        sb.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - b,  rect.Width, b), border);
                }

                if (drawBorder)
                {
                    var borderColor = Color.Black * 0.3f;
                    sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), borderColor);
                    sb.Draw(_pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), borderColor);
                }
            }
        }

        // Territory mode: draw improvement icons on top of tiles (zoom ≥ 2)
        if (territoryMode && camera.Zoom >= 2f)
            DrawImprovementIcons(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw beast markers — a small dot centered on each occupied tile
        if (camera.Zoom >= 4f)
            DrawBeastMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw character markers — small blue square at zoom ≥ 2
        if (camera.Zoom >= 2f)
            DrawCharacterMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw ruin markers — always visible regardless of zoom
        DrawRuinMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY);

        // Draw settlement markers — always visible regardless of zoom
        DrawSettlementMarkers(sb, snapshot, tw, th, minX, maxX, minY, maxY, territoryMode);

        // Draw spotlight marker — always visible regardless of zoom, distinct from the yellow
        // tile-inspector outline (that one marks *what you clicked*, this marks *who you're playing*).
        if (snapshot.SpotlightCharacterId is { } spotlightId &&
            snapshot.EntitySnapshots.TryGetValue(spotlightId, out var spotlightSnap) &&
            spotlightSnap.IsAlive)
        {
            DrawSpotlightMarker(sb, spotlightSnap.Location);
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

    // Civ color derivation now lives in UiTheme.CivColor so panels and the territory
    // overlay share one deterministic hue mapping (M6 Epic 6.2.1).

    /// <summary>
    /// Draws a small colored glyph in the top-left corner of each tile that has an improvement.
    /// Icons are pure-pixel art: Farm=green square, Mine=gray, LoggingCamp=brown, Pasture=light-green, Fishery=blue.
    /// </summary>
    private void DrawImprovementIcons(
        SpriteBatch sb, WorldSnapshot snapshot,
        int tw, int th, int minX, int maxX, int minY, int maxY)
    {
        int iconSize = MarkerPx(0.18f, 2);

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!snapshot.ImprovementMap.TryGetValue(coord, out var imp)) continue;

            var pos = camera.TileToScreen(coord);
            int ix  = (int)MathF.Round(pos.X) + 1;
            int iy  = (int)MathF.Round(pos.Y) + 1;
            var color = ImprovementColor(imp.ImprovementType);
            sb.Draw(_pixel, new Rectangle(ix, iy, iconSize, iconSize), color);
        }
    }

    // ── Marker size helpers — single source of truth for zoom-scaled pixel art ──────────────────

    /// <summary>Square marker size in pixels at the current zoom level.</summary>
    private int MarkerPx(float factor = 0.35f, int minPx = 3) =>
        Math.Max(minPx, (int)(camera.Zoom * factor));

    /// <summary>Draws a cross/+ shape centered at (cx,cy) — used for character markers.</summary>
    private void DrawCross(SpriteBatch sb, int cx, int cy, int arm, int thick, Color color)
    {
        // Horizontal bar
        sb.Draw(_pixel, new Rectangle(cx - arm, cy - thick / 2, arm * 2, thick), color);
        // Vertical bar
        sb.Draw(_pixel, new Rectangle(cx - thick / 2, cy - arm, thick, arm * 2), color);
    }

    /// <summary>Draws an × shape by overlapping two thin diagonal approximations.</summary>
    private void DrawX(SpriteBatch sb, int cx, int cy, int half, Color color)
    {
        // Diagonal approximation: draw 3 dots along each diagonal
        int t = Math.Max(1, half / 3);
        for (int i = -1; i <= 1; i++)
        {
            int ox = i * (half / 2);
            sb.Draw(_pixel, new Rectangle(cx + ox - t, cy + ox - t, t * 2, t * 2), color);
            sb.Draw(_pixel, new Rectangle(cx - ox - t, cy + ox - t, t * 2, t * 2), color);
        }
    }

    private static Color ImprovementColor(string improvementType) => improvementType switch
    {
        "Farm"        => new Color(60, 180, 60),    // green
        "Mine"        => new Color(150, 140, 130),  // gray
        "LoggingCamp" => new Color(120, 70, 30),    // brown
        "Pasture"     => new Color(160, 220, 100),  // light green
        "Fishery"     => new Color(50, 120, 200),   // blue
        _             => Color.White
    };

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

        int dotSize = MarkerPx(0.3f, 2);
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

        // Characters drawn as a + cross — distinct from beast dots and settlement squares
        int arm   = MarkerPx(0.2f, 2);
        int thick = Math.Max(1, arm / 2);
        var blue  = new Color(80, 130, 230);

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!charTiles.Contains(coord)) continue;
            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.5f); // centered like other markers
            DrawCross(sb, cx, cy, arm, thick, blue);
        }
    }

    private void DrawSettlementMarkers(
        SpriteBatch sb, WorldSnapshot snapshot,
        int tw, int th, int minX, int maxX, int minY, int maxY,
        bool territoryMode = false)
    {
        if (snapshot.Settlements.Count == 0) return;

        int markerSize = MarkerPx(0.4f, 3);
        int half       = markerSize / 2;

        var fill = Color.White;

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!snapshot.Settlements.TryGetValue(coord, out var s)) continue;

            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.5f);

            // Fill: white, or health-tinted
            if (s.Health < 40) fill = new Color(220, 80, 60);
            else if (s.Health < 70) fill = new Color(230, 200, 80);
            else fill = Color.White;

            // Border: civ color in territory mode so it updates immediately with ownership changes
            var border = (territoryMode && snapshot.TerritoryMap.TryGetValue(coord, out var terr))
                ? UiTheme.CivColor(terr.CivId)
                : new Color(30, 20, 10);

            sb.Draw(_pixel, new Rectangle(cx - half - 1, cy - half - 1, markerSize + 2, markerSize + 2), border);
            sb.Draw(_pixel, new Rectangle(cx - half, cy - half, markerSize, markerSize), fill);
        }
    }

    private void DrawRuinMarkers(
        SpriteBatch sb, WorldSnapshot snapshot, int tw, int th,
        int minX, int maxX, int minY, int maxY)
    {
        if (snapshot.Ruins.Count == 0) return;

        // Ruins drawn as an × — distinct from active settlement squares
        int ruinHalf  = MarkerPx(0.25f, 2);
        var ruinColor = new Color(110, 80, 50); // muted brown

        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            int wx    = ((tx % tw) + tw) % tw;
            var coord = new TileCoord(wx, ty);
            if (!snapshot.Ruins.ContainsKey(coord) || snapshot.Settlements.ContainsKey(coord)) continue;

            var pos = camera.TileToScreen(coord);
            int cx  = (int)MathF.Round(pos.X + camera.Zoom * 0.5f);
            int cy  = (int)MathF.Round(pos.Y + camera.Zoom * 0.5f);
            DrawX(sb, cx, cy, ruinHalf, ruinColor);
        }
    }

    /// <summary>
    /// Draws a bold orange outline around the spotlit character's tile, with corner ticks so it
    /// reads at any zoom level (including zoom &lt; 2, where the blue character-cross marker
    /// doesn't render at all). Kept visually distinct from the yellow tile-inspector highlight.
    /// </summary>
    private void DrawSpotlightMarker(SpriteBatch sb, TileCoord coord)
    {
        var pos = camera.TileToScreen(coord);
        int x0 = (int)MathF.Round(pos.X);
        int y0 = (int)MathF.Round(pos.Y);
        int x1 = (int)MathF.Round(pos.X + camera.Zoom);
        int y1 = (int)MathF.Round(pos.Y + camera.Zoom);
        int w = Math.Max(x1 - x0, 6), h = Math.Max(y1 - y0, 6);
        x1 = x0 + w; y1 = y0 + h;

        var orange = new Color(255, 140, 0);
        const int B = 3;
        int pad = Math.Max(2, MarkerPx(0.15f, 2)); // outset from the tile so it doesn't merge with the tile-inspector box
        int ox0 = x0 - pad, oy0 = y0 - pad, ox1 = x1 + pad, oy1 = y1 + pad;
        int ow = ox1 - ox0, oh = oy1 - oy0;

        // Corner ticks instead of a full box outline — reads as "marked" without occluding the tile.
        sb.Draw(_pixel, new Rectangle(ox0, oy0, B, oh / 3), orange);
        sb.Draw(_pixel, new Rectangle(ox0, oy0, ow / 3, B), orange);

        sb.Draw(_pixel, new Rectangle(ox1 - B, oy0, B, oh / 3), orange);
        sb.Draw(_pixel, new Rectangle(ox1 - ow / 3, oy0, ow / 3, B), orange);

        sb.Draw(_pixel, new Rectangle(ox0, oy1 - oh / 3, B, oh / 3), orange);
        sb.Draw(_pixel, new Rectangle(ox0, oy1 - B, ow / 3, B), orange);

        sb.Draw(_pixel, new Rectangle(ox1 - B, oy1 - oh / 3, B, oh / 3), orange);
        sb.Draw(_pixel, new Rectangle(ox1 - ow / 3, oy1 - B, ow / 3, B), orange);
    }

    public void Dispose() => _pixel.Dispose();

    private static Texture2D CreatePixel(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, 1, 1);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}
