using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Config;

/// <summary>
/// Loaded set of all ancestry configs. Accessible via world.SimConfig.AncestryRegistry.
/// Provides biome-weighted ancestry sampling and cross-ancestry trust lookups.
/// </summary>
public sealed class AncestryRegistry
{
    private readonly Dictionary<string, AncestryConfig> _byId;

    public static readonly AncestryRegistry Empty = new(new Dictionary<string, AncestryConfig>());

    public AncestryRegistry(Dictionary<string, AncestryConfig> byId) => _byId = byId;

    public AncestryConfig? Get(string id) =>
        _byId.TryGetValue(id, out var cfg) ? cfg : null;

    public AncestryConfig GetOrHuman(string id) =>
        _byId.TryGetValue(id, out var cfg) ? cfg : _byId.GetValueOrDefault("human") ?? new AncestryConfig();

    public IReadOnlyCollection<AncestryConfig> All => _byId.Values;

    /// <summary>
    /// Sample an ancestry for a spawn tile using biome-weighted probabilities.
    /// Falls back to "human" if no ancestry has a weight for this biome.
    /// </summary>
    public string SampleAncestry(BiomeType biome, int worldSeed, long seq, int salt)
    {
        string biomeKey = BiomeKey(biome);
        var weighted = new List<(string id, float weight)>();

        foreach (var (id, cfg) in _byId)
        {
            if (cfg.SpawnWeights.TryGetValue(biomeKey, out float w) && w > 0f)
                weighted.Add((id, w));
        }

        if (weighted.Count == 0) return "human";
        if (weighted.Count == 1) return weighted[0].id;

        float total = weighted.Sum(x => x.weight);
        float roll  = WorldRng.FloatAt(worldSeed, seq, (int)(seq >> 16), 0, salt) * total;

        float cumulative = 0f;
        foreach (var (id, w) in weighted)
        {
            cumulative += w;
            if (roll <= cumulative) return id;
        }
        return weighted[^1].id;
    }

    /// <summary>Trust modifier applied once when two ancestries meet for the first time.</summary>
    public float GetFirstMeetingTrust(string idA, string idB)
    {
        if (idA == idB) return 0f;
        if (_byId.TryGetValue(idA, out var cfgA)
            && cfgA.FirstMeetingTrust.TryGetValue(idB, out float modifier))
            return modifier;
        return 0f;
    }

    /// <summary>Cultural distance (0–1) driving the passive per-tick trust drain.</summary>
    public float GetCulturalDistance(string idA, string idB)
    {
        if (idA == idB) return 0f;
        if (_byId.TryGetValue(idA, out var cfgA)
            && cfgA.CulturalDistance.TryGetValue(idB, out float dist))
            return dist;
        // Symmetry fallback
        if (_byId.TryGetValue(idB, out var cfgB)
            && cfgB.CulturalDistance.TryGetValue(idA, out float distB))
            return distB;
        return 0.3f; // unknown pair gets a moderate default
    }

    private static string BiomeKey(BiomeType biome)
    {
        // PascalCase → snake_case matching the TOML keys
        var name = biome.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
