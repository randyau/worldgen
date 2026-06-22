# Phase 7 — Epic 1.7: Basic UI
**Status:** NOT STARTED  
**Requires:** Phase 6 complete (WorldSnapshot must be populated with real events)  
**Reads required:** `docs/interface_contracts.md` (WorldSnapshot, TileDisplayData, TileInspectorData), `docs/design_session_decisions.md` (DS-D)

---

## Goal
Build the MonoGame + Myra UI that shows the running simulation. No automated tests for rendering code — verification is manual. Every story in this phase has a **Manual Test** section.

## UI Architecture Rules
- UI thread NEVER touches WorldState
- Read `StateCache.Read()` each frame — frame always has a valid snapshot to render
- Send player input via `CommandQueue.Enqueue()` — never call sim objects directly
- All Myra components initialized in `LoadContent()`, updated from snapshot in `Update()`
- MonoGame `GraphicsDevice` objects (SpriteBatch, Texture2D) only created/used on UI thread

## File Structure
```
WorldEngine.UI/
  Game1.cs                    # main game class
  Rendering/
    TileMapRenderer.cs        # tile grid rendering
    Camera2D.cs               # viewport + zoom
    OverlayRenderer.cs        # color lookup per overlay type
  UI/
    TimeControlsPanel.cs      # pause/speed/year display
    EventLogPanel.cs          # scrolling event list
    TileInspectorPanel.cs     # tile detail on click
    WorldGenScreen.cs         # progress screen during gen
```

---

## Story 1.7.1 — MonoGame Window + Two-Thread Bootstrap

**File:** `WorldEngine.UI/Game1.cs`

```csharp
public sealed class Game1 : Game
{
    private readonly CommandQueue _commandQueue;
    private readonly StateCache _stateCache;
    private readonly ConcurrentQueue<(string Layer, float Fraction)> _genProgress;
    private SimLoop? _simLoop;
    private WorldState? _worldState;

    protected override void LoadContent() { /* initialize Myra desktop */ }
    protected override void Update(GameTime gt) { DrainGenProgress(); ApplySnapshot(); }
    protected override void Draw(GameTime gt) { /* render current snapshot */ }
}
```

**Startup sequence:**
1. Create `CommandQueue`, `StateCache`, `_genProgress`
2. Start world gen on `Task.Run(() => pipeline.RunFullAsync(..., new Progress<...>(p => _genProgress.Enqueue(p))))` 
3. Show `WorldGenScreen` while gen task runs
4. On task completion: create `SimLoop(worldState, ...)`, call `simLoop.Start()`
5. Transition to main sim view

**No automated tests.** Manual verification:
- Window opens at correct size
- Console/log shows "World generation starting"
- Progress screen appears
- Sim starts after gen completes
- No crash for 60 seconds of running

---

## Story 1.7.2 — WorldGenScreen

**File:** `WorldEngine.UI/UI/WorldGenScreen.cs`

A Myra `Panel` displayed while `Task<WorldState>` is running. Shows:
- Title: "Generating World..."
- Per-layer progress bar (drain `_genProgress` queue in `Game1.Update()`, update the bar)
- Current layer name label
- Estimated percentage complete (approximation from layer count)

**Manual test:**
- All layer names appear in sequence as generation runs
- Progress bar advances visibly
- Screen disappears when gen completes and sim starts

---

## Story 1.7.3 — Camera2D + TileMapRenderer

**Files:**
```
WorldEngine.UI/Rendering/Camera2D.cs
WorldEngine.UI/Rendering/TileMapRenderer.cs
```

**Camera2D:**
```csharp
public sealed class Camera2D
{
    public Vector2 Position { get; private set; }  // world tile space
    public float Zoom { get; private set; }        // pixels per tile
    
    public void Pan(Vector2 delta);                // right-drag
    public void ZoomAt(Vector2 screenPoint, float factor);  // scroll wheel
    
    public TileCoord ScreenToTile(Vector2 screenPos, GraphicsDevice gd);
    public Vector2 TileToScreen(TileCoord coord, GraphicsDevice gd);
    public (int minX, int minY, int maxX, int maxY) GetVisibleTileBounds(GraphicsDevice gd);
    
    // Send SetViewport command when bounds change:
    public void FlushViewportCommand(CommandQueue queue);
}
```

**TileMapRenderer:**
- Each frame: get `snapshot.VisibleTiles`, draw each as a colored rectangle
- Tile color from `OverlayRenderer.GetColor(tile, overlay)`
- No tile texture atlas in M1 — solid color blocks
- 1px dark border between tiles when zoom > 4
- Right-drag to pan, scroll wheel to zoom

**OverlayType color lookups (all tunable — consider adding to a colors config or hardcoding as constants):**
```
Biome overlay:    Ocean=DeepBlue, CoastalWater=LightBlue, Beach=SandYellow, 
                  Tundra=LightGrey, BorealForest=DarkGreen, TemperateForest=Green,
                  TropicalRainforest=BrightGreen, Grassland=LightGreen, Savanna=Tan,
                  Desert=Orange, Swamp=DarkOlive, HighMountain=White, Mountain=Grey,
                  Hills=DarkYellow, Plains=Wheat, Volcanic=DarkRed
Elevation overlay: byte value → greyscale (0=black, 255=white)
Temperature:       byte value → cold=blue → hot=red gradient
Moisture:          byte value → dry=tan → wet=blue gradient  
Resources:         HasDeposit=yellow, HasRareResource=purple, else=biome color
MagicIntensity:    byte value → black=none → bright violet=max
```

**Manual test:**
- Map renders all tiles in biome colors
- Right-drag pans the view
- Scroll wheel zooms in/out
- Map tiles are sized proportionally to zoom
- No stuttering for 30 seconds of panning

---

## Story 1.7.4 — Map Overlays

**File:** `WorldEngine.UI/Rendering/OverlayRenderer.cs`

Keyboard shortcuts to switch overlays. Each keypress calls `_commandQueue.Enqueue(new SetActiveOverlay(type))`:

```
B → Biome
E → Elevation
T → Temperature (EffectiveTemperature from TileDisplayData)
M → Moisture (CurrentMoisture)
R → Resources
G → MagicIntensity
```

**Manual test:**
- Each key changes the map colors immediately on next frame
- Elevation shows white mountains and black ocean
- Temperature shows obvious latitude gradient
- Moisture shows rain shadow effect behind mountains
- Resources highlights tiles with deposits

---

## Story 1.7.5 — Tile Inspector Panel

**File:** `WorldEngine.UI/UI/TileInspectorPanel.cs`

Left-click on a tile: convert screen position to TileCoord via Camera2D, enqueue `SetInspectedTile(coord)`. The sim thread builds `TileInspectorData` and includes it in the next snapshot. Panel reads `snapshot.InspectedTile`.

**Panel displays (Myra VerticalStackPanel with labels):**
```
Tile (X, Y)
Biome: [BiomeType]
Elevation: [byte]
Base Temperature: [byte]
Current Moisture: [byte]
Effective Temperature: [float:F1]
Magic Intensity: [byte]
Fertility: [byte]
Flags: [comma-separated active StaticFlags]

--- Seasonal Profile ---
Spring:  Temp [±sbyte]  Moisture [±sbyte]
Summer:  ...
Autumn:  ...
Winter:  ...

--- Resources ---
[foreach deposit: Type (Quality, Depth)]
[or "(none)" if empty]

--- Disasters ---
[foreach disaster: Type Intensity [TicksRemaining ticks]]
[or "(none)" if empty]
[In active drought: Yes/No]
```

Click outside the panel or press Escape: enqueue `SetInspectedTile(null)`.

**Manual test:**
- Click ocean tile → panel shows Ocean biome, no deposits, no disasters
- Click volcanic tile → shows Volcanic biome, has deposits, possibly has disasters
- Click forested tile during simulation → panel updates each tick as disaster state changes
- Escape closes panel

---

## Story 1.7.6 — Event Log Panel

**File:** `WorldEngine.UI/UI/EventLogPanel.cs`

A Myra `ScrollViewer` containing a `VerticalStackPanel` of event rows.

**Data source:** `snapshot.RecentEvents` — the sim thread filters these by tier already via `EventCache`.

**Display:**
- Row format: `[Year] [Season] [TYPE] [Location if any]`
- Color by tier: Headline=Gold, Regional=White, Character=LightGrey, Background=DarkGrey
- Auto-scroll to latest entry (unless player has scrolled up — detect scroll position delta)
- Three checkboxes (Myra `CheckBox`): `[x] Headline  [x] Regional  [ ] Background` — filters which tiers show

**Manual test:**
- Events appear and scroll in as simulation runs
- Tier filter checkboxes actually hide/show rows
- Headline events appear in gold
- Scrolling up freezes auto-scroll; scrolling to bottom re-enables it

---

## Story 1.7.7 — Time Controls Panel

**File:** `WorldEngine.UI/UI/TimeControlsPanel.cs`

A Myra `HorizontalStackPanel` at the top of the screen:

```
[||] [▶] [▶▶] [▶▶▶] [▶▶▶▶]   Year 47 — Autumn   [▶|] step   [FPS: 60  TPS: 4]
```

Buttons:
- `||` (Pause): `SetSimSpeed(Paused)` or `PauseToggle()`
- `▶` (Slow): `SetSimSpeed(Slow)`
- `▶▶` (Normal): `SetSimSpeed(Normal)`
- `▶▶▶` (Fast): `SetSimSpeed(Fast)`
- `▶▶▶▶` (Ultrafast): `SetSimSpeed(Ultrafast)`
- `▶|` (Step): `StepOneTick()` — only enabled when paused

`TicksPerSecond` from `snapshot.TicksPerSecond` for the TPS counter.

**Manual test:**
- Pause stops year counter advancing
- Fast makes year counter advance visibly fast
- Ultrafast makes it blur (many ticks per frame)
- Step button advances exactly one tick when clicked while paused
- Year and season display are always accurate

---

## Phase 7 Done Criteria

All manual tests pass:
- [ ] Window opens, world gen runs to completion, sim starts
- [ ] All 6 overlays render correctly (visually distinguishable)
- [ ] Pan and zoom work smoothly
- [ ] Tile inspector shows correct data for selected tile
- [ ] Event log shows events in correct tier colors
- [ ] Time controls affect sim speed visibly
- [ ] No crash or freeze in 5 minutes of interactive use
- [ ] Left-click sends SetInspectedTile (not right-click — that's pan)

**After Phase 7: Milestone 1 is complete.**
