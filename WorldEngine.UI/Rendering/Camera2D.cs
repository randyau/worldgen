using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WorldEngine.Sim.Commands;
using WorldEngine.Sim.Core;

namespace WorldEngine.UI.Rendering;

public sealed class Camera2D
{
    public Vector2 Position { get; private set; } = Vector2.Zero;
    public float Zoom { get; private set; } = 16f; // pixels per tile

    private static readonly float MinZoom = 4f;
    private static readonly float MaxZoom = 64f;

    public void Pan(Vector2 delta) => Position += delta / Zoom;

    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        var worldBefore = ScreenToWorld(screenPoint);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        var worldAfter = ScreenToWorld(screenPoint);
        Position += worldBefore - worldAfter;
    }

    public Vector2 ScreenToWorld(Vector2 screenPos) => Position + screenPos / Zoom;

    public TileCoord ScreenToTile(Vector2 screenPos)
    {
        var world = ScreenToWorld(screenPos);
        return new TileCoord((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
    }

    public Vector2 TileToScreen(TileCoord coord) =>
        new Vector2((coord.X - Position.X) * Zoom, (coord.Y - Position.Y) * Zoom);

    public (int minX, int minY, int maxX, int maxY) GetVisibleTileBounds(GraphicsDevice gd)
    {
        int w = gd.Viewport.Width, h = gd.Viewport.Height;
        var tl = ScreenToTile(Vector2.Zero);
        var br = ScreenToTile(new Vector2(w, h));
        return (tl.X - 1, tl.Y - 1, br.X + 1, br.Y + 1);
    }

    public void FlushViewportCommand(CommandQueue queue, GraphicsDevice gd)
    {
        var (minX, minY, maxX, maxY) = GetVisibleTileBounds(gd);
        queue.Enqueue(new SetViewport(minX, minY, maxX - minX + 1, maxY - minY + 1));
    }
}
