using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>
/// Populates the world with Tier 2 characters proportional to settlement population at world start.
/// </summary>
public static class Tier2Spawner
{
    private const int SaltPersonality = 800;
    private const int SaltAge         = 810;
    private const int SaltRole        = 820;
    private const int SaltName        = 830;

    // entitySeq starts above Tier 1 range (10_000–10_999 is Tier 1 budget)
    private const long Tier2SeqBase = 20_000;

    public static List<PendingEvent> SpawnAll(WorldState world, SimConfig config)
    {
        var pending = new List<PendingEvent>();
        var cfg     = config.Character;

        long seq = Tier2SeqBase;
        foreach (var kvp in world.Settlements)
        {
            var tile    = kvp.Key;
            var stub    = kvp.Value;
            int count   = Math.Max(1, stub.Population / cfg.Tier2PerPopulation);

            for (int i = 0; i < count; i++)
            {
                var role = PickRole(world.WorldSeed, (int)seq, stub.CivId);
                var personality = GeneratePersonality(world.WorldSeed, (int)seq);
                int maxAge = cfg.Tier2MaxAgeSeasonsMin
                    + (int)(WorldRng.FloatAt(world.WorldSeed, 0, (int)seq, 0, SaltAge)
                            * (cfg.Tier2MaxAgeSeasonsMax - cfg.Tier2MaxAgeSeasonsMin));

                string name = PickName(world.WorldSeed, (int)seq, config.CharacterNames);

                var livelihood = new LivelihoodData(
                    Role:           role,
                    EmployerId:     null,    // employer assigned in Phase 2.3.3 behavior
                    SettlementTile: tile,
                    IncomeLevel:    0.3f + personality.Diligence * 0.4f);

                var c = new Tier2Character(
                    id:            EntityId.New(),
                    location:      tile,
                    name:          name,
                    personality:   personality,
                    livelihood:    livelihood,
                    maxHealth:     cfg.MaxHealth,
                    maxAgeSeason:  maxAge);

                world.Entities.Add(c);
                pending.Add(MakeBornEvent(c, world));
                seq++;
            }
        }
        return pending;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Tier2Role PickRole(int seed, int seq, CivId civId)
    {
        float r = WorldRng.FloatAt(seed, civId.Value, seq, 0, SaltRole);
        return r switch
        {
            < 0.20f => Tier2Role.General,
            < 0.40f => Tier2Role.Governor,
            < 0.55f => Tier2Role.Merchant,
            < 0.70f => Tier2Role.Scholar,
            < 0.85f => Tier2Role.Physician,
            _        => Tier2Role.Artisan
        };
    }

    private static PersonalityVector6 GeneratePersonality(int seed, int seq)
    {
        float Trait(int traitIndex) => ClampedGaussian(seed, seq, traitIndex, SaltPersonality);
        return new PersonalityVector6(
            Ambition:    Trait(0),
            Loyalty:     Trait(1),
            Diligence:   Trait(2),
            Sociability: Trait(3),
            Cunning:     Trait(4),
            Rationality: Trait(5));
    }

    private static float ClampedGaussian(int seed, int seq, int traitIdx, int salt)
    {
        float u1 = WorldRng.FloatAt(seed, 0, seq, traitIdx * 3,     salt);
        float u2 = WorldRng.FloatAt(seed, 0, seq, traitIdx * 3 + 1, salt);
        float u3 = WorldRng.FloatAt(seed, 0, seq, traitIdx * 3 + 2, salt);
        float g  = (u1 + u2 + u3) / 3f; // [0, 1], CLT approx
        float v  = 0.5f + (g - 0.5f) * 0.4f; // scale stddev ≈ 0.16
        return Math.Clamp(v, 0.1f, 0.9f);
    }

    private static string PickName(int seed, int seq, CharacterNamesConfig names)
    {
        int idx = (int)(WorldRng.FloatAt(seed, 0, seq, 0, SaltName) * names.FirstNames.Length);
        idx = Math.Clamp(idx, 0, names.FirstNames.Length - 1);
        return names.FirstNames[idx];
    }

    private static PendingEvent MakeBornEvent(Tier2Character c, WorldState world)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            name        = c.Name,
            role        = c.Livelihood.Role.ToString(),
            location    = new[] { c.Location.X, c.Location.Y }
        });
        return new PendingEvent(EventType.CharacterBorn, c.Location, null, payload,
            new[] { c.Id.Value });
    }
}
