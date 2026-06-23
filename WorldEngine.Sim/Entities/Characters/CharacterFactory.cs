using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>Creates Tier1Character instances with seeded-random traits.</summary>
public static class CharacterFactory
{
    // Salt constants for WorldRng — never reuse across different trait rolls
    private const int SaltAncestry   = 390;
    private const int SaltPersonality = 400;
    private const int SaltAptitude    = 410;
    private const int SaltSkills      = 420;
    private const int SaltAge         = 430;
    private const int SaltName        = 440;
    private const int SaltEpithet     = 441;

    public static Tier1Character Spawn(
        TileCoord location,
        BiomeType biome,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear)
    {
        var id  = EntityId.New();
        var seq = (int)(entitySeq & 0x7FFFFFFF);

        var registry  = config.AncestryRegistry;
        string ancId  = registry.SampleAncestry(biome, worldSeed, entitySeq, SaltAncestry);
        var ancestry  = registry.GetOrHuman(ancId);

        var personality = new PersonalityVector(
            Ambition:     BiasedTrait(worldSeed, seq, SaltPersonality + 0,  ancestry.BiasAmbition),
            Greed:        BiasedTrait(worldSeed, seq, SaltPersonality + 1,  ancestry.BiasGreed),
            Aggression:   BiasedTrait(worldSeed, seq, SaltPersonality + 2,  ancestry.BiasAggression),
            Compassion:   BiasedTrait(worldSeed, seq, SaltPersonality + 3,  ancestry.BiasCompassion),
            Curiosity:    BiasedTrait(worldSeed, seq, SaltPersonality + 4,  ancestry.BiasCuriosity),
            Creativity:   BiasedTrait(worldSeed, seq, SaltPersonality + 5,  ancestry.BiasCreativity),
            Rationality:  BiasedTrait(worldSeed, seq, SaltPersonality + 6,  ancestry.BiasRationality),
            Wonder:       BiasedTrait(worldSeed, seq, SaltPersonality + 7,  ancestry.BiasWonder),
            Loyalty:      BiasedTrait(worldSeed, seq, SaltPersonality + 8,  ancestry.BiasLoyalty),
            Sociability:  BiasedTrait(worldSeed, seq, SaltPersonality + 9,  ancestry.BiasSociability),
            Honesty:      BiasedTrait(worldSeed, seq, SaltPersonality + 10, ancestry.BiasHonesty),
            Stability:    BiasedTrait(worldSeed, seq, SaltPersonality + 11, ancestry.BiasStability));

        var aptitude = new AptitudeVector(
            Diligence:    BiasedTrait(worldSeed, seq, SaltAptitude + 0, ancestry.BiasDiligence),
            Focus:        BiasedTrait(worldSeed, seq, SaltAptitude + 1, ancestry.BiasFocus),
            Perfectionism: BiasedTrait(worldSeed, seq, SaltAptitude + 2, ancestry.BiasPerfectionism),
            Composure:    BiasedTrait(worldSeed, seq, SaltAptitude + 3, ancestry.BiasComposure),
            Acuity:       BiasedTrait(worldSeed, seq, SaltAptitude + 4, ancestry.BiasAcuity),
            Ingenuity:    BiasedTrait(worldSeed, seq, SaltAptitude + 5, ancestry.BiasIngenuity));

        var skills = new SkillVector(
            Combat:        LowSkill(worldSeed, seq, SaltSkills + 0),
            Leadership:    LowSkill(worldSeed, seq, SaltSkills + 1),
            Administration: LowSkill(worldSeed, seq, SaltSkills + 2),
            Diplomacy:     LowSkill(worldSeed, seq, SaltSkills + 3),
            Crafting:      LowSkill(worldSeed, seq, SaltSkills + 4),
            Knowledge:     LowSkill(worldSeed, seq, SaltSkills + 5),
            Stealth:       LowSkill(worldSeed, seq, SaltSkills + 6),
            Piety:         LowSkill(worldSeed, seq, SaltSkills + 7));

        // Use ancestry lifespan if available; fall back to global config range
        int ageMin = ancestry.MinLifespanSeasons > 0
            ? ancestry.MinLifespanSeasons
            : config.Character.MaxAgeSeasonsMin;
        int ageMax = ancestry.MaxLifespanSeasons > ancestry.MinLifespanSeasons
            ? ancestry.MaxLifespanSeasons
            : config.Character.MaxAgeSeasonsMax;
        int maxAge = ageMin + (int)(WorldRng.FloatAt(worldSeed, 0, seq, 0, SaltAge) * (ageMax - ageMin));

        // Pick from ancestry-specific name pool; fall back to global list
        var namePool    = ancestry.FirstNames.Length > 0 ? ancestry.FirstNames : config.CharacterNames.FirstNames;
        var epithetPool = ancestry.Epithets.Length   > 0 ? ancestry.Epithets   : config.CharacterNames.Epithets;
        string name    = PickName(namePool,    worldSeed, seq, SaltName);
        string epithet = PickName(epithetPool, worldSeed, seq, SaltEpithet);

        var identity = new IdentityData(
            Name:        name,
            Epithet:     epithet,
            AncestryId:  ancId,
            MotherId:    null,
            FatherId:    null,
            CivId:       CivId.None,
            BirthYear:   birthYear,
            BirthSeason: 0);

        return new Tier1Character(
            id:           id,
            location:     location,
            personality:  personality,
            aptitude:     aptitude,
            skills:       skills,
            identity:     identity,
            maxHealth:    config.Character.MaxHealth,
            maxAgeSeason: maxAge);
    }

    // Backward-compat overload for call sites that don't know the biome (Tier2 promotions, tests)
    public static Tier1Character Spawn(
        TileCoord location,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear) =>
        Spawn(location, BiomeType.Grassland, worldSeed, entitySeq, config, birthYear);

    // Gaussian approximation (3-sample CLT); bias shifts the mean, individual noise ≈ stddev 0.2
    private static float BiasedTrait(int worldSeed, int seq, int salt, float bias)
    {
        float u1 = WorldRng.FloatAt(worldSeed, 0, seq, 0, salt);
        float u2 = WorldRng.FloatAt(worldSeed, 1, seq, 0, salt);
        float u3 = WorldRng.FloatAt(worldSeed, 2, seq, 0, salt);
        float gaussian = (u1 + u2 + u3) / 3f;
        float val = (0.5f + bias) + (gaussian - 0.5f) * 1.2f;
        return Math.Clamp(val, 0.1f, 0.9f);
    }

    private static float LowSkill(int worldSeed, int seq, int salt) =>
        Math.Clamp(WorldRng.FloatAt(worldSeed, 0, seq, 0, salt) * 0.2f, 0.01f, 0.2f);

    private static string PickName(string[] pool, int worldSeed, int seq, int salt)
    {
        if (pool.Length == 0) return "Unknown";
        int idx = (int)(WorldRng.FloatAt(worldSeed, 0, seq, 0, salt) * pool.Length);
        return pool[Math.Clamp(idx, 0, pool.Length - 1)];
    }
}
