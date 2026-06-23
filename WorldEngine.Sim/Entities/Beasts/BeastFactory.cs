using WorldEngine.Sim.Core;

namespace WorldEngine.Sim.Entities.Beasts;

/// <summary>
/// Creates LegendaryBeast instances from a species config and placement parameters.
/// All randomness is seeded via WorldRng for reproducibility.
/// </summary>
public static class BeastFactory
{
    // Salt constants for WorldRng calls — never reuse across different rolls
    private const int SaltLegendary   = 100;
    private const int SaltAge         = 101;
    private const int SaltNameAdj     = 102;
    private const int SaltNameNoun    = 103;
    private const int SaltNameForm    = 104;

    /// <summary>
    /// Spawns one beast from the given species. Rolls legendary check if category is predator.
    /// Mythological creatures are always treated as IsLegendary = true.
    /// </summary>
    public static LegendaryBeast Spawn(
        BeastSpeciesConfig species,
        TileCoord location,
        int worldSeed,
        long entitySeq,
        bool forceLegendary = false)
    {
        bool isLegendary = species.IsMythological || forceLegendary
            || WorldRng.FloatAt(worldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltLegendary)
               < species.LegendaryChance;

        float healthMult    = isLegendary && !species.IsMythological ? species.LegendaryHealthMult    : 1f;
        float strengthMult  = isLegendary && !species.IsMythological ? species.LegendaryStrengthMult  : 1f;
        float ageMult       = isLegendary && !species.IsMythological ? species.LegendaryAgeMult       : 1f;
        float territoryMult = isLegendary && !species.IsMythological ? species.LegendaryTerritoryMult : 1f;

        int maxHealth      = Math.Max(1, (int)(species.Health * healthMult));
        int strength       = Math.Max(1, (int)(species.Strength * strengthMult));
        int territoryRadius = Math.Max(1, (int)(species.TerritoryRadius * territoryMult));

        float ageNoise = WorldRng.FloatAt(worldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltAge);
        int maxAge = species.AgeMinSeasons
            + (int)(ageNoise * (species.AgeMaxSeasons - species.AgeMinSeasons) * ageMult);

        string name = GenerateName(species, worldSeed, entitySeq, isLegendary || species.IsMythological);

        var id = EntityId.New();
        return new LegendaryBeast(
            id:                       id,
            speciesId:                species.Id,
            name:                     name,
            location:                 location,
            isLegendary:              isLegendary,
            maxHealth:                maxHealth,
            strength:                 strength,
            speed:                    species.Speed,
            aggression:               species.Aggression,
            territoryRadius:          territoryRadius,
            abilities:                species.Abilities,
            maxAgeSeason:             maxAge,
            foodDepletion:            species.FoodDepletion,
            foodFromHunt:             species.FoodFromHunt,
            foodFromGraze:            species.FoodFromGraze,
            reproductionChance:       species.ReproductionChance,
            reproductionMinAge:       species.ReproductionMinAge,
            reproductionFoodThreshold: species.ReproductionFoodThreshold,
            hibernates:               species.Hibernates,
            prefersCompany:           species.PrefersCompany
        );
    }

    private static string GenerateName(
        BeastSpeciesConfig species, int worldSeed, long entitySeq, bool isNamed)
    {
        if (!isNamed) return species.DisplayName;

        var adjs  = species.EffectiveNameAdjectives;
        var nouns = species.EffectiveNameNouns;

        if (adjs.Length == 0 && nouns.Length == 0)
            return $"The {species.DisplayName}";

        // Pick form: noun-form if both lists non-empty and coin flip; adjective-form otherwise
        bool useNoun = nouns.Length > 0
            && (adjs.Length == 0
                || WorldRng.FloatAt(worldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltNameForm) < 0.5f);

        if (useNoun)
        {
            int idx = (int)(WorldRng.FloatAt(worldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltNameNoun)
                      * nouns.Length);
            return $"The {nouns[Math.Clamp(idx, 0, nouns.Length - 1)]}";
        }
        else
        {
            int idx = (int)(WorldRng.FloatAt(worldSeed, 0, (int)(entitySeq & 0x7FFFFFFF), 0, SaltNameAdj)
                      * adjs.Length);
            return $"The {adjs[Math.Clamp(idx, 0, adjs.Length - 1)]} {species.DisplayName}";
        }
    }
}
