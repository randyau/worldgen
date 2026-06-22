namespace WorldEngine.Sim.Core;

/// <summary>The tile-coordinate rectangle visible to the UI camera.</summary>
public readonly record struct ViewportRect(int X, int Y, int Width, int Height)
{
    public static ViewportRect Default => new(0, 0, 80, 60);

    public bool Contains(int x, int y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;
}
