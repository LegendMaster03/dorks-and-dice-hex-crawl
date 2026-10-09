# Phase 16 operational audit — implementation staging, not release acceptance

**Roadmap:** `docs/tile-crawl-development-plan.md` at/after Hex Crawl PR #66 (Phases 16–21). **Scope:** D-symbol mathematics, translation topology, and generalized Surveyor image reconstruction are all Phase 16 requirements.

**Production authority:** no change. `OverworldDefinition.Grid`, `HexTraversalState.CurrentHex`, `HexDirection`, procedure schema 1.2, and Surveyor `/v2/periodic-tiling/detect` remain authoritative for supported tester workflows. This staging work does not modify schema version 9 or previously stored JSON.

## Implementation status and mathematical boundary

- Independently specified numerical D-symbol parsing/classification, exact-rational curvature, chamber relabeling, finite connected involutions and m-orbit axioms: staged C# and TypeScript implementations with matching conformance examples.
- Chamber-isomorphism and explicitly supplied common-cover tests: staged. Inconclusive is distinct from proved non-equivalence. Metric interval conflicts are distinct from structural conflicts.
- Typed finite motif addresses `(motifCellId, translation.u, translation.v)` and boundary-relative reciprocal adjacency: staged C# contract. Full metric consistency and correspondence from those boundary records back to the asserted chamber cover are **not** certified in C#.
- A general polygonal witness can be validated and its full translation-group chamber graph derived without an image in the staged TypeScript implementation. This accepts unfamiliar mixed-cell motifs and subdivided boundaries. **However, it requires an externally supplied geometric translation basis and cell polygons; it does not construct a translation cover from a D-symbol alone.** This is an unmet mandatory acceptance condition.
- New detected-translation candidate module consumes the retained Surveyor edge evidence and tests pairs of rigid shifts across distant source regions. This is only step 1 of the generalized detector, not an observed D-symbol.
- Surveyor raster-derived polygons, edge/vertex incidence, D-symbol quotient reduction, held-out mixed-motif detection, confidence calibration and complete original-raster residual validation: **unmet**. These must be implemented and verified within Phase 16 following PR #66, not deferred to a separate Phase 20.

## Structural witness verification and face orientation correction

The C# `PeriodicTopologyWitness.ValidateAdjacency` now performs more than local interface reciprocity: a dedicated structural validator reconstructs chamber involutions and valences from ordered motif boundaries, matches the independently declared canonical translation D-symbol, checks exact closure of every vertex in periodic coordinates, and verifies that all cells connect through a *primitive* rank-two translation lattice. An internally consistent but false declared identity or disconnected sublattice is rejected. No metric geometry is inferred from a D-symbol, and geometry still requires separate validation.

Both symbol-to-torus implementations enumerate every face cycle using one globally consistent chamber-orientation parity. The previous arbitrary-per-face choice could produce mismatched oriented vertex loops even when local reciprocal edges appeared valid. Cross-language tests exercise the same square and mixed-symbol cases and independently generated geometric witnesses; neither runtime spatial authority nor production HTTP output is affected.

## Phase 17 migration field inventory

| Existing source | Current field / meaning | Planned neutral representation | Rollout and data preservation |
| --- | --- | --- | --- |
| `HexGridDefinition` | `Id` | Existing world/grid identity retained as tiling realization identity | Keep existing UUID; introduce additive topology metadata before changing authority |
| `HexGridDefinition` | orientation, origin, rotation, radius, neighbor-center distance | Chosen realization basis, polygons, units and pose | Convert current hex geometry via a validated adapter; preserve exact old geometry and distance units |
| `HexCoordinate(Q,R)` | axial cell coordinate | `(motifCellId, translationU, translationV)` | Preserve old Q/R in stored records; deterministic bijective hex adapter must be proved and tested |
| `HexId(GridId, Coordinate)` | stable cell identity | realization/world ID plus periodic cell address | Do not regenerate IDs or relabel existing saved grid cells |
| `HexDirection` | 0–5 facing and neighbor selection | boundary interface identity + reciprocal entry interface | Preserve six-direction adapter for all existing expeditions until movement equivalence is verified |
| `HexTraversalState` | current hex, entry/last direction, progress, exit requirement | current cell, entering edge, movement intent, accumulated progress | Requires lossless data-preserving expedition JSON migration and replay tests |
| `ExpeditionState` | current/intended/actual hex and direction | cell address and chosen traversal interface | Maintain existing response DTOs during expand/migrate/cutover |
| `OverworldDefinition` | `Grid`, `Features`, `Locations`, `SourceMaps`, `EnvironmentAnnotations` | world tiling topology/realization plus independent semantic layers | Keep world UUID, feature IDs, anchor geometry and source assets unchanged |
| `FeatureIntersection`, `HexGeometry` | polygon/hex containment, hex corners and projection | world polygon intersection and generalized cell geometry | Dual-run pure calculations before replacement; do not reinterpret stored anchors |
| Source map representation | pixel/world affine and control points | independent source-image registration to selected realization | Preserve map ID, raster binary, affine and existing control points |
| `SurveyorMapAnalysisClient` | `expectedDsSymbol` hint and hex-only `fit` | no-hint generalized observed motif on additive versioned endpoint | Maintain deployed v2 until old and new clients pass transition matrix |
| Canvas, map selection and current-cell adjacency adapter | axial visible hex bounds and six boundary interfaces | lazy visible-region cell enumeration and polygon interface renderer | Keep old hex drawing path operational until generalized UI is tested |
| `PostgresSchemaMigrator` | current schema 9; explicit 8→9; other versions reject with reset-oriented text | no Phase 16 schema change | Change reset wording separately to safe operator diagnostics; never auto-reset tester data |
| Procedure `tilingDsSymbol` | literal regular hex canonical D-symbol (schema 1.2) | local structured requirement and compatibility result | Pin existing revisions as immutable and preserve JSON editing and nonspatial procedures |

## Compatibility matrix for the coordinated API switch

1. Old Tile Crawl client + old Surveyor v2: required to keep working unchanged.
2. Old Tile Crawl client + augmented Surveyor: required to preserve v2 request/response and old `expectedDsSymbol` hint while consumers transition.
3. New Tile Crawl client + old Surveyor v2: explicit capability negotiation and hex-only fallback, no fabricated generalized result.
4. New Tile Crawl client + augmented Surveyor: use independently observed symbols and local compatibility only after generalized contract validation.
5. Old/new database snapshots: no mutation in Phase 16; migration of established spatial identities belongs to Phase 17 and must be non-destructive.

**No database schema change** is part of this staged implementation. No database reset or deployment is authorized. The known reset-oriented error text is not permission to reset.

## Verification limitations

The TypeScript core was compiled with local `tsc` and its focused topology tests passed. C# tests are included but were not executed locally because a .NET SDK is unavailable in this execution environment. The complete CI suites, database-backed tests, live signed-in tester smoke checks and held-out generalized image analysis have not been run. This work must not be marked Phase 16 complete or merged until all mandatory gates pass.


## New bounded Euclidean multi-chamber quotient construction

Both C# `DelaneyDressUniformQuotientUnfolding` and TypeScript
`unfoldUniformEuclideanQuotient` now construct a **connected fiber product**
of a regular Euclidean reflection torus with a quotient chamber action whose
`m01` and `m12` orders are each constant and satisfy
`(p-2)(q-2)=4`. An independent tree/cotree construction verifies primitive
integer translation generators, and the C#/TypeScript structural witness
verifiers rebuild the incidence and require a valid projection onto the
original quotient. The universal reflection-torus seed is verified, not
treated as an arbitrary shape-registration result. The common
`uniformQuotientCases` fixture explicitly names accepted, rejected, and
limit-exceeded examples.

This is still **not** general mixed-face-orbit or metric-embedding
reconstruction. It is additive domain logic only; no schema or existing
spatial authority changes. Tests must be rerun before Phase 16 acceptance.


## Bounded orientation double for arbitrary Euclidean quotient

`DelaneyDressOrientationCover.Construct` implements the connected parity
double cover for valid zero-curvature D-symbols, including nonuniform
face-degree and vertex-valence examples. It proves each chamber projection,
eliminates all fixed-point involutions, returns an idempotent result for
already oriented symbols and reports any remaining local branching.
`orientationCoverCases` is byte-identical to Surveyor's versioned fixture.
This is not a translation lattice or a metric embedding; removal of residual
rotational branching is still required before generalized acceptance.

## General mixed-cell Euclidean quotient translation construction

`DelaneyDressGeneralQuotientUnfolding` implements the same bounded
orientation-plus-cyclic-holonomy covering method as Surveyor. A
connected orientation double strips reflection identifications; local
cone-point orders are read directly from two-involution chamber orbits,
and a sparse integral voltage flow produces a cyclic unbranching cover.
The resulting chamber graph must be orientable, locally unbranched,
combinatorially a torus, and independently confirmed to have reciprocal
cell interfaces, **primitive Z² lattice generators**, and a projection
back to the original quotient.

This method supports nonuniform polygon degrees and vertex valences,
including independent symmetry-reduced mixed square/triangle and
non-edge-to-edge examples. It does not consult a list of pattern names.
The shared `generalQuotientCases` conformance set exercises its
supported, invalid and resource-limited outcomes in both runtimes.
The actual user-facing Tile Crawl spatial authority is unchanged.

Surveyor has additionally demonstrated an image-free harmonic metric
construction with independent polygonal proof for eight regular and
mixed-cell cases. The image-free metric algorithm is not yet part of
the .NET operational path, and not every metric constraint or arbitrary
Euclidean D-symbol has a proved realization. Domain work must not
silently accept unverified geometry or treat this milestone as the
generalized detector acceptance gate.
