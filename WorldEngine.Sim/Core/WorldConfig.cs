namespace WorldEngine.Sim.Core;

public sealed class WorldConfig
{
    public int Seed { get; init; }
    public int WidthKm { get; init; } = 4000;
    public int HeightKm { get; init; } = 3000;
    public int TileWidthKm { get; init; } = 10;

    public int TileWidth => WidthKm / TileWidthKm;
    public int TileHeight => HeightKm / TileWidthKm;

    public static WorldConfig Default(int seed = 0) => new() { Seed = seed };
}
