# Phase 16 operational audit — implementation staging, not release acceptance

**Roadmap:** `docs/tile-crawl-development-plan.md` at/after Hex Crawl PR #66 (Phases 16–21). **Scope:** D-symbol mathematics, translation topology, and generalized Surveyor image reconstruction are all Phase 16 requirements.

**Production authority:** no change. `OverworldDefinition.Grid`, `HexTraversalState.CurrentHex`, `HexDirection`, procedure schema 1.2, and Surveyor `/v2/periodic-tiling/detect` remain authoritative for supported tester workflows. This staging work does not modify schema version 9 or previously stored JSON.

## Implementation status and mathematical boundary

- Independently specified numerical D-symbol parsing/classification, exact-rational curvature, chamber relabeling, finite connected involutions and m-orbit axioms: staged C# and TypeScript implementations with matching conformance examples.
- Chamber-isomorphism and explicitly supplied common-cover tests: staged. Inconclusive is distinct from proved non-equivalence. Metric interval conflicts are distinct from structural conflicts.
- Typed finite motif addresses `(motifCellId, translation.u, translation.v)` and reciprocal adjacency: implemented in the C# domain. The independent structural validator reconstructs chamber incidence and proves the claimed translational graph; the separate bounded metric verifier checks concrete polygon geometry. These guarantees do not yet establish generalized runtime spatial authority.
- Image-independent constructors in both languages now build bounded primitive translation covers directly from fully valid Euclidean D-symbols, including nonuniform symmetry quotients. Separate harmonic constructors attempt concrete polygon geometry without an image and accept only independently validated witnesses. Not every admissible Euclidean symbol has a demonstrated nondegenerate metric construction under the current bounds.
- Surveyor's opt-in authenticated v3 investigation extends the original edge and translation pipeline to reconstruct candidate polygons, reciprocal motif adjacency, derived D-symbols, and original-image global rigid-fit evidence. Candidates are explicitly non-authoritative, may be inconclusive, and cannot be persisted as accepted world topology.
- Raster-derived incidence, provisional source-pixel geometry, basic held-out synthetic mixed motifs, and multi-region residual checks are implemented experimentally. A statistically independent real-map corpus, calibrated confidence, authoritative candidate resolution, proven maximal-symmetry reduction, production world/ruleset integration, and signed-in regression testing remain **unmet Phase 16 acceptance requirements**. An opt-in read-only v3 consumer now exists but is not an accepted spatial runtime.

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
3. New Tile Crawl client + old Surveyor v2: the opt-in investigation client probes capabilities; a missing v3 capability produces `unsupported` without attempting v3 or fabricating a candidate. Existing hex grid analysis still uses v2. Verified with an isolated HTTP provider stub; cross-deployment tests are still required.
4. New Tile Crawl client + augmented Surveyor: an explicit authenticated v3 observation request validates response version, experimental/non-authoritative flags, finite basis, bounded candidate geometry, and the claimed primitive translational chamber graph independently in .NET. Verified with provider contract fixtures; real end-to-end mixed-image and production world/ruleset integration remain unverified.
5. Old/new database snapshots: no mutation in Phase 16; migration of established spatial identities belongs to Phase 17 and must be non-destructive.

**No database schema change** is part of this staged implementation. No database reset or deployment is authorized. The known reset-oriented error text is not permission to reset.

## Verification limitations

Both feature branches have executed their GitHub Actions validation suites, including domain tests, Surveyor raster benchmarks, and database-backed tests where configured. Individual successful CI runs do **not** prove cross-deployment compatibility, real-image generalization, live signed-in workflows, data preservation on deployment, or generalized product integration. Those release gates are still open, and Phase 16 must not be merged on CI status alone.


## Independent raster geometry plausibility and combinatorial symmetry quotient

The read-only v3 client now independently checks provisional source-pixel
polygon simplicity, orientation, nonzero sides, bounded fundamental-domain
area coherence, and reciprocal pixel-segment matching after translating by
the observed lattice generators. It permits bounded white-contour/ink
uncertainty in the **analysis-image pixel space**, then maps that tolerance to
source pixels; it does not incorrectly cap source-pixel errors when Surveyor
downscales an image. A failure returns a protocol error and never saves a
candidate or changes the established v2 path.

Provider tests reject valid D-symbols paired with forged geometries, including
crossing polygons and wrong translation lengths; subpixel and downsampled
measurement cases must remain provisional candidates. Additional interop
tests construct image-free validated mixed triangle/quadrilateral and
non-edge-to-edge polygon motifs, serialize them through the actual v3
candidate wire shape, and verify their full reciprocal incidence is consumed
without pattern-name lookup or a single-shape assumption. These are
mathematical fixtures, **not real Surveyor image detections**.

The bounded `DelaneyDressChamberSymmetryReduction.Construct` additionally
enumerates all color/multiplicity-preserving automorphisms of a finite
connected Euclidean chamber graph (at most one automorphism per possible
image of its first chamber), constructs their orbit quotient, and requires an
independent covering projection from the source. The same mathematical
operation is independently implemented in Surveyor. A square translation
torus reduces to the one-chamber square D-symbol, and mixed/non-edge-to-edge
test cases remain valid covering witnesses.

**Crucial boundary:** this is a maximal **combinatorial** quotient. It does
not prove that measured Euclidean polygon geometry possesses those
isometries. Therefore no observed v3 D-symbol is replaced automatically
by the combinatorial quotient, and no raster candidate is promoted into a
saved world. Metric-symmetry verification, calibrated real-image detection
and generalized authoritative world integration remain open.

## Read-only Surveyor v3 capability negotiation (new Hex Crawl consumer)

The additive `SurveyorPeriodicMotifInvestigationClient` registers separately
from `SurveyorMapAnalysisClient`. Only the explicit authenticated
`POST /api/overworlds/{overworldId}/source-maps/{sourceMapId}/motif-investigation`
route invokes it; normal grid analysis continues unchanged on v2.

Before consuming a source raster, the client queries Surveyor's root discovery,
requires the exact experimental, non-authoritative `map.periodic-tiling.investigate`
capability and rejects any expectation of caller-provided tiling identity.
Old Surveyor deployments produce an explicit `unsupported` result with no
candidate and no v3 POST. The client bounds remote JSON input, checks
metadata and numeric evidence, and independently derives a .NET
`PeriodicTopologyWitness` from every candidate's reciprocal motif boundaries.
Only a valid Euclidean, primitive Z² chamber graph with the claimed canonical
D-symbol can be surfaced as a **provisional raster observation**. Candidate
polygon pixel coordinates are not promoted to validated world metric geometry.

The source-map route checks account ownership before opening the stored asset,
verifies returned source dimensions/media against stored metadata, marks results
non-cacheable, and does not change world versions, grids, source maps, files,
registrations, procedures, expeditions or database schema. Ordinary UI does not
display experimental D-symbol notation. It is an opt-in developer/API seam,
**not** the final Tile Crawl generalized map-authoring experience.

On the Hex Crawl branch, [CI run 37983749528](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/37983749528)
passed 297 client tests, 112 domain tests, 332 application tests, and 145
PostgreSQL-backed HTTP integration tests, including new provider contract,
old-service fallback, malformed/forged candidate rejection, authorization,
read-only version preservation, and source-metadata mismatch cases.
The separate Surveyor branch retains successful CI run 37982387751.
These automated tests do not substitute for a deployed cross-version
matrix, independent real-raster calibration or existing signed-in tester smoke
checks. No merge, deployment, or migration was performed at this checkpoint.

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
mixed-cell cases. The same bounded image-free harmonic metric construction is now independently
implemented in .NET and verified against the domain metric validator. Neither
implementation guarantees a nondegenerate embedding under arbitrary metric
constraints, and not every Euclidean D-symbol has a proved realization. Domain work must not
silently accept unverified geometry or treat this milestone as the
generalized detector acceptance gate.


## Independent bounded .NET metric witness validation

`PeriodicMetricWitnessValidator.Validate` now provides an additive,
non-authoritative .NET check for a supplied concrete realization paired with
an independently validated topology witness. It rejects polygon
self-intersection, nonfinite or degenerate vertices, unpaired translated
interfaces, mismatched fundamental-domain area, overlapping periodic
interiors, invalid orientation, missing cell identities and unsatisfied or
unknown metric constraints. Named constraint keys currently verified are
`period-u-length`, `period-v-length`, `period-angle-degrees`,
`edge-length`, `tile-area`, and `cell-area:<cellId>`.

The domain validator is limited to 24 polygonal motif cells and 64 corners
per cell and does not perform image analysis, construct world state, or
write to the database. The additive .NET
`DelaneyDressHarmonicMetricRealization.Construct` independently derives a
bounded polygon witness from the general Euclidean translation cover and
verifies it with this validator. The constructor now also fits requested period-u length, period-v length,
and included-angle intervals through a bounded, positive-orientation affine
transformation, with the entire polygonal realization then independently
validated. It still does not solve arbitrary vertex-angle, polygon edge-length,
area, or other coupled metric constraints, nor does it guarantee a solution
for every geometrically admissible Euclidean D-symbol. Existing geometry and asset pathways remain untouched.
The corresponding domain regression cases include a valid periodic
rectangle, metric constraints and adversarial invalid witnesses.


### Bounded geometry representative audit

The independent .NET metric verifier now bounds each polygon vertex to two
fundamental-lattice periods in the supplied basis before checking intersections.
It then checks the same `[-4,4] × [-4,4]` periodic copy neighborhood as the
TypeScript polygon-witness validator, avoiding a narrower-than-documented
collision search. More distant but equivalent polygon representatives must be
translated back near the origin by an exact lattice gauge change before they
can be certified; they are not classified as mathematically invalid tilings.
An adversarial distant-representative regression test enforces this limit.


### Image-independent .NET harmonic construction checkpoint

The .NET harmonic constructor has been exercised on eight representative
regular and nonuniform Euclidean D-symbols, including a symmetry-reduced
non-edge-to-edge motif, with independent polygon, reciprocal-edge, overlap,
periodicity, and metric-constraint validation. It globally normalizes a
clockwise harmonic solution only by reflecting **both** every polygon and
the corresponding lattice-voltage coordinate; mixed or degenerate face
orientations remain unresolved. The constructor makes no Surveyor request.

The verifier additionally bounds polygon representatives to two lattice
periods from the origin, and checks periodic intersections through a
`[-4,4] × [-4,4]` lattice neighborhood. Valid more-distant representatives
must be recentered by exact periodic translations before validation. These
bounds are computational limits, not statements that the underlying
Euclidean tiling is invalid. Neither capability creates or modifies saved
expeditions, worlds, maps, assets, or PostgreSQL schema.
