namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>Bit flags for <see cref="LocalTileData"/>.Flags. Only River (11.4) is assigned so far.</summary>
[Flags]
public enum LocalTileFlags : byte
{
    None  = 0,
    River = 1 << 0,
}
