using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// </summary>
public static class CivTracker
{
    private const int SettlementStartPop    = 50;
    private const int SettlementStartHealth = 100;
    private const int RaidDamageMin         = 10;
    private const int RaidDamageMax         = 30;
    private const int SaltRaidDamage        = 700;

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
        }
    }

    // ─── Establish ────────────────────────────────────────────────────────────

    private static void ResolveEstablish(
        EstablishSettlement cmd, WorldState world, List<PendingEvent> pending,
        SettlementNamesConfig namesConfig)
    {
        if (world.Settlements.ContainsKey(cmd.Tile)) return;
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character founder) return;

        // Create settlement
        var civId = founder.Identity.CivId;
        bool newCiv = !civId.IsValid;
        if (newCiv)
        {
            civId = new CivId(world.NextCivId++);
            string civName = $"{founder.Identity.Name}'s Domain";
            var civ = new Civilization(civId, civName, founder.Id, cmd.Tile, world.CurrentYear);
            civ.Members.Add(founder.Id);
            world.Civilizations[civId] = civ;
            founder.Identity = founder.Identity with { CivId = civId };

            FireCivFounded(civ, founder, world, pending);
        }
        else
        {
            world.Civilizations[civId].Members.Add(founder.Id);
        }

        string settlementName    = GenerateSettlementName(cmd.Tile, world, namesConfig);
        float  fertilityVariance = GenerateFertilityMultiplier(cmd.Tile, world);
        var stub = new SettlementStub(
            FounderId:           founder.Id,
            CivId:               civId,
            Tile:                cmd.Tile,
            FoundedYear:         world.CurrentYear,
            Population:          SettlementStartPop,
            Health:              SettlementStartHealth,
            Name:                settlementName,
            FertilityMultiplier: fertilityVariance);
        world.Settlements[cmd.Tile] = stub;
        world.Civilizations[civId].LastSettlementFoundedYear = world.CurrentYear;

        // Mark goal as progressed
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.Expansion) g.Progress = Math.Min(1f, g.Progress + 0.5f);

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

        var payload = JsonSerializer.Serialize(new
        {
            declarerId   = c.Id.Value,
            declarerName = c.Identity.Name,
            targetId     = target.Id.Value,
            targetName   = target.Identity.Name,
            declarerCiv  = c.Identity.CivId.Value,
            targetCiv    = target.Identity.CivId.Value,
            location     = new[] { c.Location.X, c.Location.Y }
        });
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
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

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            charName    = c.Identity.Name,
            targetName  = target.Identity.Name
        });
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── War ──────────────────────────────────────────────────────────────────

    private static void ResolveWar(DeclareWar cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsAtWar) return;

        bool wasAllied = rel.IsAlly;
        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust - 0.3f, -0.3f),
            Flags = (rel.Flags & ~RelationshipFlags.IsAlly) | RelationshipFlags.IsAtWar | RelationshipFlags.IsRival
        });

        if (wasAllied)
            FireAllianceBroken(c, target, "war_declared", world, pending);

        var payload = JsonSerializer.Serialize(new
        {
            declarerId   = c.Id.Value,
            declarerName = c.Identity.Name,
            targetId     = target.Id.Value,
            targetName   = target.Identity.Name,
            declarerCiv  = c.Identity.CivId.Value,
            targetCiv    = target.Identity.CivId.Value
        });
        pending.Add(new PendingEvent(EventType.WarDeclared, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));

        // Notify target's allies: drain trust toward the aggressor, seed a Protect goal
        var cfg = world.SimConfig.Character;
        foreach (var allyEdge in world.Relationships.GetAll(target.Id).Where(e => e.IsAlly).ToList())
        {
            var allyId = allyEdge.From == target.Id ? allyEdge.To : allyEdge.From;
            if (world.GetEntity(allyId) is not Tier1Character ally || !ally.IsAlive) continue;
            if (ally.Identity.CivId == c.Identity.CivId) continue;

            var allyAggrRel = world.Relationships.GetOrCreate(ally.Id, c.Id);
            world.Relationships.Upsert(allyAggrRel with
            {
                Trust = Math.Clamp(allyAggrRel.Trust - cfg.AllianceWarTrustDrain, -1f, 1f)
            });

            bool hasProtect = ally.Goals.Any(g => g.Type == GoalType.Protect && g.TargetEntityId == target.Id);
            if (!hasProtect)
                ally.Goals.Add(new GoalData
                {
                    Type           = GoalType.Protect,
                    Object         = GoalObject.Person,
                    TargetEntityId = target.Id,
                    Priority       = cfg.AllyProtectGoalIntensity,
                    Intensity      = cfg.AllyProtectGoalIntensity,
                    FormedTick     = (int)world.CurrentTick,
                    StaleSince     = (int)world.CurrentTick
                });
        }
    }

    // ─── Raid ─────────────────────────────────────────────────────────────────

    private static void ResolveRaid(
        RaidSettlement cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character raider) return;
        if (!world.Settlements.TryGetValue(cmd.SettlementTile, out var settlement)) return;

        int damage = RaidDamageMin
            + (int)(world.GetRandomFloat(raider.Id, SaltRaidDamage)
                    * (RaidDamageMax - RaidDamageMin));
        int newHealth = settlement.Health - damage;

        raider.Skills = raider.Skills with
            { Combat = Math.Min(1f, raider.Skills.Combat + 0.02f) };
        raider.Needs = raider.Needs with
            { Status = Math.Min(1f, raider.Needs.Status + 0.1f) };

        var payload = JsonSerializer.Serialize(new
        {
            raiderId   = raider.Id.Value,
            raiderName = raider.Identity.Name,
            tile       = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
            damage,
            settlementHealth = newHealth
        });
        pending.Add(new PendingEvent(EventType.BattleOccurred, cmd.SettlementTile, null, payload,
            new[] { raider.Id.Value }));

        if (newHealth <= 0)
        {
            world.Settlements.Remove(cmd.SettlementTile);

            int timesSettled = RegisterRuin(cmd.SettlementTile, settlement, "destroyed", world);

            pending.Add(new PendingEvent(EventType.SettlementDestroyed, cmd.SettlementTile, null,
                JsonSerializer.Serialize(new
                {
                    tile           = new[] { cmd.SettlementTile.X, cmd.SettlementTile.Y },
                    settlementName = settlement.Name,
                    founderId      = settlement.FounderId.Value,
                    destroyerId    = raider.Id.Value,
                    civId          = settlement.CivId.Value,
                    timesSettled
                })));
        }
        else
        {
            world.Settlements[cmd.SettlementTile] = settlement with { Health = newHealth };
        }
    }

    // ─── Negotiate ────────────────────────────────────────────────────────────

    private static void ResolveNegotiate(
        Negotiate cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        float trustGain = 0.05f + c.Skills.Diplomacy * 0.1f;
        world.Relationships.Upsert(rel with { Trust = Math.Clamp(rel.Trust + trustGain, -1f, 1f) });

        c.Skills = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.01f) };

        var payload = JsonSerializer.Serialize(new
        {
            characterId = c.Id.Value,
            targetId    = target.Id.Value,
            trustGain
        });
        pending.Add(new PendingEvent(EventType.Negotiated, c.Location, null, payload,
            new[] { c.Id.Value, target.Id.Value }));
    }

    // ─── Ruin registration ────────────────────────────────────────────────────

    /// <summary>
    /// Records a settlement tile as a ruin. Increments TimesSettled if the tile has been ruined before.
    /// Returns the new TimesSettled count.
    /// </summary>
    public static int RegisterRuin(
        TileCoord tile, SettlementStub stub, string cause, WorldState world)
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

        return timesSettled;
    }

    // ─── Annual diplomacy maintenance ─────────────────────────────────────────

    /// <summary>
    /// Called once per year. Dissolves alliances where trust has fallen below the floor.
    /// </summary>
    public static void RunAnnualDiplomacy(WorldState world, List<PendingEvent> pending)
    {
        var cfg      = world.SimConfig.Character;
        var toBreak  = world.Relationships.AllEdges
            .Where(e => e.IsAlly && e.Trust < cfg.AllianceTrustFloor)
            .ToList();

        foreach (var edge in toBreak)
        {
            var current = world.Relationships.Get(edge.From, edge.To);
            if (current is null || !current.IsAlly) continue;

            world.Relationships.Upsert(current with
            {
                Flags = current.Flags & ~RelationshipFlags.IsAlly
            });

            // Only fire the event if both characters are still alive (dead chars leave stale edges)
            if (world.GetEntity(edge.From) is not Tier1Character a) continue;
            if (world.GetEntity(edge.To)   is not Tier1Character b) continue;
            FireAllianceBroken(a, b, "trust_decay", world, pending);
        }
    }

    private static void FireAllianceBroken(
        Tier1Character a, Tier1Character b, string reason,
        WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            characterAId   = a.Id.Value,
            characterAName = a.Identity.Name,
            characterBId   = b.Id.Value,
            characterBName = b.Identity.Name,
            reason,
            year = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.AllianceBroken, a.Location, null, payload,
            new[] { a.Id.Value, b.Id.Value }));
    }

    // ─── Event helpers ────────────────────────────────────────────────────────

    private static void FireCivFounded(
        Civilization civ, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            civId      = civ.Id.Value,
            civName    = civ.Name,
            founderId  = founder.Id.Value,
            founderName = founder.Identity.Name,
            capital    = new[] { civ.CapitalTile.X, civ.CapitalTile.Y },
            year       = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.CivilizationFounded, civ.CapitalTile, null, payload));
    }

    private static void FireSettlementFounded(
        SettlementStub stub, Tier1Character founder, WorldState world, List<PendingEvent> pending)
    {
        var payload = JsonSerializer.Serialize(new
        {
            founderId      = founder.Id.Value,
            founderName    = founder.Identity.Name,
            settlementName = stub.Name,
            civId          = stub.CivId.Value,
            tile           = new[] { stub.Tile.X, stub.Tile.Y },
            year           = world.CurrentYear
        });
        pending.Add(new PendingEvent(EventType.SettlementFounded, stub.Tile, null, payload));
    }

    // ─── Name generation ─────────────────────────────────────────────────────

    private const int SaltSettlementPrefix   = 5001;
    private const int SaltSettlementSuffix   = 5002;
    private const int SaltFertilityVariance  = 5003;

    private static string GenerateSettlementName(
        TileCoord tile, WorldState world, SettlementNamesConfig cfg)
    {
        if (cfg.Prefixes.Length == 0 || cfg.Suffixes.Length == 0)
            return $"Settlement ({tile.X},{tile.Y})";

        // Deterministic from world seed + tile position — same tile always gets same name
        float pf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementPrefix);
        float sf = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltSettlementSuffix);
        var biome = (BiomeType)world.TileGrid.GetTile(tile).BiomeType;

        // Bias prefix selection toward biome character
        int pi = BiasedIndex(pf, biome, cfg.Prefixes.Length);
        int si = (int)(sf * cfg.Suffixes.Length);
        return cfg.Prefixes[pi] + cfg.Suffixes[si];
    }

    // Deterministic founding-time fertility variance: maps [0,1] → [1-variance, 1+variance]
    // so each settlement has a permanent slight edge or disadvantage baked in at birth.
    private static float GenerateFertilityMultiplier(TileCoord tile, WorldState world)
    {
        float r = WorldRng.FloatAt(world.WorldSeed, 0, tile.X, tile.Y, SaltFertilityVariance);
        // r ∈ [0,1] → multiplier ∈ [0.85, 1.15] (variance of ±0.15 baked into SettlementConfig)
        // DECISION: variance range is hardcoded here; SettlementConfig.FertilityVariance is the
        // intended range, but injecting SimConfig into CivTracker adds coupling we avoid for now.
        const float variance = 0.15f;
        return 1f - variance + r * (variance * 2f);
    }

    // Slightly bias prefix selection so rocky biomes lean toward hard-sounding names,
    // warm biomes toward bright/green — purely cosmetic, not guaranteed.
    private static int BiasedIndex(float raw, BiomeType biome, int count)
    {
        // Map raw [0,1] through a small biome-dependent shift, then wrap
        float shift = biome switch
        {
            BiomeType.Mountain or BiomeType.Hills or BiomeType.Volcanic
                => 0.3f,   // push toward Iron/Stone/Crag/Flint end
            BiomeType.Grassland or BiomeType.Savanna or BiomeType.TemperateForest
                => -0.15f, // push toward Green/Gold/Fair end
            BiomeType.Tundra or BiomeType.BorealForest
                => 0.15f,  // push toward Cold/Frost/Dark end
            _ => 0f
        };
        return (int)(((raw + shift + 1f) % 1f) * count);
    }
}
