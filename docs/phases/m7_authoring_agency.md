# M7 — Authoring & Agency: Spotlight + God Mode

**Status:** COMPLETE — 2026-07-23
**Milestone:** M7
**Theme:** The core product promise: watch, author, inhabit.

---

## Epics and stories shipped

### Epic 7.1 — God Mode foundation (commit 2fc1301)

- **7.1.1** `AuthoringCommands.cs`: four sealed ICommand records — `AuthorPlaceArtifact`, `AuthorTriggerDisaster`, `AuthorSpawnCharacter`, `AuthorNudgeCharacter` — plus `CharacterNudge` enum. Added `GodModeCharacterNudged = 9006` to EventType.
- **7.1.2** `AuthoringResolver.cs`: static resolver wired into `SimLoop.ApplyCommand`; each handler mutates WorldState and injects a GodMode-range PendingEvent via `PhaseRunner.InjectPendingEvent`. `PhaseRunner.RunEventGeneration` stamps `IsGodMode = true` for event types ≥ 9000.
- **7.1.3** `AuthoringValidator.cs`: `ValidateCoord`, `ValidateLandTile`, `ValidateCharacterAlive`, `ValidateDisasterApplicable`; invalid commands log to stderr and silently no-op.

### Epic 7.2 — God Mode UI (commit 51b9057)

- **7.2.1** `GodModePanel.cs`: sidebar panel (IPanel) registered as "godmode", toggled with F2. Pause-gated (warning if sim running). `SetContext()` called each frame from Game1 with inspected tile + watched character.
- **7.2.2** Artifact & disaster authoring: modal Window dialogs with ComboBox dropdowns for ArtifactCategory and DisasterType; enqueues `AuthorPlaceArtifact` / `AuthorTriggerDisaster`.
- **7.2.3** Character authoring: spawn dialog (AncestryId TextBox); nudge dialog (CharacterNudge ComboBox); enqueues `AuthorSpawnCharacter` / `AuthorNudgeCharacter`.
- **7.2.4** Provenance display: `[G]` gold badge on `IsGodMode` events in EventLogPanel; god-mode ShortEventDesc entries; `HideGodMode` checkbox in FilterPanel / `PassesGodMode()` in EventLogFilter.

### Epic 7.3 — Spotlight foundation (commit 51b9057)

- **7.3.1** `SpotlightCommands.cs`: `EnterSpotlight`, `ExitSpotlight`, `SetSpotlightMoveIntent`, `SetSpotlightGoalIntent`, `SetSpotlightSocialIntent`. `SpotlightIntent.cs`: mutable session intent state with `Clear()`. `WorldState` + `IWorldStateReadOnly` + `WorldSnapshot` + `SnapshotBuilder` extended with spotlight fields. `SimLoop.ApplyCommand` handles all 5 commands; `EnterSpotlight` also sets `WatchedCharacterId`.
- **7.3.2** `UtilityScorer`: `SpotlightIntentBias = 3.0f` post-pass in `SelectAction`; biases `MoveToTile`/`FleeRegion` toward `SpotlightMoveTarget` and goal-matched actions toward `SpotlightGoalIntent`. **DECISION: bias-not-override** — multiplier preserves autonomous survival responses.
- **7.3.3** `CharacterBehaviorPhase`: clears `SpotlightCharacterId`/`SpotlightIntent` on death of the spotlighted character; no residual state after exit.

### Epic 7.4 — Spotlight UI (commit 51b9057)

- **7.4.1** `CharacterWatchPanel` promoted to interactive Spotlight HUD: `Refresh` accepts `spotlightId` + `inspectedTile`; shows "SPOTLIGHT ACTIVE" when controlled.
- **7.4.2** Intent issuance: Enter/Exit Spotlight, Move Here, Goal:Wander, Goal:Settle buttons with consume-once pattern. Map left-click in spotlight mode also enqueues `SetSpotlightMoveIntent`.
- **7.4.3** `Camera2D.CenterOn(TileCoord, viewportW, viewportH)`: camera follows spotlighted character's location each frame from `snapshot.WatchedCharacter.Location`.

---

## Architectural decisions

- **DECISION: bias-not-override policy** — spotlight intent multiplies matching actions' utility by `SpotlightIntentBias` (3.0) rather than forcing a hard override. Character still responds autonomously to survival priorities.
- **DECISION**: `EnterSpotlight` implicitly sets `WatchedCharacterId` so the watch panel tracks the spotlighted character without requiring a separate `WatchCharacter` command.
- **DECISION**: spotlight state (`SpotlightCharacterId`, `SpotlightIntent`) lives on `WorldState` (sim thread only); `WorldSnapshot` carries only the read-only projections UI needs (`SpotlightCharacterId`, `SpotlightMoveTarget`).
- **DECISION**: God Mode authoring commands reject invalid inputs via `AuthoringValidator`, log to stderr, and silently no-op rather than throwing — sim thread never panics on a bad authored command.
- **DECISION**: `ComboBox`/`ListItem` shims added to `MyraCompat.cs` because Myra 1.6.3 uses `ComboView`/`Button` internally.
