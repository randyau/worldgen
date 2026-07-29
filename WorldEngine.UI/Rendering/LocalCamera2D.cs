using Microsoft.Xna.Framework;

namespace WorldEngine.UI.Rendering;

// MAP: M11 11.7 — pan/zoom camera for the local-view screen. Mirrors Camera2D's API shape at
// 10m-local-tile granularity, operating in within-world-tile local-tile coordinates
// (0..LocalTilesPerWorldTileEdge on each axis) rather than Camera2D's global 10km-tile space.
public sealed class LocalCamera2D
{
    public Vector2 Position { get; private set; } = Vector2.Zero;
    public float Zoom { get; private set; } = 8f; // pixels per local tile

    private static readonly float MinZoom = 1f;
    private static readonly float MaxZoom = 48f;

    public void Pan(Vector2 delta) => Position += delta / Zoom;

    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var worldBefore = ScreenToWorld(screenPoint);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var worldAfter = ScreenToWorld(screenPoint);
        Position += worldBefore - worldAfter;
    }

    public Vector2 ScreenToWorld(Vector2 screenPos) => Position + screenPos / Zoom;

    public (int X, int Y) ScreenToLocalTile(Vector2 screenPos)
    {
        var world = ScreenToWorld(screenPos);
        return ((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
    }

    public Vector2 LocalTileToScreen(int x, int y) =>
        new Vector2((x - Position.X) * Zoom, (y - Position.Y) * Zoom);

    public (int minX, int minY, int maxX, int maxY) GetVisibleTileBounds(int viewportW, int viewportH)
    {
        var tl = ScreenToLocalTile(Vector2.Zero);
        var br = ScreenToLocalTile(new Vector2(viewportW, viewportH));
        return (tl.X - 1, tl.Y - 1, br.X + 1, br.Y + 1);
    }

    /// <summary>Centers the camera on a within-tile local-tile coordinate without changing zoom.</summary>
    public void CenterOn(float x, float y, int viewportW, int viewportH)
    {
        Position = new Vector2(
            x - viewportW / (2f * Zoom),
            y - viewportH / (2f * Zoom));
    }
}
