using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Persistence;

/// <summary>
/// Saves and loads WorldState to/from a save directory.
/// Format: meta.json (version/summary), state.bin (full world state JSON), config_snapshot/.
/// </summary>
public static class WorldStateSaver
{
    private const string MetaFile      = "meta.json";
    private const string StateFile     = "state.bin";
    private const string ConfigSubDir  = "config_snapshot";
    public  const string FormatVersion = "3.6";

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes WorldState to saveDir. Safe to call from a background thread.
    /// Creates saveDir if it doesn't exist.
    /// </summary>
    public static void Save(WorldState world, string saveDir, SimConfig cfg)
    {
        Directory.CreateDirectory(saveDir);

        // 1. Write meta.json
        var meta = new MetaDto(
            FormatVersion, world.Config.Seed,
            world.Config.WidthKm, world.Config.HeightKm, world.Config.TileWidthKm,
            world.CurrentYear, world.CurrentTick);
        File.WriteAllBytes(
            Path.Combine(saveDir, MetaFile),
            JsonSerializer.SerializeToUtf8Bytes(meta, WorldStateSerializerContext.Default.MetaDto));

        // 2. Write state.bin
        var dto = WorldStateMapper.ToDto(world);
        File.WriteAllBytes(
            Path.Combine(saveDir, StateFile),
            JsonSerializer.SerializeToUtf8Bytes(dto, WorldStateSerializerContext.Default.WorldStateDto));

        // 3. Copy sim_config.toml snapshot for reference (best-effort)
        try
        {
            var cfgDir = Path.Combine(saveDir, ConfigSubDir);
            Directory.CreateDirectory(cfgDir);
            // Try common locations for the config file
            foreach (var candidate in new[]
            {
                Path.Combine("config", "sim_config.toml"),
                Path.Combine(AppContext.BaseDirectory, "config", "sim_config.toml"),
            })
            {
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, Path.Combine(cfgDir, "sim_config.toml"), overwrite: true);
                    break;
                }
            }
        }
        catch
        {
            // Config snapshot is informational only — never fail the save because of it
        }
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes WorldState from saveDir. Regenerates TileGrid from saved seed.
    /// Throws if save is missing or version is incompatible.
    /// </summary>
    public static WorldState Load(string saveDir, SimConfig cfg)
    {
        var statePath = Path.Combine(saveDir, StateFile);
        if (!File.Exists(statePath))
            throw new FileNotFoundException($"Save file not found: {statePath}");

        var bytes = File.ReadAllBytes(statePath);
        var dto   = JsonSerializer.Deserialize(bytes, WorldStateSerializerContext.Default.WorldStateDto)
            ?? throw new InvalidOperationException("Failed to deserialize world state.");

        return WorldStateMapper.FromDto(dto, cfg);
    }

    // ── Probe ────────────────────────────────────────────────────────────────

    /// <summary>Returns true if a valid save exists at saveDir.</summary>
    public static bool HasSave(string saveDir) =>
        File.Exists(Path.Combine(saveDir, MetaFile)) &&
        File.Exists(Path.Combine(saveDir, StateFile));

    /// <summary>Reads meta.json without loading the full world. Returns null if no save exists.</summary>
    public static MetaDto? ReadMeta(string saveDir)
    {
        var path = Path.Combine(saveDir, MetaFile);
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize(bytes, WorldStateSerializerContext.Default.MetaDto);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes the save directory and all its contents.</summary>
    public static void DeleteSave(string saveDir)
    {
        if (Directory.Exists(saveDir))
            Directory.Delete(saveDir, recursive: true);
    }
}
