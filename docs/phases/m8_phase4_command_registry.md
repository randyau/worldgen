# M8 Phase 4 — Command Registry & Help

**Milestone:** M8 — UI Framework Rewrite
**Status:** NOT STARTED
**Depends on:** 8.2 (can run in parallel with most of 8.3; 8.3.6 Help depends on this)
**Worker model:** Sonnet
**Framework refs:** `docs/ui_design_framework.md` §9.1 (command registry), §9 (settings home)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §9.1;
> `WorldEngine.UI/UI/Input/KeybindRegistry.cs`; the keybind registration block in `Game1.cs`
> (~L546–564); `WorldEngine.UI/UI/HelpOverlayPanel.cs`. Nothing else.

## Goal

Put a named-action layer under keybinds so every user action has exactly one definition, keybinds
become **rebindable**, and Help regenerates from the same source (framework P6). Today
`KeybindRegistry` binds keys directly to inline lambdas in `Game1`; there's no action identity and
no rebinding.

## What exists now (grounding)

- `KeybindRegistry` (`UI/Input/KeybindRegistry.cs`): `Keybind(Key, Ctrl, Label, Category, Action,
  Trigger)`; `Process(kb, prev)` fires matching actions; `KeyLabel` renders accelerators. Single
  source of truth for keys — good, but the *action* is an anonymous delegate.
- `Game1` registers ~14 binds inline (overlays B/E/T/M/R/G, panels H/W/?/F2, world Space/N/Ctrl+S/
  Esc), each a lambda enqueuing a command or toggling a panel.
- `HelpOverlayPanel` renders from `KeybindRegistry.Bindings` + a static God-Mode/Spotlight text
  block.

## Stories

| # | Deliverable | Files |
|---|-------------|-------|
| 8.4.1 | `CommandRegistry` (named `UiCommand`s) | `UI/Input/CommandRegistry.cs` (new) |
| 8.4.2 | Keybinds bind to command ids; migrate `Game1` registrations | `UI/Input/KeybindRegistry.cs` (edit), `Game1.cs` (edit) |
| 8.4.3 | Rebuild Help from the registry | `UI/Panels/HelpPanel.cs` (new; delete `HelpOverlayPanel.cs`) |
| 8.4.4 | Keybind rebinding surface | part of Help panel or Settings shell (see 8.5) |

---

### 8.4.1 — `CommandRegistry`
```
readonly record struct UiCommand(
    string Id, string Label, string Category, Action Handler, Keys? DefaultKey = null,
    bool DefaultCtrl = false, KeybindTrigger Trigger = KeybindTrigger.Edge);

sealed class CommandRegistry {          // // MOD SEAM: a mod could register UiCommands
    UiCommand Register(UiCommand cmd);
    IReadOnlyList<UiCommand> Commands { get; }
    UiCommand? ById(string id);
    void Invoke(string id);
}
```
Every current action becomes a `UiCommand` with a stable `Id` (e.g. `overlay.biome`,
`panel.godmode`, `world.pause`, `world.save`, `world.newworld`, `select.clear`). The `Handler` is
the existing lambda body (enqueue command / toggle dock tab).

### 8.4.2 — Keybinds → command ids
`KeybindRegistry` binds a `Keys`(+Ctrl,Trigger) to a **command id**, not a raw delegate. On
`Process`, a matched key calls `CommandRegistry.Invoke(id)`. Load default bindings from each
`UiCommand.DefaultKey`; a user override map (persisted in 8.5's UI-prefs) can remap id→key without
touching behavior. Migrate all `Game1` inline registrations to `CommandRegistry.Register(...)` +
default binds. Keep the existing "key and any button share one path" guarantee — buttons now call
`CommandRegistry.Invoke(id)` too.

`// DECISION:` conflict policy on rebind (reject duplicate key, or last-wins) — pick one, note it.

### 8.4.3 — Help panel from the registry
New `HelpPanel : IWorkspacePanel` (summoned) rendering grouped, searchable rows from
`CommandRegistry.Commands` (group by `Category`), each showing `KeyLabel` for its current binding
(reflecting overrides). Add the God-Mode/Spotlight workflow cards as structured content (kit
components, not a raw text blob). Delete `HelpOverlayPanel.cs`. This completes 8.3.6's deferred
Help sub-part.

### 8.4.4 — Rebinding surface
A "Rebind" affordance per command row (capture next keypress → update the override map). The UI can
live in the Help panel or the Controls tab of the Settings shell (8.5) — put the *widget* in a
reusable `KeybindEditor` composite so both can host it. Persist overrides via the 8.5 UI-prefs
store (if 8.5 isn't merged, keep overrides in memory and note `// SEAM: persist in 8.5`).

## Verification
- Every action reachable by both its key and a button, through one `CommandRegistry.Invoke`.
- Help lists all commands with current keys, grouped and searchable; rebinding a key updates Help
  and takes effect immediately.
- `scripts/test-fast.sh` green; zero warnings.

## Handoff to 8.5
8.5's Controls tab hosts `KeybindEditor` and persists the override map in the UI-prefs store.
