using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Queryable, in-memory view of the beast species catalog loaded from config/beasts.toml.
/// </summary>
public sealed class BeastCatalog
{
    private readonly Dictionary<string, BeastSpeciesConfig> _byId;

    public IReadOnlyList<BeastSpeciesConfig> AllSpecies { get; }
    public BeastSpawnConfig SpawnConfig { get; }
    public CombatConfig CombatConfig { get; }

    public BeastCatalog(BeastCatalogFile file)
    {
        SpawnConfig  = file.BeastSpawn;
        CombatConfig = file.Combat;
        AllSpecies   = file.Beasts.AsReadOnly();
        _byId        = file.Beasts.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    public BeastSpeciesConfig? Get(string id) => _byId.GetValueOrDefault(id);

    public IEnumerable<BeastSpeciesConfig> ByCategory(string category) =>
        AllSpecies.Where(s => s.Category == category);

    public IEnumerable<BeastSpeciesConfig> ByBiome(string biome) =>
        AllSpecies.Where(s => s.Biomes.Contains(biome, StringComparer.OrdinalIgnoreCase));
}
