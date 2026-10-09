# Phase 16 operational audit — implementation staging, not release acceptance

**Roadmap:** `docs/tile-crawl-development-plan.md` at/after Hex Crawl PR #66 (Phases 16–21). **Scope:** D-symbol mathematics, translation topology, and generalized Surveyor image reconstruction are all Phase 16 requirements.

**Production authority:** no change. `OverworldDefinition.Grid`, `HexTraversalState.CurrentHex`, `HexDirection`, procedure schema 1.2, and Surveyor `/v2/periodic-tiling/detect` remain authoritative for supported tester workflows. This staging work does not modify schema version 9 or previously stored JSON.

## Implementation status and mathematical boundary

- Independently specified numerical D-symbol parsing/classification, exact-rational curvature, chamber relabeling, finite connected involutions and m-orbit axioms: staged C# and TypeScript implementations with matching conformance examples.
- Chamber-isomorphism and explicitly supplied common-cover tests: staged. Inconclusive is distinct from proved non-equivalence. Metric interval conflicts are distinct from structural conflicts.
- Typed finite motif addresses `(motifCellId, translation.u, translation.v)` and boundary-relative reciprocal adjacency: staged C# contract. Full metric consistency and correspondence from those boundary records back to the asserted chamber cover are **not** certified in C#.
- An explicit polygonal witness can be verified against its D-symbol and can derive a complete translation chamber graph, including subdivided boundaries. Additionally, C# and TypeScript now construct stable reciprocal integer translation covers **directly from fully expanded, orientable, unbranched torus D-symbols**, without an image or supplied polygons, using a proven tree/cotree cell-complex construction. Both implementations share an eight-case versioned corpus. **A general symmetry quotient with rotational or reflection stabilizers still requires automatic unfolding to a suitable torus presentation.** This remains a mandatory unmet acceptance condition.
- New detected-translation candidate module consumes the retained Surveyor edge evidence and tests rigid shifts across distant original-image regions. The bounded high-contrast stage now observes repeating polygon interiors, matches reciprocal raster boundaries, constructs a finite translation chamber graph, and derives a validated Euclidean D-symbol candidate without image-to-pattern catalog selection. Independent polygon witnesses confirm the derived symbols for two synthetic mixed-cell motifs, including rotation and scaling. Distinct superlattice presentations are reconciled only when chamber-cover projections prove the correspondence. T-junctions and incomplete boundaries are conservatively inconclusive; this is an internal prototype, not the finished detector. The added global rigid-fit gate predicts all cell corners from one untranslated motif per class, checks original raster edge ink across distant regions, and rejects a perturbed translation basis; an additive joint least-squares fit now refines the shared translation basis from original cell centroids, but a calibrated full-raster continuous metric optimization remains outstanding.
- Surveyor robust arbitrary-raster geometry, T-junction handling, general symmetry-quotient unfolding, maximal-symmetry quotient reduction, genuinely held-out motif benchmarks, calibrated confidence, complete original-image global-fit residual validation, and a compatible versioned observed-pattern result: **unmet**. The experimental candidate is not an authoritative observed identity. These gates remain within Phase 16 following PR #66.

**Extended synthetic detector verification:** Nine rotated/scaled/gray-stroke mixed-motif cases produced the independently derived translation-group candidate symbol. Three deterministic additive-noise cases produced one correct candidate and two inconclusive outcomes, with no false identity; a texture-only negative control was also inconclusive. These are synthetic, not frozen external held-out benchmarks. All experimental paths remain outside the current v2 production detector.\n\n## Phase 17 migration field inventory

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
| `PostgresSchemaMigrator` | current schema 9; explicit 8→9; other versions now fail with data-preserving operator guidance | no Phase 16 schema change | Regression test verifies unsupported versions leave stored records intact; never auto-reset tester data |
| Procedure `tilingDsSymbol` | literal regular hex canonical D-symbol (schema 1.2) | local structured requirement and compatibility result | Pin existing revisions as immutable and preserve JSON editing and nonspatial procedures |

## Compatibility matrix for the coordinated API switch

1. Old Tile Crawl client + old Surveyor v2: required to keep working unchanged.
2. Old Tile Crawl client + augmented Surveyor: required to preserve v2 request/response and old `expectedDsSymbol` hint while consumers transition.
3. New Tile Crawl client + old Surveyor v2: explicit capability negotiation and hex-only fallback, no fabricated generalized result.
4. New Tile Crawl client + augmented Surveyor: use independently observed symbols and local compatibility only after generalized contract validation.
5. Old/new database snapshots: no mutation in Phase 16; migration of established spatial identities belongs to Phase 17 and must be non-destructive.

**No database schema change** is part of this staged implementation. No database reset or deployment is authorized. The known reset-oriented error text is not permission to reset.

## Verification limitations

The focused TypeScript mathematical and experimental raster tests passed locally, including the no-hint multi-shape D-symbol candidate pipeline, image-independent torus-cover reconstruction, and reciprocal addressing. The full existing CI pipelines in Surveyor and Hex Crawl passed at earlier Phase 16 heads; later Surveyor commits must be revalidated after integration. C# tests are not runnable locally because a .NET SDK is unavailable here, but the Hex Crawl CI exercises the domain and PostgreSQL suites. Live signed-in existing-tester-data smoke checks and truly held-out production-quality generalized image benchmarks have not been performed. No Phase 16 migration or asset change was made. Do not mark Phase 16 complete or merge until the remaining gates pass.
