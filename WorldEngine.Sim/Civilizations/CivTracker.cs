using System.Text.Json;
using WorldEngine.Sim.Config;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.Tiles;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// Resolves character commands that affect civilizations, settlements, and relationships.
/// Split across: CivTracker.War.cs, CivTracker.Diplomacy.cs, CivTracker.Naming.cs
/// </summary>
public static partial class CivTracker
{
    /// <summary>
    /// Creates the M12 Organization backing a newly founded org (Civilization now; Guild/Religion/Family
    /// from M13-M15). Member registration is the caller's job — for civs, via SetCharacterCiv, which is
    /// what keeps Tier1Character.Memberships and Organization.Members in sync. See docs/phases/m12_organization_model.md.
    /// </summary>
    internal static OrganizationId CreateOrganization(WorldState world, OrganizationKind kind, string name, EntityId leaderId)
    {
        var orgId = new OrganizationId(world.NextOrganizationId++);
        var org = new Organization(orgId, kind, name, leaderId, world.CurrentYear);
        world.Organizations[orgId] = org;
        return orgId;
    }

    /// <summary>
    /// M12 12.2: sets (or clears, when civId is invalid) a character's Civilization-kind
    /// membership — the sole write path for Tier1Character.Memberships' civ entry, replacing the
    /// old `Identity = Identity with { CivId = x }` pattern. Keeps Tier1Character.Memberships and
    /// the backing Organization.Members in sync from one call, so they can never drift.
    /// </summary>
    internal static void SetCharacterCiv(Tier1Character c, CivId civId, OrganizationRole role, WorldState world)
    {
        var existing = c.Memberships.FirstOrDefault(m => m.CivId.IsValid);
        if (existing != null)
        {
            c.Memberships.Remove(existing);
            if (world.Organizations.TryGetValue(existing.OrganizationId, out var oldOrg))
                oldOrg.Members.Remove(c.Id);
        }

        if (!civId.IsValid) return;
        if (!world.Civilizations.TryGetValue(civId, out var civ)) return;
        // Self-heal: production code always creates the backing Organization at civ-founding
        // (CivTracker.cs/CivTracker.Unrest.cs), but pre-M12 test fixtures still construct
        // Civilization directly. Backfill rather than silently dropping the membership.
        civ.OrgId ??= CreateOrganization(world, OrganizationKind.Civilization, civ.Name, civ.RulerId);
        var orgId = civ.OrgId.Value;

        var membership = new Membership(orgId, role, 1.0f, civId);
        c.Memberships.Add(membership);
        if (world.Organizations.TryGetValue(orgId, out var org))
            org.Members[c.Id] = membership;
    }

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
            case ProposeMarriage pm:
                ResolveMarriage(pm, world, pending); break;
            case GrantAid ga:
                ResolveGrantAid(ga, world, pending); break;
            case ForgiveDebt fd:
                ResolveForgiveDebt(fd, world, pending); break;
            case Placate pl:
                ResolvePlacate(pl, world, pending); break;
            case Defect df:
                ResolveDefect(df, world, pending); break;
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

        // Hard ruin cooldown: recently destroyed sites cannot be immediately resettled.
        // Cooldown scales with TimesSettled so repeatedly contested tiles grow progressively harder
        // to reclaim — a tile destroyed 3× needs 3× the base cooldown to rebuild.
        if (world.Ruins.TryGetValue(cmd.Tile, out var ruin))
        {
            int effectiveCooldown = world.SimConfig.Character.RuinCooldownYears
                                  * Math.Max(1, ruin.TimesSettled);
            if (world.CurrentYear - ruin.DestroyedYear < effectiveCooldown)
                return;
        }

        // Create settlement
        var civId = founder.CivId;
        bool newCiv = !civId.IsValid;
        if (newCiv)
        {
            civId = new CivId(world.NextCivId++);
            string civSuffix = GetCivNameSuffix(founder.Identity.AncestryId, world.SimConfig.AncestryRegistry);
            string civName   = $"{founder.Identity.Name}'s {civSuffix}";
            var civ = new Civilization(civId, civName, founder.Id, cmd.Tile, world.CurrentYear);
            civ.Members.Add(founder.Id);
            world.Civilizations[civId] = civ;
            civ.OrgId = CreateOrganization(world, OrganizationKind.Civilization, civName, founder.Id);
            SetCharacterCiv(founder, civId, OrganizationRole.Leader, world);
            founder.Identity = founder.Identity with { RulerOrdinal = 1 };

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
            Population:          world.SimConfig.Settlement.SettlementStartPop,
            Health:              world.SimConfig.Settlement.MaxHealth,
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

        // Mark FoundCity goal as fully complete on successful founding (one city = done).
        // Setting to 1.0 lets GoalManager fire GoalResolved(completed) and remove the goal cleanly.
        foreach (var g in founder.Goals)
            if (g.Type == GoalType.FoundCity)
                g.Progress = 1f;

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
        if (c.CivId.IsValid && target.CivId.IsValid
            && c.CivId == target.CivId) return;

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

        // Org-level alliance fact (M12 12.1): only meaningful when both allying characters are
        // their civ's current ruler — that's the only case where a personal alliance is also a
        // civ-to-civ one. See CivTracker.Diplomacy.cs FormOrgAlliance.
        if (c.CivId.IsValid && target.CivId.IsValid
            && c.CivId != target.CivId
            && world.Civilizations.TryGetValue(c.CivId, out var cCiv) && cCiv.RulerId == c.Id
            && world.Civilizations.TryGetValue(target.CivId, out var tCiv) && tCiv.RulerId == target.Id
            && GetOrg(world, cCiv) is { } cOrg && GetOrg(world, tCiv) is { } tOrg)
            FormOrgAlliance(cOrg, tOrg);

        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.15f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.1f) };
        c.Skills     = c.Skills with { Diplomacy = Math.Min(1f, c.Skills.Diplomacy + 0.02f) };

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Alliance && g.TargetEntityId == target.Id)
                g.Progress = 1f;

        var payload = JsonSerializer.Serialize(new AllianceFormedPayload(
            c.Id.Value, c.Identity.Name,
            target.Id.Value, target.Identity.Name,
            c.CivId.Value, target.CivId.Value));
        pending.Add(new PendingEvent(EventType.AllianceFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name, CivId: c.CivId.Value));
    }

    // ─── Marriage (M13 13.0) ────────────────────────────────────────────────────

    /// <summary>
    /// Upgrades a high-trust Bond into marriage: RelationshipFlags.IsMarried|IsFamily, plus a new
    /// Family-kind Organization (the household) with both spouses as Members. This is the first
    /// production case (besides civ founding) of a character gaining a *second* Membership
    /// alongside their civ one — see M12 12.2's multi-membership schema and design decision 2
    /// (weighted-loyalty conflict scoring, consumed by UtilityScorer's war/raid kin-dampening).
    /// </summary>
    private static void ResolveMarriage(ProposeMarriage cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;
        if (!c.IsAlive || !target.IsAlive) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        if (rel.IsMarried) return;

        var famCfg = world.SimConfig.Family;
        if (c.AgeSeason < famCfg.MarriageMinAgeSeasons || target.AgeSeason < famCfg.MarriageMinAgeSeasons) return;

        // Leader-seat tiebreak: higher-Ambition spouse heads the new household. Arbitrary but
        // deterministic (no RNG) — matches the "simplest reasonable choice" ambiguity rule.
        bool cLeads = c.Personality.Ambition >= target.Personality.Ambition;
        var leaderId = cLeads ? c.Id : target.Id;
        string familyName = $"House of {(cLeads ? c.Identity.Name : target.Identity.Name)}";
        var orgId = CreateOrganization(world, OrganizationKind.Family, familyName, leaderId);
        var org = world.Organizations[orgId];

        var cMembership = new Membership(orgId, cLeads ? OrganizationRole.Leader : OrganizationRole.Member, 1.0f);
        var targetMembership = new Membership(orgId, cLeads ? OrganizationRole.Member : OrganizationRole.Leader, 1.0f);
        c.Memberships.Add(cMembership);
        target.Memberships.Add(targetMembership);
        org.Members[c.Id] = cMembership;
        org.Members[target.Id] = targetMembership;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(1f, rel.Trust + 0.2f),
            Flags = rel.Flags | RelationshipFlags.IsMarried | RelationshipFlags.IsFamily
        });

        // M13 13.3: ruler cross-civ marriage as a real diplomatic lever — since civ diplomacy
        // already reuses the ruler's personal RelationshipEdge (CivTracker.Diplomacy.cs), an
        // arranged marriage between two current rulers cements the same Organization-to-Organization
        // alliance fact ResolveAlly forms, so it survives either ruler's later death or succession
        // (M12 design decision 1) rather than evaporating with the personal edge.
        if (c.CivId.IsValid && target.CivId.IsValid
            && c.CivId != target.CivId
            && world.Civilizations.TryGetValue(c.CivId, out var cCiv) && cCiv.RulerId == c.Id
            && world.Civilizations.TryGetValue(target.CivId, out var tCiv) && tCiv.RulerId == target.Id
            && !cCiv.IsAtWarWith(target.CivId)
            && GetOrg(world, cCiv) is { } cOrg && GetOrg(world, tCiv) is { } tOrg)
            FormOrgAlliance(cOrg, tOrg);

        foreach (var g in c.Goals)
            if (g.Type == GoalType.Bond && g.TargetEntityId == target.Id) g.Progress = 1f;
        foreach (var g in target.Goals)
            if (g.Type == GoalType.Bond && g.TargetEntityId == c.Id) g.Progress = 1f;

        c.Needs      = c.Needs with { Belonging = Math.Min(1f, c.Needs.Belonging + 0.2f) };
        target.Needs = target.Needs with { Belonging = Math.Min(1f, target.Needs.Belonging + 0.2f) };

        var payload = JsonSerializer.Serialize(new MarriagePayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name, orgId.Value));
        pending.Add(new PendingEvent(EventType.CharacterMarried, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    // ─── Debt (M13 13.2) ────────────────────────────────────────────────────────

    private static void ResolveGrantAid(GrantAid cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.GranterId) is not Tier1Character granter) return;
        if (world.GetEntity(cmd.RecipientId) is not Tier1Character recipient) return;
        if (!granter.IsAlive || !recipient.IsAlive) return;

        var cfg = world.SimConfig.Debt;
        var rel = world.Relationships.GetOrCreate(granter.Id, recipient.Id);
        if (rel.Trust < cfg.AidTrustThreshold) return;
        if (recipient.Needs.Food >= cfg.AidNeedThreshold && recipient.Needs.Safety >= cfg.AidNeedThreshold) return;

        // Normalize the edge's signed Debt into "how much the recipient owes the granter",
        // independent of which of the two landed as the canonical From — see RelationshipEdge.DebtorId.
        float owedByRecipient = rel.DebtorId == recipient.Id ? Math.Abs(rel.Debt)
                               : rel.DebtorId == granter.Id ? -Math.Abs(rel.Debt)
                               : 0f;
        float newOwed = Math.Clamp(owedByRecipient + cfg.AidDebtIncrement, -1f, 1f);
        float sign = recipient.Id == rel.From ? 1f : -1f;

        world.Relationships.Upsert(rel with
        {
            Debt  = newOwed * sign,
            Trust = Math.Min(1f, rel.Trust + cfg.AidTrustGain)
        });

        recipient.Needs = recipient.Needs with
        {
            Food   = Math.Min(1f, recipient.Needs.Food + cfg.AidNeedRestore),
            Safety = Math.Min(1f, recipient.Needs.Safety + cfg.AidNeedRestore)
        };

        var aidPayload = JsonSerializer.Serialize(new DebtIncurredPayload(
            granter.Id.Value, granter.Identity.Name, recipient.Id.Value, recipient.Identity.Name,
            cfg.AidDebtIncrement));
        pending.Add(new PendingEvent(EventType.DebtIncurred, granter.Location, null, aidPayload,
            new[] { granter.Id.Value }, new[] { recipient.Id.Value },
            ActorId: granter.Id.Value, ActorName: granter.Identity.Name));
    }

    private static void ResolveForgiveDebt(ForgiveDebt cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CreditorId) is not Tier1Character creditor) return;
        if (world.GetEntity(cmd.DebtorId) is not Tier1Character debtor) return;
        if (!creditor.IsAlive || !debtor.IsAlive) return;

        var rel = world.Relationships.Get(creditor.Id, debtor.Id);
        if (rel == null || rel.CreditorId != creditor.Id) return;
        float forgiven = Math.Abs(rel.Debt);

        var cfg = world.SimConfig.Debt;
        world.Relationships.Upsert(rel with
        {
            Debt  = 0f,
            Trust = Math.Min(1f, rel.Trust + cfg.ForgiveTrustGain)
        });

        var forgivePayload = JsonSerializer.Serialize(new DebtForgivenPayload(
            creditor.Id.Value, creditor.Identity.Name, debtor.Id.Value, debtor.Identity.Name, forgiven));
        pending.Add(new PendingEvent(EventType.DebtForgiven, creditor.Location, null, forgivePayload,
            new[] { creditor.Id.Value }, new[] { debtor.Id.Value },
            ActorId: creditor.Id.Value, ActorName: creditor.Identity.Name));
    }

    // ─── Rivalry ──────────────────────────────────────────────────────────────

    private static void ResolveRivalry(
        DeclareRivalry cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;

        var rel = world.Relationships.GetOrCreate(c.Id, target.Id);
        var fearCfg = world.SimConfig.Fear;

        // M13 13.5: a rivalry re-declared while already active deepens into a Feud instead of
        // no-op'ing — "cheap given CivSplintered-style patterns already exist" (roadmap proposal
        // #5). A Feud that's already maximally escalated has nothing further to do.
        if (rel.IsRival)
        {
            if (rel.IsFeud) return;

            world.Relationships.Upsert(rel with
            {
                Trust = Math.Max(-1f, rel.Trust - fearCfg.FeudTrustPenalty),
                Fear  = Math.Min(1f, rel.Fear + fearCfg.FeudFearIncrement),
                Flags = rel.Flags | RelationshipFlags.IsFeud
            });

            var feudPayload = JsonSerializer.Serialize(new RivalryEscalatedToFeudPayload(
                c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
            pending.Add(new PendingEvent(EventType.RivalryEscalatedToFeud, c.Location, null, feudPayload,
                new[] { c.Id.Value }, new[] { target.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));
            return;
        }

        // M13 13.1: Fear scales with how much more formidable the target is than the declarer —
        // a rivalry with a stronger opponent is scarier than one with a weaker one, not a flat bump.
        float declarerPower = (c.Skills.Combat + c.Personality.Aggression) * 0.5f;
        float targetPower   = (target.Skills.Combat + target.Personality.Aggression) * 0.5f;
        float fearIncrement = fearCfg.RivalryBaseFearIncrement
                             + Math.Max(0f, targetPower - declarerPower) * fearCfg.RivalryFearPowerScale;

        world.Relationships.Upsert(rel with
        {
            Trust = Math.Min(rel.Trust, -0.1f),
            Fear  = Math.Min(1f, rel.Fear + fearIncrement),
            Flags = rel.Flags | RelationshipFlags.IsRival
        });

        var payload = JsonSerializer.Serialize(new RivalryFormedPayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
        pending.Add(new PendingEvent(EventType.RivalryFormed, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    private static void ResolvePlacate(Placate cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.TargetId) is not Tier1Character target) return;
        if (!c.IsAlive || !target.IsAlive) return;

        var rel = world.Relationships.Get(c.Id, target.Id);
        if (rel == null || !rel.IsRival || rel.Fear <= 0f) return;

        var cfg = world.SimConfig.Fear;
        float newFear  = Math.Max(0f, rel.Fear - cfg.PlacateFearReduction);
        float newTrust = Math.Min(1f, rel.Trust + cfg.PlacateTrustGain);

        // M13 13.5: Reconciliation — Placate itself never ended a rivalry (13.1 deferred that
        // deliberately); once enough placation has cooled Fear and warmed Trust past both
        // thresholds, the rivalry (and any Feud it escalated into) ends outright here.
        bool reconciles = newFear <= cfg.ReconciliationFearThreshold && newTrust >= cfg.ReconciliationTrustThreshold;
        var newFlags = reconciles
            ? rel.Flags & ~(RelationshipFlags.IsRival | RelationshipFlags.IsFeud)
            : rel.Flags;

        world.Relationships.Upsert(rel with { Fear = newFear, Trust = newTrust, Flags = newFlags });

        var payload = JsonSerializer.Serialize(new RivalryPlacatedPayload(
            c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
        pending.Add(new PendingEvent(EventType.RivalryPlacated, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { target.Id.Value },
            ActorId: c.Id.Value, ActorName: c.Identity.Name));

        if (reconciles)
        {
            var reconPayload = JsonSerializer.Serialize(new RivalsReconciledPayload(
                c.Id.Value, c.Identity.Name, target.Id.Value, target.Identity.Name));
            pending.Add(new PendingEvent(EventType.RivalsReconciled, c.Location, null, reconPayload,
                new[] { c.Id.Value }, new[] { target.Id.Value },
                ActorId: c.Id.Value, ActorName: c.Identity.Name));
        }
    }

    /// <summary>
    /// M13 13.4: "a cross-civ friendship could... trigger asylum/defection" (roadmap proposal #4)
    /// — a non-ruler character in personal crisis (Wellbeing spiraling — gated in UtilityScorer's
    /// candidate generation, not here) abandons their civ for a co-located foreign confidant's,
    /// via the same SetCharacterCiv write path civ founding/childbirth/succession already use.
    /// Rulers can't defect — there's no seat to hand off mid-command, only SuccessionResolver
    /// retires one. Civs already at war with the confidant's civ can't be defected to either
    /// (asylum, not treason).
    /// </summary>
    private static void ResolveDefect(Defect cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c) return;
        if (world.GetEntity(cmd.ConfidantId) is not Tier1Character confidant) return;
        if (!c.IsAlive || !confidant.IsAlive) return;
        if (!confidant.CivId.IsValid || confidant.CivId == c.CivId) return;
        if (!world.Civilizations.TryGetValue(confidant.CivId, out var targetCiv) || targetCiv.IsCollapsed) return;

        if (c.CivId.IsValid && world.Civilizations.TryGetValue(c.CivId, out var ownCiv))
        {
            if (ownCiv.RulerId == c.Id) return;
            if (ownCiv.IsAtWarWith(confidant.CivId)) return;
        }

        var rel = world.Relationships.Get(c.Id, confidant.Id);
        var cfg = world.SimConfig.Defection;
        if (rel == null || rel.Trust < cfg.ConfidantTrustThreshold) return;

        var oldCivId   = c.CivId;
        var oldCivName = world.Civilizations.TryGetValue(oldCivId, out var oc) ? oc.Name : "";

        SetCharacterCiv(c, confidant.CivId, OrganizationRole.Member, world);
        world.Relationships.Upsert(rel with { Trust = Math.Min(1f, rel.Trust + cfg.PostDefectionTrustGain) });

        var payload = JsonSerializer.Serialize(new CharacterDefectedPayload(
            c.Id.Value, c.Identity.Name,
            oldCivId.Value, oldCivName, confidant.CivId.Value, targetCiv.Name,
            confidant.Id.Value, confidant.Identity.Name));
        pending.Add(new PendingEvent(EventType.CharacterDefected, c.Location, null, payload,
            new[] { c.Id.Value }, new[] { confidant.Id.Value },
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
        // All settlements start at settlement_start_pop (500), so checking pop at abandonment
        // time incorrectly skips settlements that starved down to pop_min_viable before collapse.
        // We only skip if this is the first ruin at the tile AND the settlement was never real
        // (i.e., it was founded and immediately abandoned in the same year — a ghost entry).
        if (stub.Population <= 0 && !world.Ruins.ContainsKey(tile))
            return 0;

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
        if (!builder.CivId.IsValid) return;

        var targetTile = cmd.TargetTile;

        // Validate: tile must still be in this civ's territory with no existing improvement
        if (!world.TerritoryMap.TryGetValue(targetTile, out var cityTile)) return;
        if (world.ImprovementMap.ContainsKey(targetTile)) return;

        // Port requires a coastal tile — a harbor with no adjacent water makes no sense
        if (cmd.ImprovementType == ImprovementType.Port
            && !world.GetTile(targetTile).StaticFlags.HasFlag(TileStaticFlags.IsCoastal))
            return;

        var civ = world.GetCivilization(builder.CivId);
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
            builder.CivId.Value, targetTile.X, targetTile.Y,
            cmd.ImprovementType.ToString()));
        pending.Add(new PendingEvent(EventType.ImprovementBuilt, targetTile, null, payload,
            new[] { cmd.CharacterId.Value },
            ActorId: cmd.CharacterId.Value, ActorName: builder.Identity.Name,
            CivId: builder.CivId.Value, SettlementName: settName));
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

        // Register the conquered city as its own entry in the winner civ so TerritoryPhase
        // continues managing expansion from the conquered center rather than orphaning tiles.
        if (!winningCiv.CityTerritories.TryGetValue(conqueredCityTile, out var winnerTiles))
            winningCiv.CityTerritories[conqueredCityTile] = winnerTiles = new HashSet<TileCoord>();

        int maxR = world.SimConfig.Territory.MaxTerritoryRadius;
        foreach (var t in tiles)
        {
            // Release tiles beyond the radius cap — TerritoryPhase will re-expand naturally.
            int dx = t.X - conqueredCityTile.X, dy = t.Y - conqueredCityTile.Y;
            if (dx * dx + dy * dy > maxR * maxR)
            {
                world.TerritoryMap.Remove(t);
                continue;
            }
            world.TerritoryMap[t] = conqueredCityTile;
            winnerTiles.Add(t);
        }

        // Update improvements to reflect new city ownership
        foreach (var t in winnerTiles)
        {
            if (world.ImprovementMap.TryGetValue(t, out var imp))
                world.ImprovementMap[t] = imp with { CityTile = conqueredCityTile };
        }
    }
}
