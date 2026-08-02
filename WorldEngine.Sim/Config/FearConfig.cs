namespace WorldEngine.Sim.Config;

/// <summary>M13 13.1 — Fear as a submission/appeasement axis distinct from Trust: a feared rival
/// gets avoided (War/Raid dampened) or placated, instead of Fear only ever feeding Dominance/war.</summary>
public class FearConfig
{
    public float RivalryBaseFearIncrement { get; set; } = 0.1f;  // Fear added when a rivalry first forms (matches the original hardcoded value)
    public float RivalryFearPowerScale    { get; set; } = 0.5f;  // extra Fear from the target's Combat/Aggression edge over the declarer's
    public float PlacateFearThreshold     { get; set; } = 0.4f;  // minimum Fear toward an existing rival before appeasement becomes attractive
    public float PlacateAggressionMax     { get; set; } = 0.4f;  // only low-Aggression characters placate; aggressive ones confront despite fear
    public float PlacateFearReduction     { get; set; } = 0.3f;  // Fear reduced per successful Placate
    public float PlacateTrustGain         { get; set; } = 0.1f;  // Trust nudge from successful placation
    public float FearWarDampenMin         { get; set; } = 0.3f;  // War/Raid score multiplier floor when maximally feared of someone in the target civ

    // M13 13.5 — Reconciliation and Feud: the transitions a rivalry can end in besides war.
    public float ReconciliationFearThreshold  { get; set; } = 0.1f;  // Placate clears IsRival once Fear drops to/below this...
    public float ReconciliationTrustThreshold { get; set; } = 0.3f;  // ...and Trust has risen to/above this
    public float FeudTrustPenalty             { get; set; } = 0.2f;  // extra Trust lost when a rivalry is re-declared while already active
    public float FeudFearIncrement            { get; set; } = 0.15f; // extra Fear added on the same escalation
}
