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
- CrawlRuntimeEngine.ResolveCellTravel invokes the same domain geometry executor and requires a version-2 procedure whose canonical D-symbol matches the world's accepted quotient.
- CrawlRuntimeEngine.AdvanceCellWatch now integrates version-2 cell travel with the pinned procedure's interval duration and encounter cadence, qualified event/history sequencing, angular navigation outcomes, resolved veer, lost recognition, double-back boundary stopping, encounter occurrence/resolve, boundary review, deterministic watch completion, and physical-distance accounting. Ambiguous courses pause rather than fabricating a neighbor; ResolveCellCourse validates an explicit atomic exit and the actual outgoing ray before saving the decision.
- The generalized entry points are currently a **typed watch adapter inside the same partial CrawlRuntimeEngine**, not yet an extracted common AdvanceCore. Shared encounter scheduling, a single encounter-time window slicer for both runtime paths, time helpers, event collector, and geometry kernel are reused. A follow-up consolidation must remove the remaining duplicated orchestration before release; legacy CrawlRuntimeEngine.Advance still explicitly rejects version-2 movement.

### Runtime state and persistence

- CellExpeditionState is a distinct non-axial runtime state; it never stores fabricated Q/R coordinates. Its only authoritative position and actual course are in PeriodicCellTraversal. CellActiveWatchState retains intended course, elapsed/remaining watch time, encounter schedule and pending decision without duplicating the authoritative position.
- Uncalibrated physical travel totals are represented as unknown (null), rather than an arbitrary physical scale.
- RuntimeStateSnapshot gains the additive CellSpatial discriminator and cell-state fields. Existing Spatial and NonSpatial read/write representations are retained. New optional fields are omitted when null to avoid rewriting older legacy JSON structure.
- Pending encounters and runtime events can carry a qualified WorldCellId, and generalized movement events can carry reciprocal atomic interface identities. Historical hex event values and fields are unchanged.
- EnvironmentContextResolver uses the authoritative current cell for feature intersections and cell-scoped annotations, while preserving legacy hex-scoped annotations for hex worlds. No semantic environment state is inferred from D-symbol identity.
- PlayerKnowledgeState now optionally stores world-qualified KnownCells, with legacy KnownHexes left unchanged and the new field omitted from old snapshots. PresentationKnowledgeProjection applies actual cell-entry events according to the existing automation policy; DM-controlled views never reveal cells automatically.
- Existing owner/version-protected DiscoverAsync now accepts cell-world expeditions and reuses the same semantic feature/location validation and knowledge updates. Cell discovery events carry WorldCellId, never surrogate hex coordinates. A separate cell-world composition adapter attaches location/linked-scene snapshots to encounter triggers, validates keyed encounters against the real current-cell polygon, and appends precisely scoped location discovery events. Automated knowledge revelation obeys presentation policy. These world effects commit in the same optimistic-concurrency transaction as movement.
- The existing journey-process clock, completed-watch event/opportunity integration, relevant-travel pause gates, and forced-travel/survival accounting now accept authoritative cell expedition time. The cell application service invokes these shared hooks in the same optimistic-concurrency mutation as movement, and rejects activity assignments that differ from the authoritative party/watch snapshot.
- Ambiguous course pauses are now mandatory: ordinary watch retry cannot silently clear `CellCourseAdjudicationRequired`; only a geometrically verified `ResolveCellCourse` transition may resume. Active encounter state is also validated against its qualified cell and watch.
- The existing read-only workbench API now projects qualified cell identity, traversal cursor, exact position, intended/actual heading, cell encounter identity, KnownCells and reciprocal interface event metadata. A versioned owner-scoped `GET /api/expeditions/{expeditionId}/cell-topology` exposes the actual current polygon and atomic exits; the legacy hex and abstract/nonspatial payloads retain their old shape with optional new fields omitted.
- No relational schema change or live tester-data migration is introduced.
- ExpeditionStartService.StartWorldBoundCellsAsync starts generalized world-bound sessions at an authoritative cell center using an owner-authorized pinned procedure selection. HexCrawlService.AdvanceCellExpeditionAsync, ResolveCellExpeditionCourseAsync and ResolveCellExpeditionEncounterAsync enforce owner and expected-version checks and commit through the existing transactional PostgreSQL store. The current HTTP start/workbench routes remain gated for nonhex travel because their projections, associated gameplay services, and user interfaces are not complete; direct application capabilities are not a release-ready end-to-end path.

## Tests added

1. Geometry: triangular, square, hexagonal, mixed motifs, reciprocal atomic interfaces, vertex adjudication, selected-boundary correctness, scale/origin precision, exact-boundary double-back.
2. Execution: multi-cell continuous distance, cell-step budgets, physical calibration, no-calibration rejection for physical movement, pause/review and restart of the geometric cursor, ambiguity without phantom transition.
3. Runtime policy: pinned version-2 continuous-distance and cell-step execution for multiple tilings, rejection of legacy hex-step reinterpretation and mismatched D-symbols, refusal of v2 hex-progress semantics.
4. PostgreSQL: schema-10 generalized runtime serialization, mid-cell save/restart/cross/save, qualified event history, ownership isolation, optimistic concurrency, uncalibrated pending encounter and active-watch restart, duplicate-event protection.
5. Application: generalized cell and feature environment facts, coexistence of legacy hex and qualified-cell annotation scopes, and owner-scoped versioned movement persistence across a fresh PostgreSQL connection.
6. Watch runtime: triangular, square, hexagonal, and mixed-motif cell watches; time and qualified crossing events; boundary review/resume; vertex adjudication and explicit interface selection; uncalibrated cell steps; angular lost state; encounter pause/resolve with occurrence identity and deterministic restart. Rejected unadjudicated retries and inconsistent qualified encounter snapshots are regression-tested.
7. Read-only HTTP: retrieval of persisted nonaxial cell expeditions and versioned cell-topology interface choices, uncalibrated physical scale staying null, reciprocal interface metadata and denial to unauthorized owners. The existing semantic discovery endpoint also accepts cell expeditions without fabricating hex events.
8. World knowledge and encounter composition: polygon-bound keyed-location checks, known-cell persistence across PostgreSQL restart without known-hex contamination, manual named-subject discoveries, and DM-controlled automatic-reveal suppression.

CI, integration and release tests must still be evaluated on the exact final branch head before updating the verified status.

## Mandatory work remaining

1. Consolidate the remaining generalized cell-watch orchestration with CrawlRuntimeEngine.AdvanceCore, building on the now shared encounter-time slicer and shared journey/forced-travel hooks. Complete automatic environment consequences, additional resource mutations and the interaction-specific encounter handoff beyond newly implemented keyed-location snapshot and discovery composition. **Do not fork watch/gameplay mechanics into a second engine.**
2. Harden generalized angular navigation and selected-interface handling, including mid-watch course changes, unknown paths, deliberate reverse, direction validity after veer and explicit decisions at vertices. The initial typed domain interface decision has no versioned HTTP contract yet. Preserve v1 60-degree veer for existing hex sessions.
3. Finish connecting CellExpeditionState and periodic world authority through the remaining application surfaces: procedure-version-2 authoring and revision selection, effects and additional resource consequences, other encounter resolution handoffs, and generalized player-map rendering. Semantic discovery, cell-entry knowledge, keyed-location encounters, common journey and forced-travel/survival time hooks, and authoritative watch assignments are now wired. The new direct application entry points are not yet exposed by the supported HTTP workflows.
4. Complete versioned generalized mutation API contracts for course selection and watch/encounter resume. Read-only current-cell, adjacency and qualified event contracts are implemented. Preserve hex API payloads unchanged. Ordinary nonhex expedition entry points must remain disabled until Phase 19 presentation and full mechanics are ready.
5. Validate representative existing schema-10 saved tester-like expeditions, active/paused watches, navigation and pending encounters, pinned revisions, retry/duplicate delivery, effects and discoveries, concurrent writes, and abstract/nonspatial parity. Perform a full independent self-review and applicable Docker/PostgreSQL/frontend tests.
6. After explicitly authorized merge/deploy, independently verify authenticated development/testing workflows against existing saved tester data, maps and source assets. Do not reset or migrate the live database from the feature branch.

## Compatibility and release assessment

Legacy gameplay execution has not been replaced by the incomplete generalized watch integration. This is deliberate; accepting nonhex user-facing expeditions now would create sessions unable to advance through the established mechanics.

**Merge decision: blocked by incomplete required Phase 18 integration.** CI success for incremental foundations does not authorize merge, deploy, or a declaration that Phase 18 is done.
