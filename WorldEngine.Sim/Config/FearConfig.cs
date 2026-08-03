namespace WorldEngine.Sim.Config;

/// <summary>M13 13.1 — Fear as a submission/appeasement axis distinct from Trust: a feared rival
/// gets avoided (War/Raid dampened) or placated, instead of Fear only ever feeding Dominance/war.</summary>
public class FearConfig
{
    public float RivalryBaseFearIncrement { get; set; } = 0.1f;  // Fear added when a rivalry first forms (matches the original hardcoded value)
    public float RivalryFearPowerScale    { get; set; } = 0.5f;  // extra Fear from the target's Combat/Aggression edge over the declarer's

    // 2026-08-03 rebalance: the old threshold (0.4) was above what a same-power rivalry (base
    // Fear 0.1, +0.15 if escalated to Feud = 0.25 max) or a Tier2 rivalry (target power always 0,
    // so Fear is *always* exactly the base increment) could ever reach — Placate structurally
    // never got scored for either case, so RivalsReconciled/CharacterEstranged/OathBroken never
    // fired in any balance run from M13.5 through M13.8.3. Lowered so any formed rivalry is
    // eligible immediately; PlacateFearReduction lowered in tandem so Fear drains over several
    // successful placations instead of one, giving PlacateTrustGain room to actually climb back to
    // ReconciliationTrustThreshold before Fear bottoms out and blocks further Placate resolution
    // (see ResolvePlacate's `rel.Fear <= 0f` gate) — see docs/balance_invariants.md.
    public float PlacateFearThreshold     { get; set; } = 0.05f; // minimum Fear toward an existing rival before appeasement becomes attractive
    public float PlacateAggressionMax     { get; set; } = 0.4f;  // only low-Aggression characters placate; aggressive ones confront despite fear
    public float PlacateFearReduction     { get; set; } = 0.05f; // Fear reduced per successful Placate — several placations drain a rivalry, not one
    public float PlacateTrustGain         { get; set; } = 0.2f;  // Trust nudge from successful placation
    public float FearWarDampenMin         { get; set; } = 0.3f;  // War/Raid score multiplier floor when maximally feared of someone in the target civ

    // M13 13.5 — Reconciliation and Feud: the transitions a rivalry can end in besides war.
    public float ReconciliationFearThreshold  { get; set; } = 0.1f;  // Placate clears IsRival once Fear drops to/below this...
    public float ReconciliationTrustThreshold { get; set; } = 0.3f;  // ...and Trust has risen to/above this
    public float FeudTrustPenalty             { get; set; } = 0.2f;  // extra Trust lost when a rivalry is re-declared while already active
    public float FeudFearIncrement            { get; set; } = 0.15f; // extra Fear added on the same escalation
}
