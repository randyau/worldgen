using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 3 — per-season settlement population growth/decay, specialist crystallization,
/// and abandonment. Replaces the PopulationDynamics stub.
/// </summary>
public sealed class PopulationDynamicsPhase
{
    private readonly SettlementConfig _cfg;
    private readonly SimConfig _simCfg;

    private const int SaltCrystal = 950;

    public PopulationDynamicsPhase(SimConfig cfg)
    {
        _simCfg = cfg;
        _cfg    = cfg.Settlement;
    }

    public List<PendingEvent> Execute(WorldState world)
    {
        var pending = new List<PendingEvent>();
        var toAbandon = new List<TileCoord>();

        foreach (var kvp in world.Settlements)
        {
            var tile    = kvp.Key;
            var stub    = kvp.Value;
            var updated = UpdateSettlement(stub, tile, world, pending);

            if (updated.Population <= 0)
                toAbandon.Add(tile);
            else
                world.Settlements[tile] = updated;
        }

        foreach (var tile in toAbandon)
            AbandonSettlement(tile, world, pending);

        // Refresh TotalPopulation on each civ so InCivFoundingCooldown can read it without
        // scanning all settlements. Cost: O(settlements), paid once here vs. per-character.
        foreach (var civ in world.Civilizations.Values)
            civ.TotalPopulation = 0;
        foreach (var stub in world.Settlements.Values)
            if (world.Civilizations.TryGetValue(stub.CivId, out var civ))
                civ.TotalPopulation += stub.Population;

        return pending;
    }

    // ─── Per-settlement update ────────────────────────────────────────────────

    private SettlementStub UpdateSettlement(
        SettlementStub stub, TileCoord tile, WorldState world, List<PendingEvent> pending)
    {
        var tileData = world.GetTile(tile);
        // Per-settlement founding-time variance permanently differentiates otherwise similar tiles
        float fertility = tileData.Fertility / 255f * stub.FertilityMultiplier;

        // Safety score: entities nearby provide protection. Saturates at 7+ characters → 1.0.
        // Skip the spatial scan for established settlements (pop > 200) — they're inherently safe.
        float safetyScore;
        if (stub.Population > 200)
        {
            safetyScore = 1.0f;
        }
        else
        {
            int nearby = world.GetEntitiesInRadius(tile, 3)
                .Count(e => e is Tier1Character or Tier2Character);
            safetyScore = Math.Clamp(0.3f + nearby * 0.1f, 0f, 1f);
        }

        // Biome-based carrying capacity: computed by ResourcePressurePhase during its tile walk
        // and cached on the stub — reading it here adds zero per-tick cost.
        int carryingCapacity = stub.CarryingCapacity;

        // Decay responds to food pressure from the resource ledger
        float foodRatio = stub.FoodPressureRatio;
        float starvationDecay = foodRatio < _cfg.PopMinViable / 100f ? 0f
            : foodRatio < _simCfg.ResourcePressure.CrisisThreshold
                ? (1f - foodRatio) * _cfg.FamineDecayRate
            : foodRatio < _simCfg.ResourcePressure.ShortageThreshold
                ? (1f - foodRatio) * _cfg.StarvationDecayRate
            : 0f;

        // Logistic suppression: as population approaches carrying capacity, growth decelerates.
        // Below half capacity growth is nearly unconstrained; above it growth increasingly fights back.
        float logisticFactor = Math.Clamp(1f - (float)stub.Population / carryingCapacity, 0f, 1f);
        float foodGrowthScale = Math.Clamp(foodRatio, 0f, 1f);
        float growthF  = fertility * safetyScore * _cfg.PopGrowthRate * foodGrowthScale * logisticFactor;
        float decayF   = _cfg.PopDecayRate + starvationDecay;
        float deltaF   = growthF - decayF;

        float newPopF  = stub.PopulationF + deltaF;
        int   newPop   = Math.Clamp(stub.Population + (int)Math.Floor(newPopF), 0, Math.Min(carryingCapacity, _cfg.PopMax));
        float remainder = newPopF - (int)Math.Floor(newPopF);

        // SettlementGrew/Shrank are suppressed in config — don't generate them to avoid
        // O(settlements) pending event allocations per tick.

        // Specialist crystallization
        int newThresh = stub.LastCrystalThresh;
        newThresh = TryCrystallize(stub, tile, newPop, newThresh, world, pending);

        return stub with
        {
            Population        = newPop,
            PopulationF       = remainder,
            LastCrystalThresh = newThresh
        };
    }

    private int TryCrystallize(
        SettlementStub stub, TileCoord tile,
        int pop, int currentThresh,
        WorldState world, List<PendingEvent> pending)
    {
        // Check thresholds in order; spawn one specialist per newly crossed threshold
        var thresholds = new[]
        {
            (_cfg.CrystalPopArtisan,   Tier2Role.Artisan),
            (_cfg.CrystalPopScholar,   Tier2Role.Scholar),
            (_cfg.CrystalPopPhysician, Tier2Role.Physician),
            (_cfg.CrystalPopMerchant,  Tier2Role.Merchant),
        };

        foreach (var (threshold, role) in thresholds)
        {
            if (threshold <= currentThresh) continue;
            if (pop < threshold) break;

            // Spawn a Tier 2 specialist
            var personality = PersonalityVector6.Default; // stub — future: random personality
            var livelihood  = new LivelihoodData(role, null, tile, 0.5f);
            string name     = _simCfg.CharacterNames.FirstNames[
                Math.Abs(tile.GetHashCode() + threshold) % _simCfg.CharacterNames.FirstNames.Length];

            var specialist = new Tier2Character(
                EntityId.New(), tile, name,
                personality, livelihood,
                maxHealth: _simCfg.Character.MaxHealth,
                maxAgeSeason: _simCfg.Character.Tier2MaxAgeSeasonsMin);
            world.Entities.Add(specialist);

            var payload = JsonSerializer.Serialize(new
            {
                specialistId  = specialist.Id.Value,
                specialistName = name,
                role          = role.ToString(),
                tile          = new[] { tile.X, tile.Y },
                population    = pop,
                threshold
            });
            pending.Add(new PendingEvent(EventType.AppointedToRole, tile, null, payload,
                new[] { specialist.Id.Value }));

            currentThresh = threshold;
        }
        return currentThresh;
    }

    // ─── Abandonment ──────────────────────────────────────────────────────────

    private static void AbandonSettlement(TileCoord tile, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Settlements.TryGetValue(tile, out var stub)) return;
        world.Settlements.Remove(tile);

        int timesSettled = CivTracker.RegisterRuin(tile, stub, "abandoned", world);

        var payload = JsonSerializer.Serialize(new
        {
            tile           = new[] { tile.X, tile.Y },
            settlementName = stub.Name,
            founderId      = stub.FounderId.Value,
            civId          = stub.CivId.Value,
            foundedYear    = stub.FoundedYear,
            year           = world.CurrentYear,
            timesSettled
        });
        pending.Add(new PendingEvent(EventType.SettlementAbandoned, tile, null, payload));
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static PendingEvent MakePopEvent(EventType type, SettlementStub stub, TileCoord tile, int newPop)
    {
        var payload = JsonSerializer.Serialize(new
        {
            tile     = new[] { tile.X, tile.Y },
            oldPop   = stub.Population,
            newPop
        });
        return new PendingEvent(type, tile, null, payload);
    }
}
