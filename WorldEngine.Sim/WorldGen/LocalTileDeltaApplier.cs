using System.Text.Json;
using WorldEngine.Sim.Tiles.LocalScale;

namespace WorldEngine.Sim.WorldGen;

/// <summary>
/// Applies persisted <see cref="LocalTileDelta"/> overrides on top of an already-generated
/// <see cref="LocalChunk"/> — the last post-process pass in the local-gen pipeline (after
/// <see cref="LocalTerrainAmplifier.Amplify"/> and <see cref="LocalRiverThreader.Thread"/>), since
/// a player-caused modification must win over regenerated base terrain.
/// </summary>
public static class LocalTileDeltaApplier
{
    public static void Apply(LocalChunk chunk, IReadOnlyList<LocalTileDelta> deltas)
    {
        foreach (var delta in deltas)
        {
            // DECISION: LocalChangeType has only one value so far (11.5's "start minimal"); this
            // switch is here so a future change kind fails loudly instead of being silently
            // no-op'd if someone forgets to extend it.
            switch (delta.ChangeType)
            {
                case LocalChangeType.CellOverride:
                    ApplyCellOverride(chunk, delta);
                    break;
            }
        }
    }

    private static void ApplyCellOverride(LocalChunk chunk, LocalTileDelta delta)
    {
        var payload = JsonSerializer.Deserialize<LocalTileDeltaPayload>(delta.PayloadJson);
        if (payload is null) return;

        ref var tile = ref chunk.GetTileRef(delta.Local);
        if (payload.Elevation.HasValue) tile.Elevation = payload.Elevation.Value;
        if (payload.BiomeType.HasValue) tile.BiomeType = payload.BiomeType.Value;
        if (payload.DecorationType.HasValue) tile.DecorationType = payload.DecorationType.Value;
        if (payload.Flags.HasValue) tile.Flags = payload.Flags.Value;
    }
}
