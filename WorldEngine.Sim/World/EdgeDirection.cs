namespace WorldEngine.Sim.World;

/// <summary>Which side of a world tile a border-manifest edge (or river crossing) refers to.</summary>
public enum EdgeDirection : byte { North, South, East, West }

public static class EdgeDirectionExtensions
{
    public static EdgeDirection Opposite(this EdgeDirection edge) => edge switch
    {
        EdgeDirection.North => EdgeDirection.South,
        EdgeDirection.South => EdgeDirection.North,
        EdgeDirection.East  => EdgeDirection.West,
        EdgeDirection.West  => EdgeDirection.East,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };
}
