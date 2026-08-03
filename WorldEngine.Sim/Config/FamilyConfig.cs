namespace WorldEngine.Sim.Config;

/// <summary>M13 13.0 — marriage formation, household Family Organization, and childbirth/inheritance knobs.</summary>
public class FamilyConfig
{
    /// <summary>Bond-goal trust required before either party will propose marriage.</summary>
    public float MarriageTrustThreshold      { get; set; } = 0.6f;
    /// <summary>Compassion personality floor for proposing marriage.</summary>
    public float MarriageCompassionThreshold { get; set; } = 0.4f;
    /// <summary>Minimum age (in seasons) before a character can marry.</summary>
    public int   MarriageMinAgeSeasons       { get; set; } = 60;

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
    /// how the relationship has decayed.</summary>
    public float EstrangementTrustThreshold  { get; set; } = -0.3f;

    /// <summary>M13 13.6 — marriage-specific hardship sink, on top of the general same-civ
    /// companionship drift (CharacterSimConfig.SameCivFamiliarityBaseRate/FrictionRate): annual
    /// Trust drain on a married edge when either spouse's Food or Safety need has been critical,
    /// giving Estrangement a distinct "poverty strained the marriage" cause rather than only ever
    /// following from baseline personality mismatch.</summary>
    public float MarriageHardshipNeedThreshold { get; set; } = 0.4f;
    public float MarriageHardshipTrustDrain     { get; set; } = 0.16f;

    /// <summary>M13 13.6 — marriage-specific milestone source: childbirth nudges marital Trust up
    /// a little too, not just Belonging — shared joy reinforces the bond.</summary>
    public float ChildbirthTrustGain            { get; set; } = 0.05f;
}
