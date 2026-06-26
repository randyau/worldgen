using System.Text.Json;
using WorldEngine.Sim.Civilizations;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;
using S = WorldEngine.Sim.Simulation.SimRngSalts;

namespace WorldEngine.Sim.Simulation.Phases;

/// <summary>
/// Phase 3 — per-season settlement population growth/decay, specialist crystallization,
/// and abandonment. Replaces the PopulationDynamics stub.
/// </summary>
public sealed class PopulationDynamicsPhase
{
    private readonly SettlementConfig _cfg;
    private readonly SimConfig _simCfg;

    // 4 seasons × 4 ticks per season = 16 ticks per in-game year
    private const int TicksPerYear = 16;

    public PopulationDynamicsPhase(SimConfig cfg)
    {
        _simCfg = cfg;
        _cfg    = cfg.Settlement;
    }

    public List<PendingEvent> Execute(WorldState world, bool isAnnualTick = false)
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

        if (isAnnualTick)
        {
            RunAnnualDiseaseChecks(world, pending);
            RunAnnualWildlifeAttacks(world, pending);
        }

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

        // Succession crisis: distant settlements decay faster after the founding ruler dies
        var charCfg = _simCfg.Character;
        if (world.Civilizations.TryGetValue(stub.CivId, out var settleCiv)
            && settleCiv.SuccessionCrisisEndYear != int.MinValue
            && world.CurrentYear < settleCiv.SuccessionCrisisEndYear)
        {
            int sdx = stub.Tile.X - settleCiv.CapitalTile.X;
            int sdy = stub.Tile.Y - settleCiv.CapitalTile.Y;
            if (sdx * sdx + sdy * sdy > charCfg.SuccessionStableRadius * charCfg.SuccessionStableRadius)
                decayF *= charCfg.SuccessionCrisisDecayMult;
        }

        float deltaF   = growthF - decayF;
        float newPopF  = stub.PopulationF + deltaF;
        int   newPop   = Math.Clamp(stub.Population + (int)Math.Floor(newPopF), 0, Math.Min(carryingCapacity, _cfg.PopMax));
        float remainder = newPopF - (int)Math.Floor(newPopF);

        // Disease: proportional per-tick mortality while settlement is infected
        if (stub.IsInfected && newPop > 0)
        {
            int diseaseDrain = Math.Max(1, (int)(newPop * _cfg.DiseaseMortalityPerYear / TicksPerYear));
            newPop = Math.Max(0, newPop - diseaseDrain);
        }

        // SettlementGrew/Shrank are suppressed in config — don't generate them to avoid
        // O(settlements) pending event allocations per tick.

        // Specialist crystallization
        int newThresh = stub.LastCrystalThresh;
        newThresh = TryCrystallize(stub, tile, newPop, newThresh, world, pending);

        // Passive health recovery: settlements repair between raids
        int newHealth = Math.Min(_cfg.MaxHealth, stub.Health + _cfg.HealthRecoveryPerTick);

        return stub with
        {
            Population        = newPop,
            PopulationF       = remainder,
            LastCrystalThresh = newThresh,
            Health            = newHealth
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

            int ageRange = _simCfg.Character.Tier2MaxAgeSeasonsMax - _simCfg.Character.Tier2MaxAgeSeasonsMin;
            int maxAge   = _simCfg.Character.Tier2MaxAgeSeasonsMin
                         + (int)(WorldRng.FloatAt(world.WorldSeed, world.CurrentYear, threshold, 0, S.PopCrystallise) * ageRange);
            var specialist = new Tier2Character(
                EntityId.New(), tile, name,
                personality, livelihood,
                maxHealth: _simCfg.Character.MaxHealth,
                maxAgeSeason: maxAge);
            world.Entities.Add(specialist);

            var payload = JsonSerializer.Serialize(new SpecialistAppointedPayload(
                specialist.Id.Value, name, role.ToString(), pop, threshold));
            pending.Add(new PendingEvent(EventType.AppointedToRole, tile, null, payload,
                new[] { specialist.Id.Value },
                ActorId: specialist.Id.Value, ActorName: name));

            currentThresh = threshold;
        }
        return currentThresh;
    }

    // ─── Abandonment ──────────────────────────────────────────────────────────

    private static void AbandonSettlement(TileCoord tile, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Settlements.TryGetValue(tile, out var stub)) return;
        world.Settlements.Remove(tile);

        int timesSettled = CivTracker.RegisterRuin(tile, stub, "abandoned", world, pending);

        var payload = JsonSerializer.Serialize(new SettlementAbandonedPayload(
            stub.FounderId.Value, stub.FoundedYear, timesSettled, stub.Population));
        pending.Add(new PendingEvent(EventType.SettlementAbandoned, tile, null, payload,
            new[] { stub.FounderId.Value },
            ActorId: stub.FounderId.Value,
            CivId: stub.CivId.Value, SettlementName: stub.Name));
    }

    // ─── Annual disease checks ────────────────────────────────────────────────

    /// <summary>
    /// Annual pass: new outbreaks (scaled by population density), disease spread between nearby
    /// settlements, and recovery checks. Per-tick mortality is applied in UpdateSettlement.
    /// </summary>
    private void RunAnnualDiseaseChecks(WorldState world, List<PendingEvent> pending)
    {
        int year        = world.CurrentYear;
        var settlements = world.Settlements.ToList(); // snapshot; we mutate the dict during iteration
        var toInfect    = new HashSet<TileCoord>();
        var toRecover   = new HashSet<TileCoord>();

        foreach (var (coord, stub) in settlements)
        {
            if (stub.IsInfected)
            {
                int yearsInfected = year - stub.InfectedSinceYear;
                bool recover = yearsInfected >= _cfg.DiseaseMaxDurationYears;
                if (!recover)
                {
                    float roll = WorldRng.FloatAt(world.WorldSeed, year, coord.X * 31 + coord.Y, 0, S.PopDiseaseRecovery);
                    recover = roll < _cfg.DiseaseRecoveryChance;
                }
                if (recover) { toRecover.Add(coord); continue; }

                // Spread to nearby uninfected settlements
                foreach (var (nCoord, nStub) in settlements)
                {
                    if (nCoord == coord || nStub.IsInfected || toInfect.Contains(nCoord)) continue;
                    int dx = coord.X - nCoord.X, dy = coord.Y - nCoord.Y;
                    if (dx * dx + dy * dy > _cfg.DiseaseSpreadRadius * _cfg.DiseaseSpreadRadius) continue;
                    float roll = WorldRng.FloatAt(world.WorldSeed, year, nCoord.X * 31 + nCoord.Y, 1, S.PopDiseaseSpread);
                    if (roll < _cfg.DiseaseSpreadChance) toInfect.Add(nCoord);
                }
            }
            else
            {
                // Outbreaks require minimum population — small settlements can't sustain endemic disease
                if (stub.Population < _cfg.DiseaseMinPop) continue;
                float density        = Math.Min(1f, (float)stub.Population / Math.Max(1, stub.CarryingCapacity));
                float outbreakChance = _cfg.DiseaseBaseChance * (1f + density * _cfg.DiseaseDensityMult);
                float roll = WorldRng.FloatAt(world.WorldSeed, year, coord.X * 31 + coord.Y, 0, S.PopDiseaseOutbreak);
                if (roll < outbreakChance) toInfect.Add(coord);
            }
        }

        foreach (var coord in toInfect)
        {
            if (!world.Settlements.TryGetValue(coord, out var stub) || stub.IsInfected) continue;
            world.Settlements[coord] = stub with { IsInfected = true, InfectedSinceYear = year };
            pending.Add(new PendingEvent(EventType.DiseaseOutbreak, coord, null,
                JsonSerializer.Serialize(new DiseaseOutbreakPayload(stub.Population)),
                CivId: stub.CivId.Value, SettlementName: stub.Name));
        }

        foreach (var coord in toRecover)
        {
            if (!world.Settlements.TryGetValue(coord, out var stub)) continue;
            world.Settlements[coord] = stub with { IsInfected = false, InfectedSinceYear = 0 };
            pending.Add(new PendingEvent(EventType.DiseaseRecovered, coord, null,
                JsonSerializer.Serialize(new DiseaseRecoveredPayload(
                    stub.Population, year - stub.InfectedSinceYear)),
                CivId: stub.CivId.Value, SettlementName: stub.Name));
        }
    }

    // ─── Annual wildlife attacks ──────────────────────────────────────────────

    /// <summary>
    /// Annual pass: each settlement has a chance of a wildlife raid proportional to its
    /// vulnerability (inverse of population). Large settlements can defend themselves.
    /// </summary>
    private void RunAnnualWildlifeAttacks(WorldState world, List<PendingEvent> pending)
    {
        int year = world.CurrentYear;
        foreach (var (coord, stub) in world.Settlements.ToList())
        {
            if (stub.Population <= 0) continue;

            var biome         = (BiomeType)world.TileGrid.GetTile(coord).BiomeType;
            float biomeMult   = BiomeWildlifeRisk(biome);
            float sizeDefense = Math.Min(1f, (float)stub.Population / _cfg.WildlifeDefensePopScale);
            float attackChance = _cfg.WildlifeAttackBaseChance * biomeMult * (1f - sizeDefense * 0.8f);
            float roll = WorldRng.FloatAt(world.WorldSeed, year, coord.X * 31 + coord.Y, 0, S.PopWildlife);
            if (roll >= attackChance) continue;

            int damage = Math.Max(1, (int)(stub.Population * _cfg.WildlifeAttackDamage * (1f - sizeDefense)));

            // Named characters at the settlement can defend, reducing casualties and taking wounds
            long defenderId = 0;
            string? defenderName = null;
            foreach (var e in world.GetEntitiesAt(coord))
            {
                if (e is not Tier1Character defender || !defender.IsAlive) continue;
                if (defender.Identity.CivId != stub.CivId) continue;

                var charCfg = _simCfg.Character;
                float defenseReduction = defender.Skills.Combat * charCfg.WildlifeCharDefenseReduction;
                damage = Math.Max(1, (int)(damage * (1f - defenseReduction)));
                int injury = Math.Max(1, (int)(charCfg.MaxHealth * charCfg.WildlifeCharInjuryFraction));
                defender.Health -= injury;
                defender.Skills  = defender.Skills with
                    { Combat = Math.Min(1f, defender.Skills.Combat + 0.01f) };
                defenderId   = defender.Id.Value;
                defenderName = defender.Identity.Name;
                break;
            }

            world.Settlements[coord] = stub with { Population = Math.Max(0, stub.Population - damage) };
            pending.Add(new PendingEvent(EventType.WildlifeRaid, coord, null,
                JsonSerializer.Serialize(new WildlifeRaidPayload(
                    stub.Population, damage, defenderId, defenderName)),
                CivId: stub.CivId.Value, SettlementName: stub.Name));
        }
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    // Open terrain (plains/savanna/desert) offers visibility — raiding predators prefer dense cover.
    // Forest/jungle/swamp multipliers are above 1.0; open biomes below 1.0.
    private static float BiomeWildlifeRisk(BiomeType biome) => biome switch
    {
        BiomeType.TropicalRainforest => 2.0f,
        BiomeType.BorealForest       => 1.6f,
        BiomeType.TemperateForest    => 1.4f,
        BiomeType.Swamp              => 1.5f,
        BiomeType.Grassland          => 1.0f,
        BiomeType.Hills              => 0.9f,
        BiomeType.Mountain           => 0.8f,
        BiomeType.Savanna            => 0.6f,
        BiomeType.Plains             => 0.5f,
        BiomeType.Desert             => 0.4f,
        BiomeType.Tundra             => 0.5f,
        BiomeType.HighMountain       => 0.3f,
        BiomeType.Volcanic           => 0.4f,
        _                            => 0.6f
    };

    private static PendingEvent MakePopEvent(EventType type, SettlementStub stub, TileCoord tile, int newPop)
    {
        var payload = JsonSerializer.Serialize(new { oldPop = stub.Population, newPop });
        return new PendingEvent(type, tile, null, payload,
            CivId: stub.CivId.Value, SettlementName: stub.Name);
    }
}
