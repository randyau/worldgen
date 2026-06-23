# World Generation Tuning Reference

A quick-reference guide to the config parameters that most affect visual output.
Each entry lists: the TOML key, current value, what the knob does perceptually,
and what artifact you'll see if it's too high or too low.

Update this file whenever a new artifact is discovered and diagnosed.

---

## How to read this doc

- **Current value** is the live value in `config/sim_config.toml`.
- **Safe range** is the band that consistently produces plausible maps.
- Artifacts are described visually (what you see on the map), not mechanically.

---

## Tectonic Layer

### `[world_gen.tectonics]`

| Key | Current | Safe range | Too low | Too high |
|-----|---------|------------|---------|----------|
| `plate_count` | 15 | 8–25 | Huge plates, boring geography | Sliver plates, chaotic fault lines |
| `min_plate_separation_fraction` | 0.12 | 0.08–0.18 | Plates cluster, uneven continent sizes | Too few valid positions, slow generation |
| `continental_plate_fraction` | 0.45 | 0.30–0.60 | Water world (too little land) | Land world (too little ocean) |
| `boundary_perturb_strength` | 10.0 | 5–15 | Perfectly straight plate edges; all mountain ridges, rivers, and biome bands follow straight lines | Coastlines look jagged/noisy; plate boundaries too irregular |
| `boundary_perturb_frequency` | 0.07 | 0.04–0.12 | Broad smooth curves (at low strength, nearly straight) | Tight wiggly boundaries; looks random not geological |

---

## Elevation Layer

### `[world_gen.elevation]`

| Key | Current | Safe range | Too low | Too high |
|-----|---------|------------|---------|----------|
| `noise_scale` | 0.3 | 0.1–0.5 | Smooth, blob-like continents with few local features | Very noisy elevation, archipelago-heavy |
| `tectonic_intensity` | 0.8 | 0.4–1.0 | Gentle rolling terrain, few dramatic peaks | Extreme ridges, large ocean trenches; many tiles snap to Mountain/HighMountain |
| `smoothing_passes` | 3 | 0–5 | Sharp fault-line steps; rivers snap to straight trenches along plate edges | Terrain is mushy; elevation gradients too gentle for good river carving |

**Note on `smoothing_passes`:** Each pass blends each tile with its 4 cardinal neighbours (centre weight 4, neighbour weight 1, divisor 8). Passes stack multiplicatively — 3 passes softens significantly more than 1.

---

## Ocean Layer

### `[world_gen.ocean]`

| Key | Current | Safe range | Too low | Too high |
|-----|---------|------------|---------|----------|
| `default_sea_level` | 0.40 | 0.30–0.55 | Less ocean, more land; continents crowd together | More ocean; island-heavy world |
| `erosion_passes` | 2 | 0–4 | Thin continental fault lines protrude into ocean as Mountain/Tundra ridges (white lines in ocean) | Wide ocean erosion; eats narrow peninsulas |
| `min_ocean_8neighbors` | 5 | 4–6 | Threshold 4 erodes too aggressively (converts coastal peninsulas to ocean); threshold 6 leaves 2-tile ridges | Threshold 6 leaves 2-tile-wide ridges; pair with 3+ erosion passes |

**Artifact: ridges in ocean** — if you see white/gray lines crossing open ocean, increase `erosion_passes` or lower `min_ocean_8neighbors` to 4.

---

## Climate Layer (moisture and temperature)

### `[climate]`

| Key | Current | Safe range | Too low | Too high |
|-----|---------|------------|---------|----------|
| `moisture_carry_decay` | 0.993 | 0.985–0.997 | Deep interiors are desert (0.97 = ~22% moisture at 50 tiles in) | Too much moisture everywhere; very little desert or arid land |
| `tropical_band_half_width` | 0.25 | 0.15–0.35 | Narrow tropical belt; little rainforest | Tropics cover most of the world |
| `rain_shadow_loss_fraction` | 0.6 | 0.3–0.8 | Weak rain shadow; leeward deserts don't form | Extreme rain shadow; everything east of a mountain range is desert |
| `temperature_noise_scale` | 0.20 | 0.10–0.28 | Pure horizontal temperature bands; biomes stripe cleanly by latitude | Temperature swings too wild; hot tiles appear in polar bands |
| `temperature_noise_frequency` | 0.015 | 0.008–0.025 | Very broad temperature anomaly blobs (~125 tiles/period) | Fine-grained speckle; anomalies too small to feel like climate regions |
| `moisture_noise_scale` | 40.0 | 20–60 | Horizontal moisture bands dominate; same biome across an entire latitude row | Chaotic moisture; rain shadow signal is masked, deserts appear in coastal zones |
| `moisture_noise_frequency` | 0.013 | 0.008–0.022 | Large wet/dry blobs (~77 tiles/period) | Fine moisture speckle; biome variety but no coherent wet or dry regions |

**Artifact: horizontal biome banding** — if biomes stripe strongly by latitude (big orange desert band, big green forest band, big gray tundra band), increase `temperature_noise_scale` and/or `moisture_noise_scale`, and lower both frequency params for broader regional blobs (current: 0.009 ≈ 111-tile period).

**Two persistent band locations and their causes:**
- **~45° latitude** — the `hot_temperature = 180` byte threshold is where the cosine latitude curve crosses 180/255 ≈ 70% latFrac. This is the hard tropical/temperate biome boundary. Noise blurs it but can't eliminate it entirely. Increasing `temperature_noise_scale` pushes more tiles across this threshold from both sides.
- **~15–25° latitude** — the edge of `tropical_band_half_width = 0.25`. At this latitude the moisture sweep switches direction (trade winds E→W in tropics, westerlies W→E outside). This creates a sharp moisture transition at the band edge. Increasing `moisture_noise_scale` blurs it; lowering `tropical_band_half_width` moves it closer to the equator.

---

## Biome Thresholds

### `[world_gen.biome_thresholds]`

These are byte values (0–255) used by `BiomeClassifier`. Thresholds partition the
temperature and moisture byte range into zones. All values here are **upper bounds**
(Mountain = elevation ≥ this value, etc.).

| Key | Current | Effect of changing |
|-----|---------|-------------------|
| `high_mountain_elevation` | 220 | Raise = fewer HighMountain tiles; lower = more snow-capped peaks |
| `mountain_elevation` | 180 | Raise = fewer Mountain tiles (more land accessible); lower = more rugged terrain |
| `hills_elevation` | 140 | **Note:** Hills is no longer a standalone biome — this threshold is only used for the Beach check (no beach on hills). Changing it only affects where beaches appear. |
| `hot_temperature` | 180 | Raise = smaller tropical zone; lower = more of the world is "tropical" |
| `cold_temperature` | 80 | Raise = larger boreal zone; lower = smaller |
| `polar_temperature` | 40 | Raise = bigger polar/tundra zone; lower = less tundra |
| `wet_moisture` | 160 | Raise = less forest/rainforest (harder to be "wet"); lower = forests everywhere |
| `dry_moisture` | 60 | Raise = more desert (easier to be "dry"); lower = more grassland |
| `arid_moisture` | 30 | Raise = more true desert; lower = less desert, more plains |

**Note on Hills:** Hills elevation was previously a biome override, turning all tiles
at 140–179 elevation into "Hills" regardless of climate. This caused a large gray band
across mid-latitude terrain. Hills is now treated as a terrain feature only; those tiles
classify by temperature/moisture normally.

---

## What to check when you see an artifact

| What you see | Likely cause | Knob to turn |
|---|---|---|
| Straight mountain ridges / rivers / biome edges | Voronoi plate boundaries propagating | Raise `boundary_perturb_strength` (currently 10) |
| White/gray lines across open ocean | Thin tectonic fault ridges above sea level | Raise `erosion_passes` or lower `min_ocean_8neighbors` |
| Massive desert everywhere | Moisture decaying too fast inland | Raise `moisture_carry_decay` toward 0.995 |
| Horizontal biome stripes | Temperature and/or moisture too uniform per latitude row | Raise `temperature_noise_scale` and `moisture_noise_scale` |
| Giant gray "Hills" band | (Fixed) Hills was a biome override — now classified by climate | No longer an issue |
| World is all ocean / all land | `default_sea_level` off | Adjust `default_sea_level` |
| Mountains everywhere / gentle hills only | `tectonic_intensity` too high/low | Adjust `tectonic_intensity` |
| Rivers carve straight channels | Elevation step at plate boundaries too sharp | Raise `smoothing_passes` |
