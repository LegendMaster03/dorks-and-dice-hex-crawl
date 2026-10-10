# Phase 18 — traversal integration and release gates

**Status:** Partial implementation; not complete, merge-ready, or authorized for deployment.

**Baseline:** main at c3c16e9e4ab09c2006162fc69079aef0ca2aa86e; PostgreSQL schema 10 and campaign procedure schema 1.3. Only the Hex Crawl repository has changed.

## Implemented foundations

### One authoritative geometric crossing resolver

- PeriodicCellTraversal identifies the exact world-qualified cell, current world-coordinate position, optional entry interface and prior cell, chosen heading, and optional selected exit interface. Cursor format version 1 is validated on read.
- PeriodicCellTraversalGeometry crosses only an atomic WorldCellBoundary from PeriodicWorldTiling and verifies its reciprocal. Subdivided polygon sides are not treated as whole side indexes, and heading is not an interface ordinal.
- A vertex tie, outward departure through an unselected side, selected inward/tangential departure, ambiguous coincident course, or unreliable boundary resolution requires adjudication; no arbitrary neighbor selection.
- PeriodicTraversalExecution handles physical-distance budgets and discrete cell-step budgets through this same geometry kernel. One cell step is one successful atomic boundary crossing. The actually traveled route length is measured; no equal-distance assumption or cell counting is used for physical travel. Physical movement requires the tiling's explicit calibration; uncalibrated cell-step movement has no fabricated physical distance.
- Travel can stop at a boundary, resume, traverse several cells, or pause after one crossing for a review. Bounded crossing enumeration prevents unbounded zero-progress loops.

### Materialized procedure semantics

- Legacy movement handler version 1 and HexProgressPolicy remain intact for saved hex expeditions and abstract-hex sessions; original six-direction veering and partial-progress factors have not been reinterpreted.
- Movement handler version 2 explicitly separates ContinuousDistance from CellSteps and prohibits legacy hex-progress factors. It binds through GenericProcedureRuntime.Bind from the pinned CampaignProcedure, not a preset key.
- CrawlRuntimeEngine.ResolveCellTravel invokes the same domain geometry executor and requires a version-2 procedure whose canonical D-symbol matches the world's accepted quotient. This is a geometry/movement segment entry point, **not** a full generalized watch advance.
- Existing CrawlRuntimeEngine.Advance remains the legacy watch path and explicitly rejects the new movement version until watch orchestration is generalized. This preserves old behavior while preventing accidental partial rollout.

### Runtime state and persistence

- CellExpeditionState is a distinct non-axial runtime state; it never stores fabricated Q/R coordinates. Its only authoritative position and actual course are in PeriodicCellTraversal. CellActiveWatchState retains intended course, elapsed/remaining watch time, encounter schedule and pending decision without duplicating the authoritative position.
- Uncalibrated physical travel totals are represented as unknown (null), rather than an arbitrary physical scale.
- RuntimeStateSnapshot gains the additive CellSpatial discriminator and cell-state fields. Existing Spatial and NonSpatial read/write representations are retained. New optional fields are omitted when null to avoid rewriting older legacy JSON structure.
- Pending encounters and runtime events can carry a qualified WorldCellId, and generalized movement events can carry reciprocal atomic interface identities. Historical hex event values and fields are unchanged.
- EnvironmentContextResolver uses the authoritative current cell for feature intersections and cell-scoped annotations, while preserving legacy hex-scoped annotations for hex worlds. No semantic environment state is inferred from D-symbol identity.
- No relational schema change or live tester-data migration is introduced. The current backend remains gated against creating ordinary generalized expeditions, because watch mechanics and user interfaces are not complete.

## Tests added

1. Geometry: triangular, square, hexagonal, mixed motifs, reciprocal atomic interfaces, vertex adjudication, selected-boundary correctness, scale/origin precision, exact-boundary double-back.
2. Execution: multi-cell continuous distance, cell-step budgets, physical calibration, no-calibration rejection for physical movement, pause/review and restart of the geometric cursor, ambiguity without phantom transition.
3. Runtime policy: pinned version-2 continuous-distance and cell-step execution for multiple tilings, rejection of legacy hex-step reinterpretation and mismatched D-symbols, refusal of v2 hex-progress semantics.
4. PostgreSQL: schema-10 generalized runtime serialization, mid-cell save/restart/cross/save, qualified event history, ownership isolation, optimistic concurrency, uncalibrated pending encounter and active-watch restart, duplicate-event protection.
5. Application: generalized cell and feature environment facts, and coexistence of legacy hex and qualified-cell annotation scopes.

CI, integration and release tests must still be evaluated on the exact final branch head before updating the verified status.

## Mandatory work remaining

1. Refactor CrawlRuntimeEngine.AdvanceCore and watch lifecycle so its existing navigation, encounters, lost/veer/reorientation, deliberate double-back, environmental effects, survival resources, journey hooks, pause and event replay all consume a generalized spatial execution result. **Do not fork watch/gameplay mechanics into a second engine.**
2. Define fully generalized navigation/adjudication intent, heading-deviation and selected-interface contracts, including explicit decisions at vertices, unknown paths and navigation failure. Preserve v1 60-degree veer for existing hex sessions.
3. Connect CellExpeditionState and periodic world authority through application services, ownership checks, progression/pause workflows, discovery and journey integration. Materialized procedure version-2 authoring must be available without preset-specific dispatch.
4. Provide additive/versioned generalized API contracts for course selection, adjacency, current cell, events and resume. Preserve hex API payloads unchanged. Ordinary nonhex expedition entry points must remain disabled until Phase 19 presentation and full mechanics are ready.
5. Validate representative existing schema-10 saved tester-like expeditions, active/paused watches, navigation and pending encounters, pinned revisions, retry/duplicate delivery, effects and discoveries, concurrent writes, and abstract/nonspatial parity. Perform a full independent self-review and applicable Docker/PostgreSQL/frontend tests.
6. After explicitly authorized merge/deploy, independently verify authenticated development/testing workflows against existing saved tester data, maps and source assets. Do not reset or migrate the live database from the feature branch.

## Compatibility and release assessment

Legacy gameplay execution has not been replaced by the incomplete generalized watch integration. This is deliberate; accepting nonhex user-facing expeditions now would create sessions unable to advance through the established mechanics.

**Merge decision: blocked by incomplete required Phase 18 integration.** CI success for incremental foundations does not authorize merge, deploy, or a declaration that Phase 18 is done.
