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
    private const int SaltName        = 440;  // NameGenerator.GenerateGivenName uses 440-442 internally
    private const int SaltEpithet     = 443;
    private const int SaltSurname     = 444;  // NameGenerator.GenerateSurname uses 444-445 internally
    private const int SaltStartAge    = 450;

    /// <summary>
    /// Fraction-of-lifespan range sampled for <c>startAsAdult</c> spawns. Expressed as a fraction
    /// of the individual's own rolled MaxAgeSeason (not an absolute season count) so it scales
    /// correctly across ancestries with wildly different lifespans (e.g. short-lived humans vs.
    /// elves living 10x longer) — a "founder in their prime" is ~15-45% of the way through their
    /// own life regardless of whether that life is 20 years or 400.
    /// </summary>
    private const float AdultAgeFractionMin = 0.15f;
    private const float AdultAgeFractionMax = 0.45f;

    public static Tier1Character Spawn(
        TileCoord location,
        BiomeType biome,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear,
        bool startAsAdult = false)
    {
        // DECISION: EntityId value is derived from entitySeq so that two runs with the
        // same seed produce identical IDs (in-process reproducibility). entitySeq must be
        // unique per spawn site; all callers derive it deterministically from tick/tile/year.
        var id  = new EntityId(entitySeq);
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

        // Syllable-generated given name + surname (ancestry-flavored); epithet still from a flat pool
        var epithetPool = ancestry.Epithets.Length > 0 ? ancestry.Epithets : config.CharacterNames.Epithets;
        string name    = NameGenerator.GenerateGivenName(ancestry, config.CharacterNames, worldSeed, seq, SaltName);
        string surname = NameGenerator.GenerateSurname(ancestry, config.CharacterNames, worldSeed, seq, SaltSurname);
        string epithet = PickName(epithetPool, worldSeed, seq, SaltEpithet);

        var identity = new IdentityData(
            Name:        name,
            Epithet:     epithet,
            AncestryId:  ancId,
            MotherId:    null,
            FatherId:    null,
            BirthYear:   birthYear,
            BirthSeason: 0,
            Surname:     surname);

        var character = new Tier1Character(
            id:           id,
            location:     location,
            personality:  personality,
            aptitude:     aptitude,
            skills:       skills,
            identity:     identity,
            maxHealth:    config.Character.MaxHealth,
            maxAgeSeason: maxAge);

        // DECISION: a "leader emerges from the population" spawn (civ founding, secession,
        // ruler backfill after a civ's last named member dies) represents someone who already
        // existed, not a birth — without this they spawn at AgeSeason 0 and can found/rule in
        // the same tick as their CharacterBorn event, i.e. an infant monarch. Genuine births
        // (CharacterBehaviorPhase's population-growth path) leave startAsAdult=false.
        if (startAsAdult)
        {
            float frac = AdultAgeFractionMin
                + WorldRng.FloatAt(worldSeed, 0, seq, 0, SaltStartAge) * (AdultAgeFractionMax - AdultAgeFractionMin);
            int startAge = Math.Clamp((int)(frac * maxAge), config.Character.MinRulerAgeSeasons, Math.Max(1, maxAge - 1));
            character.AgeSeason = startAge;
        }

        return character;
    }

    /// <summary>
    /// M13 13.0: spawns a child of two named parent characters. Rolls a fresh character the normal
    /// way (ancestry-biased traits, name, lifespan) and then blends its Personality/Aptitude toward
    /// the parent average by <see cref="FamilyConfig.TraitInheritanceWeight"/> — Personality and
    /// Aptitude have no setters (stable-at-generation invariant), so the blended values must be
    /// baked in at construction rather than mutated afterward. AncestryId is inherited from the
    /// mother rather than resampled from biome — DECISION: simplest reasonable choice for
    /// mixed-ancestry households; a full ancestry-mixing model is out of scope for 13.0.
    /// </summary>
    public static Tier1Character SpawnChild(
        Tier1Character mother,
        Tier1Character father,
        TileCoord location,
        BiomeType biome,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear)
    {
        var rolled = Spawn(location, biome, worldSeed, entitySeq, config, birthYear);
        float w = config.Family.TraitInheritanceWeight;

        var mp = mother.Personality; var fp = father.Personality; var rp = rolled.Personality;
        var personality = new PersonalityVector(
            Ambition:    BlendTrait(rp.Ambition,    mp.Ambition,    fp.Ambition,    w),
            Greed:       BlendTrait(rp.Greed,       mp.Greed,       fp.Greed,       w),
            Aggression:  BlendTrait(rp.Aggression,  mp.Aggression,  fp.Aggression,  w),
            Compassion:  BlendTrait(rp.Compassion,  mp.Compassion,  fp.Compassion,  w),
            Curiosity:   BlendTrait(rp.Curiosity,   mp.Curiosity,   fp.Curiosity,   w),
            Creativity:  BlendTrait(rp.Creativity,  mp.Creativity,  fp.Creativity,  w),
            Rationality: BlendTrait(rp.Rationality, mp.Rationality, fp.Rationality, w),
            Wonder:      BlendTrait(rp.Wonder,      mp.Wonder,      fp.Wonder,      w),
            Loyalty:     BlendTrait(rp.Loyalty,     mp.Loyalty,     fp.Loyalty,     w),
            Sociability: BlendTrait(rp.Sociability, mp.Sociability, fp.Sociability, w),
            Honesty:     BlendTrait(rp.Honesty,     mp.Honesty,     fp.Honesty,     w),
            Stability:   BlendTrait(rp.Stability,   mp.Stability,   fp.Stability,   w));

        var ma = mother.Aptitude; var fa = father.Aptitude; var ra = rolled.Aptitude;
        var aptitude = new AptitudeVector(
            Diligence:     BlendTrait(ra.Diligence,     ma.Diligence,     fa.Diligence,     w),
            Focus:         BlendTrait(ra.Focus,         ma.Focus,         fa.Focus,         w),
            Perfectionism: BlendTrait(ra.Perfectionism, ma.Perfectionism, fa.Perfectionism, w),
            Composure:     BlendTrait(ra.Composure,     ma.Composure,     fa.Composure,     w),
            Acuity:        BlendTrait(ra.Acuity,        ma.Acuity,        fa.Acuity,        w),
            Ingenuity:     BlendTrait(ra.Ingenuity,     ma.Ingenuity,     fa.Ingenuity,     w));

        // DECISION: Surname is inherited from the mother rather than resampled, mirroring the
        // existing AncestryId-from-mother precedent above — a child keeps the family name rather
        // than rolling a fresh unrelated house name.
        var identity = rolled.Identity with
        {
            AncestryId = mother.Identity.AncestryId,
            MotherId   = mother.Id,
            FatherId   = father.Id,
            Surname    = mother.Identity.Surname,
        };

        return new Tier1Character(
            id:           rolled.Id,
            location:     rolled.Location,
            personality:  personality,
            aptitude:     aptitude,
            skills:       rolled.Skills,
            identity:     identity,
            maxHealth:    rolled.MaxHealth,
            maxAgeSeason: rolled.MaxAgeSeason);
    }

    private static float BlendTrait(float ancestryBiased, float motherVal, float fatherVal, float weight) =>
        Math.Clamp(ancestryBiased * (1f - weight) + (motherVal + fatherVal) * 0.5f * weight, 0.1f, 0.9f);

    // Backward-compat overload for call sites that don't know the biome (Tier2 promotions, tests)
    public static Tier1Character Spawn(
        TileCoord location,
        int worldSeed,
        long entitySeq,
        SimConfig config,
        int birthYear,
        bool startAsAdult = false) =>
        Spawn(location, BiomeType.Grassland, worldSeed, entitySeq, config, birthYear, startAsAdult);

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
