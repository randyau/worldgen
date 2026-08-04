namespace WorldEngine.Sim.Config;

/// <summary>M13 13.0 — marriage formation, household Family Organization, and childbirth/inheritance knobs.</summary>
public class FamilyConfig
{
    /// <summary>Bond-goal trust required before either party will propose marriage.</summary>
    public float MarriageTrustThreshold      { get; set; } = 0.6f;
    /// <summary>Compassion personality floor for proposing marriage.</summary>
    public float MarriageCompassionThreshold { get; set; } = 0.4f;
    /// <summary>Minimum AgeSeason before a character can marry. 240 = 15 years at 16 ticks/year.</summary>
    public int   MarriageMinAgeSeasons       { get; set; } = 240;

    /// <summary>Per-annual-tick chance a married couple, co-located, conceives a child.</summary>
    public float ChildbirthChancePerYear     { get; set; } = 0.15f;
    /// <summary>Max living children (Family membership as a child) counted per couple before childbirth stops rolling.</summary>
    public int   MaxChildrenPerCouple        { get; set; } = 4;
    /// <summary>Fraction of the gap between a parent's trait and the ancestry-biased midpoint that
    /// carries over to a child's own trait roll (0 = pure ancestry bias, 1 = pure parent average).</summary>
    public float TraitInheritanceWeight      { get; set; } = 0.4f;
    /// <summary>Starting Loyalty for a newborn's Family membership.</summary>
    public float NewbornFamilyLoyalty        { get; set; } = 0.8f;

    /// <summary>War/Raid score multiplier floor applied when a character has a Family-org relative
    /// living in the target civ and Family Loyalty &gt;= Civ Loyalty (full dampening).</summary>
    public float KinInEnemyCivWarDampenMin   { get; set; } = 0.2f;

    /// <summary>M13 13.5 — Estrangement: annual check on married couples. Trust at/below this
    /// clears IsMarried|IsFamily instead of the marriage persisting indefinitely regardless of
    /// how the relationship has decayed.
    /// 2026-08-03 rebalance: `ResolveMarriage` sets Trust to `min(1, priorTrust + 0.2)` at the
    /// moment of marriage (and proposal itself requires Trust >= MarriageTrustThreshold 0.6), so
    /// married Trust starts around 0.8-1.0, and ChildbirthTrustGain/the general same-civ
    /// companionship drift below both push it upward further from there. The old threshold (-0.3)
    /// needed a ~1.1-1.3 point swing that hardship alone (see MarriageHardshipTrustDrain) could
    /// never plausibly deliver — calibration observed married-edge Trust pinned at 0.80-1.00 in
    /// every seed, CharacterEstranged never firing. Loosened so a marriage that's genuinely
    /// soured — not driven all the way to active hatred — can actually end.</summary>
    public float EstrangementTrustThreshold  { get; set; } = 0.65f;

    /// <summary>M13 13.6 — marriage-specific hardship sink, on top of the general same-civ
    /// companionship drift (CharacterSimConfig.SameCivFamiliarityBaseRate/FrictionRate): annual
    /// Trust drain on a married edge when either spouse's Food or Safety need has been critical,
    /// giving Estrangement a distinct "poverty strained the marriage" cause rather than only ever
    /// following from baseline personality mismatch. Raised 2026-08-03 alongside
    /// EstrangementTrustThreshold — see that field's comment for the reachability math.</summary>
    public float MarriageHardshipNeedThreshold { get; set; } = 0.4f;
    public float MarriageHardshipTrustDrain     { get; set; } = 0.5f;

    /// <summary>M13 13.6 — marriage-specific milestone source: childbirth nudges marital Trust up
    /// a little too, not just Belonging — shared joy reinforces the bond.</summary>
    public float ChildbirthTrustGain            { get; set; } = 0.05f;
}
