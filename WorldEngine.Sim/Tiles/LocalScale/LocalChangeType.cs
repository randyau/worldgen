namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// Kind of change a <see cref="LocalTileDelta"/> represents. Starts with a single generic value
/// per 11.5's "start minimal" scoping — split into more specific kinds only once a real
/// gameplay system needs to distinguish them.
/// </summary>
public enum LocalChangeType
{
    /// <summary>Overrides one or more <see cref="LocalTileData"/> fields; see <see cref="LocalTileDeltaPayload"/>.</summary>
    CellOverride = 0,
}
