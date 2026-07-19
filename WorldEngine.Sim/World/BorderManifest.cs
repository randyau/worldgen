namespace WorldEngine.Sim.World;

/// <summary>Per-tile border sampling data (North/South/East/West edges, 64 samples each) for civ contact detection.</summary>
public sealed class BorderManifest
{
    public const int SampleCount = 64;

    public BorderManifestSample[] North { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] South { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] East  { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] West  { get; } = new BorderManifestSample[SampleCount];
}
