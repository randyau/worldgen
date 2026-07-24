# M8 Phase 2 — Selection Unification

**Milestone:** M8 — UI Framework Rewrite
**Status:** COMPLETE — 2026-07-24. `SelectionBus` (implements `ISelectionSink`) replaces
`SelectionState`+`SelectionRouter`; all navigation consume-once pollers deleted (Tile Inspector
Watch, Event Log actor/civ/cause-chain, CharacterWatch spotlight/goal buttons) — each now fires
a direct callback at the click site instead of being polled by `Game1` each frame. Spotlight/goal
intents route through `CommandGateway` per the selection-vs-intent split (8.2.3). Focus lens
(8.2.4) is now actually wired — it existed but nothing ever called `FocusCharacter`/`FocusCiv`
before this phase; the `SelectionBus.Changed` handler now drives it on Character/Civ selection
and clears it on deselect. `git grep ConsumePending` shows only the unrelated, justified
`WorldGenScreen.ConsumePendingStart` (screen transition, not navigation). Build 0 warnings,
499/499 tests green.
**Depends on:** 8.1 (`SimWorkspace`, `PanelContext`, `ISelectionSink`)
**Worker model:** Sonnet (touches `Game1` control flow)
**Framework refs:** `docs/ui_design_framework.md` §7.1 (one selection bus), §7.2 (links), §7.4 (focus lens)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §7.1, §7.2, §7.4;
> `WorldEngine.UI/UI/Selection/SelectionState.cs`; `WorldEngine.UI/UI/FocusLensState.cs`; and the
> `ConsumePendingX()` call sites in `Game1.cs` (grep `Consume` — ~L339–390). Nothing else.

## Goal

Collapse the **two** parallel navigation mechanisms into one. Today `Game1` runs both the clean
`SelectionState`/`SelectionRouter` **and** a scattered set of consume-once pollers
(`_tileInspector.ConsumePendingWatch()`, `_eventLog.ConsumePendingCiv()`,
`ConsumePendingCauseChain()`, the spotlight intents, etc.), each set in a panel and polled/cleared
every frame in `Game1`. This is the framework's "one way to do each thing" (P6) and the biggest
source of navigation bugs. After this phase there is **one** `SelectionBus`; the dock reacts to it.

## What exists now (grounding)

- `SelectionState` (`UI/Selection/SelectionState.cs`): `SelectionKind {None,Tile,Settlement,
  Character,Civ}`, `Select*` methods, `Dirty` flag. `SelectionRouter.Apply()` dispatches to
  wired `OnTile/OnSettlement/OnCharacter/OnCiv/OnClear` callbacks. This is the good model — you
  promote it.
- `Game1` consume-once pollers (to delete): `_tileInspector.ConsumePendingWatch()` →
  `_panelManager.Show("watch")`; `_eventLog.ConsumePendingCharacterProfile/ConsumePendingCiv/
  ConsumePendingCauseChain`; `_charWatch.ConsumePending{EnterSpotlight,ExitSpotlight,MoveIntent,
  WanderGoal,SettleGoal}`. **Spotlight/move/goal ones are commands, not selections** — they move
  to `CommandGateway`, not the bus (see 8.2.3).
- `FocusLensState` (`UI/FocusLensState.cs`): the dim-not-hide soft filter.

## Stories

| # | Deliverable | Files |
|---|-------------|-------|
| 8.2.1 | Promote `SelectionState`/`Router` → `SelectionBus` implementing `ISelectionSink` | `UI/Selection/SelectionBus.cs`, `SelectionState.cs` (edit) |
| 8.2.2 | Route navigation through the bus; delete consume-once *navigation* pollers | `Game1.cs`, panel files (edit) |
| 8.2.3 | Move Spotlight/goal *intents* to `CommandGateway` (not selection) | `Game1.cs`, `CharacterWatchPanel.cs` (edit) |
| 8.2.4 | Focus-lens as a bus-driven service | `UI/FocusLensState.cs` (edit), `SimWorkspace` wiring |

---

### 8.2.1 — `SelectionBus`

Wrap `SelectionState` + `SelectionRouter` into one `SelectionBus : ISelectionSink` (the 8.0/8.1
seam interface). API:

```
sealed class SelectionBus : ISelectionSink {
    void Select(EntityRef target);          // (kind,id,coord) — the one entry point
    void Clear();
    SelectionSnapshot Current { get; }      // kind,id,coord — read by panels/focus lens
    event Action<SelectionSnapshot> Changed; // fired once per change (replaces per-frame Dirty poll)
    void Apply();                            // dispatch to SimWorkspace + focus lens each frame
}
```

Keep the deterministic-safety note from `SelectionState`: this is **UI-only** state; tile
inspection still round-trips the `SetInspectedTile` command (the snapshot must carry tile detail),
but "what is selected" needs no sim round-trip. Preserve that comment.

Done when: `SelectionBus` compiles; `EntityLink` (from 8.0) and `SimWorkspace.SetSelection` bind to
it (`Changed` → `SetSelection`).

### 8.2.2 — Route navigation through the bus; delete pollers

For every *navigation* consume-once poller, replace the panel-side `_pendingX` field + `Consume`
method with a direct `bus.Select(...)` call at the click site, and delete the `Game1` poll block:
- Tile Inspector `[Watch]` → `bus.Select(character)` (dock shows the Character contextual tab; no
  more explicit `Show("watch")`).
- Event Log actor name → `bus.Select(character)`; civ name → `bus.Select(civ)`.
- Event Log `->` cause chain → **not a selection**; it opens a modal. Keep it as a command/callback
  into `ModalHost` (route via `CommandGateway` or a dedicated `OpenCauseChain(eventId)` action —
  `// DECISION:` note which; a `CommandGateway` UI-action is cleaner and sets up 8.4).

Delete the corresponding `Consume*` methods and `_pending*` fields from the panels and the poll
blocks in `Game1`. Grep `Consume` afterwards — only intent handlers (8.2.3) should remain, and
they move next.

Done when: clicking any name anywhere selects via the bus and the dock routes to the right tab; no
navigation `Consume*` remains; `git grep -n "ConsumePending" WorldEngine.UI` shows only the
intents handled in 8.2.3.

### 8.2.3 — Spotlight/goal intents → `CommandGateway`

These are world/character intents, not "what am I looking at," so they must **not** flow through
the selection bus (framework §7.1). Replace the `_charWatch.ConsumePending{EnterSpotlight,
ExitSpotlight,MoveIntent,WanderGoal,SettleGoal}` pollers with direct `ctx.Commands.Enqueue(...)`
calls at the button click sites (the same sim commands they enqueue today — `SetSpotlight*`,
goal/move intents). Entering spotlight also selects the character (`bus.Select`) so the Character
tab follows — that part *is* a selection.

`// DECISION:` document the split explicitly at the top of `CharacterWatchPanel`: *selection =
what I'm looking at (bus); intent = change the world (CommandGateway).*

Done when: no `ConsumePending*` remains anywhere; spotlight enter/exit/move/goals still work via
commands; entering spotlight moves the dock to the Character tab.

### 8.2.4 — Focus-lens service

Drive `FocusLensState` from `SelectionBus.Changed` instead of ad-hoc setting. When an entity is
selected, broadcast it; the Event Log (and later the map) dims non-matching rows. One
implementation, opt-in per surface via `PanelContext`. Keep "dim, don't hide."

Done when: selecting a character dims unrelated event-log rows; clearing selection restores them.

## Verification

- One navigation path: `git grep -n "ConsumePending\|_pending" WorldEngine.UI` → empty (or only
  clearly-non-navigation leftovers you can justify).
- Clicking names routes correctly; entering spotlight follows to the Character tab; focus lens
  dims on selection.
- `scripts/test-fast.sh` green; zero warnings; determinism baseline unaffected (UI-only).

## Handoff to 8.3

Panels now receive `PanelContext` with a real `SelectionBus`. 8.3 rebuilds each panel's internals
against the kit; `EntityLink` already routes through the bus, so migrated panels get click-through
for free.
