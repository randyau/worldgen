namespace WorldEngine.Sim.Config;

public class ElevationConfig
{
    /// <summary>FastNoiseLite frequency for base elevation noise.</summary>
    public float NoiseScale { get; set; } = 0.3f;

    /// <summary>How dramatic plate collision mountain ridges are (0=gentle, 1=extreme).</summary>
    public float TectonicIntensity { get; set; } = 0.8f;

    /// <summary>Elevation boost on continental plates above the noise baseline.</summary>
    public float MountainThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Number of box-blur smoothing passes applied to elevation after normalization.
    /// Each pass blends each tile with its 4 cardinal neighbors (weighted 0.5/0.5).
    /// 0 = no smoothing. 2–4 softens tectonic step discontinuities and gives rivers
    /// natural curved paths instead of following straight fault lines.
    /// </summary>
    public int SmoothingPasses { get; set; } = 0;
}
