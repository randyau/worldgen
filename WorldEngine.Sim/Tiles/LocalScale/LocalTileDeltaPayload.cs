namespace WorldEngine.Sim.Tiles.LocalScale;

/// <summary>
/// JSON payload shape for a <see cref="LocalChangeType.CellOverride"/> delta — same
/// PendingEvent/SimEvent payload pattern (a JSON string on the persisted record, a typed shape
/// for producers/consumers to agree on). Each field is independently optional so a delta can
/// override just the fields a future change actually touches.
/// </summary>
public sealed record LocalTileDeltaPayload(
    byte? Elevation = null,
    byte? BiomeType = null,
    byte? DecorationType = null,
    byte? Flags = null);
