namespace WorldEngine.Sim.Core;

public readonly record struct ArtifactId(long Value)
{
    public static ArtifactId New() => new(IdGenerator.Next());
}
