namespace WorldEngine.Sim.Entities.Characters;

/// <summary>8 dynamic skills. Grow through use, capped at 1.0.</summary>
public record struct SkillVector(
    float Combat, float Leadership, float Administration,
    float Diplomacy, float Crafting, float Knowledge, float Stealth, float Piety)
{
    public static SkillVector Default => new(0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f);

    public void GrowCombat(float amount)      => Combat      = Math.Min(1f, Combat      + amount);
    public void GrowLeadership(float amount)  => Leadership  = Math.Min(1f, Leadership  + amount);
    public void GrowDiplomacy(float amount)   => Diplomacy   = Math.Min(1f, Diplomacy   + amount);
    public void GrowAdministration(float amount) => Administration = Math.Min(1f, Administration + amount);
}
