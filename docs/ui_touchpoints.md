# World Engine — UI Touchpoints Reference

**Audience:** Interaction designers and UI designers planning a comprehensive redesign or design-system pass.
**Purpose:** Enumerate every existing UI surface and interaction in the game, and all future surfaces implied by the remaining milestone roadmap, so the designer has a full inventory to work from.
**Date:** 2026-07-23
**Codebase milestone:** M7 complete (God Mode + Spotlight). M8–M10 are planned but unbuilt.

---

## Product context

World Engine is a **procedural history simulation**, not a traditional game. A world is generated, history runs forward (tens to thousands of in-game years), and the player observes, queries, and optionally influences events. The primary audience is **worldbuilders and writers** who want to watch a history unfold and then read/export it. There is no win condition, no player avatar (except in Spotlight mode), and no failure state.

The simulation runs on a background thread and is completely headless; the UI reads immutable snapshots of world state every frame. The engine is deterministic — the same seed produces identical history. UI actions send commands to the sim; the sim processes them asynchronously.

**Key mental model:** The map is the world. The sidebar is the reading room. Time runs continuously in the background. The player pauses to author, inspect, and read.

---

## Application lifecycle and screens

### 1. Launch → World Generation Screen

**What it is:** Full-screen overlay that appears immediately on launch and during any "New World" reset. Blocks all other UI.

**Current behavior:**
- Shows "Generating World…" header text
- A horizontal progress bar (0–100%)
- A layer-name label that ticks through the generation pipeline step names (e.g. "Tectonic", "Elevation", "Climate", "Biome", "Rivers", "Resources", "Magic")
- Transitions to a "World ready!" state with a "▶ Start Simulation" button when all layers complete
- Clicking "▶ Start Simulation" dismisses the screen and starts the sim loop

**Missing / design gaps:**
- No preview of the world while it generates — the map is completely hidden
- No per-layer visual preview; the player can't see what elevation looks like before climate runs
- No world parameters visible (seed, size, world name)
- No ability to adjust parameters and regenerate (M9 feature)
- No progress estimate or time remaining

**Future (M9):** This screen is where layered preview + world parameter adjustment will live. Players need to be able to see each layer's output as it generates, tweak sliders (sea level, world size, rainfall, etc.), and re-run individual layers before committing.

---

### 2. Main Simulation View

The primary and only view while the sim is running. Composed of:
- **Map canvas** (left/center) — the procedurally rendered tile grid
- **Sidebar** (right, fixed width 360px) — stacked panels
- **Top bar** — time controls and overlay buttons
- **Timeline bar** — horizontal bar at the bottom of the map

These are detailed individually below.

---

## Map canvas

The map is a 2D tile grid rendered with a pan/zoom camera. Each tile is a colored rectangle; at high zoom levels, thin borders appear between tiles.

### Camera and navigation

- **Pan:** click-and-drag anywhere on the map (not handled in sidebar)
- **Zoom:** mouse scroll wheel, centered on cursor position
- **Default zoom:** full world visible on launch (zoom-to-fit behavior)
- **Coordinate wrapping:** the world wraps east-west (cylinder model); panning left indefinitely wraps around. The map does not wrap north-south.
- **Spotlight follow:** when a character is spotlighted, the camera re-centers on that character's tile each frame

**Design note:** There is no minimap. At maximum zoom-out the full world fits on screen. There are no named map regions or zoom-to-selection controls.

### Tile click — selection model

Left-clicking a map tile sets it as the **inspected tile**. This:
1. Opens/refreshes the Tile Inspector Panel in the sidebar
2. Updates GodModePanel with the new target tile (used for artifact placement, disaster targeting, character spawning)
3. In Spotlight mode: also enqueues a `SetSpotlightMoveIntent` command, biasing the spotlighted character to move toward that tile

**Escape** clears the inspected tile and hides the inspector.

### Tile rendering — overlays

Seven named overlays, each coloring every tile differently:

| Overlay | What it shows | Color encoding |
|---------|---------------|----------------|
| Biome | Biome classification | Fixed palette per biome type (green, tan, blue, white, etc.) |
| Elevation | Terrain height | Greyscale ramp, 0–255 |
| Temperature (Temp) | Current effective temperature | Blue (cold) → red (hot) |
| Moisture | Current moisture | Brown (dry) → teal (wet) |
| Resources | Mineral/resource deposit presence | Black base + colored marker per deposit type |
| Magic | Magic intensity layer | Black base + purple/gold intensity |
| Territory | Civ ownership + settlement dots | Procedurally assigned civ color per tile; settlement tiles rendered as dots |

**Active overlay state:** One overlay is always active. The active button is highlighted in the OverlayBar. Switching overlay does not affect any panel state.

**Currently missing:** Per-overlay legend (color ramp + labels rendered on the map). The designer would need to add a floating legend overlay or corner legend widget for each mode.

### Entity and settlement markers on map

Rendered on top of tile colors at all zoom levels (scale with zoom):

- **Settlements:** small dot in the civ's assigned color, at the settlement's founding tile
- **Ruins:** rendered if a settlement was destroyed and the tile is unoccupied by a new one
- **Named characters (Tier 1):** small colored marker
- **Legendary beasts:** distinct marker (different shape/color from characters)
- **Tile improvements** (farms, mines, etc.): icon on the tile if zoom is sufficient

**Missing:** No marker tooltip on hover. No click-on-entity to select it directly (you must click the tile and then use the inspector). At low zoom, markers overlap unreadably.

---

## Top bar

A horizontal strip at the top of the window, always visible.

### Time controls (left side of top bar)

Five speed buttons (always visible, no label — icon only):

| Button | Speed | Effect |
|--------|-------|--------|
| `\|\|` | Paused | Freezes simulation; required for God Mode authoring |
| `▶` | Slow | ~1 tick/second |
| `▶▶` | Normal | Default speed |
| `▶▶▶` | Fast | 4–8× normal |
| `▶▶▶▶` | Ultrafast | Maximum throughput |

**Space key (registered in M7 patch):** toggles between Paused and Normal.

**Step button (`▶|`):** advances exactly one tick; only enabled when paused. Allows frame-by-frame inspection.

**Year/season label:** "Year 42 — Summer" — updated every frame.

**TPS display:** "TPS: 14" — ticks per second, shows sim throughput.

### Overlay bar (right side of top bar, or sidebar section)

Seven buttons: Biome, Elevation, Temp, Moisture, Resources, Magic, Territory.
The active overlay's button text is highlighted in the accent color. Clicking any button sets that overlay as active.

**Keyboard accelerators:** B, E, (T is territory), M, R, G — these are the single-key shortcuts registered in the keybind system.

---

## Sidebar

Fixed-width panel column (360px) on the right edge of the screen. Contains a stack of panels that can be shown/hidden independently via the PanelManager. Panels are toggled by keyboard shortcuts or toolbar buttons.

**Current panel toggle bar:** a row of buttons corresponding to each panel (implemented as part of the sidebar); buttons reflect open/closed state.

---

## Sidebar panels (current)

Each panel has a title bar (with chrome), optional close button, and a scrollable content area.

---

### Event Log Panel

**Key:** no keyboard toggle (embedded in sidebar, always shown unless hidden)
**What it does:** Streams recent simulation events in reverse-chronological order. This is the primary "reading" surface — the player watches history happen here.

**Content per event row:**
- `[G]` gold badge if the event was authored via God Mode
- Tier color stripe (4px left bar): gold for Headline, blue for Regional, grey for Background
- Year + season abbreviation: `[42 Su]`
- Short event description: "war declared", "settlement founded", "artifact created", etc.
- Location: `@ Stonehaven` or `@(12,34)` if no named settlement
- Actor name button (clickable): opens Character Profile for named-character events
- Civ name button (clickable): opens Civ History panel for that civilization
- `->` button: opens Causal Chain dialog for that event
- `★` gold star if this event is the first of its type in the world's history

**Filter Panel (collapsible header above event log):**

A collapsible "▼ Filters" / "▶ Filters" header that expands to show:
- **Tier checkboxes:** Headline (default on), Regional (default on), Background (default off)
- **Hide God Mode checkbox:** hides player-authored events from the log
- **Domain text field:** free-text substring filter on event domain/category
- **Actor text field:** free-text substring filter on actor name
- **Year range:** From / To year fields
- **Clear button:** resets all filters to defaults
- Shows "(no events match filter)" empty state when filters eliminate everything

**Empty states:**
- `(no events yet)` before the sim produces any events
- `(no events match filter)` when filters are active and eliminate all results

**Focus lens (cross-panel feature):** When a settlement or character is selected elsewhere, events not involving that entity are dimmed (greyed out text) rather than hidden. This is the "focus lens" — a soft filter mode.

**Design gaps:**
- Event rows have no hover state or tooltip
- The compact format is often cryptic ("battle" with a location coordinate, no names)
- No click-through from the event row itself (only the actor button and -> button are interactive)
- No "load more" or pagination — limited to the in-memory ring buffer of recent events
- No way to search the full historical database, only recent events

---

### Filter Panel

Described above as part of the Event Log surface. Technically a separate component but always rendered above the Event Log.

---

### Tile Inspector Panel

**Key:** no keyboard toggle — appears automatically when a tile is clicked, hides on Escape
**What it does:** Shows complete data about the last-clicked tile.

**Content:**

If the tile has ruins:
- Ruin banner: "RUINS OF STONEHAVEN (destroyed 2x)"
- Destroyed year and cause

If the tile has a settlement:
- Settlement name, civ, population, health score
- Founded year; conquered year/from-civ if applicable
- Resource ledger (this-tick production): food, water, timber, iron, etc. (positive/negative)
- Resource stores: total stockpile quantities with qualitative labels (well-stocked / adequate / bare)

Always shown:
- Tile coordinates (X, Y)
- Biome type
- Elevation (0–255 raw)
- Base temperature (°C and °F)
- Current moisture
- Effective temperature (seasonal adjusted)
- Magic intensity
- Fertility
- Seasonal profile: temperature delta and moisture delta per season (Spring/Summer/Autumn/Winter)
- Resource deposits: type, quality, depth
- Active disasters: type, intensity, ticks remaining (or ∞)
- In-drought status

Territory section:
- Owning civ name
- City name that claims this tile
- Tile improvement if present: type, year built, builder name

Characters on this tile:
- Named characters (Tier 1): name, civ, ancestry, HP, age, wellbeing; each has a `[Watch]` button that opens the Character Watch panel for that character
- Tier 2 specialists: name, HP, age
- Artifacts carried by characters on this tile

Legendary beasts on this tile:
- Name, legendary flag, HP%, food%, age

Settlement artifacts (if settlement on this tile):
- Name, category, quality, origin, creator name

History at this tile:
- List of historical events at this coordinate: (Year, description)

**Design gaps:**
- Very dense text-only layout, no visual hierarchy
- No click-through on civ names, character names, or artifact names (only the `[Watch]` button links out)
- No visual map marker highlighting the inspected tile
- Raw numbers (0–255 elevation, raw temperature byte) shown directly — not user-friendly
- Seasonal profile is hard to parse as 8 separate delta numbers

---

### Character Watch Panel

**Key:** W  
**What it does:** Live tracking panel for one named character. Also serves as the Spotlight HUD.

**Content when a character is being watched (no spotlight):**

- Name + epithet: "Aria the Bold"
- Civ and age: "Civ: Ironhold  |  Age: 12s  (3 yrs)"
- Location: "(42, 18) — Temperate Forest"
- Wellbeing status: Flourishing / Content / Neutral / Distressed / Spiraling (color-coded green → red)
- **Needs bars** (7 needs): Food, Safety, Shelter, Belonging, Status, Purpose, Spiritual — each shown as a 10-segment text bar and numeric value
- **Active goals**: list of current goal types with priority scores
- **Personality traits**: Ambition, Curiosity, Loyalty, Compassion, Creativity, Aggression — each displayed as a 5-tick bar
- Spotlight description (2 lines): "Spotlight biases this character's decisions without overriding survival autonomy. Click tile → move intent."
- `[Enter Spotlight]` button
- `[Full Profile]` button → opens Character Profile Panel
- `[Close]` button

**Content when character is spotlighted:**

Everything above, plus:
- `[Exit Spotlight]` button at the top
- "SPOTLIGHT ACTIVE" label in cyan
- `[Move to inspected tile]` button (enabled only when a tile is inspected)
- `[Goal: Wander]` and `[Goal: Settle]` buttons side by side

**Behavior notes:**
- Refreshed every frame when visible
- The watched character is set via `[Watch]` buttons in the Tile Inspector, the `[Full Profile]` button, or by clicking an actor name in the Event Log (which opens Profile, not Watch)
- Entering Spotlight also sets the watched character, so the Watch panel auto-switches to that character
- When the spotlighted character dies, spotlight is automatically exited and watch panel loses context

**Design gaps:**
- No visual indicator of which character is being watched on the map (no map pin or highlight)
- Goals list lacks descriptions — shows "GoalType: FoundCity" not prose
- No relationship display (who does this character know, like, or hate?)
- No history navigation from this panel
- The Watch panel and Profile panel are separate; the player has to click `[Full Profile]` to get historical context

---

### Character Profile Panel

**Key:** no keyboard toggle — opened by clicking actor names in the event log or `[Full Profile]` in Watch panel
**What it does:** Static historical summary of one character (living or dead), queried from the history database.

**Content:**
- Full name with ordinal (e.g. "Aria IV the Bold") and ancestry
- Life span: "Born Year 12  |  Died Year 67 (combat)" or "Born Year 12  |  Alive"
- Cultural descriptor from ancestry (architectural style, artistic traditions)
- Ruler info if applicable: "Ruler of Ironhold (3rd ruler)"
- **Life Events** (top 10 by significance, chronological): "Year 15 — Formed alliance", "Year 32 — Declared war", etc.
- **Relationships** (if any): Bond partners, rivals
- `Generate Narrative` button (permanently disabled stub — V2 LLM feature)

**Design gaps:**
- No visual (portrait, icon, ancestry flag)
- Life events show type strings, not prose descriptions
- No artifacts section (the character may own legendary items — not shown here)
- No links from life events back to those events in the log or to the causal chain
- "Generate Narrative" button present but always disabled — confusing

---

### Civ History Panel

**Key:** H  
**What it does:** Full historical arc of a civilization, queried from the history database.

**Content:**
- **Civ selector ComboBox** at the top: all known civs, labeled with "(active)" or "(collapsed YearNNN)"
- After selecting a civ:
  - Name (gold)
  - Founded / collapsed years; origin (nomads / splinter from Civ X)
  - Dominant ancestry + cultural style + artistic traditions
  - Stats: peak settlements, total rulers, wars declared, wars suffered, years at war
  - **Cultural Traits:** comma-separated list (e.g. "Warlike, Merchant, Scholarly")
  - **Rulers:** succession list — name, ordinal, epithet, birth–death years
  - **Key Wars:** up to 5 most significant wars — year and opponent
  - **Major Events:** up to 10 Headline-tier events — year and type string

**Design gaps:**
- Dropdown shows all civs but lacks sorting (by era, by size, by status)
- Cultural traits are just labels with no explanation of what they mean
- Rulers list is raw text; no click-through to character profiles
- War entries show opponent name but no outcome, duration, or territory result
- Major events show type strings ("WarDeclared", "SettlementFounded"), not prose
- No summary statistics visualization (timeline, era chart)
- Summaries are only rebuilt every 50 in-game years; panel may show stale data

---

### God Mode Panel

**Key:** F2  
**What it does:** Authoring panel — lets the player pause the sim and inject events into history. All actions are pause-gated.

**Content:**
- Status label: "Paused — ready" (green) or "Pause to use God Mode" (red)
- 2×2 grid of action buttons:
  - `Place Artifact` — opens modal dialog
  - `Trigger Disaster` — opens modal dialog
  - `Spawn Character` — opens modal dialog
  - `Nudge Character` — opens modal dialog
- HOW TO USE section (static hint text):
  - Space — pause / resume
  - Click map tile → sets target for Place Artifact / Trigger Disaster / Spawn
  - W → Watch panel → select char → Nudge

**Target context (set by map click each frame):**
- Current inspected tile coordinate → used as target for artifact/disaster/spawn
- Current watched character → used as target for nudge

**Behavior:** The panel is disabled (buttons still visible, but actions warn on click) when the sim is running. A `CheckPaused()` guard runs before any modal dialog opens.

**See also:** Four modal dialogs opened from this panel (described in Modals section below).

---

### Help Overlay Panel

**Key:** ? (OemQuestion)  
**What it does:** Lists all keyboard shortcuts and explains button-based workflows.

**Content — keyboard shortcuts (generated from KeybindRegistry):**

*Overlays group:*
- B — Biome overlay
- E — Elevation overlay
- T — Territory overlay
- M — Moisture overlay
- R — Resources overlay
- G — Magic overlay

*Panels group:*
- H — Civ history panel
- W — Character watch panel
- ? — This help
- F2 — God Mode panel

*World group:*
- Space — Pause / resume
- N — New world
- Ctrl+S — Save world
- Escape — Deselect tile

*Static GOD MODE & SPOTLIGHT section:*
- GOD MODE (F2): 1) Click map tile, 2) Pause (Space), choose action; Nudge: open Watch (W) first
- SPOTLIGHT (W panel): Open Watch → [Enter Spotlight]; click map tile → move intent; goal buttons → bias behavior; character remains autonomous

**Design gaps:**
- No visual layout — pure text list
- No grouping by feature area; keyboard shortcuts and workflow descriptions are interleaved
- No search
- No examples or screenshots

---

## Modal dialogs (current)

All four are opened from God Mode Panel and require the sim to be paused.

### Place Artifact Dialog

- Shows target tile coordinate
- Dropdown (ComboBox): ArtifactCategory — Weapon, Armor, Regalia, Tool, Tome, Relic
- Text field: Custom name (optional; auto-generated if blank)
- Confirm / Cancel buttons
- On confirm: enqueues `AuthorPlaceArtifact(coord, category, name?)`

### Trigger Disaster Dialog

- Shows target tile coordinate
- Dropdown: DisasterType — Wildfire, Flood, VolcanicAsh, SeismicDamage
- Confirm / Cancel buttons
- On confirm: enqueues `AuthorTriggerDisaster(coord, type)`

### Spawn Character Dialog

- Shows target tile coordinate
- Text field: Ancestry ID (optional; random ancestry if blank)
- Confirm / Cancel buttons
- On confirm: enqueues `AuthorSpawnCharacter(coord, ancestryId?)`

### Nudge Character Dialog

- Shows currently watched character name (or "(none)" if no watched character)
- Dropdown: CharacterNudge — RaiseMorale, LowerMorale, SetWander, SetSettle
- Confirm / Cancel buttons
- On confirm: enqueues `AuthorNudgeCharacter(characterId, nudge)` — silent no-op if no character is watched

### Causal Chain Dialog

**What it is:** Opened when the player clicks `->` on any event in the Event Log.  
**What it does:** Shows the causal graph for that event — what led to it (upstream causes) and what it caused downstream.  
**Current behavior:** The data exists in the database (CausalEdges table). The dialog is wired to `_pendingCauseChainEventId` in the event log and consumed in Game1, but the visual implementation of the causal chain dialog itself is rendered as a text list of connected events (chain view in the civ history panel's "MAJOR EVENTS" section, not a visual graph).  
**Design gap:** No interactive graph view. No click-through to related events. No visualization of edge types (triggered-by vs. influenced-by).

### First-Run Orientation Dialog

**What it is:** A modal dialog shown exactly once on the first launch after install.  
**Content:** Welcome text, 5 tips (time controls, overlays, event log, causal chain button, help key), "Got it — start exploring" button.  
**Dismiss behavior:** Clicking the button (or closing the window) writes a flag file and never shows again.

---

## Timeline bar

A horizontal bar drawn at the **bottom of the map area**, below the tile grid.

**What it shows:**
- **Density heatmap:** per-decade event count, colored blue (few events) → cyan (many events)
- **Headline event pips:** gold 3×3 dots at the year each Headline-tier event occurred
- **Century tick marks:** faint white vertical lines at every 100-year boundary
- **Scrub handle:** white vertical line at the current scrub year (while dragging)

**Interactions:**
- **Click and drag:** scrubs to a historical year — while held, the event log filters to events up to that year and shows a "Year NNN" tooltip above the handle
- **Hover (no drag):** shows a hint label: "History bar: blue=event density · gold dots=headlines · click to scrub to year"
- **Release:** returns to current sim year (scrub is non-destructive — the sim keeps running)

**Design gaps:**
- Scrub state is not wired to actually replay history; it only filters what the event log shows
- No named era regions or labeled century markers
- Bar height is fixed and small; hard to click precisely on specific years
- No indication of when major civs were founded/collapsed (horizontal annotations)
- Tooltip label positioning is hardcoded, may overlap map content

---

## Keyboard and input model summary

All keyboard bindings are registered in `KeybindRegistry` and rendered in the Help panel. The full list:

| Key | Action | Category |
|-----|--------|----------|
| Space | Pause / resume | World |
| B | Biome overlay | Overlays |
| E | Elevation overlay | Overlays |
| T | Territory overlay | Overlays |
| M | Moisture overlay | Overlays |
| R | Resources overlay | Overlays |
| G | Magic overlay | Overlays |
| H | Civ history panel | Panels |
| W | Character watch panel | Panels |
| ? | Help overlay | Panels |
| F2 | God Mode panel | Panels |
| N | New world | World |
| Ctrl+S | Save world | World |
| Escape | Deselect tile / close | World |

**No keyboard shortcut for:**
- Opening / navigating Tile Inspector (mouse-only)
- Full Profile panel (no keyboard path — opened from Watch or Event Log)
- Causal chain (no keyboard path — opened from Event Log `->` button)
- Any Spotlight intent (all button-based in Watch panel)
- Zoom (scroll wheel only)
- Pan (drag only)

---

## Theme and visual system

`UiTheme.cs` is the single source of design tokens. Current tokens:

| Token | Usage |
|-------|-------|
| `HeaderText` | Panel section headers |
| `BodyText` | Default readable text |
| `MutedText` | Secondary / hint text |
| `Accent` | Active overlay highlight |
| `DisabledText` | Unfocused / de-emphasized items |
| `TierColor(tier)` | Returns gold (Headline), blue (Regional), grey (Background) |
| `CivColor(civId)` | Deterministic per-civ hue from civId hash |
| `PanelSpacing` | Margin between elements |
| `ScrollWidth` | Standard panel content width (360px) |

`PanelChrome.Wrap(title, content, onClose)` builds the standard panel frame: title bar, close button, bordered container.

---

## Cross-panel interactions (navigation links)

The panels are connected by a set of "navigate to" links. These are all one-directional and consume-once (event set in the source panel, polled each frame by Game1, then cleared):

| Source | Trigger | Destination |
|--------|---------|-------------|
| Tile Inspector | `[Watch]` button on character | Character Watch panel |
| Event Log | Click actor name button | Character Profile panel |
| Event Log | Click civ name button `[CivName]` | Civ History panel (jumps to that civ) |
| Event Log | Click `->` button | Causal Chain dialog |
| Character Watch | `[Full Profile]` button | Character Profile panel |
| Character Watch | `[Enter Spotlight]` | Spotlight mode + camera follow |
| Character Profile | *(no outbound links)* | — |
| Civ History | *(no outbound links from rulers/events)* | — |

**Design gaps in navigation:**
- No back button / navigation history
- Artifact names in the inspector don't link anywhere
- Civ names in the inspector don't link to Civ History
- Settlement names in the event log don't link to the Tile Inspector for that tile
- Character names in the Civ History rulers list don't link to Character Profile

---

## Current state: what's functional vs. rough

| Surface | State |
|---------|-------|
| Map rendering + overlays | Functional, no legends |
| Event log + filter | Functional |
| Tile inspector | Functional but text-dense |
| Character watch + spotlight HUD | Functional |
| Character profile | Functional, read-only |
| Civ history | Functional, text-only |
| God Mode panel + 4 dialogs | Functional |
| Help panel | Functional |
| Timeline bar | Functional |
| First-run dialog | Functional |
| World gen screen | Functional, no preview |
| Causal chain visualization | Data exists; UI is list-only, no graph |
| Overlay legends | **Missing** |
| Map entity tooltips | **Missing** |
| Cross-panel navigation (full) | **Partial** — several links don't exist yet |

---

## Future touchpoints — M8: Created-Object Unification & Economic Depth

**M8 introduces a unified taxonomy for things characters create** — collapsing 4 divergent type systems (artworks, crafted goods, discoveries, artifacts) into one. This requires UI changes across several existing surfaces:

### Crafted object detail surface (new)
Currently, "ArtworkCreated", "ArtisanCrafted", "ScholarDiscovery" are events in the log with a type label. After M8, each produced object (weapon, tome, painting, discovery) has a unified record with:
- Name, creator, type, quality, year, civ context
- Whether it crossed the quality threshold to become a legendary Artifact
A **detail view for created objects** will be needed — similar in design to the artifact display but covering all creation types. The Event Log row for creation events should link to this detail view.

### Artifact/object panel (enhancement)
The current tile inspector lists artifacts in plain text. After M8, the designer should plan for:
- A dedicated Artifact/Object panel (possibly a sub-panel of the tile inspector or watch panel) showing the full lineage of an object: creator → owner chain → current state
- Thumbnail/icon per category (weapon, tome, relic, etc.)
- Click-through from artifact name → artifact detail

### Economic ledger panel (new)
M8 adds per-capita demand, goods flow, and trade networks. Players will want to see:
- Which settlements produce what, and in what quantities
- Trade routes between settlements (arrows on the territory overlay?)
- Settlement-level economic health: surplus / deficit per resource category
- This could be a new sidebar panel (toggled with a key, e.g. "$" or "Eco") or an expansion of the Tile Inspector's existing resource section

### Territory overlay enhancement
Trade routes and goods flow may need to be represented on the map. The designer should plan for visual overlays that show directional flows (settlement → settlement arrows) or a "Trade" overlay type in addition to the existing seven.

---

## Future touchpoints — M9: Worldgen Preview & Modding

### World generation screen (major redesign)
The current world gen screen is a progress bar with a layer name. M9 needs it to become an interactive preview environment:
- **Per-layer preview map:** as each generation layer completes, the map thumbnail updates to show that layer's output (elevation → grey heightmap, climate → temperature/moisture, biome → color, etc.)
- **Parameter controls:** sliders and inputs for seed, world size, sea level, rainfall, temperature band, etc.
- **"Rerun from layer" control:** after tweaking a parameter, player can re-run only the affected layers and later, rather than regenerating from scratch
- **Layer list with status:** shows each layer name, completed/pending/running state, and elapsed time
- **Commit button:** replaces the current "Start Simulation" — transitions to live sim with the configured world

**This is a significant design project.** The layered preview + adjustment flow is the core product promise of M9.

### Modding / config exposure panel (new)
M9 exposes `sim_config.toml` tunables through the UI. This needs:
- A settings/config panel (possibly accessible from a gear icon or main menu)
- Grouped categories of settings: Character behavior, Disaster rates, Civ dynamics, Artifact generation, etc.
- Safe presets (e.g. "High conflict", "Peaceful", "Dense population") that batch-set related settings
- Reset-to-default per setting or per group
- Potentially a diff view showing which settings differ from the default preset
- **Not:** code modding or plugin loading (out of scope)

---

## Future touchpoints — M10: Scale & Distribution

### Performance and long-run indicators (enhancements)
For 10k+ year runs, the UI needs to handle:
- Event log with potentially millions of historical events (current ring buffer is fine for recent events, but history query and profile panels may time out or be slow)
- Timeline bar at decade or century resolution instead of per-year pips when zoomed out to a 10k-year scale
- Progress / throughput indicators when loading a large save

### Local-scale generation (new overlay or view mode)
M10 activates zoomed-in local generation (the border manifest system already exists in the sim layer). This may require:
- A "zoom in to local area" mode that renders a higher-resolution sub-map of a selected region
- A separate generation step / progress indicator for local gen
- Integration with the world gen screen or a new local-gen overlay

### Distribution / packaging
No new in-game UI required by this milestone specifically, but may need:
- An update check indicator
- A "version" or "about" display
- A crash / error reporter surface

---

## Cross-cutting design requirements

The following requirements apply to all future design work and to any redesign of existing surfaces.

### 1. Legibility at all zoom levels
The map must remain readable from full-world (all tiles visible at ~2–4px each) to individual tile (20–30px per tile). Entity markers, settlement dots, and tile borders must scale appropriately. Overlay color schemes must be distinguishable for users with common color vision deficiencies (consider the elevation overlay's greyscale as a baseline).

### 2. Panel coexistence and overflow
Multiple panels can be open simultaneously. The sidebar currently stacks all open panels vertically; at typical window heights, only 1–2 panels can be fully visible at once. The designer should consider:
- Collapsible panel headers (only Event Log's filter has this today)
- Floating / detachable panels for secondary views (profile, civ history)
- A panel priority or slot system

### 3. The pause gate
God Mode authoring is only possible when the sim is paused. The current design shows a status label ("Pause to use God Mode") but the pause state is not globally communicated in the chrome — players must notice the time control buttons. Consider a **global pause indicator** (screen overlay edge, toolbar state, or panel dim) that makes the sim's run/pause state immediately legible anywhere in the UI.

### 4. Empty states
Three types of empty state must be designed for:
- **Pre-simulation:** world generating, no sim state yet
- **No data yet:** "summaries build every 50 in-game years" (Civ History panel)
- **Filter eliminated everything:** "(no events match filter)" (Event Log)
Each should have a distinct, non-alarming visual treatment.

### 5. First-run and onboarding
The first-run dialog exists but only covers the basics. For a worldbuilder audience who may have no prior simulation game experience, the onboarding journey should consider:
- Contextual tooltips on first use of major features (first time Event Log opens, first time a tile is clicked, etc.)
- A "tutorial world" or starter scenario
- Inline explanations of sim concepts (what is a "Headline" tier event? what is "Territory"?)

### 6. The no-avatar model
There is no persistent player avatar or HUD. The player is an observer, not an actor, 99% of the time. The UI must resist imposing a game-HUD aesthetic (health bars, inventories, quest trackers) and instead feel closer to a history atlas or reference application. Design decisions should reinforce the "reading history" metaphor.

### 7. God Mode and Spotlight should feel distinct
God Mode is about authoring world history — it should feel weighty, intentional, and rare. Spotlight is about inhabiting a character and steering their choices — it should feel immediate and focused. These are two very different modes of engagement and should have visually distinct and behaviorally distinct design treatments, even though they share the same underlying panel real estate today.

### 8. The time dimension
Everything in this game exists in historical time. The designer should think about how to make the **year/season** always legible, how to represent **historical depth** (events that happened 300 years ago feel different from events happening right now), and how to communicate **temporal scale** (a 500-year run feels different from a 5000-year run). The timeline bar is a start; it likely needs to become a first-class navigation surface.
