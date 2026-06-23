using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Characters;

/// <summary>Creates Tier1Character instances with seeded-random traits.</summary>
public static class CharacterFactory
{
    // Salt constants for WorldRng — never reuse across different trait rolls
    private const int SaltPersonality = 400;
    private const int SaltAptitude    = 410;
    private const int SaltSkills      = 420;
    private const int SaltAge         = 430;
    private const int SaltName        = 440;
    private const int SaltEpithet     = 441;

    public static Tier1Character Spawn(
        TileCoord location,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear)
    {
        var id = EntityId.New();
        var seq = (int)(entitySeq & 0x7FFFFFFF);

        var personality = new PersonalityVector(
            Ambition:     Trait(worldSeed, seq, SaltPersonality + 0),
            Greed:        Trait(worldSeed, seq, SaltPersonality + 1),
            Aggression:   Trait(worldSeed, seq, SaltPersonality + 2),
            Compassion:   Trait(worldSeed, seq, SaltPersonality + 3),
            Curiosity:    Trait(worldSeed, seq, SaltPersonality + 4),
            Creativity:   Trait(worldSeed, seq, SaltPersonality + 5),
            Rationality:  Trait(worldSeed, seq, SaltPersonality + 6),
            Wonder:       Trait(worldSeed, seq, SaltPersonality + 7),
            Loyalty:      Trait(worldSeed, seq, SaltPersonality + 8),
            Sociability:  Trait(worldSeed, seq, SaltPersonality + 9),
            Honesty:      Trait(worldSeed, seq, SaltPersonality + 10),
            Stability:    Trait(worldSeed, seq, SaltPersonality + 11));

        var aptitude = new AptitudeVector(
            Diligence:    Trait(worldSeed, seq, SaltAptitude + 0),
            Focus:        Trait(worldSeed, seq, SaltAptitude + 1),
            Perfectionism: Trait(worldSeed, seq, SaltAptitude + 2),
            Composure:    Trait(worldSeed, seq, SaltAptitude + 3),
            Acuity:       Trait(worldSeed, seq, SaltAptitude + 4),
            Ingenuity:    Trait(worldSeed, seq, SaltAptitude + 5));

        var skills = new SkillVector(
            Combat:        LowSkill(worldSeed, seq, SaltSkills + 0),
            Leadership:    LowSkill(worldSeed, seq, SaltSkills + 1),
            Administration: LowSkill(worldSeed, seq, SaltSkills + 2),
            Diplomacy:     LowSkill(worldSeed, seq, SaltSkills + 3),
            Crafting:      LowSkill(worldSeed, seq, SaltSkills + 4),
            Knowledge:     LowSkill(worldSeed, seq, SaltSkills + 5),
            Stealth:       LowSkill(worldSeed, seq, SaltSkills + 6),
            Piety:         LowSkill(worldSeed, seq, SaltSkills + 7));

        int ageRange = config.Character.MaxAgeSeasonsMax - config.Character.MaxAgeSeasonsMin;
        int maxAge = config.Character.MaxAgeSeasonsMin
            + (int)(WorldRng.FloatAt(worldSeed, 0, seq, 0, SaltAge) * ageRange);

        string name    = PickName(config.CharacterNames.FirstNames, worldSeed, seq, SaltName);
        string epithet = PickName(config.CharacterNames.Epithets, worldSeed, seq, SaltEpithet);

        var identity = new IdentityData(
            Name:        name,
            Epithet:     epithet,
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

    // Gaussian approximation via sum of 3 uniform samples (central limit theorem)
    private static float Trait(int worldSeed, int seq, int salt)
    {
        float u1 = WorldRng.FloatAt(worldSeed, 0, seq, 0, salt);
        float u2 = WorldRng.FloatAt(worldSeed, 1, seq, 0, salt);
        float u3 = WorldRng.FloatAt(worldSeed, 2, seq, 0, salt);
        float gaussian = (u1 + u2 + u3) / 3f;  // mean=0.5, stdDev≈0.17
        // Scale to center=0.5 with stdDev≈0.2, clamp to [0.1, 0.9]
        float val = 0.5f + (gaussian - 0.5f) * 1.2f;
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
