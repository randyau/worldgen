# Phase 3.3 — Narrative UI

**Milestone:** 3 — Narrative Exploration  
**Status:** COMPLETE — 2026-06-26  
**Goal:** Build the UI that makes history readable: character profile cards, civilization history views, timeline scrubber, and the filter/focus lens system.

Depends on Phase 3.1 (HistoryQuery API) and Phase 3.2 (trait data and significance scores).

---

## Epic 3.3.1 — Character Profile Card

**Goal:** A player can click a character name (in the event log, on the map, or in a civ view) and see a structured profile card.

### What it Shows

```
┌─────────────────────────────────────────────────┐
│ Caelen III the Brave                            │
│ Ruler of Arlen's Domain (47th ruler)            │
│ Elf   Born Year 2140   Died Year 2210 (wounds)  │
├─────────────────────────────────────────────────┤
│ PERSONALITY                                     │
│  Aggressive ████████░░  Ambitious ███████░░░    │
│  Compassionate ██░░░░░░  Curious ████░░░░░░     │
├─────────────────────────────────────────────────┤
│ LIFE EVENTS                                     │
│  2140 — Born in Arlen's Domain                  │
│  2153 — Took throne after Caelen II died        │
│  2155 — Declared war on Pella's Domain (3rd war)│
│  2158 — Conquest of Pella's outer settlement    │
│  2162 — Created artwork: Epic "The Iron Years"  │
│  2178 — War exhaustion — longest peace yet      │
│  2210 — Died in battle (raider wounded)         │
├─────────────────────────────────────────────────┤
│ RELATIONSHIPS                                   │
│  Bonded: Ilyarel the Dreamer (physician)        │
│  Rival: Vael of Pella's Domain                  │
└─────────────────────────────────────────────────┘
```

Populated entirely from `IHistoryQuery`. No prose generation needed — structured data presented cleanly.

### Stories

**3.3.1.1 — CharacterProfilePanel (Myra)**
Read `CharacterSummary` and `GetCharacterHistory()` from `IHistoryQuery`. Render the card structure shown above. Wire to character name click in the event log panel and in future map overlays.

**3.3.1.2 — Relationship Display**
Show bond targets and rivals pulled from the character's event history (BondFormed events — need `BondFormed` event type added, or derive from GoalFormed(Bond) events). Show trust level if available.

---

## Epic 3.3.2 — Civilization History View

**Goal:** A panel showing the full arc of a civilization — its rulers, key events, wars, and current status.

### Stories

**3.3.2.1 — CivHistoryPanel**
Shows:
- Founding year, founder name, current status (alive / collapsed Year N)
- Cultural traits (icons + labels)
- Succession list: each ruler with years of reign and death cause
- Key wars: ordered by significance score, with outcomes
- Major events: Headline-tier events in chronological order

**3.3.2.2 — Civ Selector**
A dropdown in the UI that lists all civs (with alive/collapsed status). Selecting opens the CivHistoryPanel.

---

## Epic 3.3.3 — Timeline Scrubber

**Goal:** A horizontal timeline bar that shows the simulation's full history. Player can scrub to any year and see what was happening.

### Stories

**3.3.3.1 — TimelineBar (Myra)**
Full-width bar showing:
- Density heatmap of events over time (more events = darker/taller)
- Marked eras from the EraTag system
- Wars shown as colored spans per civ pair
- Scrub handle: drag to any year

**3.3.3.2 — Historical Snapshot Rendering**
When scrubbing to year N, render the world as it was at Year N:
- Settlement positions and sizes (from SettlementFounded/Abandoned events)
- Civ territory colors
- Active war spans shown on the map
Uses the event log to reconstruct world state at any year. Does NOT require storing full `WorldSnapshot` per year — derives from events.

---

## Epic 3.3.4 — Focus Lens System

**Goal:** Player can "focus" on a character, civ, or region and see only events relevant to that focus.

### Stories

**3.3.4.1 — Focus Target Selection**
Player selects a focus target by clicking on the map, the event log, or the civ selector. Three target types: Character, Civilization, Region (tile bounding box).

**3.3.4.2 — Filtered Event Log**
When a focus is active, the event log panel filters to events involving the focus target (using `EventEntities` FK table). Events involving the focus target are highlighted; other events are dimmed or hidden depending on player preference.

**3.3.4.3 — "What Led to This" Causal Chain View**
Player right-clicks an event → "Trace causes." Opens a modal showing the causal chain: the selected event, the events that caused it (from `CausalEdges`), and their causes, up to 3 levels deep. Example: "Conquest of Pella's outer settlement ← Battle (Year 2158) ← War Declared (Year 2155) ← Border tension accumulated since Year 2140."

---

## Epic 3.3.5 — LLM Prose Hook (V2)

**DEFER TO V2.** Do not implement in Milestone 3.

When the `IHistoryQuery` API is stable and the profile cards work, the hook for LLM prose generation is straightforward: pass the structured `CharacterSummary` + `GetCharacterHistory()` result to an LLM prompt and ask for a narrative paragraph. The hook should be clearly stubbed with a `// V2: LLM_PROSE_HOOK` comment at the right places so it's easy to wire in.

Add the stub to `CharacterProfilePanel` — a "Generate Narrative" button that is disabled in M3, with the hook comment showing where to call the LLM.

---

## God Mode (Milestone 4)

Per the MVP spec, God Mode authoring tools are Milestone 4. Do not implement in M3. Leave `// M4: GOD_MODE` stubs at any natural hook points (event injection, character stat editing, war declaration override).

---

## Definition of Done

- Character profile card opens on click for any character in the event log
- Civ history panel accessible from a UI selector; shows succession chain correctly
- Timeline scrubber renders event density and can scrub to any year
- Historical snapshot rendering shows settlement positions at the scrubbed year
- Focus lens filters the event log to the selected target
- Causal chain modal shows 3-level deep cause graph for any event
- All displayed names include ordinal suffix where NameOrdinal > 0
- All tests pass
