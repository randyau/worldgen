# Milestone 1 Test Runbook

This document covers everything needed to verify that all M1 work behaves as designed. The automated suite runs in WSL2. The UI runs on the Windows side.

---

## Prerequisites

**In WSL2:**
- .NET 8 SDK (`dotnet --version` → `8.x.x`)
- Git repo at `/mnt/e/linux/dev/worldgen` (or wherever you cloned it)

**On Windows (for UI testing):**
- Self-contained publish: **nothing extra** — the exe bundles .NET and SDL2
- Framework-dependent publish: .NET 8 Runtime or SDK installed on Windows
- A keyboard and mouse (camera controls require both)

---

## Step 1 — Automated Tests (WSL2)

Run the full test suite first. If anything fails here, fix it before doing manual UI tests.

```bash
cd /mnt/e/linux/dev/worldgen
scripts/build.sh
```

Expected output:
```
=== WorldEngine — build + test ===
[1/2] Building WorldEngine.Sim ...      Build OK
[2/2] Running test suite ...
Passed!  - Failed: 0, Passed: 192, Skipped: 0
=== All done ===
```

**If tests fail:** do not proceed to UI testing. The sim is the foundation.

---

## Step 2 — Publish for Windows

```bash
cd /mnt/e/linux/dev/worldgen
scripts/publish-win.sh
```

This produces `publish/win-x64/WorldEngine.UI.exe` (self-contained, ~80 MB). The script prints the exact Windows path.

> To force a clean DB between test runs, delete `publish\win-x64\world.db` on the Windows side before launching.

---

## Step 3 — Launch on Windows

Open PowerShell or Windows Explorer and navigate to:
```
E:\linux\dev\worldgen\publish\win-x64\
```
Double-click `WorldEngine.UI.exe`, or from PowerShell:
```powershell
& 'E:\linux\dev\worldgen\publish\win-x64\WorldEngine.UI.exe'
```

A 1280×720 window should appear immediately.

---

## Manual Test Cases

Work through these in order. Each has a pass/fail criterion. Tick the box when verified.

---

### TC-01 — World Generation Screen

**What to do:** Launch the exe. Watch the startup sequence.

**Expected:**
- [ ] Window opens at ~1280×720
- [ ] A centered panel appears: "Generating World..." title, a progress bar, a layer name label
- [ ] Layer names cycle through in order: Tectonics → Elevation → Ocean → Rivers → Magic → Climate → Biomes → Resources → POI → Assembling
- [ ] Progress bar visibly advances
- [ ] Generation completes in under 60 seconds and the gen screen disappears
- [ ] The sim starts (tile map becomes visible, time controls appear at top)

**Failure signs:** Black screen on launch; crash/exception; progress bar stuck; gen never completes.

---

### TC-02 — Tile Map Renders

**What to do:** After gen completes, inspect the tile map.

**Expected:**
- [ ] The world fills the window (minus the sidebar on the right)
- [ ] Tiles are distinct colored blocks — no solid black or magenta
- [ ] Ocean tiles are dark blue; land tiles show varied greens/browns/grey; mountains show grey/white
- [ ] The world has both ocean and land (a mix of biomes is visible)
- [ ] A 1px grid border is visible between tiles at default zoom

**Failure signs:** Solid black or magenta everywhere; no grid border; all one color.

---

### TC-03 — Camera Pan and Zoom

**What to do:** Use the camera controls.

**Steps and expected:**
- [ ] Right-click and drag → map pans smoothly in the drag direction
- [ ] Scroll wheel up → map zooms in (tiles get larger)
- [ ] Scroll wheel down → map zooms out (tiles get smaller)
- [ ] Zoom in fully (many scroll-ups) → zoom stops at a maximum (tiles don't become enormous); no crash
- [ ] Zoom out fully (many scroll-downs) → zoom stops at a minimum (tiles don't disappear); no crash
- [ ] After panning to edge of world, tiles beyond the world boundary show as black (no wrap-around tearing)

**Failure signs:** Pan inverted; zoom doesn't stop; crash on zoom limits.

---

### TC-04 — Map Overlays

**What to do:** Press each overlay key and observe the map color change.

| Key | Overlay | What to look for |
|-----|---------|-----------------|
| `B` | Biome | Ocean=dark blue, forest=green, desert=orange, tundra=light grey |
| `E` | Elevation | High peaks=white, sea floor=black, gradient in between |
| `T` | Temperature | Equatorial (center lat)=red/warm, poles=blue/cold |
| `M` | Moisture | Wet coasts and rainforests=blue, deserts=tan |
| `R` | Resources | Most tiles=biome color; some tiles=yellow (deposit) or purple (rare) |
| `G` | Magic Intensity | Most tiles=black; magic hotspots=bright violet |

**Expected:**
- [ ] `B` — biome colors (baseline, should match TC-02)
- [ ] `E` — greyscale gradient; clearly white at mountain peaks
- [ ] `T` — obvious temperature gradient; not all one color
- [ ] `M` — ocean coasts and tropical tiles visibly wetter (bluer) than deserts
- [ ] `R` — most tiles unchanged, scattered yellow/purple specks visible
- [ ] `G` — mostly black with small violet clusters

**Failure signs:** Key press changes nothing; all tiles stay one color; magenta appears (missing case in OverlayRenderer).

---

### TC-05 — Tile Inspector

**What to do:** Left-click various tile types and read the inspector panel.

**Setup:** Press `B` to ensure you're on biome overlay so you can identify tile types by color.

**Steps:**
1. Left-click an ocean tile (dark blue)
   - [ ] Inspector panel appears on the right side
   - [ ] Shows `Biome: Ocean`
   - [ ] `Elevation` is a low number (< 80)
   - [ ] `Fertility` is probably 0
   - [ ] Resources section says `(none)`
   - [ ] Disasters section says `(none)`

2. Left-click a mountain tile (grey/white)
   - [ ] Shows `Biome: Mountain` or `HighMountain`
   - [ ] `Elevation` is a high number (> 180)
   - [ ] Seasonal deltas for temperature are negative in winter

3. Left-click a volcanic tile (dark red) if one exists
   - [ ] Shows `Biome: Volcanic`
   - [ ] May show deposits in the Resources section
   - [ ] May show active disasters after sim has run a while

4. Press `Escape`
   - [ ] Inspector panel disappears

5. Left-click another tile while paused, then unpause — inspect a tile near the equator that might drought
   - [ ] Panel updates each sim tick if that tile's state changes (moisture value changes)

**Failure signs:** Click does nothing; panel doesn't appear; panel shows wrong tile; Escape doesn't close it.

---

### TC-06 — Time Controls

**What to do:** Test the speed buttons and year display.

**Steps:**
- [ ] On launch: sim starts at Slow speed or paused — check Year counter is visible at top
- [ ] Click `||` (Pause) → Year/Season display stops advancing
- [ ] Click `▶|` (Step one tick) — only enabled when paused
   - [ ] Year/Season display advances by exactly one tick worth
   - [ ] Button remains enabled (still paused)
- [ ] Click `▶` (Slow) → Year counter advances slowly; `▶|` button becomes disabled
- [ ] Click `▶▶` (Normal) → Year counter advances faster
- [ ] Click `▶▶▶` (Fast) → Year counter blurs through seasons quickly
- [ ] Click `▶▶▶▶` (Ultrafast) → Years advance multiple per second
- [ ] TPS counter (top right) shows a nonzero number while unpaused
- [ ] Click `||` again → sim pauses; TPS shows 0 or stops

**Expected sequence:** Spring → Summer → Autumn → Winter → Spring (next year).

**Failure signs:** Year display frozen despite unpaused; Step button enabled while unpaused; TPS shows 0 while sim is running.

---

### TC-07 — Event Log

**What to do:** Let the sim run for ~30 seconds at Fast speed, then inspect the event log.

**Steps:**
- [ ] Set speed to `▶▶▶` (Fast) and wait until Year 10+
- [ ] Event log (bottom-right panel) shows at least a few entries
- [ ] Each entry shows `[Year] Season TYPE @(X,Y)` or similar format
- [ ] Headline events appear in **gold**
- [ ] Regional events appear in **white**
- [ ] Uncheck `Regional` checkbox → regional events disappear from the list
- [ ] Re-check `Regional` → they reappear
- [ ] Check `Background` → additional lower-tier events appear

**Expected event types:** After Year 5+ you should see environmental events: `WildfireOccurred`, `FloodOccurred`, `DroughtBegan`, `EarthquakeOccurred`, `VolcanicEruption`, `ClimateShifted`, `BiomeChanged`, `SeaLevelChanged`, `ResourceRecovered`.

**If no events appear after Year 20:** The event pipeline may be broken. Check EventGate config in `config/sim_config.toml` — `minimum_recorded_tier` should be `0`.

**Failure signs:** No events ever appear; all entries same color; filter checkboxes don't change what's shown.

---

### TC-08 — Stability Run

**What to do:** Let the sim run unattended.

- [ ] Set to `▶▶▶▶` (Ultrafast)
- [ ] Pan around, switch overlays, click tiles at intervals
- [ ] Leave running for **5 minutes**
- [ ] No crash, freeze, or window hang
- [ ] Year counter has advanced well past Year 50 by the end

**Failure signs:** OOM crash; deadlock (window stops responding); exception popup.

---

## Acceptance Criteria Summary

All 8 test cases must pass for Milestone 1 to be considered complete:

| TC | Description | Pass? |
|----|-------------|-------|
| TC-01 | World gen screen | |
| TC-02 | Tile map renders | |
| TC-03 | Camera pan/zoom | |
| TC-04 | All 6 overlays | |
| TC-05 | Tile inspector | |
| TC-06 | Time controls | |
| TC-07 | Event log | |
| TC-08 | 5-minute stability | |

---

## Known Limitations (not bugs)

- **No fonts/sprites:** All tiles render as solid color blocks. Text labels use Myra's built-in default font.
- **World regens each run:** The world is always Seed=42, 2000×1600 km, 10 km tiles. Changing this requires editing `Game1.cs` for now.
- **world.db accumulates across runs:** The SQLite database grows each session. Delete `publish\win-x64\world.db` before a clean test run.
- **No civilizations/characters yet:** The event log will only show environmental events (disaster, climate, sea level, biome). M2 adds history entities.
- **DesktopGL on Windows:** MonoGame DesktopGL uses SDL2 + OpenGL. If the OpenGL driver is missing or outdated, the window may fail to open. Update your GPU driver if this happens.
- **Myra UI is unstyled:** The default Myra theme is functional but visually plain. UI polish is post-M1.

---

## Troubleshooting

**Window opens then immediately closes:**
Run from PowerShell (not Explorer) to see the error output printed to stdout before the process exits.

**`world.db` locked error:**
Another instance of the exe is still running. Kill it via Task Manager before relaunching.

**Config not found / sim uses wrong values:**
Verify `publish\win-x64\config\sim_config.toml` exists. The publish script copies it from `config/sim_config.toml` in the repo.

**Tiles all appear magenta:**
`OverlayRenderer.GetColor` has an unhandled `BiomeType` value. Check that `BiomeType` enum in the sim matches the switch cases in `OverlayRenderer`.

**Events never appear in the log:**
- Confirm sim is unpaused and running (Year counter advancing)
- Check `publish\win-x64\config\sim_config.toml`: `minimum_recorded_tier` should be `0`, `suppressed_types` should be `[]`
- After Year 5+ at Fast speed, at least wildfire or drought events should appear
