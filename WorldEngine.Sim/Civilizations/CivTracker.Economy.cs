using System.Text.Json;
using WorldEngine.Sim.Core;
using WorldEngine.Sim.Entities;
using WorldEngine.Sim.Entities.Characters;
using WorldEngine.Sim.Events;
using WorldEngine.Sim.Organizations;
using WorldEngine.Sim.World;

namespace WorldEngine.Sim.Civilizations;

/// <summary>
/// M14 14.4 — decision 9's two treasury commands, and decision 10's civ-level economic-ruin
/// scoring (extends the *existing* CivSplintered/instability pathway rather than a parallel one —
/// see RunUnrestAndSecession's Driver 4 in CivTracker.Unrest.cs and CheckTreasuryInsolvency below).
/// See docs/phases/m14_economy_independent_wealth.md decisions 9/10, phase-sequence "14.4" entry.
/// </summary>
public static partial class CivTracker
{
    // ─── Treasury commands (decision 9) ────────────────────────────────────────

    /// <summary>
    /// Any member of OrganizationId may deposit personal Wealth into its Treasury — no authority
    /// check (decision 9: this is the one side of the treasury model that isn't gated). Amount is
    /// config-driven (EconomyConfig.ContributeToTreasuryAmount), capped at the contributor's
    /// available Wealth, mirroring how GrantAid's amount is config-driven rather than a command
    /// field.
    /// </summary>
    private static void ResolveContributeToTreasury(
        ContributeToTreasury cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.CharacterId) is not Tier1Character c || !c.IsAlive) return;
        if (!world.Organizations.TryGetValue(cmd.OrganizationId, out var org)) return;
        if (!org.Members.ContainsKey(c.Id)) return; // must be a member — that's the only gate

        float amount = Math.Min(c.Wealth, world.SimConfig.Economy.ContributeToTreasuryAmount);
        if (amount <= 0f) return;

        c.AddWealth(-amount);
        org.Treasury += amount;

        var payload = JsonSerializer.Serialize(new TreasuryContributionPayload(
            c.Id.Value, c.Identity.Name, org.Id.Value, org.Name, amount));
        pending.Add(new PendingEvent(EventType.TreasuryContribution, c.Location, null, payload,
            new[] { c.Id.Value }, ActorId: c.Id.Value, ActorName: c.Identity.Name));
    }

    /// <summary>
    /// Gated on <c>c.Id == org.LeaderId</c> — the only authority check in the whole treasury
    /// model (decision 9), applying uniformly to every OrganizationKind since Civilization already
    /// mirrors civ.RulerId onto Organization.LeaderId. Moves Organization.Treasury into
    /// RecipientId's personal Wealth (RecipientId may be the leader themself or any other living
    /// member). Amount is config-driven, capped at the Treasury's available balance — voluntary
    /// withdrawal can never itself drive the Treasury negative; only war reparations (EndWarBetween)
    /// can do that.
    /// </summary>
    private static void ResolveWithdrawFromTreasury(
        WithdrawFromTreasury cmd, WorldState world, List<PendingEvent> pending)
    {
        if (world.GetEntity(cmd.LeaderId) is not Tier1Character leader || !leader.IsAlive) return;
        if (!world.Organizations.TryGetValue(cmd.OrganizationId, out var org)) return;
        if (org.LeaderId != leader.Id) return;

        var recipient = world.GetEntity(cmd.RecipientId);
        if (recipient == null || !recipient.IsAlive) return;

        float amount = Math.Min(org.Treasury, world.SimConfig.Economy.WithdrawFromTreasuryAmount);
        if (amount <= 0f) return;

        switch (recipient)
        {
            case Tier1Character t1: t1.AddWealth(amount); break;
            case Tier2Character t2: t2.AddWealth(amount); break;
            default: return;
        }
        org.Treasury -= amount;

        var payload = JsonSerializer.Serialize(new TreasuryWithdrawalPayload(
            leader.Id.Value, leader.Identity.Name, org.Id.Value, org.Name,
            cmd.RecipientId.Value, DisplayName(recipient), amount));
        pending.Add(new PendingEvent(EventType.TreasuryWithdrawal, leader.Location, null, payload,
            new[] { leader.Id.Value }, new[] { cmd.RecipientId.Value },
            ActorId: leader.Id.Value, ActorName: leader.Identity.Name));
    }

    // ─── Civ-level economic ruin (decision 10) ─────────────────────────────────

    /// <summary>
    /// Edge-triggers <see cref="EventType.TreasuryInsolvent"/>: fires once per crossing into
    /// negative Treasury (via Organization.TreasuryInsolvencyFlagged), not once per tick the
    /// Treasury happens to stay negative — same "fire on crossing" shape as the M13.5
    /// Estrangement/OathBroken cooldown fix. Called once per year from RunUnrestAndSecession,
    /// alongside the unrest Driver 4 contribution this same negative-Treasury condition feeds
    /// (see that method) — one legible causal chain ("lost a war, paid reparations, Treasury went
    /// negative, unrest rose, civ splintered") rather than two unrelated collapse pathways.
    /// </summary>
    internal static void CheckTreasuryInsolvency(WorldState world, List<PendingEvent> pending)
    {
        foreach (var civ in world.Civilizations.Values)
        {
            if (civ.IsCollapsed) continue;
            if (GetOrg(world, civ) is not { } org) continue;

            if (org.Treasury < 0f)
            {
                if (org.TreasuryInsolvencyFlagged) continue;
                org.TreasuryInsolvencyFlagged = true;

                var payload = JsonSerializer.Serialize(new TreasuryInsolventPayload(
                    civ.Id.Value, civ.Name, org.Treasury));
                pending.Add(new PendingEvent(EventType.TreasuryInsolvent, civ.CapitalTile, null,
                    payload, CivId: civ.Id.Value));
            }
            else
            {
                org.TreasuryInsolvencyFlagged = false; // recovered — can fire again on a future crossing
            }
        }
    }

    // ─── War reparations (deep-review finding, folded into 14.4) ──────────────

    /// <summary>
    /// One-time Wealth transfer from the losing civ's Treasury to the winner's, called from
    /// CivTracker.EndWarBetween (the single canonical "a war concludes" point covering every
    /// WarOutcome — Truce/Surrender/Conquest/Destruction — via the same battle-win-advantage
    /// winner/loser determination EndWarBetween already computes for territory transfer). Allowed
    /// to drive the loser's Treasury negative — that's exactly TreasuryInsolvent's trigger.
    /// Resolves inside RunAnnualDiplomacy's step 6 (war resolution), which runs *after* step 5b2's
    /// RunUnrestAndSecession/CheckTreasuryInsolvency in the same annual tick; this is safe because
    /// neither Organization nor its Treasury field is ever removed/cleared on civ collapse
    /// (verified: no code path clears Civilization.OrgId or zeroes Organization.Treasury on
    /// collapse) — GetOrg resolves the same Organization instance regardless of IsCollapsed, so a
    /// same-tick collapse never leaves reparations with stale or missing treasury state to
    /// transfer into/out of. See M14_ReparationsSequencingTests for the regression guard.
    /// </summary>
    internal static void ApplyWarReparations(
        CivId winnerId, CivId loserId, int battleWinAdvantage, WorldState world, List<PendingEvent> pending)
    {
        if (battleWinAdvantage <= 0) return;
        if (!world.Civilizations.TryGetValue(winnerId, out var winner)) return;
        if (!world.Civilizations.TryGetValue(loserId, out var loser)) return;
        if (GetOrg(world, winner) is not { } winnerOrg) return;
        if (GetOrg(world, loser) is not { } loserOrg) return;

        float amount = battleWinAdvantage * world.SimConfig.Economy.WarReparationsPerBattleWinAdvantage;
        if (amount <= 0f) return;

        loserOrg.Treasury -= amount;
        winnerOrg.Treasury += amount;

        var payload = JsonSerializer.Serialize(new WarReparationsPaidPayload(
            winner.Id.Value, winner.Name, loser.Id.Value, loser.Name, amount));
        pending.Add(new PendingEvent(EventType.WarReparationsPaid, winner.CapitalTile, null, payload,
            CivId: winner.Id.Value));
    }
}
