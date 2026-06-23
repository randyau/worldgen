namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Top-level wrapper for beasts.toml deserialization.
/// Tomlyn maps [[beasts]] arrays to the Beasts list via snake_case conversion.
/// </summary>
public sealed class BeastCatalogFile
{
    public BeastSpawnConfig BeastSpawn { get; set; } = new();
    public CombatConfig Combat { get; set; } = new();
    public List<BeastSpeciesConfig> Beasts { get; set; } = new();
}
