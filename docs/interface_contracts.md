# World Engine — Interface Contracts (Index)
**Version:** 0.9 | **Status:** M3 complete (M4 Phase 1 emissary system included)

**Rule:** Do not add methods to these interfaces without updating the relevant split file first. Interface changes are breaking changes.

This file is now an index. Load only the section you need:

| File | Contents |
|------|----------|
| [`interface_contracts_tiles.md`](interface_contracts_tiles.md) | TileData, TileStaticFlags, TileDynFlags, ChunkSummaryFlags, SeasonalProfile, ResourceDeposit, DisasterType, ActiveDisaster, ActiveDrought |
| [`interface_contracts_core.md`](interface_contracts_core.md) | PendingEvent, IEntity, ICommand, IWorldStateReadOnly, IWorldGenLayer, StateCache |
| [`interface_contracts_snapshot.md`](interface_contracts_snapshot.md) | TileDisplayData, EntitySnapshot, IdentityData, AncestryConfig, AncestryRegistry, TileInspectorData, WorldSnapshot, SettlementStub, SettlementSnapshot, RuinRecord, TerritorySnapshot, ImprovementSnapshot, CharacterWatchSnapshot, Civilization, ID wrappers |
| [`interface_contracts_events.md`](interface_contracts_events.md) | EventEntities table, EventType ranges, SimEvent, IHistoryGraphReadOnly, IHistoryQuery, Key Enumerations, WarOutcome/WarCause constants, ID wrappers |

**Quick lookup:**
- Working on tile rendering or world gen? → `_tiles.md`
- Implementing IEntity, ICommand, or reading world state? → `_core.md`
- Working on UI, snapshots, settlements, civs? → `_snapshot.md`
- Adding event types, querying history? → `_events.md`
