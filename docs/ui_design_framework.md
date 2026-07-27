# World Engine — UI Design Framework

**Status:** Proposed (design authority) — supersedes ad-hoc panel conventions
**Author role:** Lead UX/UI Design
**Date:** 2026-07-23
**Companion:** `docs/ui_touchpoints.md` (the inventory this framework answers)
**Applies to:** `WorldEngine.UI` only. `WorldEngine.Sim` is never touched (headless, snapshot-in / command-out).

---

## 0. How to read this document

This is the target-state design system for the World Engine UI. It is the source of truth
for *how* UI is built, not *what features* ship (that's `docs/roadmap.md`). It exists so that a
developer building any panel — today's event log or M9's worldgen preview — makes the same
structural choices and never re-solves the same layout, navigation, or formatting problem twice.

Read in order:
- **§1–§2** — principles and design language (the "why" and the tokens).
- **§3–§5** — the architecture: the layered stack, the layout/workspace model, and the
  component library. This is the heart of the refactor.
- **§6–§9** — the standards a developer applies per panel (interaction, information display,
  settings/keybindings).
- **§10** — modding seams to leave in place now.
- **§11–§13** — per-feature application, the migration path, and the per-panel Definition of Done.

**Decisions locked by the design lead (2026-07-23):**
1. **Widget layer:** new WorldEngine-owned abstraction layer over Myra; panels rebuilt against it (greenfield-leaning, but migrated incrementally — see §12).
2. **Layout model:** **tabbed contextual dock** (§5).
3. **Moddability:** coherence-first; leave marked seams, do not build the mod schema yet (§10).
4. **Toolkit:** keep Myra, but **wrap it** — panels never reference Myra types directly (§3).

---

## 1. Design principles

These are the tie-breakers. When two implementations are equally plausible, the one that better
serves these principles wins.

### P1 — A history atlas, not a game HUD
The player is an **observer 99% of the time** (`ui_touchpoints.md` §cross-cutting-6). No avatar,
no health bar for "the player," no quest tracker, no inventory. The aesthetic target is a
**reference application / historical atlas**: dense but legible, calm, authored. Reject anything
that imports an action-game HUD idiom.

### P2 — The map is the world; the dock is the reading room
The map canvas is the primary surface and is never permanently occluded by chrome. Reading,
inspecting, and authoring happen in a **bounded dock** that cannot spill onto the map. Transient
surfaces (tooltips, legends, toasts) may float *over* the map but are explicitly non-interactive
or self-dismissing (§5.4).

### P3 — Time is always legible
Year/season, sim run/pause state, and speed are readable from anywhere without hunting. Historical
depth (a 300-year-old event vs. a live one) and temporal scale (500 vs. 5000 years) are expressed
consistently (§8.4). The timeline is a first-class navigation surface, not decoration.

### P4 — Structural correctness over discipline
The historic layout bugs — **panels running off-screen, floating over the map or over each other,
clicks leaking through panels to the map, content hidden behind scrollbars, illegible overlap** —
are not to be fixed case-by-case. The layout system (§3 Layer 4, §5) makes them *impossible to
express*. A developer should have to go out of their way to create these bugs, not out of their
way to avoid them. This principle has veto power over convenience.

### P5 — Two modes, two feelings
**God Mode** = authoring history: weighty, deliberate, rare, pause-gated, gold/ceremonial accent.
**Spotlight** = inhabiting a character: immediate, focused, live, cyan/present accent. They share
real estate today but must read as different modes of engagement (§7.7). Never let them blur.

### P6 — One way to do each thing
One selection bus. One navigation mechanism. One formatting layer. One keybind/command registry.
One panel base. Duplicated mechanisms (today: `SelectionState` **and** `ConsumePendingX()`) are a
defect, not a style choice — they get unified (§7.1).

### P7 — Present, never dump
The UI never shows raw sim internals: no `0–255` elevation bytes, no `WarDeclared` type strings,
no `GoalType: FoundCity`. A **presenter layer** (§8.1) translates sim data into human language
exactly once, reused everywhere.

### P8 — Coherence first, extension-ready
Build for internal coherence now; leave clearly-named registries and interfaces (§10) where a
future mod system will plug in. Do not pay the cost of a data-driven schema yet, but never design
a surface that would have to be torn down to add one.

---

## 2. Design language (tokens)

`UiTheme` today holds colors and a few metrics. It becomes the full token set below. **Every
visual constant lives here** — the same rule the sim has for `SimConfig`. No literal color, pad,
or font size in a panel, ever. This is enforceable in an architecture test (§13).

### 2.1 Color — by role, not by hue
Tokens are named for *meaning*, so a theme swap (or a future high-contrast / colorblind mode)
retunes everything centrally.

| Role token | Meaning | Current value |
|---|---|---|
| `TextPrimary` | Body copy | white |
| `TextSecondary` | Supporting detail | light gray |
| `TextMuted` | Hints, captions, timestamps | gray |
| `TextDisabled` | Inactive/unavailable | dark gray |
| `TextHeader` | Section & panel titles | gold |
| `AccentInteractive` | Links, active toggle | `(120,190,255)` |
| `AccentGodMode` | God Mode authoring surfaces | gold/amber |
| `AccentSpotlight` | Spotlight surfaces | cyan |
| `StatePositive` / `StateWarning` / `StateNegative` | surplus/deficit, healthy/critical, good/bad deltas | green / amber / red |
| `SurfacePanel` / `SurfaceRaised` / `SurfaceModalScrim` | dock bg / floating bg / modal dim | existing panel bg + new |
| `BorderPanel` / `BorderFocus` | panel edge / focused element | existing + accent |
| `TierColor(tier)` | Headline/Regional/Character/Background | existing |
| `CivColor(civId)` | deterministic per-civ hue | existing (shared with territory overlay) |

**Semantic pairs must meet contrast:** every `Text*` on its intended `Surface*` targets WCAG-AA
(4.5:1 body, 3:1 large). Overlay palettes (biome, elevation, temp, moisture) must be
distinguishable under common color-vision deficiencies; the elevation greyscale ramp is the
baseline safe case (`ui_touchpoints.md` §cross-cutting-1).

### 2.2 Typography — a fixed scale
Define named roles, not point sizes at call sites:
`Display` (worldgen/title) · `Title` (panel header) · `SectionHeader` · `Body` · `BodyStrong`
· `Caption` (timestamps, hints) · `Mono` (coordinates, raw numeric ledgers). Everything else is a
bug. This kills the "every label is default size" flatness noted across the inspector and profile
panels.

### 2.3 Spacing & sizing — one scale
A single spacing ramp (`Xs=2, Sm=4, Md=8, Lg=12, Xl=16`) replaces scattered `Spacing = 2/4`.
Panels reference `Space.Md`, never `8`. Component padding, row gaps, and section gaps all draw
from this ramp so vertical rhythm is uniform across panels.

### 2.4 Elevation / z-layers
A closed enum of z-bands (§5.1) — panels can't invent their own z. This is half of how P4 is
enforced.

### 2.5 Iconography & motion
- **Icons:** one pinned icon set (FontAwesome free is already pinned per project deps). Icons
  always pair with a text label or a tooltip — never icon-only for anything non-obvious (the
  current icon-only speed buttons get tooltips).
- **Motion:** minimal and functional — panel show/hide, tab switch, toast in/out. No decorative
  animation. Respect a "reduce motion" setting (§9). This is an atlas, not an arcade.

---

## 3. Architecture — the layered UI stack

The refactor's spine. Six layers, each depending only on those below it. **Panels live at Layer
3 and may only see Layers 1–2 and the cross-cutting services — never Myra (Layer 0) directly.**

```
┌─────────────────────────────────────────────────────────────────────┐
│ Layer 5  Screens          WorldGenScreen · SimWorkspace (§5)         │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 4  Layout host      Regions · Dock · Z-bands · Overflow ·      │
│                           Scroll reserve · Input/hit-test router     │  ← enforces P4
├─────────────────────────────────────────────────────────────────────┤
│ Layer 3  Panels           EventLog · Inspector · Watch · CivHistory… │
│                           (ViewModels + declarative content build)   │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 2  Composite kit    SectionHeader · StatRow · Meter · Chip ·   │
│                           EntityLink · Legend · EmptyState · Toast    │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 1  Widget kit       WeText · WeButton · WeList · WeScroll ·     │
│                           WeStack · WeField · WeDropdown (Myra wrap)  │
├─────────────────────────────────────────────────────────────────────┤
│ Layer 0  Myra             never referenced above Layer 1             │
└─────────────────────────────────────────────────────────────────────┘

Cross-cutting services (available to Layers 3–5):
  • SelectionBus (§7.1)     • Presenter/Formatter (§8.1)
  • CommandGateway → sim    • KeybindRegistry + CommandRegistry (§9)
  • SnapshotContext (current WorldSnapshot per frame)
```

### 3.1 Layer 0 → 1: wrap Myra, don't leak it
Myra stays (decision 4) but is quarantined. `WeText`, `WeButton`, `WeStack`, `WeScroll`,
`WeField`, `WeDropdown`, `WeList` etc. wrap the Myra widget and expose only tokenized properties
(a `WeText` takes a `TypographyRole` + `ColorRole`, not a Myra `Label` with a raw `Color`). Panels
build UI from these. Benefits:
- A future toolkit swap touches one layer, not 15 panels.
- Tokens are enforced by construction — a `WeText` *cannot* take a raw color.
- Myra's known sharp edges (scrollbar sizing, hit-testing quirks) are patched once in the wrapper,
  matching the existing `MyraCompat` shim pattern.

Architecture test: no `using Myra…` outside `WorldEngine.UI/UI/Kit/` (§13).

### 3.2 Layer 4 is where the historic bugs die
This is the direct answer to P4 and to the pain the design lead called out. See §5 for the full
model; the guarantees are:
- Panels **never** set absolute `Top/Left/Width/Height`. They declare *min/preferred content
  size*; the layout host assigns the rectangle. (Kills off-screen overflow and float-over-map.)
- Every scrollable region reserves scrollbar width from content width via a `ScrollReserve`
  token, so content is never hidden behind the scrollbar. (Kills scrollbar obstruction.)
- Regions are **non-overlapping by construction** except the explicit float/modal z-bands, and
  every opaque region **consumes input within its bounds** through the central hit-test router.
  (Kills click-through leakage and illegible overlap.)
- A region whose content exceeds its rectangle **always** scrolls internally; it can never grow
  past the viewport. (Kills off-screen growth.)

If a developer can't cause these bugs without editing Layer 4, the framework is working.

---

## 4. Component library (Layers 1–2)

The catalog every panel builds from. This replaces the `AddLine(string)` idiom
(`TileInspectorPanel`) with a structured vocabulary. Each entry: what it is, and which current
ad-hoc surface it absorbs.

### 4.1 Layer 1 — widget kit (thin Myra wrappers)
| Component | Purpose |
|---|---|
| `WeText` | Text with `TypographyRole` + `ColorRole`. The only way to render text. |
| `WeButton` | Text/icon button; variants: `Primary`, `Ghost`, `Link`, `Toggle`, `Danger`. |
| `WeStack` (V/H) | Tokenized-spacing stack. |
| `WeScroll` | Scroll container that auto-reserves scrollbar width (§3.2). |
| `WeField` | Labeled text input with validation state. |
| `WeDropdown<T>` | Typed combo box (replaces raw `ComboBox` in dialogs & civ selector). |
| `WeList<T>` | Virtualizable list (critical for M10 million-event logs, §11). |
| `WeIcon` | Icon from the pinned set + mandatory accessible label/tooltip. |

### 4.2 Layer 2 — composite components (the reusable vocabulary)
| Component | Purpose | Replaces / serves |
|---|---|---|
| `PanelFrame` | Titled, bordered, padded panel shell + optional close/pin/tab affordances. | Supersedes `PanelChrome.Wrap`; **all** panels use it (Tile Inspector currently doesn't). |
| `SectionHeader` | `--- Resources ---` style dividers, tokenized. | The `AddLine("--- X ---")` hack everywhere. |
| `StatRow` | Label ↔ value pair, aligned columns, optional unit/qualifier + state color. | Inspector's `Pop:`, `Health:`, temps, stores; profile lifespan lines. |
| `KeyValueGrid` | Multi-row aligned `StatRow` set. | Inspector tile block, civ stats block. |
| `Meter` | Labeled bar (0–1 or n-segment) with numeric readout + state color. | Watch panel's 7 needs bars, 6 trait bars, settlement health. |
| `SeasonalStrip` | Compact 4-season delta viz (Sp/Su/Au/Wi) instead of 8 loose numbers. | Inspector seasonal profile (a named §gap). |
| `EntityLink` | Clickable entity reference (character/civ/settlement/artifact) → fires SelectionBus. | Every place a name should be a link but isn't (§7.2). |
| `EntityChip` | Compact entity token w/ color dot (civ color, tier color). | Event-log actor/civ badges; relationship lists. |
| `TagChip` | Non-interactive label chip (cultural traits, categories) with tooltip explaining meaning. | Civ "Warlike/Merchant" bare labels. |
| `EventRow` | One event: tier stripe, `[G]` badge, year/season, presented text, entity links, cause `->`, `★` first-of-kind. | Event log rows (structured, hover-able, click-through). |
| `Legend` | Floating map legend: ramp/swatches + labels for the active overlay. | The **missing** overlay legends. |
| `Tooltip` | Standard hover tooltip (delay, follow, clamp-to-viewport). | Missing map-marker & event-row tooltips. |
| `EmptyState` | Standard "no data / no match / not built yet" treatment (icon + message + optional hint). | The three empty states (§7.5). |
| `Toast` | Transient confirmation ("Artifact placed", "World saved"). | Currently silent God-Mode actions. |
| `Timeline` | The bottom scrub/heatmap/pip surface as a first-class component. | `TimelineBar`. |
| `ModeBanner` | The God Mode / Spotlight mode indicator strip (§7.7). | Ad-hoc "SPOTLIGHT ACTIVE" label. |

Every composite takes data + callbacks, holds no sim reference, and is unit-testable in isolation.

---

## 5. The workspace: tabbed contextual dock

The chosen layout model (decision 2). The `SimWorkspace` (Layer 5) is a fixed grid of
**regions**. Regions are the *only* things that own screen rectangles; panels are assigned into
regions and never position themselves.

### 5.1 Regions & z-bands
```
┌───────────────────────────────────────────────────────────────┐
│ TOP COMMAND BAR   time · speed · year/season · pause state · overlays │  z: Chrome
├──────────────────────────────────────────────┬────────────────┤
│                                               │  RIGHT DOCK    │
│                                               │  ┌───pinned──┐ │
│                 MAP CANVAS                     │  │ Event Log │ │  z: Base(map)
│         (pan/zoom, markers, overlays)         │  ├──tabbed───┤ │     + Chrome(dock)
│                                               │  │ contextual│ │
│    ◦ Legend (float, non-interactive)          │  │  panel    │ │
│    ◦ Tooltip (float)                          │  └───────────┘ │
├──────────────────────────────────────────────┴────────────────┤
│ TIMELINE  density heatmap · headline pips · scrub · eras        │  z: Chrome
└───────────────────────────────────────────────────────────────┘
   Toasts: bottom-right float (z: Transient)
   Modals: centered + full scrim (z: Modal, captures all input)
```

**Z-bands (closed enum, §2.4):** `Base` (map) < `Chrome` (bars, dock) < `Float` (legend, tooltip)
< `Transient` (toasts) < `Modal` (dialogs + scrim). Higher bands capture input first via the
hit-test router; opaque Chrome regions block map input beneath them; Float regions are
click-through *by design* (pointer passes to the map) and never contain controls; Modal captures
everything and dims the rest.

### 5.2 The right dock: pinned + contextual tabs
The dock (default 360px, user-resizable within min/max) has two stacked zones:

- **Pinned zone (top):** panels the user wants always-on. **Event Log is pinned by default** — it
  is the primary reading surface and should never disappear behind a tab. Users can pin/unpin
  others.
- **Contextual tab zone (bottom):** a tab strip whose active panel follows **selection**
  (SelectionBus, §7.1):
  - Select a **tile** → **Inspector** tab.
  - Select a **character** → **Character** tab (Watch/Profile as sub-views, §11 — unifies the
    two-panel split the inventory flags).
  - Select a **civ** → **Civ History** tab.
  - God Mode / Help / Settings open as **explicitly-summoned tabs** (keybind or button), not
    selection-driven.

Tabs the user has visited stay available in the strip (with a close affordance), so back-and-forth
between the last character and last civ is one click — this is the framework's answer to the
"no navigation history / no back button" gap, without a heavyweight browser-history model.

### 5.3 Overflow & coexistence — solved by the region, not the panel
The old problem was "many panels stacked in a sidebar, only 1–2 visible." The tabbed dock removes
stacking entirely: **exactly one contextual panel is visible at a time**, plus the pinned zone.
Each zone is an independent `WeScroll` region that clamps to its rectangle. No panel can push
another off-screen because no panel shares a scroll flow with another. Pinned-zone height is
capped (e.g. ≤55% of dock height) so it can never starve the contextual zone.

### 5.4 Floating surfaces — the *only* things over the map
Legends and tooltips float over the map (P2). They are **non-interactive** (`Float` z-band,
click-through) and **self-clamping** (never drawn past the viewport edge — fixes the timeline
tooltip's hardcoded-position overlap gap). Toasts float bottom-right, auto-dismiss, and stack.
Nothing else is ever allowed over the map. This is the enforced version of "panels floating on top
of the map / other panels" never happening again.

### 5.5 Modals
One `ModalHost`. A modal always: dims the scrim, captures all input (no leak), centers, clamps to
viewport, traps focus, closes on Esc/Cancel. God Mode's four dialogs, the causal-chain dialog, and
first-run all route through it — no bespoke modal code per dialog.

---

## 6. Panel contract (Layer 3)

Every panel implements one base contract so the dock, keybinds, and selection treat them
uniformly (today `IPanel` exists but isn't uniformly implemented — Inspector, Timeline, OverlayBar
opt out).

```
IWorkspacePanel:
  PanelId        Id            // stable id (keybind, layout persistence, future mod registry)
  string         Title
  PanelPlacement Placement     // PinnedDefault | Contextual(SelectionKind) | Summoned
  Widget         Build()       // returns a PanelFrame; builds from Layer-2 components only
  void           Bind(PanelContext ctx)   // snapshot + selection + presenter + command gateway
  void           Refresh()      // called per snapshot while visible; no-op when hidden
  EmptyState?    EmptyFor(state)
```

Rules:
- A panel **builds from composites**, never `AddLine`, never raw Myra, never absolute geometry.
- A panel is a **pure function of the current snapshot + selection**; it holds no authoritative
  state (matches the sim/UI boundary — snapshots in, commands out).
- A panel **only refreshes when visible** (perf; today several `Refresh every frame` panels run
  hidden).
- A panel declares its **empty states** (§7.5) rather than rendering nothing.

---

## 7. Interaction patterns & standards

### 7.1 One selection bus (retire consume-once)
`SelectionState`/`SelectionRouter` already exist and are the right model. Promote them to **the**
navigation mechanism and **delete the parallel `ConsumePendingX()` polling** in `Game1`
(`ConsumePendingWatch`, `ConsumePendingCiv`, `ConsumePendingCauseChain`, the spotlight intents,
etc.). Every "go to X" becomes `SelectionBus.Select(kind, id)`; the dock reacts by routing to the
contextual tab (§5.2). This is P6 made concrete and removes the most error-prone code in the UI.

Mode intents that aren't selections (enter/exit spotlight, move-intent, goal nudges, God-Mode
authoring) go through the **CommandGateway** to the sim — not through selection. Keep the two
buses cleanly separated: *SelectionBus = "what am I looking at" (UI-only)*, *CommandGateway =
"change the world" (sim round-trip)*.

### 7.2 Everything nameable is a link
Any rendered entity name (character, civ, settlement, artifact) is an `EntityLink` that selects
that entity. This closes the whole "no click-through" cluster of gaps: civ names in the inspector,
ruler names in civ history, settlement names in the event log, artifact names everywhere.
Non-linkable names are the exception and must be deliberate.

### 7.3 Hover & tooltip standard
Map markers, event rows, meters, and tag chips all get tooltips via the standard `Tooltip`
component (consistent delay, cursor-follow, viewport-clamped). No bespoke tooltip positioning.
Map markers additionally get hover highlight; the inspected tile gets a persistent map highlight
ring (a named inventory gap).

### 7.4 Focus lens
Keep the "dim, don't hide" soft-filter behavior, but make it a **framework service** driven by
SelectionBus: when an entity is selected, `FocusLensState` broadcasts it and any panel can honor
it (event log dims non-matching rows; the map can de-emphasize unrelated markers). One
implementation, opt-in per surface.

### 7.5 Empty states — always designed, never blank
Three named treatments (`EmptyState` component, `ui_touchpoints.md` §cross-cutting-4):
- **Pre-sim** — "World is generating…" (calm, expected).
- **Not-built-yet** — "Civilization summaries rebuild every 50 years" (informative, non-alarming;
  shows next rebuild year).
- **Filtered-empty** — "No events match these filters" + a one-click Clear.
No panel ever renders an ambiguous blank area.

### 7.6 The global pause gate
Pause state must be legible everywhere (P3, and God Mode depends on it). Standard treatment:
- Top command bar shows an unmistakable **Paused** state (not just which speed button is lit).
- When paused, a subtle full-screen **edge treatment** (thin accent frame) signals "time is
  stopped."
- God Mode surfaces read the same pause state; their affordances are enabled/disabled from it
  rather than each re-checking (`CheckPaused()` becomes a shared gate, not per-dialog logic).

### 7.7 God Mode vs Spotlight — visibly different (P5)
Both open in the dock, but:
- **God Mode:** `ModeBanner` in `AccentGodMode` (gold), authoring language ("Author history"),
  weighty confirm dialogs with a summary of what will be written, a Toast confirming the authored
  event, and the `[G]` badge propagating to the event it creates. Only usable paused.
- **Spotlight:** `ModeBanner` in `AccentSpotlight` (cyan), live and present-tense, camera-follow,
  immediate goal/move nudges, no confirmation friction. Runs while time flows.
Never render them with the same chrome.

### 7.8 Confirmation & reversibility
Irreversible authoring (God Mode writes to history) confirms with a plain-language summary.
Reversible/observational actions never nag. Matches CLAUDE.md's "prefer reversible" ethos at the
UX layer.

---

## 8. Information display standards

### 8.1 The presenter layer (P7)
A single `Presenter` service converts sim data → display strings, used by **every** panel and the
event log. No panel formats sim internals itself. It owns:
- **Enums → prose:** `WarDeclared` → "declared war"; `GoalType.FoundCity` → "wants to found a
  city"; `DisasterType.VolcanicAsh` → "volcanic ashfall".
- **Raw units → human units:** the `0–255` elevation byte → meters/relative label; the temperature
  byte → °C/°F (the `TempC/TempF/TempDeltaC` math currently duplicated in `TileInspectorPanel`
  moves here and is shared).
- **Qualitative labels:** health → Good/Struggling/Critical; wellbeing → Flourishing…Spiraling;
  stores → well-stocked/adequate/bare (all currently inline in the inspector — centralized here so
  thresholds live in one place, ideally sourced from `SimConfig`-mirrored UI constants).
- **Names & ordinals:** "Aria IV the Bold," civ founding/collapse annotations.

This is the single most reused piece of the refactor and the direct fix for "life events show type
strings," "major events show `WarDeclared`," "goals show `GoalType:`," and "raw 0–255 shown."

### 8.2 Density & hierarchy
The inspector's wall-of-text is the anti-pattern. Standard: `SectionHeader` + `KeyValueGrid` +
`Meter`s, with the **most important facts first** (settlement identity/health before raw tile
bytes), and progressive disclosure — secondary detail in collapsible sections. Aligned columns via
`StatRow`, never free-typed `"  key: value"` strings.

### 8.3 Numbers
Right-aligned in `Mono` where columns compare; thousands separators for population; fixed decimals
per quantity type (owned by the Presenter); sign + color for deltas (`StatePositive/Negative`);
qualifiers ("bare", "abundant") accompany bare numbers so novices aren't lost.

### 8.4 Temporal display (P3)
- **Absolute time:** "Year 42 — Summer" always in the command bar.
- **Relative depth:** events carry a subtle age cue (recent = full color, ancient = muted) so
  300-years-ago *reads* older than *now* without a date lookup.
- **Scale adaptivity:** the Timeline renders per-year pips at small spans and auto-coarsens to
  decade/century resolution at 10k-year scale (M10) — one component, resolution-aware.
- **Scrub semantics:** scrubbing filters what's shown up to a year (non-destructive), clearly
  labeled as viewing history, visibly distinct from live.

---

## 9. Settings, keybindings & configuration

**Status: shipped.** The Settings shell (Controls + Display) landed in M8 phase 8.5;
the Simulation config tab landed in M10 phase 10.2/10.2a (`ConfigRegistry`, generic
over `SimConfig`). This section is kept as the design rationale for that surface — the
"M9" labels below are pre-renumbering references (this doc predates the 2026-07-23
milestone renumber; see `docs/roadmap.md`) and describe what actually shipped in M10.
Presets (batch-set related keys) were **not** built — out of scope, not currently planned.

### 9.1 Command registry (new) sits under keybinds
Introduce a `CommandRegistry`: every user action is a named `UiCommand` (id, label, category,
handler, default keybind). `KeybindRegistry` binds keys *to command ids*; the Help panel, the
future keybind-rebinding UI, and any toolbar button all render from the same registry. This makes
keybinds **rebindable** (a named gap) without rewiring behavior, and gives every action exactly one
definition (P6). Keeps the existing "key and button share one delegate" guarantee, generalized.

### 9.2 The Settings screen
A summoned, full-height dock tab (or overlay screen) with grouped sections:
- **Controls** — keybinding list with rebind, driven by CommandRegistry.
- **Display** — theme/contrast, reduce-motion, overlay palette choice, dock width, density.
- **Simulation config** — grouped `sim_config.toml` tunables (Character behavior, Disaster
  rates, Civ dynamics, Artifact/economy…), each a `WeField`/`Meter`/`WeDropdown` bound to a config
  key, with **per-setting and per-group reset-to-default** and a **diff-from-default** view.
- ~~**Presets** — "High conflict / Peaceful / Dense population" batch-set related keys.~~ Not built; out of scope for now.
These use the **same component kit** as everything else — settings are not a special-case UI.

### 9.3 Config surfacing pattern
A config control declares `{key, group, kind, range, default}` and the Settings screen renders it
generically. This is *coherence-first* (§10): it's a clean internal registry today and the natural
seam a mod system would later populate — but we are **not** building the mod schema now.

---

## 10. Modding seams (coherence-first, build later)

Per decision 3, do not build the mod system. Do leave these named seams so it can be added without
a teardown (P8). Mark each with `// MOD SEAM:` in code. (Note: M10 phase 10.3 shipped *data*
modding — TOML config/data with load-time validation, see `docs/modding.md` — which is a
different, narrower thing than the plugin/code mod system this section describes; that stays
out of scope.)

| Seam | What it is now | What a mod could do later |
|---|---|---|
| `PanelRegistry` | Internal list of `IWorkspacePanel` the dock knows about. | Register a new dock panel. |
| `OverlayRegistry` | Internal table of the 7 overlays (id, palette, legend, tile→color fn). | Add a new map overlay + legend. |
| `CommandRegistry` (§9.1) | Named actions + default keybinds. | Add actions / rebindable commands. |
| `Presenter` maps (§8.1) | Enum→prose, unit tables. | Localize / re-skin terminology. |
| Token set (`UiTheme`, §2) | Central design tokens. | Ship an alternate theme. |
| `PanelContext` | The data/services a panel receives. | Stable contract for third-party panels. |

Rule: any new UI subsystem that could plausibly be extended gets a **registry** rather than a
hardcoded switch — even if we're the only ones registering into it today.

---

## 11. Applying the framework to each touchpoint

| Surface | Framework treatment |
|---|---|
| **Map + overlays** | `OverlayRegistry`; each overlay ships a `Legend` (floats, non-interactive). Markers get `Tooltip` + hover highlight; inspected tile gets a highlight ring. Click marker → `SelectionBus.Select` (no more tile-then-inspector detour). |
| **Event Log** | Pinned dock panel. `WeList<EventRow>` (**virtualized** for M10 scale). Rows use `EntityLink`s, `Presenter` text, `Tooltip`, focus-lens dimming. Full-history search backed by the DB (a named gap), paged via the virtual list. |
| **Filter panel** | Collapsible section inside the Event Log's `PanelFrame`, not a separate floating thing. |
| **Tile Inspector** | Contextual tab on tile select. Rebuilt: `KeyValueGrid` + `SectionHeader` + `Meter` + `SeasonalStrip`; identity-first ordering; `Presenter` for all units/labels; `EntityLink` for civ/character/artifact. This panel is the flagship of the `AddLine`→components migration. |
| **Character (Watch + Profile)** | **Unified** into one contextual Character tab with **Live** and **History** sub-views (removes the two-panel split). `Meter`s for needs/traits; `EntityChip` relationships; life events are `EventLink`s back into the log/causal chain. Spotlight = the Live sub-view in Spotlight mode (`AccentSpotlight` `ModeBanner`). |
| **Civ History** | Contextual tab on civ select. `WeDropdown` civ selector with sorting; `TagChip` (with meaning tooltips) for cultural traits; ruler/war/event lists become `EntityLink`/`EventLink`s; stale-summary state uses the not-built-yet `EmptyState` showing next rebuild year. |
| **God Mode** | Summoned tab, `AccentGodMode` `ModeBanner`, pause-gated via the global gate (§7.6). Four dialogs → `ModalHost` + `WeDropdown`/`WeField`. Confirm shows a summary; success fires a `Toast`; authored event carries `[G]`. |
| **Causal chain** | `ModalHost` dialog; upgrade from list-only toward a node/edge view with `EntityLink`/`EventLink` click-through and edge-type styling (triggered-by vs. influenced-by). |
| **Help** | Summoned tab rendered from `CommandRegistry`, grouped by category, searchable; God-Mode/Spotlight workflow cards. No more hand-maintained text list. |
| **Timeline** | First-class bottom region (`Timeline` component): resolution-adaptive (§8.4), era/civ-lifespan annotations, viewport-clamped scrub tooltip, larger hit target. |
| **Worldgen screen (M9)** | Layer-5 screen reusing the kit: per-layer preview thumbnails, parameter `WeField`/`Meter`s, "rerun from layer," layer-status list, Commit. Not a bespoke UI — same tokens/components. |
| **Economic panels (M8)** | New pinned/summoned panel via `PanelRegistry`; `KeyValueGrid` for surplus/deficit (`StatePositive/Negative`), `WeList` for trade routes; trade-flow overlay via `OverlayRegistry`. Created-object detail = a Character-tab-style detail view reusing `KeyValueGrid` + `EntityLink` lineage. |
| **First-run / onboarding** | `ModalHost` for the welcome; contextual first-use `Tooltip`s; inline concept explainers ("what is a Headline event?") as `Tooltip`/`EmptyState` hints. |

---

## 12. Migration path (incremental, not big-bang)

The end state is greenfield-leaning, but shipped in safe phases so the app runs throughout.

- **Phase 0 — Tokens & kit foundation.** Expand `UiTheme` to the full token set (§2). Build Layer
  1 (`We*`) and the highest-value Layer 2 components (`PanelFrame`, `SectionHeader`, `StatRow`,
  `Meter`, `EntityLink`, `EmptyState`, `Tooltip`). Add the `Presenter` service. No visible change
  yet; add architecture tests (§13).
- **Phase 1 — Layout host.** Build Layer 4 (regions, z-bands, scroll-reserve, hit-test router) and
  the `SimWorkspace` tabbed dock (§5). Port existing panels *as-is* into regions (they still use
  old internals) to prove the host. **This phase alone kills the overflow/float/click-leak/scrollbar
  bugs (P4).**
- **Phase 2 — Selection unification.** Route everything through `SelectionBus`; delete
  `ConsumePendingX()` polling (§7.1). Contextual tabs go live.
- **Phase 3 — Panel migration.** Rebuild panels against the component kit, one per PR, starting
  with the Tile Inspector (worst offender) and the Character tab unification. Each migrated panel
  drops `AddLine` and raw Myra.
- **Phase 4 — Command registry & Help.** Introduce `CommandRegistry`; regenerate Help from it;
  add keybind rebinding.
- **Phase 5 — Settings screen scaffold.** Build the Settings screen shell + Display/Controls tabs
  (Sim-config tab lands with M9).
- **Ongoing seams.** Add `PanelRegistry`/`OverlayRegistry` as the touched code passes through
  them (§10).

Each phase is independently shippable and leaves the app working.

---

## 13. Per-panel Definition of Done (design checklist)

A panel/surface is design-complete when:
- [ ] Uses only Layer 1–2 components; **no raw Myra above Layer 1, no `AddLine` string dumps.**
- [ ] Every color/size/font from a token; **no literals** (architecture-testable).
- [ ] Wrapped in `PanelFrame`; assigned to a region; **sets no absolute geometry.**
- [ ] All text through the `Presenter` — **no raw enums, no `0–255`, no type strings.**
- [ ] All entity names are `EntityLink`s unless deliberately inert.
- [ ] Navigation goes through `SelectionBus`; world changes through `CommandGateway`; **no new
      consume-once polling.**
- [ ] Declares its empty state(s); never renders an ambiguous blank.
- [ ] Refreshes only when visible.
- [ ] Content scrolls within its region; **nothing off-screen, nothing behind a scrollbar, no
      float over the map, no click-through.**
- [ ] Actions are keybound via `CommandRegistry` where applicable; Help updates automatically.
- [ ] God Mode vs Spotlight surfaces carry the correct `ModeBanner`/accent.
- [ ] Contrast/colorblind-safe per §2.1.

**New architecture tests to add** (alongside the existing rule tests):
1. No `using Myra` outside `WorldEngine.UI/UI/Kit/`.
2. No raw `Color`/pixel literals in `WorldEngine.UI/UI/Panels/`.
3. Every panel implements `IWorkspacePanel`.
4. No panel sets `Top/Left/Width/Height` directly.

---

## 14. Open questions (deferred, not blocking)

- **Dock side & detachment:** framework fixes the dock right; do we ever want it left-dockable or
  a second dock? (Deferred — tabbed model makes one dock sufficient near-term.)
- **Localization timing:** the `Presenter` is the natural i18n seam; when does l10n become a
  requirement? (Affects string externalization now vs. later.)
- **Causal graph fidelity:** list → node-graph is scoped as an enhancement; is a true graph view a
  product priority or is an indented link-tree enough?
- **Save-scoped layout persistence:** should dock layout/pins persist per-world or globally?
  (Leaning global via a UI-prefs file, mirroring the first-run flag pattern.)

---

*This framework is the design authority for `WorldEngine.UI`. Changes to it are design decisions,
not implementation details — record rationale here (or in `docs/design_session_decisions.md`) when
it evolves.*
