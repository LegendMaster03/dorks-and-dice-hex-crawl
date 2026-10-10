# Phase 18 — traversal integration status

**Status:** In progress. Not complete, not authorized for merge or deployment.

**Baseline:** main at c3c16e9e4ab09c2006162fc69079aef0ca2aa86e, PostgreSQL schema 10, procedure schema 1.3.

## Geometry increment

The first Phase 18 increment introduces PeriodicCellTraversal and PeriodicCellTraversalGeometry in the domain spatial layer. It consumes only the existing Phase 17 PeriodicWorldTiling authority: qualified WorldCellId, atomic WorldCellBoundary interfaces, reciprocal adjacency, world-coordinate polygon geometry and optional physical-distance calibration. There is no shape-specific dispatch, additional world authority, Surveyor change, or new database migration.

The cursor records the authoritative cell identity, world position, optional reciprocal entry and previous cell, normalized-by-use course vector, and explicitly selected exit interface. Its format version is 1. The geometric helper determines the earliest valid boundary crossing on a straight course. A coincident/collinear boundary, vertex tie, invalid selected interface, outward departure through an unselected boundary, or unconfirmed destination requires explicit adjudication. Reciprocal transitions use the target's atomic entry interface, not a polygon side index or a six-way direction.

Domain tests cover several known and mixed examples, geometric scale, selected interfaces, exact-boundary double-back, and disallowed implicit physical-distance inference. CI results and further robustness tests must be recorded before claiming the geometry increment validated.

## Still required before Phase 18 can be marked complete

1. Unify the current HexTraversalState and the new cell-based traversal contract without storing two independently authoritative positions or inventing axial coordinates for nonhex cells. Preserve all existing hex snapshots, progress, active watches and event histories.
2. Update the materialized procedure contract and native movement binding for generic continuous-distance and cell-step semantics, retaining exact legacy hex factors and 60-degree veer rules for pinned hex procedures.
3. Wire the geometric resolver through CrawlRuntimeEngine AdvanceCore, navigation/lost/double-back handling, event collection, encounters and watch pause/continuation without duplicating the gameplay runtime.
4. Supply immutable PeriodicWorldTiling context through the application boundary. Remove hex-only service admission for explicitly gated generalized API operations only after generalized state, environment composition and persistence work.
5. Introduce additive/versioned expedition snapshot and API contracts for cell identity, entry interfaces, actual and intended course, adjudication and generalized events. Preserve old endpoint payloads and historical event consumers. Avoid database migration if compatible JSON is sufficient.
6. Validate procedure authority, physical calibration and no-path adjudication, save/restart determinism, retries, stale writes, owner isolation and nonspatial / abstract-hex regressions with actual PostgreSQL.
7. Run the full frontend, .NET, PostgreSQL, HTTP and container test matrix, then conduct the required signed-in development deployment checks after separately authorized merge and deployment. Phase 19 presentation remains gated.

**Compatibility:** Existing hex and nonspatial runtime paths have not been switched to this incomplete generalized geometry component. This is intentional: adding geometric crossing by itself does not make a persisted generalized expedition executable or resume-safe.

**Release decision:** Incomplete. Neither the source branch nor this document authorizes changing development/tester data or merging.
