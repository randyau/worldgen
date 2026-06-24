using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Entities.Characters;

public static class NeedsUpdater
{
    public static void Update(Tier1Character c, IWorldStateReadOnly world, CharacterSimConfig cfg)
    {
        var n = c.Needs;

        // Decay
        n.Safety = Math.Max(0f, n.Safety - cfg.NeedsDecaySafety);
        n.Food   = Math.Max(0f, n.Food   - cfg.NeedsDecayFood);

        // Shelter decay scales with temperature extremes. BaseTemperature (annual average)
        // is used here because IWorldStateReadOnly doesn't expose SeasonalProfiles —
        // seasonal effects already show up via character movement responses to settlement pulls.
        var tile        = world.GetTile(c.Location);
        float tempPress = TileShelterPressure(tile.BaseTemperature, cfg);
        float shelterDecay = cfg.NeedsDecayShelter * (1f + tempPress * cfg.ShelterTemperatureScale);
        n.Shelter = Math.Max(0f, n.Shelter - shelterDecay);
        n.Belonging = Math.Max(0f, n.Belonging - cfg.NeedsDecayBelonging);
        n.Status    = Math.Max(0f, n.Status    - cfg.NeedsDecayStatus);
        n.Purpose   = Math.Max(0f, n.Purpose   - cfg.NeedsDecayPurpose);
        n.Spiritual = Math.Max(0f, n.Spiritual - cfg.NeedsDecaySpiritual);

        // Ambient recovery stubs (food web, ambient safety)
        n.Safety += 0.05f;
        n.Food   += 0.07f;

        // Shelter recovery from environment. Settlements provide the best shelter;
        // forests and mountains provide meaningful natural shelter so explorers can
        // sustain themselves by camping in the right terrain.
        float shelterRecovery = world.Settlements.ContainsKey(c.Location)
            ? 0.10f
            : BiomeShelterRecovery((BiomeType)tile.BiomeType);
        n.Shelter = Math.Min(1f, n.Shelter + shelterRecovery);

        // Ally presence slightly helps Belonging
        if (world.GetEntitiesAt(c.Location)
            .Any(e => e is Tier1Character other && other.Id != c.Id
                   && (world.GetRelationship(c.Id, other.Id)?.IsAlly ?? false)))
            n.Belonging = Math.Min(1f, n.Belonging + 0.05f);

        // Settlement presence restores social/identity needs — community provides recognition,
        // shared purpose, and ritual that solitary wandering cannot supply.
        if (world.Settlements.TryGetValue(c.Location, out var stub))
        {
            bool atOwnCiv = c.Identity.CivId.IsValid && stub.CivId == c.Identity.CivId;
            n.Belonging = Math.Min(1f, n.Belonging + (atOwnCiv
                ? cfg.BelongingOwnSettlementRecovery
                : cfg.BelongingForeignSettlementRecovery));
            if (atOwnCiv)
            {
                n.Status  = Math.Min(1f, n.Status  + cfg.StatusOwnSettlementRecovery);
                n.Purpose = Math.Min(1f, n.Purpose + cfg.PurposeOwnSettlementRecovery);
            }
            n.Spiritual = Math.Min(1f, n.Spiritual + cfg.SpiritualSettlementRecovery);
        }

        // Clamp all
        n.Safety    = Math.Min(1f, n.Safety);
        n.Food      = Math.Min(1f, n.Food);
        n.Belonging = Math.Min(1f, n.Belonging);
        n.Status    = Math.Min(1f, n.Status);
        n.Purpose   = Math.Min(1f, n.Purpose);
        n.Spiritual = Math.Min(1f, n.Spiritual);

        c.Needs = n;
    }

    /// <summary>
    /// Returns 0 in the comfort band [low, high] and rises linearly to 1.0
    /// at the temperature extremes. Used to scale shelter need decay.
    /// </summary>
    private static float TileShelterPressure(byte baseTemp, CharacterSimConfig cfg)
    {
        if (baseTemp < cfg.ShelterComfortTempLow)
            return (float)(cfg.ShelterComfortTempLow - baseTemp) / cfg.ShelterComfortTempLow;
        if (baseTemp > cfg.ShelterComfortTempHigh)
            return (float)(baseTemp - cfg.ShelterComfortTempHigh) / (255 - cfg.ShelterComfortTempHigh);
        return 0f;
    }

    /// <summary>
    /// How much shelter a character naturally recovers per tick in the given biome,
    /// absent a settlement. Dense canopy and rocky terrain provide real cover;
    /// open plains and desert offer very little.
    /// </summary>
    private static float BiomeShelterRecovery(BiomeType biome) => biome switch
    {
        // Dense forest — canopy + deadfall = functional camp; near-settlement recovery
        BiomeType.TemperateForest    => 0.05f,
        BiomeType.TropicalRainforest => 0.05f,
        BiomeType.BorealForest       => 0.04f,  // good shelter, brutal temperature (offset by cold pressure)
        BiomeType.Swamp              => 0.03f,  // cover but wet; net mediocre
        // Elevated terrain — rock faces, overhangs, natural caves
        BiomeType.Mountain           => 0.04f,
        // HighMountain excluded from movement so characters won't be there; include for completeness
        BiomeType.HighMountain       => 0.02f,
        // Open terrain — can make a lean-to or windbreak but nothing substantial
        BiomeType.Grassland          => 0.02f,
        BiomeType.Plains             => 0.015f,
        BiomeType.Savanna            => 0.015f,
        BiomeType.Tundra             => 0.015f,
        // Exposed / hostile — minimal shelter, exposed to elements
        BiomeType.Beach              => 0.01f,
        BiomeType.Desert             => 0.01f,
        BiomeType.Volcanic           => 0.01f,
        _                            => 0.01f,
    };
}
