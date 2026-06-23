namespace WorldEngine.Sim.Config;

public class TectonicsConfig
{
    public int PlateCount { get; set; } = 15;
    public float MinPlateSeparationFraction { get; set; } = 0.12f;
    public float ContinentalPlateFraction { get; set; } = 0.45f;

    /// <summary>
    /// How far (in tiles) coherent noise can displace a tile's position when
    /// assigning it to a Voronoi plate. Makes plate boundaries wavy rather than
    /// straight. 0 = perfectly straight Voronoi edges. 8–12 produces organic shapes.
    /// </summary>
    public float BoundaryPerturbStrength { get; set; } = 0f;

    /// <summary>
    /// Noise frequency for boundary perturbation. Lower = broader, smoother waves;
    /// higher = tighter, more irregular. 0.05–0.1 works well with strength 8–12.
    /// </summary>
    public float BoundaryPerturbFrequency { get; set; } = 0.07f;
}
