namespace WorldEngine.Sim.World;

public sealed class BorderManifest
{
    public const int SampleCount = 64;

    public BorderManifestSample[] North { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] South { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] East  { get; } = new BorderManifestSample[SampleCount];
    public BorderManifestSample[] West  { get; } = new BorderManifestSample[SampleCount];
}
