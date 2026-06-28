namespace WorldEngine.Sim.Config;

public class ReligionConfig
{
    /// <summary>Spiritual need level required to trigger a FoundReligion goal.</summary>
    public float SpiritualFoundingThreshold     { get; set; } = 0.75f;
    /// <summary>Piety skill floor; characters below this can't found religions.</summary>
    public float PietyFoundingThreshold         { get; set; } = 0.50f;
    /// <summary>Wonder personality trait floor for religion founding.</summary>
    public float WonderFoundingThreshold        { get; set; } = 0.60f;
    /// <summary>Progress added to FoundReligion goal per year while Spiritual stays high (~3 years to complete).</summary>
    public float ReligionFoundingProgressPerYear { get; set; } = 0.35f;
    /// <summary>Minimum years between religion foundings for the same character.</summary>
    public int   ReligionFoundingCooldownYears  { get; set; } = 50;
}
