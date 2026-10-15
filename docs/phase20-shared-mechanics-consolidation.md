# Phase 20: shared mechanics ownership

## Non-negotiable boundary

Runtime implementations do **not** dispatch on preset key, published game, or hex/square tiling. Geometry stays in the shared spatial engine; behavior comes from an exact pinned, validated procedure. Do not add an engine just because a different preset needs an existing mechanic.

## Authority inventory

| Responsibility | Owner | Exploration's integration |
| --- | --- | --- |
| Tiling, cell identity, and traversal | Periodic tiling and expedition traversal state | Records reference authoritative `WorldCellId`; never derive a second geometry |
| Watch clock and movement | Expedition runtime and watch integration | The configured stage reacts to completed authoritative watches |
| Random dice and procedural resolution | `ProcedureResolutionResolver`, `IProcedureResolutionRandomSource` | GM-authored range tables use the same dice engine as hex travel, navigation, and encounters |
| Random-table domain rules | `ProcedureRangeTable` in `Domain.Procedure` | Validated independently of ruleset or geometry |
| Roll audit | `ProcedureResolutionRoll` | Range-table results carry this existing audit, not a parallel record format |
| Stage progression | `JourneyProcessEngine` | Stages and selected actions use ordinary journey process resolution |
| Encounters | `JourneyEventEngine` | Action obligations create existing generic event occurrences |
| Time/resource/recovery consequences | Generic expedition consequence transition | Explicitly resolved values apply atomically to the expedition |
| World geography | Overworld locations and spatial feature services | GM session notes are not a replacement world geography model |
| Player revelation | `KnowledgeDiscovery` and player knowledge projection | GM-only table results/notes do not automatically grant discoveries |

## Corrected duplication

The exploratory `ExplorationTableRoller` was deleted. `ProcedureResolutionResolver.ResolveRangeTables` rolls user-provided tables with the existing RNG, and returns the existing procedure roll audit. `ExplorationTableRollRequest` only maps a generic table to a location or region result destination.

The premature expedition-local `ExplorationSavedTable` library was deleted, including the duplicate editor, mutation receipts, and endpoint. Reusable procedure data should live in the existing versioned procedure-authoring system and be snapshotted into the expedition, not rewritten in journey execution history. A versioned cross-procedure table catalog is **not yet implemented**. Explicit one-off GM-supplied tables remain supported without assuming publisher-owned table contents.

Expedition-local visited cells and region notes are history/DM working state, especially for mapless sessions with no saved world. They must not become a second authoritative world, geographic feature system, or player knowledge system.

## Regression contract

Both hex and square presets must use the same procedure-range-table resolver. Keep the hex travel/navigation/encounter suites and all generic expedition persistence/restart suites green. No legacy fallback, destructive database migration, or ruleset-name branching is allowed.
