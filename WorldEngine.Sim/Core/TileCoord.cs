namespace WorldEngine.Sim.Core;

public readonly record struct TileCoord(int X, int Y)
{
    public TileCoord Wrap(int width) => this with { X = ((X % width) + width) % width };

    public TileCoord North() => this with { Y = Y - 1 };
    public TileCoord South() => this with { Y = Y + 1 };
    public TileCoord East(int width) => this with { X = (X + 1) % width };
    public TileCoord West(int width) => this with { X = ((X - 1) + width) % width };

    public int ChebyshevDistance(TileCoord other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
}
