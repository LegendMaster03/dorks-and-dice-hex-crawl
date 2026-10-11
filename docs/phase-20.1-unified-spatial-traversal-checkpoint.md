# Phase 20.1 — unified spatial traversal checkpoint

Status: **domain/application authoring implementation verified on the feature branch**.
Branch: `feature/tile-crawl-phase-20.1-unified-traversal`.
This document records the handoff boundary; it does not authorize a merge, production
deployment, or migration of existing persisted expeditions.

## Decisions and runtime authority

- For a new V2 spatial watch, the actual periodic tiling, polygon boundaries,
  atomic interfaces, reciprocal adjacency, and the geometric cursor determine
  position and crossings. `PeriodicTraversalExecution` remains the shared
  geometry executor for hex and nonhex cells. No separate Alexandrian executor
  or hard-coded six-direction branch exists in that geometry kernel.
- Historical V1 hex progress factors are **not** geometrical truths. The V1
  starting/near/far/back thresholds remain pinned to their existing procedures
  for historical execution, but do not move a new V2 cursor. A 60-degree
  course change from an actual hex entry may reach another boundary sooner
  than the historical far-exit factor predicts. This difference is intentional
  and regression-tested.
- The explicit V2 `procedure.movement.turn-cost` handler describes a
  nongeometric debit to a resolved movement budget. The cost is not added to
  `DistanceTraveled`, not treated as a boundary crossing, and not invented
  for uncalibrated cell-step travel. The old Alexandrian 1/6-center-distance
  turning cost is configurable without preset-key runtime dispatch.
- Unpaid turning effort is a separate `PendingTravelBudgetCharge`. Its
  serialization and resumption, including a midwatch encounter and a completed
  watch, are covered by tests. A rejected immediate boundary decision does not
  pay a proposed turn; an accepted course charges once. An initial course with
  no prior accepted heading is not charged for a fictitious turn.
- A future vertex or T-junction can be approached by continuous-distance
  movement up to the actual ambiguous intersection. No destination cell is
  selected there until adjudicated. Logical cell-step travel does not invent
  fractional steps or physical-distance calibration.
- Suppressing a navigation check does not erase an already-lost state. Explicit
  recognition/reorientation and deliberate double-back retain their existing
  separate semantics.

## Creation-time adapters and preset fidelity

- `SpatialCourseSelection` translates a legacy six-direction request or an
  actual neighbor/interface choice into a geometric heading. Only the
  periodic-cell geometry resolver determines whether that heading crosses a
  selected interface.
- `AbstractCellSessionGeometry` creates reproducible calibrated hex geometry
  for **new** mapless spatial sessions with a stable session identity, known
  scale and exact initial center position. It is **not** a converter for
  historical V1 mapless sessions with abstract progress.
- `GeometricNativeProcedureMaterializer` is an explicitly selected creation
  path for the three existing executable hex-origin presets:
  `alexandrian-advanced`, `simple-fixed-distance`, and
  `simple-hex-step`. Ordinary catalog materialization is unchanged and V1.
  The new geometric procedure contains the pinned V2 movement mechanic, optional
  generic turning policy, original time/navigation/encounter choices, and no
  V1 hex-progress handler. Its source-preset revision is intentionally not
  represented as a new published catalog revision.
- The nine other catalog presets remain declarative where they lack a complete
  native movement/watch execution contract. Do not synthesize watch durations,
  spatial distances, or capabilities to force these presets into V2.
- The generic procedure Composer has a safe default for the new mechanic and
  an ordinary parameter presentation label. **No tiling-selection UI** or
  nonhex ruleset presets were added. Existing hex-oriented preset presentation
  remains the tester-facing default.

## Verification

The feature branch was validated by GitHub Actions `Validate Hex Crawl`.
Run [38173207054](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38173207054)
passed frontend build/tests, .NET Release solution build, domain tests,
application and PostgreSQL tests, HTTP integration tests, isolated container
build, database restart persistence, mapless restart, and backup/restore
rehearsal.

Key regression coverage:

- `Phase20SpatialAdapterTests`: deterministic mapless geometry, six-way
  heading conversion, and turning effort independent of cursor displacement.
- `Phase20GeometricNativeProcedureTests`: native preset fidelity and origin,
  opt-in V2 binding, declarative-preset rejection, discrete-step policy
  validation, and nongeometric Alexandrian charges.
- `Phase20GeometricTurningRegressionTests`: outstanding debt across watches
  and serialized snapshots; rejected/accepted course adjudication; geometric
  versus historical exit factors; encounter-pause debit replay; and no
  first-course false charge.
- Updated `Phase18CellTraversalGeometryTests`,
  `PeriodicStraightEdgeSubdivisionTests`, and
  `Phase18CellWatchRuntimeTests`: vertex and T-junction adjudication with
  accurate approach distance/time, without phantom crossings, alongside the
  existing triangle/square/hex/mixed runtime matrix.

## Deliberately deferred to Phase 20.2

1. Move the application start/advance and persistence pathways to the single
   geometric authority, including mapless spatial sessions. Continue to
   enforce ownership, expected revision/version, activity and consequence
   integration, and typed event/encounter identities.
2. Define explicit versioned migration/replay for historical V1 states and
   pinned procedures. Historical abstract progress can not be presumed to
   identify a unique geometric position. Preserve and audit original snapshots
   until an approved loss-aware migration or historical replay route exists.
3. Ensure persisted cell-watch DTOs represent turning-budget debt, pending
   adjudication and accepted intent losslessly through database restart.
   The Phase 20.1 JSON roundtrip tests are not evidence that the existing
   application persistence DTOs have already been migrated.
4. Keep nonspatial sessions nonspatial. Maintain compatibility projections
   for legacy hex IDs until all consumers and historical records are addressed.
5. Heterogeneous tilings with unequal neighbor-center distances require an
   explicit procedure-defined turning-cost reference; do not silently choose
   an arbitrary adjacent center distance. Existing V2 zero-cost or cell-step
   procedures do not require such a reference.

The broader UI and additional square/other tiling presets belong to a later
reviewed phase, after personal verification; historical V1 retirement and
regression cleanup belong to Phase 20.4. The repository's `main` branch,
deployment, and production data are untouched by this checkpoint.

## Post-validation self-review fixes (2026-10-11)

An independent review of the live branch found and corrected early
turning-effort mutations that the original geometric tests did not cover:

- Preflight for an ambiguous boundary happens **before** any turn debit,
  including when the new cost would consume the entire budget. No rejected
  interface can consume nongeometric effort.
- A zero-time encounter preserves the previous accepted heading so a
  proposed deliberate turn is charged after the encounter resolves.
- The first proposed heading is not marked geometrically accepted at a
  zero-time encounter. Changing this untraveled proposal does not produce
  a fictitious first turn or movement.
- Deliberate double-back is validated before any encounter can pause the
  operation, but its lost-state reset and interface selection occur only
  when the watch actually proceeds to travel.

`Phase20GeometricTurningRegressionTests` covers each case, including
JSON replay. Existing V1 movement/presets remain unchanged. The application
and persistence cutover remains explicitly assigned to Phase 20.2.
