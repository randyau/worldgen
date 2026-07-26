namespace WorldEngine.Sim.Config;

/// <summary>FastNoiseLite terrain noise and mountain/tectonic thresholds for world generation.</summary>
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

    // ─── Tectonic contribution weights (each multiplied by TectonicIntensity) ──────────
    /// <summary>Continental fault line → mountain ridge.</summary>
    public float ContinentalFaultWeight { get; set; } = 0.6f;
    /// <summary>Volcanic/subduction fault → volcanic peaks (large positive boost).</summary>
    public float VolcanicFaultWeight    { get; set; } = 0.5f;
    /// <summary>Oceanic non-volcanic fault → slight trench (negative).</summary>
    public float OceanicFaultWeight     { get; set; } = -0.3f;
    /// <summary>Continental interior (no fault) → highland bias.</summary>
    public float ContinentalInteriorWeight { get; set; } = 0.15f;
    /// <summary>Oceanic interior (no fault) → slight basin (negative).</summary>
    public float OceanicInteriorWeight  { get; set; } = -0.10f;

    // ─── FastNoiseLite fractal parameters ───────────────────────────────────────
    public int   FractalOctaves    { get; set; } = 5;
    public float FractalLacunarity { get; set; } = 2.0f;
    public float FractalGain       { get; set; } = 0.5f;
}
