# M8 Phase 1 — Layout Host & Tabbed Dock

**Milestone:** M8 — UI Framework Rewrite
**Status:** COMPLETE — 2026-07-23. Compile + full test suite verified in-session; user confirmed
by manual playtest that the app runs correctly (panels, overlays, event log, tile inspector, and
the cause-chain modal all functioning). Window resize specifically not exercised (app window is
fixed-size, pre-existing and unrelated to M8 — see milestone doc); full resize-bug-sweep deferred
until resizing is supported.
**Depends on:** 8.0 (tokens, `WeScroll`, `PanelFrame`, `Tooltip`)
**Worker model:** Sonnet (architectural — this is where the historic bugs die)
**Framework refs:** `docs/ui_design_framework.md` §3 (Layer 4), §5 (workspace), §6 (panel contract)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §3, §5, §6;
> `WorldEngine.UI/Game1.cs` (the composition/wiring — skim `StartSim`, the panel registration
> ~L500–520, keybind registration ~L546–564, and the per-frame `Draw`/UI region);
> `WorldEngine.UI/UI/PanelManager.cs`. Use SCIP for anything else.

## Goal

Build Layer 4: a layout host that owns **every** screen rectangle, z-band, scroll reserve, and
hit-test, plus the `SimWorkspace` tabbed dock and a `ModalHost`. Then **port the existing panels
as-is** into regions (via a thin adapter) to prove the host without rebuilding panel internals
yet. After this phase, the four historic bug classes are structurally impossible (framework §3.2):

- off-screen overflow → host clamps every region to the viewport;
- float over map/other panels → regions are non-overlapping by construction; only Float/Modal
  z-bands draw over the map, and they follow strict rules;
- click-through leakage → the input router consumes input top-down by z-band; opaque regions block
  the map beneath;
- content hidden behind scrollbar → every scroll region uses `WeScroll`'s reserve.

## What exists now (grounding)

- `Game1.cs` composes everything: builds sim, holds `WorldSnapshot` per frame, adds Myra widgets
  to a root desktop. Panels are positioned ad-hoc (absolute `Top/Left`, e.g. `ToggleBar.Top = 84`).
- `PanelManager` shows/hides panels and renders a toggle bar; it does **not** own geometry.
- Panels currently self-size (`TileInspectorPanel` sets `Width = 340`, its own `ScrollViewer`
  `Height = 220`). This is exactly what the host takes over.

## Stories

| # | Deliverable | Files (new unless noted) | 
|---|-------------|--------------------------|
| 8.1.1 | Region + z-band model + `LayoutHost` | `UI/Layout/Region.cs`, `UI/Layout/LayoutHost.cs` |
| 8.1.2 | Input / hit-test router | `UI/Layout/InputRouter.cs` |
| 8.1.3 | `PanelContext` + `IWorkspacePanel` contract | `UI/Layout/IWorkspacePanel.cs`, `UI/Layout/PanelContext.cs` |
| 8.1.4 | `SimWorkspace` tabbed dock | `UI/Layout/SimWorkspace.cs` |
| 8.1.5 | `ModalHost` | `UI/Layout/ModalHost.cs` |
| 8.1.6 | Port existing panels into regions (adapter) + wire into `Game1` | `UI/Layout/LegacyPanelAdapter.cs`, `Game1.cs` (edit) |
| 8.1.7 | Architecture tests: no absolute geometry in Layout consumers | `ArchitectureRuleTests.cs` (edit) |

---

### 8.1.1 — Region & LayoutHost

A `Region` owns a rectangle, a `ZBand`, an opacity flag, and a content `Widget`. Regions do not
overlap within a band; the host computes rectangles from the viewport each resize.

```
enum RegionSlot { TopBar, MapCanvas, RightDock, Timeline, Float, Modal }

sealed class Region {
    RegionSlot Slot; ZBand Band; bool Opaque; Rectangle Bounds; Widget Content;
    bool HitTest(Point p) => Opaque && Bounds.Contains(p);
}

sealed class LayoutHost {
    void SetViewport(Rectangle vp);            // recomputes all region Bounds; called on resize
    Region Slot(RegionSlot slot);
    IReadOnlyList<Region> RegionsTopDown();    // for input routing (Modal→…→Base)
    // Fixed grid: TopBar (full width, height = TopBarClearance), Timeline (full width, bottom
    // strip), RightDock (right column, width = dock width, between bars), MapCanvas (remaining).
    // Float/Modal are viewport-sized overlays, drawn/hit-tested by band, not the grid.
}
```

Rules the host enforces (this is the whole point):
- Panels are placed **into** a region's content; they never set `Top/Left/Width/Height`.
- Every region's content is wrapped so it clamps to `Bounds` and scrolls internally (use
  `WeScroll` for dock/timeline content). Content can never grow past its rectangle.
- Dock width is a host property (default `UiTheme.SidebarWidth`), resizable within `[min,max]`;
  `MapCanvas` recomputes from it so the map is never occluded by a resized dock.

Done when: resizing the window re-lays-out all regions; no region exceeds the viewport; the map
region shrinks to accommodate the dock.

### 8.1.2 — Input router

One method arbitrates input each frame, top-down by z-band (framework §5.1):

```
sealed class InputRouter {
    // Returns the region that consumes this pointer event, or null (→ map/camera gets it).
    Region? Route(Point pointer, LayoutHost host);
    // Modal captures unconditionally; Transient(toasts) & Float(legend/tooltip) are
    // click-through (never consume); Chrome regions consume when Opaque && Bounds.Contains.
}
```

`Game1` asks the router **before** feeding the click to the camera/tile-pick logic (today the map
pan/tile-select reads the mouse directly ~L600). If the router returns a region, the map does not
receive the event. This is the click-leak fix. `// DECISION:` document that Float regions
(legends/tooltips) are intentionally click-through so the map stays interactive beneath them.

Done when: clicking on a docked panel never pans the map or selects a tile beneath it; clicking a
floating legend *does* pass through to the map.

### 8.1.3 — `PanelContext` + `IWorkspacePanel`

Per framework §6. Define exactly:

```
readonly record struct PanelContext(
    WorldSnapshot Snapshot, ISelectionSink Selection, Presenter Present, CommandGateway Commands);

enum PanelPlacementKind { PinnedDefault, Contextual, Summoned }
readonly record struct PanelPlacement(PanelPlacementKind Kind, SelectionKind? For = null);

interface IWorkspacePanel {
    string Id { get; }
    string Title { get; }
    PanelPlacement Placement { get; }
    Widget Build();                 // returns a PanelFrame built from the kit
    void Bind(PanelContext ctx);
    void Refresh();                 // called per snapshot only while visible
    EmptyState? EmptyFor(PanelContext ctx);
}
```

`CommandGateway` is a thin wrapper over the existing `_commandQueue.Enqueue` in `Game1` (so panels
don't touch the queue directly). `ISelectionSink` is the 8.0 seam; 8.2 makes it `SelectionBus`.

Done when: interfaces compile; a trivial stub panel implements the contract.

### 8.1.4 — `SimWorkspace` (the tabbed dock)

Framework §5.2–5.3. Owns the RightDock region content:
- **Pinned zone** (top): a `WeVStack` of pinned panels; height-capped (≤55% dock height).
- **Contextual tab zone** (bottom): a tab strip + a single visible contextual panel body. Exactly
  one contextual panel shows at a time (no stacking → no cross-panel overflow).
- API: `Register(IWorkspacePanel)`, `Pin(id)/Unpin(id)`, `ShowSummoned(id)`, `SetSelection(kind,
  id)` (routes Contextual panels — full routing arrives in 8.2; here wire a direct call).
- Visited contextual tabs stay in the strip with a close affordance (the lightweight nav-history,
  framework §5.2).
- Uses a `PanelRegistry` (`// MOD SEAM:`) for the set of known panels.

Done when: pinned Event Log stays visible while the contextual tab swaps between Inspector/Civ/
Character; only one contextual panel visible at once; nothing overflows the dock.

### 8.1.5 — `ModalHost`

One modal surface (framework §5.5): dims scrim (`SurfaceModalScrim`), captures all input (via
router Modal band), centers + viewport-clamps, closes on Esc/Cancel. API: `Show(Widget content,
Action? onClose)`, `Close()`, `bool IsOpen`. God Mode's 4 dialogs, causal-chain, and first-run all
route here (rewired in 8.3; for now just stand it up and move the first-run dialog onto it as the
proof case).

Done when: first-run dialog shows via `ModalHost`, dims the app, captures clicks, closes on Esc.

### 8.1.6 — Port existing panels (adapter) + Game1 wiring

Do **not** rebuild panel internals yet. Wrap each current panel in `LegacyPanelAdapter :
IWorkspacePanel` that exposes the panel's existing `Root` widget as `Build()` and forwards
show/hide. Register them with `SimWorkspace`:
- Pinned: Event Log.
- Contextual: Tile Inspector (Tile), Character Watch (Character), Civ History (Civ).
- Summoned: God Mode (F2), Help (?).
- Timeline → Timeline region. OverlayBar/TimeControls → TopBar region.

Edit `Game1`: replace ad-hoc absolute placement + `PanelManager` toggling with `LayoutHost` +
`SimWorkspace` + `InputRouter`. Keep keybinds pointing at `SimWorkspace.ShowSummoned/Toggle` for
now. `PanelManager` is retired here (delete it and its `ToggleBar`) — the dock owns visibility.

`// DECISION:` The adapter keeps old panels' self-sizing internally for one phase; 8.3 removes it
when each panel is rebuilt against `PanelFrame`. The host clamps them regardless, so the bugs are
already fixed even before the rebuild.

Done when: the app runs with the new dock; every current panel reachable; **manual bug sweep
passes** (see verification).

### 8.1.7 — Architecture tests

- `PanelsSetNoAbsoluteGeometry` — scan `UI/Layout/` + adapters: assert no assignment to
  `.Top/.Left/.Width/.Height` on Myra widgets **in host code** (the host uses `Bounds`/Myra
  layout, not per-widget absolutes). Full ban on `UI/Panels/` lands in 8.3.

## Verification (manual bug sweep — this is the phase's whole point)

Run the app and confirm:
1. Resize the window small → no panel runs off-screen; dock content scrolls instead of clipping.
2. Open several panels → contextual panels never stack/overlap; pinned Event Log always visible;
   nothing floats over the map except legend/tooltip.
3. Click on a docked panel over where the map is → map does **not** pan/select beneath it.
4. A long panel (Tile Inspector on a rich tile) → scrollbar present, no content hidden behind it.
5. Open a modal → rest of UI dimmed and non-interactive; Esc closes.
6. `scripts/test-fast.sh` green; zero warnings; New World (N) resets cleanly.

## Handoff to 8.2

8.2 replaces `ISelectionSink` with `SelectionBus`, deletes `Game1`'s `ConsumePendingX()` polling,
and makes `SimWorkspace.SetSelection` fire from the bus automatically.
