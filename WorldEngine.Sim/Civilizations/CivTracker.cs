using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
/// </summary>
public static partial class CivTracker
{
    private const int SettlementStartPop    = 50;
    private const int SettlementStartHealth = 100;

    public static void Resolve(
        ICommand cmd,
        WorldState world,
        List<PendingEvent> pending,
        SettlementNamesConfig? namesConfig = null)
    {
        switch (cmd)
        {
            case EstablishSettlement es:
                ResolveEstablish(es, world, pending, namesConfig ?? new()); break;
            case AllyWith aw:
                ResolveAlly(aw, world, pending); break;
            case DeclareRivalry dr:
                ResolveRivalry(dr, world, pending); break;
            case DeclareWar dw:
                ResolveWar(dw, world, pending); break;
            case RaidSettlement rs:
                ResolveRaid(rs, world, pending); break;
            case Negotiate ng:
                ResolveNegotiate(ng, world, pending); break;
            case BuildImprovement bi:
                ResolveBuildImprovement(bi, world, pending); break;
        }
    }

    // ─── Establish ────────────────────────────────────────────────────────────

    private static void ResolveEstablish(
        EstablishSettlement cmd, WorldState world, List<PendingEvent> pending,
        SettlementNamesConfig namesConfig)
    {
        if (world.Settlements.ContainsKey(cmd.Tile)) return;
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character founder) return;

        // Reject founding if any existing settlement (any civ) is within GlobalSettlementMinDist tiles
        int globalMinDist = world.SimConfig.Character.GlobalSettlementMinDist;
        if (globalMinDist > 0 && world.Settlements.Values.Any(s =>
            Math.Sqrt(Math.Pow(s.Tile.X - cmd.Tile.X, 2) + Math.Pow(s.Tile.Y - cmd.Tile.Y, 2)) < globalMinDist))
            return;

        // Create settlement
        var civId = founder.Identity.CivId;
        bool newCiv = !civId.IsValid;
        if (newCiv)
        {
            civId = new CivId(world.NextCivId++);
            string civSuffix = GetCivNameSuffix(founder.Identity.AncestryId, world.SimConfig.AncestryRegistry);
            string civName   = $"{founder.Identity.Name}'s {civSuffix}";
            var civ = new Civilization(civId, civName, founder.Id, cmd.Tile, world.CurrentYear);
            civ.Members.Add(founder.Id);
            world.Civilizations[civId] = civ;
            founder.Identity = founder.Identity with { CivId = civId, RulerOrdinal = 1 };

            // Build initial cultural profile from founding ancestry
            var capitalBiome = (BiomeType)world.TileGrid.GetTile(cmd.Tile).BiomeType;
            civ.CulturalProfile = BuildCulturalProfile(
                founder.Identity.AncestryId, capitalBiome, world.SimConfig.AncestryRegistry, []);

            FireCivFounded(civ, founder, world, pending);
        }
        else
        {
            world.Civilizations[civId].Members.Add(founder.Id);
        }

        string settlementName    = GenerateSettlementName(cmd.Tile, world, namesConfig);
        settlementName           = ApplyCulturalSettlementName(
            settlementName, founder.Identity.AncestryId, world.SimConfig.AncestryRegistry);
        float  fertilityVariance = GenerateFertilityMultiplier(cmd.Tile, world);

        // Classify: colony if no same-civ settlement is within ColonyMinDistance tiles
        int colonyMinDist = world.SimConfig.Character.ColonyMinDistance;
        bool isColony = !newCiv && !world.Settlements.Values
            .Any(s => s.CivId == civId
                   && Math.Sqrt(Math.Pow(s.Tile.X - cmd.Tile.X, 2) + Math.Pow(s.Tile.Y - cmd.Tile.Y, 2)) < colonyMinDist);

        var stub = new SettlementStub(
            FounderId:           founder.Id,
            CivId:               civId,
            Tile:                cmd.Tile,
            FoundedYear:         world.CurrentYear,
            Population:          SettlementStartPop,
            Health:              SettlementStartHealth,
            Name:                settlementName,
            FertilityMultiplier: fertilityVariance,
            IsColony:            isColony);
        world.Settlements[cmd.Tile] = stub;
        world.AddActiveFounder(founder.Id);
        var civRecord = world.Civilizations[civId];
        if (isColony) civRecord.ColonyCount++;
        else          civRecord.SettlementCount++;
        civRecord.LastSettlementFoundedYear = world.CurrentYear;
        civRecord.TotalSettlementsFounded++;

        // Claim initial territory around the new city
        ClaimInitialTerritory(cmd.Tile, civId, world, pending);

        // Mark goal as progressed for FoundCity delegated founders
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.FoundCity)
                g.Progress = Math.Min(1f, g.Progress + 0.5f);

        FireSettlementFounded(stub, founder, world, pending);

        founder.Needs = founder.Needs with
        {
            Status  = Math.Min(1f, founder.Needs.Status  + 0.2f),
            Purpose = Math.Min(1f, founder.Needs.Purpose + 0.15f)
        };
        founder.Skills = founder.Skills with
        {
            Leadership    = Math.Min(1f, founder.Skills.Leadership    + 0.02f),
            Administration = Math.Min(1f, founder.Skills.Administration + 0.02f)
        };
    }

    // ─── Alliance ─────────────────────────────────────────────────────────────

    private static void ResolveAlly(AllyWith cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsAlly) return;

        var cfg = world.SimConfig.Character;

        // Cross-civ only — same-civ relationships are just trust edges
        if (c.Identity.CivId.IsValid && target.Identity.CivId.IsValid
            && c.Identity.CivId == target.Identity.CivId) return;

        // Alliance cap
        int allianceMax = cfg.AllianceMaxBase + (int)(c.Personality.Sociability * cfg.AllianceMaxPerSociability);
        if (world.Relationships.CountAlliances(c.Id) >= allianceMax) return;

        // Enemy-of-ally: if target is allied with any of c's rivals, drain that relationship
        foreach (var bEdge in world.Relationships.GetAll(target.Id).Where(e => e.IsAlly).ToList())
        {
            var thirdId = bEdge.From == target.Id ? bEdge.To : bEdge.From;
            var cThird  = world.Relationships.Get(c.Id, thirdId);
            if (cThird?.IsRival ?? false)
            {
                world.Relationships.Upsert(cThird with
                {
                    Trust = Math.Clamp(cThird.Trust - cfg.EnemyOfAllyTrustDrain, -1f, 1f)
                });
            }
        }

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(1f, rel.Trust + 0.3f),
            Flags = rel.Flags | RelationshipFlags.IsAlly
        });

        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.15f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.1f) };
        c.Skills     = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.02f) };

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Alliance && g.TargetEntityId == target.Id)
                g.Progress = 1f;

        var payload = JsonSerializer.Serialize(new AllianceFormedPayload(
            c.Id.Value, c.Identity.Name,
            target.Id.Value, target.Identity.Name,
            c.Identity.CivId.Value, target.Identity.CivId.Value));
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.Identity.CivId.Value));
    }

    // ─── Rivalry ──────────────────────────────────────────────────────────────

    private static void ResolveRivalry(
        DeclareRivalry cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsRival) return;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust, -0.1f),
            Fear  = Math.Min(1f, rel.Fear + 0.1f),
            Flags = rel.Flags | RelationshipFlags.IsRival
        });

        var payload = JsonSerializer.Serialize(new RivalryFormedPayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── Ruin registration ────────────────────────────────────────────────────

    /// <summary>
    /// Records a settlement tile as a ruin. Increments TimesSettled if the tile has been ruined before.
    /// Returns the new TimesSettled count. Releases all territory tiles claimed by this city.
    /// </summary>
    public static int RegisterRuin(
        TileCoord tile, SettlementStub stub, string cause, WorldState world,
        List<PendingEvent>? pending = null)
    {
        int timesSettled = world.Ruins.TryGetValue(tile, out var existing)
            ? existing.TimesSettled + 1
            : 1;

        world.Ruins[tile] = new RuinRecord(
            Tile:           tile,
            SettlementName: stub.Name,
            OriginalCivId:  stub.CivId,
            DestroyedYear:  world.CurrentYear,
            Cause:          cause,
            TimesSettled:   timesSettled);

        world.RemoveActiveFounder(stub.FounderId);

        if (world.Civilizations.TryGetValue(stub.CivId, out var civ))
        {
            if (stub.IsColony) civ.ColonyCount    = Math.Max(0, civ.ColonyCount    - 1);
            else               civ.SettlementCount = Math.Max(0, civ.SettlementCount - 1);

            // Release territory tiles
            ReleaseTerritory(tile, stub.CivId, civ.Name, cause, world, pending);
        }

        return timesSettled;
    }

    // ─── Build improvement ────────────────────────────────────────────────────

    private static void ResolveBuildImprovement(
        BuildImprovement cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character builder) return;
        if (!builder.Identity.CivId.IsValid) return;

        var targetTile = cmd.TargetTile;

        // Validate: tile must still be in this civ's territory with no existing improvement
        if (!world.TerritoryMap.TryGetValue(targetTile, out var cityTile)) return;
        if (world.ImprovementMap.ContainsKey(targetTile)) return;

        var civ = world.GetCivilization(builder.Identity.CivId);
        if (civ == null || !civ.CityTerritories.ContainsKey(cityTile)) return;

        // Advance progress on the character's BuildImprovement goal (ImprovementBuildTicks ticks to complete)
        var buildGoal = builder.Goals.FirstOrDefault(g => g.Type == GoalType.BuildImprovement
                                                       && g.TargetTile == targetTile);
        if (buildGoal == null) return;

        int buildTicks = world.SimConfig.Improvements.ImprovementBuildTicks;
        buildGoal.Progress = Math.Min(1f, buildGoal.Progress + 1f / Math.Max(1, buildTicks));

        if (buildGoal.Progress < 1f) return; // still building

        // Construction complete — place the improvement
        var improvement = new TileImprovement(cmd.ImprovementType, cityTile, world.CurrentYear, cmd.CharacterId);
        world.ImprovementMap[targetTile] = improvement;

        // Remove the completed goal
        builder.Goals.Remove(buildGoal);

        builder.Needs = builder.Needs with
        {
            Purpose = Math.Min(1f, builder.Needs.Purpose + 0.15f),
            Status  = Math.Min(1f, builder.Needs.Status  + 0.1f)
        };
        builder.Skills = builder.Skills with
        {
            Administration = Math.Min(1f, builder.Skills.Administration + 0.02f)
        };

        string settName = world.Settlements.TryGetValue(cityTile, out var sett) ? sett.Name : null!;
        var payload = System.Text.Json.JsonSerializer.Serialize(new ImprovementBuiltPayload(
            cmd.CharacterId.Value, builder.Identity.Name,
            builder.Identity.CivId.Value, targetTile.X, targetTile.Y,
            cmd.ImprovementType.ToString()));
        pending.Add(new PendingEvent(EventType.ImprovementBuilt, targetTile, null, payload,
            new[] { cmd.CharacterId.Value },
            ActorId: cmd.CharacterId.Value, ActorName: builder.Identity.Name,
            CivId: builder.Identity.CivId.Value, SettlementName: settName));
    }

    // ─── Territory helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Claims all unclaimed land tiles within InitialCityClaimRadius around the new city tile.
    /// The city tile always claims itself. Writes to both TerritoryMap and CityTerritories.
    /// </summary>
    private static void ClaimInitialTerritory(
        TileCoord cityTile, CivId civId, WorldState world, List<PendingEvent> pending)
    {
        if (!world.Civilizations.TryGetValue(civId, out var civ)) return;

        var cfg = world.SimConfig.Territory;
        if (!civ.CityTerritories.ContainsKey(cityTile))
            civ.CityTerritories[cityTile] = new HashSet<TileCoord>();

        var owned = civ.CityTerritories[cityTile];

        // Collect unclaimed land tiles in radius, sorted by fertility descending
        var candidates = world.GetTilesInRadius(cityTile, cfg.InitialCityClaimRadius)
            .Where(t => world.IsLand(t) && !world.TerritoryMap.ContainsKey(t))
            .OrderByDescending(t => world.TileGrid.GetTile(t).Fertility)
            .ToList();

        // City tile always claims itself first (may already be in candidates or not)
        if (!world.TerritoryMap.ContainsKey(cityTile))
        {
            world.TerritoryMap[cityTile] = cityTile;
            owned.Add(cityTile);
        }

        foreach (var t in candidates)
        {
            if (t == cityTile) continue; // already handled
            world.TerritoryMap[t] = cityTile;
            owned.Add(t);
        }

        if (owned.Count == 0) return;

        var payload = JsonSerializer.Serialize(new TerritoryExpandedPayload(
            civId.Value, civ.Name, cityTile.X, cityTile.Y, owned.Count, owned.Count));
        pending.Add(new PendingEvent(EventType.TerritoryExpanded, cityTile, null, payload,
            CivId: civId.Value, SettlementName: world.Settlements.TryGetValue(cityTile, out var s) ? s.Name : null));
    }

    /// <summary>
    /// Releases all territory tiles belonging to a city. Called on abandonment or destruction.
    /// </summary>
    internal static void ReleaseTerritory(
        TileCoord cityTile, CivId civId, string civName, string reason,
        WorldState world, List<PendingEvent>? pending)
    {
        if (!world.Civilizations.TryGetValue(civId, out var civ)) return;
        if (!civ.CityTerritories.TryGetValue(cityTile, out var tiles)) return;

        int count = tiles.Count;
        foreach (var t in tiles)
            world.TerritoryMap.Remove(t);
        civ.CityTerritories.Remove(cityTile);

        // Also remove any improvements on released tiles
        foreach (var t in tiles)
            world.ImprovementMap.Remove(t);

        if (pending != null && count > 0)
        {
            var payload = JsonSerializer.Serialize(new TerritoryLostPayload(
                civId.Value, civName, cityTile.X, cityTile.Y, count, 0, reason));
            pending.Add(new PendingEvent(EventType.TerritoryLost, cityTile, null, payload,
                CivId: civId.Value));
        }
    }

    /// <summary>
    /// Reassigns all territory tiles of a conquered city to the nearest city of the winning civ.
    /// Updates both TerritoryMap and both Civilization.CityTerritories dicts.
    /// </summary>
    internal static void TransferTerritory(
        TileCoord conqueredCityTile, CivId losingCivId, CivId winningCivId,
        WorldState world)
    {
        if (!world.Civilizations.TryGetValue(losingCivId, out var losingCiv)) return;
        if (!world.Civilizations.TryGetValue(winningCivId, out var winningCiv)) return;
        if (!losingCiv.CityTerritories.TryGetValue(conqueredCityTile, out var tiles)) return;

        losingCiv.CityTerritories.Remove(conqueredCityTile);

        // Find the nearest winning-civ city to absorb these tiles
        TileCoord? nearestCity = null;
        float nearestDist = float.MaxValue;
        foreach (var cityTile in winningCiv.CityTerritories.Keys)
        {
            int dx = cityTile.X - conqueredCityTile.X, dy = cityTile.Y - conqueredCityTile.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < nearestDist) { nearestDist = dist; nearestCity = cityTile; }
        }

        // If no existing city found, let the conquered tile become its own city entry
        var targetCity = nearestCity ?? conqueredCityTile;
        if (!winningCiv.CityTerritories.TryGetValue(targetCity, out var winnerTiles))
            winningCiv.CityTerritories[targetCity] = winnerTiles = new HashSet<TileCoord>();

        foreach (var t in tiles)
        {
            world.TerritoryMap[t] = targetCity;
            winnerTiles.Add(t);
        }

        // Update improvements to reflect new city ownership
        foreach (var t in tiles)
        {
            if (world.ImprovementMap.TryGetValue(t, out var imp))
                world.ImprovementMap[t] = imp with { CityTile = targetCity };
        }
    }
}
