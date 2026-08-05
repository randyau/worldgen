using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

public static partial class CivTracker
{
    private const int SaltSecessionRoll = 810;

    // DECISION: unrest lives in CivTracker (not PopulationDynamicsPhase) because secession
    // creates civilizations, transfers territory, and integrates with succession-crisis and
    // border-tension state — all civ-level concerns that CivTracker already owns. The pass is
    // called once per year from RunAnnualDiplomacy, alongside the other annual civ passes.

    /// <summary>
    /// Annual unrest update + secession roll for every settlement.
    /// Unrest drivers: distance from capital beyond a comfort radius, civ size above a soft
    /// city threshold, active famine, and a succession-crisis multiplier. Unrest decays toward
    /// zero when no drivers apply. When unrest crosses the secession threshold, an annual
    /// probabilistic roll decides whether the settlement (plus nearby high-unrest same-civ
    /// settlements) secedes and forms a new civilization.
    /// </summary>
    internal static void RunUnrestAndSecession(WorldState world, List<PendingEvent> pending)
    {
        var cfg = world.SimConfig.Unrest;

        // Pass 1: update unrest for every settlement (record rewrite; sim thread only).
        // Snapshot keys first — we mutate the dictionary values as we go.
        var tiles = world.Settlements.Keys.ToList();
        foreach (var tile in tiles)
        {
            var stub = world.Settlements[tile];
            if (!world.Civilizations.TryGetValue(stub.CivId, out var civ) || civ.IsCollapsed)
                continue;

            float accrual = 0f;

            // Driver 1: distance from capital beyond comfort radius
            int dx = tile.X - civ.CapitalTile.X, dy = tile.Y - civ.CapitalTile.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > cfg.UnrestComfortRadius)
                accrual += (dist - cfg.UnrestComfortRadius) * cfg.UnrestDistancePerTile;

            // Driver 2: civ size above the soft city threshold (empire overstretch).
            // Only applies to non-capital settlements — the capital never resents itself.
            int civCities = civ.SettlementCount + civ.ColonyCount;
            bool isCapital = tile == civ.CapitalTile;
            if (!isCapital && civCities > cfg.UnrestSoftCityThreshold)
                accrual += (civCities - cfg.UnrestSoftCityThreshold) * cfg.UnrestPerExcessCity;

            // Driver 3: active famine / food crisis
            if (stub.FoodPressureRatio < world.SimConfig.ResourcePressure.CrisisThreshold)
                accrual += cfg.UnrestFamineBonus;

            // Succession crisis multiplier on all sources
            bool inCrisis = civ.SuccessionCrisisEndYear != int.MinValue
                         && world.CurrentYear < civ.SuccessionCrisisEndYear;
            if (inCrisis) accrual *= cfg.UnrestSuccessionMult;

            // bonus_civ_cohesion (M9 9.1, Artisan notable work): dampens accrual, capped so
            // accumulated cohesion can reduce but never fully cancel unrest drivers.
            if (accrual > 0f)
                accrual = Math.Max(0f, accrual - Math.Min(cfg.CohesionBonusCap,
                    stub.GetStore("bonus_civ_cohesion") * cfg.CohesionBonusScale));

            float unrest = stub.Unrest;
            if (accrual > 0f)
                unrest = Math.Clamp(unrest + accrual, 0f, 1f);
            else
                unrest = Math.Max(0f, unrest * (1f - cfg.UnrestDecayRate)); // slow decay when calm

            if (Math.Abs(unrest - stub.Unrest) > 1e-6f)
                world.Settlements[tile] = stub with { Unrest = unrest };
        }

        // Pass 2: secession rolls. At most one secession per civ per year — the probabilistic
        // roll plus this cap prevents an all-settlements-secede-same-tick cascade.
        var secededCivs = new HashSet<CivId>();
        foreach (var tile in tiles)
        {
            if (!world.Settlements.TryGetValue(tile, out var stub)) continue; // may have transferred
            if (stub.Unrest < cfg.UnrestSecessionThreshold) continue;
            if (!world.Civilizations.TryGetValue(stub.CivId, out var civ) || civ.IsCollapsed) continue;
            if (secededCivs.Contains(stub.CivId)) continue;
            if (tile == civ.CapitalTile) continue;              // capitals do not secede from themselves
            if (civ.SettlementCount + civ.ColonyCount < 2) continue; // nothing to splinter from

            // Ramp secession probability with civ population — no cliff at the floor
            int civPop = world.Settlements.Values.Where(s => s.CivId == stub.CivId).Sum(s => s.Population);
            float popFactor = Math.Clamp(
                (civPop - cfg.SecessionMinCivPop) / (float)cfg.SecessionPopRampRange, 0f, 1f);
            if (popFactor <= 0f) continue;

            float roll = WorldRng.FloatAt(world.WorldSeed, world.CurrentYear,
                                          tile.X, tile.Y, SaltSecessionRoll);
            if (roll >= cfg.UnrestSecessionChance * popFactor) continue;

            secededCivs.Add(stub.CivId);
            ExecuteSecession(tile, civ, world, pending, cfg);
        }
    }

    /// <summary>
    /// Executes a secession: gathers a cluster of nearby high-unrest same-civ settlements,
    /// creates a new civilization led by a local character (or one promoted from the settlement
    /// population), transfers settlements + territory + population, and fires CivSplintered.
    /// </summary>
    private static void ExecuteSecession(
        TileCoord leadTile, Civilization parent, WorldState world,
        List<PendingEvent> pending, UnrestConfig cfg)
    {
        var leadStub = world.Settlements[leadTile];

        // ── Gather the seceding cluster ───────────────────────────────────────
        var cluster = new List<TileCoord> { leadTile };
        int r2 = cfg.UnrestClusterRadius * cfg.UnrestClusterRadius;
        foreach (var (tile, stub) in world.Settlements)
        {
            if (tile == leadTile || stub.CivId != parent.Id) continue;
            if (tile == parent.CapitalTile) continue;
            if (stub.Unrest < cfg.UnrestClusterMinUnrest) continue;

            int dx = tile.X - leadTile.X, dy = tile.Y - leadTile.Y;
            if (dx * dx + dy * dy > r2) continue;

            // Must be closer to the secessionist settlement than to the parent capital
            int cx = tile.X - parent.CapitalTile.X, cy = tile.Y - parent.CapitalTile.Y;
            if (dx * dx + dy * dy < cx * cx + cy * cy)
                cluster.Add(tile);
        }

        // ── Find or promote a leader ──────────────────────────────────────────
        // Prefer a living non-ruler parent-civ Tier1 located at a cluster settlement.
        Tier1Character? leader = null;
        var clusterSet = new HashSet<TileCoord>(cluster);
        foreach (var memberId in parent.Members)
        {
            if (world.GetEntity(memberId) is not Tier1Character m || !m.IsAlive) continue;
            if (m.Id == parent.RulerId) continue;
            if (clusterSet.Contains(m.Location)) { leader = m; break; }
        }

        if (leader is null)
        {
            // Promote from settlement population (reuses the auto-succession spawn approach).
            long seq = (400_000L + world.CurrentYear * 997L + leadTile.X * 31L + leadTile.Y) & 0x7FFFFFFF;
            var tileData = world.TileGrid.GetTile(leadTile);
            leader = CharacterFactory.Spawn(leadTile, (BiomeType)tileData.BiomeType,
                world.WorldSeed, seq, world.SimConfig, world.CurrentYear, startAsAdult: true);
            int ordinal = world.ClaimNameOrdinal(leader.Identity.Name);
            if (ordinal > 0)
                leader.Identity = leader.Identity with { NameOrdinal = ordinal };
            world.Entities.Add(leader);
            pending.Add(new PendingEvent(EventType.CharacterBorn, leadTile, null,
                JsonSerializer.Serialize(new CharacterBornPayload(
                    leader.Id.Value, leader.Identity.Name, leader.Identity.Epithet,
                    leader.Personality.Ambition, leader.Personality.Aggression,
                    Source: "secession", AncestryId: leader.Identity.AncestryId)),
                new[] { leader.Id.Value },
                ActorId: leader.Id.Value, ActorName: leader.Identity.Name));
        }

        // ── Create the new civilization ───────────────────────────────────────
        var newCivId = new CivId(world.NextCivId++);
        string suffix  = GetCivNameSuffix(leader.Identity.AncestryId, world.SimConfig.AncestryRegistry);
        string civName = $"{leader.Identity.Name}'s {suffix}";
        var newCiv = new Civilization(newCivId, civName, leader.Id, leadTile, world.CurrentYear);
        world.Civilizations[newCivId] = newCiv;
        newCiv.OrgId = CreateOrganization(world, OrganizationKind.Civilization, civName, leader.Id, leadTile);

        parent.Members.Remove(leader.Id);
        newCiv.Members.Add(leader.Id);
        SetCharacterCiv(leader, newCivId, OrganizationRole.Leader, world);
        leader.Identity = leader.Identity with { RulerOrdinal = 1 };

        var capitalBiome = (BiomeType)world.TileGrid.GetTile(leadTile).BiomeType;
        newCiv.CulturalProfile = BuildCulturalProfile(
            leader.Identity.AncestryId, capitalBiome, world.SimConfig.AncestryRegistry, []);

        // Move any other parent members located in the cluster to the new civ
        var movingMembers = new List<EntityId>();
        foreach (var memberId in parent.Members)
        {
            if (world.GetEntity(memberId) is Tier1Character m && m.IsAlive
                && clusterSet.Contains(m.Location))
                movingMembers.Add(memberId);
        }
        foreach (var id in movingMembers)
        {
            parent.Members.Remove(id);
            newCiv.Members.Add(id);
            if (world.GetEntity(id) is Tier1Character m)
                SetCharacterCiv(m, newCivId, OrganizationRole.Member, world);
        }

        // ── Transfer settlements, counts, territory, population ───────────────
        int popTransferred = 0;
        foreach (var tile in cluster)
        {
            var stub = world.Settlements[tile];
            popTransferred += stub.Population;
            world.Settlements[tile] = stub with { CivId = newCivId, Unrest = 0f };

            if (stub.IsColony) { parent.ColonyCount = Math.Max(0, parent.ColonyCount - 1); newCiv.ColonyCount++; }
            else               { parent.SettlementCount = Math.Max(0, parent.SettlementCount - 1); newCiv.SettlementCount++; }

            // Move the city's territory entry wholesale — TerritoryMap already points each
            // owned tile at this city tile, so only the CityTerritories owner needs to change.
            if (parent.CityTerritories.TryGetValue(tile, out var owned))
            {
                parent.CityTerritories.Remove(tile);
                newCiv.CityTerritories[tile] = owned;
            }
        }
        newCiv.LastSettlementFoundedYear = world.CurrentYear;

        // ── Initial diplomatic tension toward the parent (feeds war triggers) ─
        newCiv.BorderTension[parent.Id] = cfg.SplinterInitialTension;
        parent.BorderTension[newCivId]  = cfg.SplinterInitialTension;

        // ── Events ────────────────────────────────────────────────────────────
        FireCivFounded(newCiv, leader, world, pending, "Splinter", parent.Id.Value, parent.Name);
        pending.Add(new PendingEvent(EventType.CivSplintered, leadTile, null,
            JsonSerializer.Serialize(new CivSplinteredPayload(
                parent.Id.Value, parent.Name,
                newCivId.Value, civName,
                leader.Id.Value, leader.Identity.Name,
                cluster.Count, popTransferred, leadStub.Unrest,
                leadTile.X, leadTile.Y)),
            new[] { leader.Id.Value },
            ActorId: leader.Id.Value, ActorName: leader.Identity.Name,
            CivId: newCivId.Value, SettlementName: leadStub.Name));
    }
}
