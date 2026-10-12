# Phase 20.2 — Application, persistence and Phase 20.3 contract checkpoint

**Repository:** `LegendMaster03/dorks-and-dice-hex-crawl`  
**Reviewed baseline:** `68a5c231e411a969b52e9dd3cdb23c8452f9735c` on `feature/tile-crawl-phase-20.2-application-persistence`  
**Baseline CI:** [38196941315](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38196941315), successful (domain, application, HTTP, frontend, PostgreSQL and restart smoke)  
**Verified review fix:** `fb86c28b65a34a6db5359d045a7ed79779be04fd` ([CI 38200571111](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38200571111); full test and PostgreSQL/container checks passed). The explicit-null-kind migration preflight and rollback theory are verified. No merge, dev migration or deployment is claimed.

## Scope and decision precedence

The authoritative `docs/tile-crawl-development-plan.md` supersedes preservation and phase-boundary assumptions in the older Phase 20.2 handoff. Phase 20 is active development: the approved one-time schema-10→11 migration intentionally removes V1 `Spatial` expeditions and cascaded events, not V2 or nonspatial sessions or unrelated content. Do not reconstruct exact historical hex progress with fictional geometric coordinates, reintroduce the V1 engine, resurrect deprecated API endpoints or defer feasible V1 removal because of phase numbering.

## Canonical HTTP surface (authenticated /api prefix)

| Operation | HTTP |
| --- | --- |
| Start world-bound spatial | `POST /api/overworlds/{worldId}/cell-expeditions` |
| Start mapless spatial (currently abstract hex context) | `POST /api/expeditions/cells` |
| Start nonspatial journey | `POST /api/expeditions` with a NonSpatial context |
| Read any supported session | `GET /api/expeditions/{id}` |
| Exact cell geometry, cursor and reciprocal atomic interfaces | `GET /api/expeditions/{id}/cell-topology` |
| Advance/resume cell watch | `POST /api/expeditions/{id}/cells/advance` |
| Produce audited helper results | `POST /api/expeditions/{id}/cells/resolution-helper` |
| Adjudicate a selected atomic course | `POST /api/expeditions/{id}/cells/course` |
| Correct position explicitly | `POST /api/expeditions/{id}/cells/reposition` |
| Resolve a pending keyed encounter | `POST /api/expeditions/{id}/cells/encounters/{occurrenceId}/resolve` |
| Manual semantic discovery | `POST /api/expeditions/{id}/discover` |
| Party, effects, consequences, resources and journey | Existing owner/version-scoped non-V1-specific routes |

The HTTP schema uses camelCase. Example *world-bound* request for a world whose motif has cell `cell-0` (replace that motif key and the IDs with actual topology data):

```json
{
  "name": "Generalized expedition",
  "presetKey": "alexandrian-advanced",
  "startCell": {
    "motifCellId": "cell-0",
    "translation": {"u": 0, "v": 0}
  },
  "presentationKey": "dm-controlled"
}
```

Saved campaign procedures instead use `procedureId` and `procedureRevision`, never a silent overwrite/upgrade. The supported native geometric preset keys are `alexandrian-advanced`, `simple-fixed-distance`, `simple-hex-step`; other catalog presets remain declarative. The available nonhex compatible-cell testing ruleset does not turn into an automatic preset fallback.

Example *mapless* start: `POST /api/expeditions/cells` uses the existing `StartProcedureSessionRequest`, with an abstract-hex `context` and `startHex`; `AbstractCellSessionGeometry` reconstructs image-free V2 geometry from persisted session identity/context. The wire's `startHex` is an initialization address adapter, not V1 runtime authority. Nonspatial journeys use `/api/expeditions`, not fictional spatial cells.

An advance requires `expectedVersion`, `plan` (`intendedHeading`, `deliberateDoubleBack`, `continueAcrossBoundaries`, `mode`, `navigationAid`) and `inputs` (`travel`, optional `navigation`, `encounter`, `boundaryDecision`). Travel is either qualified physical `expectedDistance`/`actualDistance` or integer cell steps, as authorized by the pinned movement mode. Helper-backed automatic values supply `generatedProcedureResolutionId`; arbitrary client `AutomaticRoll` is rejected. Course and reposition use explicit expected versions; course selection is not movement.

Example topology response excerpt (illustrative IDs and coordinates, not a real fixture):

```json
{
  "formatVersion": 1,
  "currentCell": {
    "tilingId": "11111111-1111-4111-8111-111111111111",
    "address": {"motifCellId": "cell-0", "translation": {"u": 0, "v": 0}}
  },
  "position": {"x": 0, "y": 0},
  "center": {"x": 0, "y": 0},
  "heading": null,
  "hasPhysicalCalibration": false,
  "interfaces": []
}
```

The actual topology response additionally includes the authoritative polygon and interface records (`interfaceIndex`, `boundarySideIndex`, `neighborCell`, `reciprocalInterfaceIndex`, `start`, `end`); `interfaces: []` above is only a formatting illustration, **not** the expected geometry for a traversable cell. The verified current cursor format is `PeriodicCellTraversal.CurrentFormatVersion = 1`; clients should read `formatVersion` from the server rather than hardcode it.

## Authoritative execution, persistence and mutations

- The common domain executor is `CrawlRuntimeEngine.AdvanceCellWatch`; regular hex and nonhex worlds supply `PeriodicWorldTiling`. No dispatcher chooses a runtime by grid shape, catalog preset or D-symbol label.
- Spatial authority is `CellExpeditionState.Traversal.CurrentCell` (`WorldCellId` = tiling GUID + periodic motif address) and exact `Traversal.Position`. Intended and realized headings are vectors; `SelectedExitInterfaceIndex` resolves ambiguous atomic crossings. A mapless session uses reproducible `AbstractCellSessionGeometry`, without a source-image record.
- `DistanceTraveled` may be null on uncalibrated worlds; cell steps and nongeometric pending turning budget must not be converted into fabricated miles. `PendingTravelBudgetCharge`, `CellActiveWatch.PendingTurnFromHeading`, `TurnPreflightWasDeferred`, pending decision and encounter data round-trip in the V2 snapshot. Saved history is stored separately in `expedition_events`.
- World-bound crossing/discovery uses `ExpeditionCellWorldComposition` and knowledge policy; historical encounter identity is cell-qualified. Mapless travel does not manufacture world-bound discoveries or keyed authored locations. Party, effects, consequences, survival and journey services consume V2 where relevant.
- Saved `RuntimeStateSnapshot.Kind` accepts `CellSpatial` and `NonSpatial` only; schema 11 adds `ck_expeditions_runtime_v2_only`. The remaining `ExpeditionState`/hex DTO code is legacy source pending deletion, not a supported persisted new state.
- Helper generations are owner/session/version/watch bound, include an audit event and expire on mutation or consumption. The current code additionally checks the suppression and deliberate-double-back flags. **Not every future travel-plan property is bound to helper generation**; review plan-affecting fields during Phase 20.3 UI work.

## One-time schema-10→11 development migration

- `PostgresSchemaMigrator.UpgradeFrom10Async` runs under one transaction and advisory lock. It preflights state kinds, deletes only V1 spatial `expeditions` and cascading event rows, establishes the V2-only check constraint and inserts version 11. Repeated startup on version 11 is idempotent.
- V1 records without a `kind` field are intentionally classified as historical `Spatial`; explicitly null `kind` must instead fail closed. Unknown kinds or nonobject state must abort before any deletion. A proposed regression test exercises both `FutureKind` and null kinds and checks the entire transaction rolls back.
- The migration is **not executed on the development database** by any source commit. Before the coordinated deployment, verify a recoverable PostgreSQL **and map asset** backup, an isolated restore and expected data-kind counts. After the first successful schema-11 development deployment, remove the temporary `UpgradeFrom10Async` startup upgrade code on the following deployment, retaining schema 11 and its constraint.
- Retained V2/nonspatial data, world definitions, source maps, saved procedures/revisions, linked asset references and account data must not be deleted or silently converted. The one-time intentional V1 expedition deletion has been approved in the authoritative plan.

## Phase 20.3 release-blocking client integration

The still-deployed Phase 15 frontend calls now-retired hex routes such as `POST /api/overworlds/{id}/expeditions` and `POST /api/expeditions/{id}/advance`. No partial backend branch may be merged/deployed as a functioning release until Guided/Compact/Advanced/JSON, map rendering and world authoring are rewired to current qualified-cell contracts. Use polygon geometry and actual atomic interfaces for navigation, selection and hit tests; preserve the existing hex preset experience and phase-15 UX. Do not expose other-tiling customization or additional presets before owner review.

The generic expedition response temporarily retains legacy-named fields. For `CellExpeditionState`, `currentHex`, `hexProgress`, six-direction fields and `veerSteps` are null; `currentCell`, `cellTraversal`, `intendedHeading`, `actualHeading`, exact `position` and `veerDegrees` are the authoritative V2 presentation. Never fabricate a hex-progress projection to make the old frontend appear functional. `GET /cell-topology` is the geometry authority for interface selection.

## Phase 20.4 removal inventory (work can be moved earlier)

| Residual source | Removal condition |
| --- | --- |
| `ExpeditionState`, `HexTraversalState`, legacy `CrawlRuntimeEngine` movement/watch path | All V2 gameplay and preserved behavior tests exclusively exercise `CellExpeditionState` |
| `HexCrawlService.Expeditions` legacy `AdvanceExpeditionAsync`, `RepositionExpeditionAsync`, `SetExpeditionCourseIntentAsync` and related commands | No active API or application callers |
| `ExpeditionAssistantService` V1 spatial paths and `ExpeditionWorldComposition` | Nonspatial and V2-specific work remains covered; legacy spatial assistant code is unreferenced |
| Unreachable V1 start helpers in `ExpeditionStartService` | Mapless and nonspatial starts exclusively use supported V2/nonspatial contracts |
| Axial-named world/grid presentation and old client `currentHex`, `hexProgress` types | Phase 20.3 unified geometry/UI tests and final client cutover |
| One-time `UpgradeFrom10Async` | First approved schema-11 deployment and recovery verification |

Do not restore old API routes, V1 persistence codecs, deleted workbench services or prototype-only tests as compatibility shims.

## Verification and remaining decision gates

**Verified source review:** the pre-review baseline `68a5c231` passed [CI 38196941315](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38196941315). The corrected code `fb86c28b65a34a6db5359d045a7ed79779be04fd` passed [CI 38200571111](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38200571111): domain **227/227**, focused V2 application/migration **17/17**, focused V2 HTTP **3/3**, full application **377/377**, full HTTP integration **190/190**. Frontend/embedded builds, container build, PostgreSQL/filesystem-asset restart, mapless restart, and isolated PostgreSQL/map-asset backup/restore also passed. Phase 15 rendered-review capture was skipped. This is source/CI evidence, not signed-in live UI acceptance or authorization for schema migration.

**Outstanding:** Review whether helper generation should bind further plan-dependent pace/navigation/environment semantics; obtain owner authorization for the coordinated 20.3 UI/backend cutover and database migration; perform pre-deployment backup/restore and signed-in verification; complete legacy removal, presentation cleanup, arbitrary-cell test coverage and product naming in 20.4. No live migration, merge or deployed tester acceptance is claimed here.
