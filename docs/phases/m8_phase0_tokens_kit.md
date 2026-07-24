# M8 Phase 0 — Design Tokens & Component Kit

**Milestone:** M8 — UI Framework Rewrite
**Status:** COMPLETE — 2026-07-23
**Depends on:** nothing (first phase)
**Worker model:** Sonnet (foundation — every later phase builds on this)
**Roadmap:** `docs/roadmap.md` § M8, phase 8.0
**Framework refs:** `docs/ui_design_framework.md` §2 (tokens), §4 (components), §8.1 (presenter)

> Read first (only these): this doc; `m8_ui_framework_rewrite.md`; framework §2, §4, §8.1;
> `WorldEngine.UI/UI/Theme/UiTheme.cs`; `WorldEngine.UI/UI/Theme/PanelChrome.cs`;
> `WorldEngine.UI/UI/TileInspectorPanel.cs` (as the reference for the `AddLine` idiom and the
> temp-conversion math you'll move into the Presenter). Nothing else.

## Goal

Build the bottom of the stack: the full token set, the Layer-1 widget wrappers, the highest-value
Layer-2 composites, and the Presenter service. **No panel is rebuilt in this phase and there is no
visible change** — this is pure substrate plus new architecture tests. Later phases fail fast if
this is wrong, so correctness here matters more than breadth.

## What exists now (grounding — don't re-explore)

- `UiTheme` (`UI/Theme/UiTheme.cs`): colors + a few metrics (`SidebarWidth 360`, `PanelWidth 330`,
  `ScrollWidth 340`, `PanelSpacing 4`, `PanelPad 6`, `TopBarClearance 44`), `TierColor`,
  `CivColor`. Thin. You expand it.
- `PanelChrome.Wrap(title, body, onClose)` (`UI/Theme/PanelChrome.cs`): builds a titled bordered
  panel. Superseded by `PanelFrame` (this phase) but **not deleted yet** — old panels still call
  it until 8.3.
- Panels build content by appending `new Label { Text = … }` per line (`AddLine`), with inline
  formatting (e.g. `TempC/TempF/TempDeltaC` in `TileInspectorPanel`, wellbeing/health/stores
  label thresholds). All of that formatting moves into the Presenter here.

## Stories

| # | Deliverable | Files | Worker |
|---|-------------|-------|--------|
| 8.0.1 | Expand `UiTheme` to the full token set | `UI/Theme/UiTheme.cs` | Sonnet |
| 8.0.2 | Layer-1 widget kit (`We*`) | `UI/Kit/*.cs` (new) | Sonnet |
| 8.0.3 | Layer-2 core composites | `UI/Kit/*.cs` (new) | Sonnet |
| 8.0.4 | `Presenter` service | `UI/Present/Presenter.cs` (new) | Sonnet |
| 8.0.5 | Architecture tests for Kit isolation | `WorldEngine.Tests/Architecture/ArchitectureRuleTests.cs` | Sonnet |

---

### 8.0.1 — Token set

Extend `UiTheme` (keep existing members; add the following). Group with region comments matching
the file's current style.

- **Color roles** (framework §2.1): `TextPrimary, TextSecondary, TextMuted, TextDisabled,
  TextHeader, AccentInteractive, AccentGodMode (gold/amber), AccentSpotlight (cyan),
  StatePositive, StateWarning, StateNegative, SurfacePanel, SurfaceRaised, SurfaceModalScrim,
  BorderPanel, BorderFocus`. Map existing names to roles (e.g. keep `HeaderText` but add
  `TextHeader` as the canonical; you may alias). `AccentGodMode`/`AccentSpotlight` are new.
- **Typography roles** — an enum `TypographyRole { Display, Title, SectionHeader, Body,
  BodyStrong, Caption, Mono }` and a `FontFor(TypographyRole)` returning the Myra font (fonts are
  currently default; if only one font exists, map size/weight via Myra scale — `// DECISION:`
  note the mapping). Keep this in `UI/Kit/` if it needs Myra types; keep the *enum* in Theme.
- **Spacing ramp** — `static class Space { Xs=2, Sm=4, Md=8, Lg=12, Xl=16 }`. Keep `PanelSpacing`
  as an alias of `Space.Sm` during migration.
- **Z-bands** — `enum ZBand { Base=0, Chrome=100, Float=200, Transient=300, Modal=400 }`
  (consumed by the layout host in 8.1; define here so it's a token).
- **Scroll reserve** — `const int ScrollReserve = 16;` (px reserved for the scrollbar so content
  never hides behind it — framework §3.2).

Done when: `UiTheme` exposes the full set; existing references still compile; no behavior change.

### 8.0.2 — Layer-1 widget kit (`UI/Kit/`)

Thin wrappers over Myra. **This folder is the only place `using Myra…` is allowed above nothing**
(enforced in 8.0.5). Each wrapper exposes tokenized props only — a caller cannot pass a raw color.

Minimum set (framework §4.1):
- `WeText` — ctor `(string text, TypographyRole role = Body, ColorRole color = TextPrimary)`;
  `Text` settable. Wraps `Label`.
- `WeButton` — `(string text, Action onClick, WeButtonVariant variant = Primary)` where variant ∈
  `{ Primary, Ghost, Link, Toggle, Danger }`; `Active` bool for Toggle. Wraps `TextButton`.
- `WeStack` — `WeVStack`/`WeHStack` with `Space` spacing; `Add(Widget)`; `Clear()`.
- `WeScroll` — wraps `ScrollViewer`; **content width = available − `UiTheme.ScrollReserve`**;
  clamps to assigned height (never grows past it). This single class is where the scrollbar-
  obstruction bug is fixed once.
- `WeField` — labeled text input (`Label` + `TextBox`); `Value`, `Placeholder`,
  `ValidationState`.
- `WeDropdown<T>` — typed combo (`ComboBox`); `Items`, `Selected`, `OnChanged`, `Render(Func<T,string>)`.
- `WeList<T>` — vertical list with `SetItems(IEnumerable<T>, Func<T,Widget>)`. A simple non-virtual
  implementation is fine here; note `// PERF: virtualize in 8.3.4 for M11 scale`.
- `WeIcon` — icon glyph + mandatory `tooltip`/`label` (FontAwesome free is pinned in deps).

Expose only `Widget Root { get; }` (or inherit a common `WeWidget` base) so composites can nest
them. Keep them dumb — no data/sim knowledge.

Done when: kit compiles; a throwaway `[Fact]` can instantiate each; nothing else references them yet.

### 8.0.3 — Layer-2 composites (`UI/Kit/`)

Build these now (the rest come with the panels that need them in 8.3):
- `PanelFrame(string title, Widget body, PanelFrameOptions? opts)` — the replacement for
  `PanelChrome.Wrap`. Titled (`Title` role), bordered (`BorderPanel`), padded (`Space`),
  `SurfacePanel` background; optional close/pin affordances via `opts`. **Does not set Width** —
  the layout host sizes it (framework §3.2). This is the key difference from `PanelChrome`, which
  hardcoded `Width = PanelWidth`.
- `SectionHeader(string text)` — replaces `AddLine("--- X ---")`.
- `StatRow(string label, string value, ColorRole valueColor = TextPrimary, string? unit = null)`
  — aligned label↔value.
- `KeyValueGrid` — `Add(label, value, …)`; column-aligned set of `StatRow`s.
- `Meter(string label, float value01, int segments = 10, ColorRole? state = null)` — labeled bar +
  numeric readout (serves the watch panel's needs/traits/health).
- `EntityLink(EntityRef target, string text, SelectionBus bus)` — clickable; on click calls
  `bus.Select(target)`. `EntityRef` is a small record `(SelectionKind kind, long id, TileCoord
  coord)` — define it in `UI/Selection/`. (`SelectionBus` itself is promoted in 8.2; for now
  depend on the existing `SelectionState.Select*` methods via a thin `ISelectionSink` interface so
  this composite doesn't block on 8.2. `// SEAM: ISelectionSink → SelectionBus in 8.2`.)
- `EmptyState(EmptyStateKind kind, string message, string? hint = null)` where kind ∈
  `{ PreSim, NotBuiltYet, FilteredEmpty }` (framework §7.5).
- `Tooltip` — attach-to-widget hover tooltip; delay + cursor-follow + **viewport clamp** (fixes
  the timeline tooltip overflow). May need a per-frame position update hook from the layout host;
  if so, expose `Tooltip.Update(mousePos, viewport)` and note `// SEAM: driven by LayoutHost 8.1`.

Done when: composites compile and are unit-instantiable; `PanelFrame` visibly matches the old
chrome when handed the same title/body (compare in a scratch harness or just by inspection).

### 8.0.4 — Presenter (`UI/Present/Presenter.cs`)

The single formatting authority (framework §7-P7, §8.1). Pure functions, no Myra, no sim
reference beyond the enums/records it formats. Move here (delete from panels in 8.3):

- **Units:** `TempC(byte raw)`, `TempF(byte raw)`, `TempDeltaC(sbyte/float)` — currently in
  `TileInspectorPanel`. `Elevation(byte raw)` → relative label + optional metric. `Moisture`,
  `Fertility`, `MagicIntensity` → human strings.
- **Qualitative labels:** `Health(int) → Good/Struggling/Critical`; `Wellbeing(float) →
  Flourishing…Spiraling`; `Store(resource, amount) → well-stocked/adequate/bare|abundant/…`.
  Pull the *thresholds* from config where a sim constant already exists; otherwise centralize the
  literal here with a `// DECISION:` note (these are display bands, not sim behavior).
- **Enums → prose:** `EventType → verb phrase` ("declared war"); `GoalType → intent phrase`;
  `DisasterType → name`; `ArtifactCategory`, `SelectionKind` labels. Use pattern matching.
- **Names:** `CharacterName(snapshot entry)` with ordinal+epithet; `CivLabel(civ)` with
  active/collapsed annotation; `YearSeason(year, season)`.

Keep it a plain `static class Presenter` (or instance passed via `PanelContext` — instance is
better for a future localization seam; `// MOD SEAM: localizable via Presenter`). Prefer instance.

Done when: Presenter covers every formatting currently inlined in the existing panels (grep the
old panels for `:F`, `switch`, `°C`, `--- `), unit-tested for a representative case each.

### 8.0.5 — Architecture tests

Add to `ArchitectureRuleTests.cs`:
- `NoMyraOutsideKit` — scan `WorldEngine.UI/UI/**/*.cs`; fail if a file outside `UI/Kit/` contains
  `using Myra`. (Old panels still use Myra now, so **scope the test to `UI/Kit/` + `UI/Present/`
  + `UI/Layout/` being *clean of business logic*** — actually invert: assert Present/ has no Myra;
  the full panel ban lands in 8.3 when `UI/Panels/` exists. Add a `// enforced-fully-in-8.3`
  comment.) Implement the strict version but restrict its file set to folders that exist now.
- `PresenterHasNoMyra` — `UI/Present/` has no `using Myra`, no XNA `Color`.

Done when: new tests pass; the 6 existing arch tests still pass; `scripts/test-fast.sh` green.

## Verification (whole phase)

- `scripts/test-fast.sh` green; zero warnings.
- No visible/behavioral change when running the app (kit/presenter unused by panels yet).
- `git grep -n "using Myra" WorldEngine.UI/UI/Present` → empty.

## Handoff to 8.1

8.1 consumes: `ZBand`, `ScrollReserve`, `WeScroll`, `PanelFrame`, `Tooltip`, `PanelContext`
shape. Leave `PanelChrome` and old panels untouched. Note in the commit which composites are
still pending (they arrive with their panel in 8.3).
