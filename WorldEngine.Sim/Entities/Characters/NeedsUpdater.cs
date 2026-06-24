using WorldEngine.Sim.Config;
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

        // Situational recovery (stub — Phase 2.3 will use real territory/settlement data)
        n.Safety += 0.05f;  // stub: ambient safety recovery
        n.Food   += 0.07f;  // stub: lower food web (same rationale as beasts)
        if (world.Settlements.ContainsKey(c.Location))
            n.Shelter = Math.Min(1f, n.Shelter + 0.10f);
        else
            n.Shelter = Math.Min(1f, n.Shelter + 0.01f);

        // Ally presence slightly helps Belonging
        if (world.GetEntitiesAt(c.Location)
            .Any(e => e is Tier1Character other && other.Id != c.Id
                   && (world.GetRelationship(c.Id, other.Id)?.IsAlly ?? false)))
            n.Belonging = Math.Min(1f, n.Belonging + 0.05f);

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
}
