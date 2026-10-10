# Tile Crawl transition — development plan

**Status:** Phases 16 and 17 merged and deployed to development/testing. Phase 18 traversal generalization is in progress on feature/tile-crawl-phase-18; generalized runtime integration, persistence, and release gates remain pending.  
**Roadmap:** Phases 16–21 (the successor to the completed Hex Crawl / generic-procedure roadmap)  
**Primary repository:** LegendMaster03/dorks-and-dice-hex-crawl  
**Analysis-service repository:** LegendMaster03/dorks-and-dice-surveyor  
**Planning baseline:** Hex Crawl main at 70629398cf1b30c43addef5299ce1ad23efbfd7a; Surveyor main at e9ad1cc9f04ce375fa1baf241894357abc116e09 (9 October 2026)

## Objective

Complete the product and architectural transition from **Hex Crawl** to **Tile Crawl**. The existing expedition and ruleset engine becomes independent of hexagonal geometry. A world can use a periodic tiling composed of any supported collection of cell shapes and adjacencies, without installing a new named tiling or hard-coding a new pattern-specific runtime. Surveyor independently observes image structure; Tile Crawl interprets that observation against its own world and ruleset.

This is **one coordinated project with separately validated and merged phases**, not one enormous development cycle. The old generic-procedure development plan is superseded and removed. Existing architecture, proof, and completed Phase 15 design documents remain historical/architectural references, not competing roadmaps.

**Phase 16 scope decision:** generalize **Surveyor's existing periodic-grid detector** in the same phase as the shared D-symbol/operational-topology foundation. This extends the proven edge evidence, autocorrelation, Hough/translation fitting, continuous parameter refinement, multi-region support and original-image rigid-fit checks; it is **not a greenfield detector rewrite**. Generalized motif reconstruction and D-symbol derivation are additional stages where the current three-geometry selection cannot express mixed-cell arrangements. Prove feasibility early on unfamiliar mixed motifs; retain explicit checkpoints and measured acceptance tests because implementation effort cannot be assumed from the size of the proposed abstraction. If a blocker requires changing the agreed scope, report it rather than silently substituting a larger catalog of hard-coded shape detectors. The existing live hex workflow must remain available.

**Completion is not the ability to select hexagons, squares, or triangles from a list.** Completion is the ability to ingest a structurally valid supported periodic tiling and its suitable geometric realization, address and render its cells, cross their actual boundaries, and run the relevant generic procedures **without adding pattern-specific source code**. Surveyor must additionally discover the structure of supported unfamiliar periodic tilings from sufficient image evidence without being told which pattern to find.

## Architectural decisions — authoritative for this plan

1. **Delaney–Dress is the only periodic-tiling notation.** Neither Cundy–Rollett nor GomJau–Hogg returns as a persisted, public, or parallel notation. Standard D-symbols are not a custom Tile Crawl syntax; avoid Tegula-specific wire formats and importing restrictive third-party implementations.
2. **No hard-coded catalog of supported pattern implementations or per-pattern engine dispatch.** Example tilings are test fixtures, not the set of patterns the system can recognize or traverse. A **data-only catalog of familiar tiling names, aliases, and descriptions is required as a nonauthoritative presentation feature**, but a catalog *match* is never required for using a tiling; its entries must never determine recognition, validity, realizability, or support. General-purpose image-analysis operations, mathematical constraints, and optimized numerical kernels are allowed; an unfamiliar valid tiling must not require a named shape-specific code path.
3. **Surveyor observes; Tile Crawl decides.** Surveyor receives an image and analysis options, without an expected D-symbol or information about the selected ruleset. It derives and reports a supported detected D-symbol, measured geometry, evidence, and uncertainty. Tile Crawl compares that observation with its ruleset and world state locally.
4. **A D-symbol is structural, not a complete geometric map.** It encodes incidence/symmetry information under an explicitly chosen group and convention. It does not by itself specify the chosen translational cover, an integer-addressed fundamental motif, or a unique straight-edge metric embedding. Exact polygon coordinates, chosen geometric embedding, metric scale, distortions, image alignment, and registration are separately represented *measured or realized geometry*, not alternative tiling notations or attributes stuffed into the D-symbol.
5. **World topology, ruleset requirements, and source images are distinct authorities.** A procedure/ruleset expresses the expected tiling. The world owns its accepted spatial topology and stable cells. The source map holds its image and alignment. Surveyor cannot silently change any of them.
6. **Map semantics remain separate.** Terrain, biome, labels, regions, roads, rivers, structures, locations, visibility, encounters, and gameplay state must not become part of a tiling identity or geometric motif.
7. **Existing runtime behavior is a regression contract.** Preserve movement budgeting, navigation/lost/veer, partial traversal, double-back, discoveries, encounters, environment, effects, survival, and the current user-oriented Guided/Compact UI. Nonspatial and no-interval procedures remain first-class.
8. **Post-Phase-15 tester data has enduring value.** Existing world/expedition geometry, procedure revisions, anchored content, accounts, maps and tester data require explicit consistency checks and preferably a lossless, repeatable migration. **No routine database resets, no automatic drops/reinitialization, and no silent conversion** from an established hex world into a different tiling. A destructive reset is an **exceptional, explicitly authorized last resort**, not an ordinary development technique; see the release/data-preservation gates below.
9. **No invented certainty.** A parseable string, a mathematically valid D-symbol, a theorem-backed Euclidean periodic tiling, a *particular admissible metric realization*, and a tiling actually observed in an image are different claims. The standard two-dimensional zero-curvature criterion applies to fully valid D-symbols; it does **not** certify that arbitrary chosen polygon coordinates or image evidence match. APIs, validators, and UI must distinguish these claims.
10. **Shared contract, independent services.** The two repositories must pass the same D-symbol and geometry conformance fixtures, but Tile Crawl may not call Surveyor simply to compare a detected pattern with the selected ruleset.
11. **The tiling code is not ordinary user-facing language.** Normal users interact with named tilings and understandable shape descriptions, not D-symbols. **Raw Delaney–Dress notation may be exposed only in the explicit JSON editor, or in an error that identifies a *supported* tiling for which Tile Crawl cannot resolve a reliable human-readable name.** No ordinary selector, Guided/Compact/Advanced authoring screen, map-alignment prompt, navigation display, help card, success notification, or named-pattern contradiction message may require users to read, type, paste, or understand the notation. Developer logs and machine-readable API/persistence formats remain unaffected by this presentation restriction.

## Continuous usability and tester-data preservation — mandatory release gates

**Phase 15 marked the start of human testing. Every subsequent phase is a working release, not an isolated experimental milestone.** This policy applies from Phase 16 onward to both Tile Crawl and its Surveyor dependency, including documentation-only merges that change deployment expectations.

### No nonfunctional intermediate releases

- Every merge/deployment must leave the **currently available tester workflows usable end to end**: authentication, opening existing worlds/procedures/expeditions, saved-map assets, current hex movement and navigation, procedure editing, saving/reloading, map analysis, and supported nonspatial/journey paths. A phase may add functionality or internal infrastructure but may not remove, disable, or strand an existing working capability while waiting for a later phase.
- Implement generalized interfaces **behind** existing behavior until equivalent functionality has been proven. A temporary adapter or feature gate is acceptable where necessary for live compatibility; it must have a defined removal condition, not become an indefinite second authoritative engine. Do not advertise unsupported tilings or switch the default world type before their full creation/rendering/movement/persistence workflow works.
- Continue tester-facing bug fixes and compatibility work during development. If a proposed phase cannot land independently while preserving working workflows, **split it into smaller compatible releases or coordinate the tightly coupled changes before merging**; do not redefine “phase done” to allow a knowingly broken deployment.
- For breaking Surveyor/Tile Crawl changes, publish and test the transition matrix (old client/new Surveyor, new client/old Surveyor, old and new persisted records). Introduce the new version alongside the old contract when required, deploy compatible parts in an explicitly safe order, switch consumers, then retire obsolete surfaces only after the live cutover passes.
- CI success is necessary but not sufficient: perform signed-in smoke tests against deployed services on representative *existing tester data*, including restart/reload, authenticated API and map workflows, not only a newly initialized empty database. Recover or roll back any detected functionality regression immediately rather than deferring it to the next phase.

### Database and asset migration policy

- **Preserve by default.** Produce a versioned, idempotent migration for schema and persisted domain changes, preserving identities, procedure revisions and pinned snapshots, saved expeditions, world features, map registrations, audit history and source-map assets. Prefer transactional/online backfills with checksums or count/invariant comparisons as appropriate. A schema change is incomplete without its persisted-data conversion.
- **Plan before deploy.** Inventory live/legacy schema versions and JSON variants, including the actual deployed version; define a supported upgrade path, application/schema compatibility window, expected write behavior during migration, migration-lock/concurrency handling, and a failure/retry/rollback procedure. Use expand → migrate/backfill → cut over → contract when a rolling transition requires overlapping representations, but remove temporary compatibility scaffolding after verified cutover.
- **Back up and verify recovery.** Before any risky migration or destructive operation, take an appropriate recoverable backup/snapshot of PostgreSQL **and independently stored map binaries/assets**. Verify integrity and that a restore can be performed in an isolated rehearsal or documented tested recovery path. A backup that cannot be restored is not sufficient.
- **Test on pre-upgrade data.** Exercise migrations from snapshots representing **real earlier tester schemas**, not only pristine current-schema fixtures. Verify idempotence, interrupted runs, concurrent startup/retry, pinned procedure behavior, stable IDs, counts, map assets, and no silent loss or substitution. Verify that the prior application version remains usable during the planned compatibility window, or explicitly coordinate the cutover with a safe maintenance plan.
- **Fail safely.** An unknown/unsupported version or failed conversion must stop migration/startup with clear operator guidance and leave the database intact. It must **never** automatically reset, drop tables, recreate the database, overwrite tester data, or present dropping the database as the default repair.
- **Reset only as a last resort.** If preservation is genuinely impractical after investigating targeted migration, data repair and backup/restore alternatives, document the blocker, affected data, expected tester impact, recovery options, and why a reset is justified. Obtain the project owner's **explicit authorization for that specific reset** before performing it, preserve a recoverable backup where feasible, and provide a clear explanation to affected testers. A broad phase/plan approval or permission to merge does **not** authorize deleting tester data. Resetting to make an implementation easier is not acceptable.
- **Separate merge from deployment risk.** Each phase gate verifies main-branch tests, deployed service readiness, data migration and user-visible workflows. A failed deployment or migration is a phase failure requiring recovery; do not treat merge success alone as acceptance.

### Gate evidence required for **every** Phase 16–21

A phase handoff must report: exact commits for both repositories; migration steps and source/target schema versions; pre/post data integrity checks and backup/restore readiness (or “no database change”); explicit verification of existing tester workflows; targeted new-feature tests; deployment compatibility and rollback plan; post-deploy health/readiness plus signed-in existing-data smoke results; known defects and any required tester notice. **No phase can be accepted while the deployed tool is knowingly nonfunctional or a data migration remains unverified.**

**Current-state correction to address early:** the checked-in `PostgresSchemaMigrator` has an explicit version 8 → 9 migration, but other unrecognized old versions are rejected with text instructing operators to reset the development database. That is **existing behavior, not this project's target policy**. Early implementation must replace reset-as-default diagnostics with fail-safe data-preserving guidance and establish a tested path for the *actual* deployed tester schema(s). This planning PR does not claim the current migrator has already been changed.

## Scope and definitions

A **pattern** is the combinatorial periodic tiling; its authoritative identity is a canonical standard D-symbol. A **geometric realization** supplies cell polygons, edges, periodic translations, and coordinates for that pattern. A **fit** registers that realization against a particular source image, with confidence and residual evidence. A **cell address** identifies a specific cell independently of its polygon side count. A **traversal interface** is an actual shared boundary (which may comprise multiple segments) from one cell to another, not a number from 0 through 5.

The initial mathematically explicit target is **two-dimensional Euclidean periodic polygonal tilings with a finite repeating fundamental domain**, including mixed polygon types. The design must account for subdivided edges and more than one cell orbit. Treat non-edge-to-edge incidences explicitly; do not silently force T-junctions into one-edge/one-neighbor assumptions. The plan does not claim that every non-polygonal, fractal, aperiodic, curved-surface, or arbitrarily illustrated pattern is automatically reconstructible. When a tiling class is unsupported or the evidence insufficient, return a specific unsupported/inconclusive state rather than a fabricated identity.

A standard D-symbol may not uniquely determine metric geometry; different choices of symmetry group can also alter the symbol used to describe a visually similar arrangement. Phase 16 must establish and test **one documented identity/symmetry convention** and distinguish chamber-relabeling isomorphism, equivalence under alternative group presentations, and metric-shape compatibility. Two strings can describe the same underlying periodic cell structure under different valid group choices, while identical combinatorics can have metrically incompatible realizations. Do not assume raw D-symbol string equality alone establishes all ruleset/world compatibility, and do not equate visually similar polygons without checking their incidence. Test independently derived examples instead of assuming the current three one-chamber symbols prove general correctness.

### Human-readable tiling nomenclature catalog

Tile Crawl must provide a **versioned, maintainable, data-only catalog** for recognizable tiling names, not a catalog of implemented shapes. This is a required user-facing vocabulary service, while a successful *name lookup* is optional. The catalog belongs in Tile Crawl's presentation/reference layer, not Surveyor's detector dispatch, and should be maintainable as data without exposing an ordinary end-user code editor.

- **Lookup:** use a canonical D-symbol under the documented symmetry convention as the initial lookup key; where a familiar name requires additional geometric evidence (for example, square versus general quadrilateral, or rhombus versus general parallelogram), attach and validate explicit geometric qualifiers before using that name. Do not assume an arbitrary abstract D-symbol uniquely fixes metric shape or everyday nomenclature.
- **Entry:** preferred everyday name, optional established mathematical name, aliases/search terms, concise explanation, possible localization identifier, source/provenance, and optional geometric qualifiers. Do not store terrain, encounters, map annotations, or gameplay settings in this catalog.
- **Many-to-one and one-to-many:** multiple aliases may name the same structure, and a visually familiar family may have variant symbol representations depending on symmetry convention. Treat ambiguous matches explicitly; never use display-name equality to establish ruleset compatibility.
- **Fallback:** if no catalog entry applies, derive accurate plain-language descriptions from the observed/realized cell geometry and topology (side counts, repeated shape mixes, and actual angles/lengths where known), such as "a repeating pattern of triangles and four-sided cells." A four-sided polygon is not necessarily a square. Use that description in all ordinary, successful, or routine mismatch interfaces; lack of a catalog name does not make a valid tiling unsupported.
- **Only permitted user-facing notation surfaces:** the explicitly selected JSON editor, which represents raw authoritative procedure data, and a genuine *error* describing a tiling that the system supports but cannot resolve to a reliable human-readable name. In that narrowly scoped error, explain in plain English first; a labeled D-symbol may appear as a secondary identifier for reporting/identifying the otherwise unnamed pattern. Do not use the exception for regular unnamed-pattern confirmations, selections, comparisons, authoring, or successful analysis results. Unrecognized/invalid/unsupported patterns do not get raw code merely to fill a missing message.
- **Responsibility:** catalog names are for readable explanations, search, ruleset browsing, and mismatch dialogs only. The canonical D-symbol and authoritative topology determine structure and compatibility. An unlisted tiling remains fully usable. The UI must not require code entry as a workaround for missing names outside the JSON editor.
- **Initial coverage and validation:** populate verified familiar names for regular square, triangular, and hexagonal patterns, plus independently verified examples of other commonly named periodic arrangements (including rhombille where identity and metric evidence support it). This is catalog **content**, not a set of supported implementations. Permit further entries without changing algorithms; verify aliases, ambiguity, geometric qualifiers, and localization-safe fallback wording. A missing or invalid catalog entry must never corrupt a world or prevent traversal.

### Target system flow

1. User imports an image into Tile Crawl, with or without a ruleset/procedure already selected. Existing world/map authoring must not require an expedition or an invented default ruleset.
2. Tile Crawl sends **only raster data and detector options** to Surveyor. Remove the currently optional expectedDsSymbol request parameter; no replacement shape/category/side-count selector.
3. Surveyor searches for periodic structure, derives a validated observed D-symbol and geometric realization/fit when supported, or reports uncertainty with grounded diagnostics.
4. Tile Crawl locally compares the observed identity and **documented structural-equivalence/metric constraints** with the **applicable selected/pinned procedure when one exists** and the world's established topology. Without an applicable procedure, do not synthesize an expected ruleset merely because a newly created world has a hexagonal default. No Surveyor round-trip is needed to do this comparison.
5. If patterns agree and geometry is compatible, provide the normal map alignment workflow.
6. If they differ, the server evaluates authoritative contradictions. With none, offer a **one-click change of the applicable selected campaign-owned procedure/ruleset and world geometry**. If there is **no selected procedure**, offer a safe world-tiling update without creating an artificial procedure revision, followed by normal procedure-compatibility checks when a procedure is subsequently chosen. With blockers, explain them concretely in ordinary shape language; do not modify data.
7. Recheck versions and compatibility at commit time. An accepted change updates its legitimate authorities atomically and records the resulting procedure revision/provenance. A canceled, stale, unsupported, or failed action makes no changes.

**Important:** All freshly created worlds currently have a default hex grid. A mere unused default is **not** a contradiction. An expedition using hex addresses, grid-dependent feature anchoring, previously accepted incompatible map registrations, or immutable active procedure snapshots may be. Determine the actual dependency, not merely the presence of a grid object.

## Phase 16 — D-symbol, operational topology, and generalized Surveyor detection

**Owners:** Tile Crawl domain and Surveyor periodic-tiling resource.  
**Prerequisite:** merged Surveyor v2 / procedure schema 1.2 baseline.  
**Goal:** establish one mathematically sound, versionable cross-service contract **and generalize the existing Surveyor lattice-fitting pipeline to discover unfamiliar periodic motifs and derive their D-symbols**, before changing production spatial authority. This is one coordinated phase with work in both repositories.

### Implementation

- Audit Delaney–Dress validation/canonicalization in Surveyor against independent mathematical examples; cover involutions, orbit/multiplicity axioms, orientability where applicable, connectedness, Euclidean curvature, isomorphism, and the chosen symmetry/normal-form convention. Respect the two-dimensional realizability theorem: a **valid** D-symbol with zero curvature encodes a Euclidean periodic topological tiling, but not every supplied metric embedding is admissible or recoverable. Explicitly distinguish incomplete/axiom-invalid input, Euclidean topological validity, invalid/incompatible requested geometry, implementation limits, and image observation.
- Define the shared, versioned *conceptual contract*, with matching language-specific DTOs and conformance vectors rather than creating a new Tile Crawl tiling notation:
  - canonical D-symbol and optional validation/realizability status;
  - finite fundamental-domain cell and vertex/edge incidence;
  - a **constructed periodic translation cover** of the D-symbol's symmetry quotient, including translational generators, a finite lifted motif and deterministic motif-cell IDs plus integer lattice translations for globally addressable cells;
  - reciprocal adjacency interfaces with periodic displacement and boundary segments;
  - independently supplied planar realization, translation basis, polygon coordinates, units, and map registration;
  - source pixel provenance, confidence/residuals, ambiguity and unsupported reasons.
- Specify how a geometric realization is **constructed or obtained**, then validated against a D-symbol, including the symmetry-to-translation cover, finite motif assembly, polygon closure, legal shared boundaries, nonoverlap, connectivity, deterministic address enumeration, orientation, and traversal reciprocity. Store metric data separately from tiling identity. Provide a path for generating/choosing a valid realization from **a D-symbol plus explicit metric constraints, without importing an image**; if more information is required, surface that as a normal guided choice, not raw-notation entry.
- Design a local Tile Crawl equivalence/compatibility service that distinguishes (a) exact canonical symbol, (b) provably equivalent underlying tilings under permitted symmetry/group presentations, and (c) same combinatorial topology with unmet geometry/metric restrictions. If equivalence cannot be proved, return an **uncertain/inconclusive** assessment rather than silently declaring a mismatch or a match. Share test vectors across C# and TypeScript; do not use Surveyor as runtime comparison authority.
- Define the **non-authoritative tiling nomenclature catalog schema** and its geometry-qualified lookup/fallback contract, including a testable two-exception policy for displaying raw D-symbols. No naming lookup may restrict which structurally valid tilings can be created, detected, or traversed.
- Resolve what is *not* determined by a D-symbol (for example, free geometric parameters). Define explicit user-supplied/derived realization requirements rather than hard-coded realizations for named patterns.
- Audit all existing HexCoordinate, HexGridDefinition, HexGeometry, HexTraversalState, HexId, map-feature, renderer, and world/expedition persistence consumers. Record a field-by-field migration matrix before implementing Phase 17.
- Define status semantics and API compatibility/version rollout across both repositories. Remove expectedDsSymbol from Surveyor only in the coordinated API rollout; do not break the deployed consumer between merges.
- Start Surveyor work with a **bounded feasibility check** using the current detector's periodic translation fitting and a synthetic *non-regular mixed-cell tiling*. Record accuracy, ambiguity, CPU/memory cost, and missing topology/realization steps **before** committing to broader changes. This is the first implementation checkpoint **within Phase 16**, not a substitute for delivering the generalized extension and its acceptance tests in this phase.

### Acceptance gate

- Independent D-symbol conformance corpus includes equivalent chamber relabelings, **axiom-invalid inputs that happen to give an apparent zero-curvature sum**, valid zero-curvature Euclidean symbols, invalid or self-intersecting *metric witnesses*, nontrivial multi-chamber mixed motifs, and alternative realizations/group descriptions for the same underlying periodic structure. No test may presume a fully valid zero-curvature 2D D-symbol is topologically nonrealizable.
- The fundamental-domain model can represent triangle, quadrilateral, hexagon, rhombille, and mixed-polygon fixtures **through input data, with no required registration or pattern-specific source paths**. Optional nomenclature entries do not affect whether these fixtures execute.
- Deriving the translation cover and finite motif from nontrivial D-symbols produces reciprocal adjacency and stable translation-relative cell addresses, round-tripping deterministically with and without source imagery. If the translation cover cannot be constructed within supported limits, Phase 16 cannot be accepted. No terrain, labels, or ruleset semantics appear in the structural contract.
- All existing Surveyor and Tile Crawl tests pass; generalized Surveyor detection passes the additional Phase 16 gates below, and shared contracts are versioned and documented before either service is deployed. Publish the initial feasibility findings, remaining limitations, and exact evidence that the generalized extension is ready; **no later Surveyor implementation phase is planned**. Do not mark Phase 16 complete based on a spike alone.

**Mathematical references for independent verification:** Olaf Delgado-Friedrichs, [*Data Structures and Algorithms for Tilings I*](https://gavrog.org/TCS.pdf), especially the 2D curvature/realizability theorem; and the Australian National University's [introduction to Delaney–Dress chamber systems](https://epinet.anu.edu.au/page/epinet2_mathematics_delaney_dress). These are mathematical references, not dependencies on either project's software implementation.

### Surveyor implementation — extend the existing detector within Phase 16

**Owner:** Surveyor; the Tile Crawl domain consumes the shared topology/geometry contract.  
**Approach:** reuse and generalize the algorithms already implemented in `src/analysis/hex-grid/detector.ts` and `src/analysis/regular-tiling/`. Preserve their original-raster, multi-region and global-fit behavior. **Do not replace the established detector simply to create a new algorithm.**

The existing detector already performs edge sampling, repeated line-family and periodicity analysis, rotation/spacing/phase fitting, alternative candidate evaluation and checks against distant parts of the source image. Its current limitation is the fixed triangle/square/hexagon geometry profiles and their hard-coded correspondence to D-symbols. Generalize the representation of a repeated geometric motif and reconstruct its combinatorial structure from evidence, while retaining the successful fitting/refinement operations and any proven general optimizations.

#### Implementation

- Remove the three-pattern catalog as the **authority for determining observed tiling identity**. Do not create a larger catalog of implementations or add a separate detector for every named pattern.
- Extend the existing evidence and fitting pipeline to handle a periodic **translation basis, multiple edge families and offsets, mixed polygons, distinct cells per repeat unit, and their boundary/incidence relationships**. Extend/refactor existing fitting kernels before introducing new ones; add only the topology-reconstruction stages the proven fitter cannot supply.
- Implement and test four ordered checkpoints: (1) detect a stable translation lattice using the existing global evidence techniques; (2) reconstruct a repeating geometric motif with edges and cells; (3) derive consistent chamber adjacency and a validated D-symbol under Phase 16 conventions; (4) verify the complete motif, including its geometry and identity hypotheses, against the unchanged source raster. Report failures instead of fabricating a result.
- Evaluate multiple complete hypotheses when the image is ambiguous. Distinguish gridless, inconclusive, supported-but-unreliable, structurally invalid and presently unsupported image geometry. No shape hint, expected D-symbol, selected ruleset or campaign metadata is permitted to influence the observed pattern's identity.
- Preserve **multi-region checks, continuous spacing and rotation fitting, global rigid-lattice residuals, candidate comparison and original-image verification**. All motif components must support the same jointly evaluated transformation. Avoid repeatedly adjusting locally corrected intermediate images; this causes cumulative drift.
- Bound raster preparation, hypothesis count, motif complexity, CPU/memory, worker queue, timeouts, cancellation and fallback behavior. Search must remain feasible without enumerating all periodic tilings.
- Publish a versioned observed-pattern result containing **derived** canonical D-symbol, measured motif/geometry, image registration, uncertainty, confidence and supporting evidence. Keep semantics, rulesets, accepted worlds, and persistence out of Surveyor.
- Remove the `expectedDsSymbol` API hint **only through a compatible rollout**. While the current Hex Crawl deployment relies on the existing API, keep it fully functional; provide an additive/versioned path for the generalized results, verify both client/service versions and retire obsolete selectors only after consumers switch safely.
- Preserve the existing three regular-grid regression cases as part of the new generalized detector's test corpus. An existing fast kernel can remain if it is a numerical optimization behind the generic reconstruction process, not an allowlist that decides whether an unfamiliar valid motif can be detected.

#### Additional Phase 16 Surveyor acceptance gates

- Publish a declared operating envelope for motif complexity, detectable line quality/noise, image size/transform and analysis-time limits **before** final validation.
- Verify regular hexagons, squares, triangles, rhombille and mixed-polygon motifs, including translated, rotated, scaled, cropped and noisy raster examples, distractor patterns and images with no detectable grid.
- Recover **multiple independently generated held-out unfamiliar periodic motifs**, including at least one mixed-cell pattern and one whose translation unit has inequivalent cells, with a correct derived D-symbol, incidence/adjacency and useful image alignment within independently established tolerances. A name/shape-registration change is not allowed to make these tests pass.
- Reject locally plausible but globally incorrect candidates using distant-region evidence. Evaluate ambiguity, false positives, residual drift, calibration, timeouts and resource limits against predeclared thresholds. Underconstrained images should return inconclusive rather than a confident wrong identity.
- Validate **end-to-end compatibility** of the existing authenticated hex grid detection and image-import workflow and the coordinated new contract. No production user-facing exposure of nonhex worlds is required yet, and no existing tester workflow may become nonfunctional.
- A completed research spike or an expanded fixed-shape catalog **does not satisfy** the Phase 16 Surveyor completion requirement. Any proven blocker must be reported with evidence and a proposed roadmap revision for explicit approval.

**Phase 17 implementation note (10 October 2026):** The proposed full-database version transition is procedure schema 1.3 and PostgreSQL schema 10. Legacy world records are converted in a single transactional migration while retaining original hex grid parameters as a verified compatibility projection; schema 10 and all procedure updates commit atomically. This is not an authorization to deploy or merge. See `docs/phase-17-world-authority-and-persistence.md` for recovery and release gates.

## Phase 17 — Generalize world/cell authority and persistence

**Owner:** Tile Crawl.  
**Depends on:** Phase 16 contracts.  
**Goal:** replace the hex-only world/spatial data model with cell- and tiling-based truth while preserving saved state.

### Implementation

- Replace the authoritative dependence on HexGridDefinition, HexId, and axial HexCoordinate with generalized TilingDefinition, CellId/CellAddress, and topology/realization abstractions. These are conceptual target names, not a requirement to mechanically rename every existing class.
- Address any cell by motif-cell index plus lattice translations, including tilings with multiple distinct cells per repeat. Ensure canonical/stable identifiers across pagination, reload, projection, and unchanged topology revisions. **Generate only the finite visible/requested region** of a theoretically unbounded periodic tiling; index/cache bounded subsets without eagerly enumerating the infinite world.
- Make polygon lookup, nearest/containing cell, neighbor enumeration, crossed boundary and feature intersection generic. Do not silently equate cell center distance with polygon edge length or world travel distance.
- Update source-map registration and world spatial semantics to reference world topology plus realization/transform independently. Avoid tying unrelated feature, terrain, or player knowledge state to the tiling symbol.
- Design and implement a **non-destructive, versioned, idempotent** persistence migration for current hex worlds and saved expeditions from the **actual deployed tester schema**, with backups and verified recovery before production cutover. Preserve world/grid/expedition stable IDs and establish a **reversible, documented mapping of existing axial cell coordinates to the generalized cell-address scheme**; preserve locations, route/selection state, source map transforms, relevant semantic features and ownership checks. Explicitly classify records that cannot be migrated automatically and defer them for safe remediation, never silent loss. Verify procedure/world/session references under concurrent access, interrupted migration, retry, old/new version overlap and rollback. Do not require a database reset as part of normal completion.
- Keep standalone and nonspatial procedures/sessions operational without a fabricated spatial topology. Ensure changing ruleset default tiling does not silently mutate pinned expedition procedure snapshots.
- Maintain optimistic concurrency, owners' authorization, and existing protections against geometry changes while dependent expeditions or spatial records exist. During the phased transition, **feature-gate creation/acceptance of nonhex worlds** until generalized traversal and rendering are operational; internal data-only fixtures may precede those capabilities, but production users must never be offered a nonfunctional tiling.

### Acceptance gate

- Existing **pre-upgrade tester** hex worlds/expeditions produce identical cell identity, neighbors, feature intersection and coordinate conversion after migration; stored data, maps/assets, procedure revisions, snapshots and history round-trip without loss and replay remains deterministic. The existing deployed hex and nonspatial workflows still function after the phase's migration and deployment.
- A previously unseen generated finite periodic motif can be instantiated, addressed, indexed and persisted **from structural and geometric input without an imported image**, without modifying application source or catalogs.
- Corrupt adjacency, nonreciprocal boundary, invalid coordinate basis, duplicate cell, or stale version is rejected safely.
- World-less sessions and nonspatial procedures remain unchanged.

## Phase 18 — Generalize traversal and the runtime

**Owner:** Tile Crawl.  
**Depends on:** Phase 17.  
**Goal:** make geometry-aware cell traversal independent of six axial directions without forking procedure behavior.

### Implementation

- Replace the runtime's current Neighbor(0–5) and hex-center-distance assumptions with a topology service that resolves traversable adjacency, edge identity, boundary crossing, actual intersection point, and the next cell.
- Represent travel intent independently of polygon side count. Support cells with a varying number of interfaces, subdivided boundaries, and direction/heading independent of edge numbering.
- Preserve continuous-distance and cell-step procedures as distinct policies. Budget distance using actual world geometry/route length, not a universal assumed center-to-center spacing. Specify what a "step" means for mixed-size cells.
- Handle boundary/corner crossings deterministically, including near-vertex ambiguity, re-entry, double-back, lost/veer, partial travel, and crossings involving multiple candidate neighbors. When a required geometric choice is ambiguous, request DM adjudication rather than silently selecting a neighbor.
- Keep existing runtime execution deterministic and based on materialized procedure snapshots, not catalog branding. No duplicate square, triangle, or rhombille runtime engines.
- Preserve effects/resources/environment/navigation/encounter/journey evaluation paths and current abstract/nonspatial session behavior.

### Acceptance gate

- Existing hex procedural replay and movement fixtures remain byte/semantically equivalent where the serialization contract permits. **Tester expeditions saved before this phase remain openable, movable, editable and resumable after deployment**, with no dependency on unfinished Phase 19 rendering.
- The *same* runtime tests drive ordinary and mixed-geometry tilings, including a generated previously unseen motif with unequal cell edges, boundary counts and route lengths.
- Movement cannot enter nonadjacent cells, cross a blocked interface, create phantom neighbors at a T-junction, or duplicate travel/discovery events.
- Explicit tests exercise lost course, deliberate double-back, veer, interrupted travel, fractional distance at a boundary, encounter pause and nonspatial journeys.

## Phase 19 — Generalize spatial presentation and map registration

**Owner:** Tile Crawl frontend, application and source-map modules.  
**Depends on:** Phases 17–18; consumes Phase 16 geometry contracts.  
**Goal:** display and manipulate actual cell geometry rather than rendered hexagons while retaining the current Guided/Compact UX.

### Implementation

- Replace authoritative hex corner/center drawing assumptions in world rendering, hit testing, selection, highlighting, projection, route previews and overlays with realized polygon and interface geometry. Support world setup and manual polygonal-grid use **without any source image** as well as Surveyor-driven import; the D-symbol and its chosen realization do not depend on image analysis.
- Reuse the existing topology-neutral spatial-adjacency component and make the current-cell navigator consume returned cell interfaces, including cells with different side counts.
- Keep map image alignment as a transform separate from world topology. Support independently measured pixel scale, rotation and motif phase; retain Wonderdraft physical-scale checks when applicable without treating them as a general tiling definition.
- Adapt source-map feature intersection, coverage bounds, semantic overlays, player/DM knowledge and discovery behavior to general cells. Never infer terrain, labels or activated structures from D-symbols.
- Implement and use the non-authoritative nomenclature catalog with common verified seed entries, and present tiling names, cell shapes, and boundary labels in ordinary language; no raw D-symbol or code-entry control in normal map interaction or Guided/Compact/Advanced editors. Provide keyboard/accessible alternatives to map-click selection. On smaller viewports, show only relevant operations; retain improved Phase 15.1.1 layout, progressive disclosure and procedural guidance.
- Update UI/domain variable names only when the generalized behavior is proven; avoid broad cosmetic renaming that obscures architecture defects.

### Acceptance gate

- The same UI and renderer can handle hexagonal, quadrilateral, triangular, rhombille and mixed-cell fixtures **including a no-image world**, without tiling-specific UI components. The seeded catalog resolves familiar names without changing the underlying structure; unlisted patterns with missing metric parameters receive a guided, understandable configuration path rather than a forced JSON edit.
- Polygon hit testing, boundary previews, zoom/pan, transformed source imagery and selected-cell actions agree; no map shift/drift from repeated local fitting.
- Mobile/tablet/desktop, embedded, light/dark, keyboard and pointer review covers no map, unrecognized map, geometry mismatch, current cell, travel, encounter and nonspatial states. Normal selectors, map dialogs, navigation, error recovery, and Advanced screens contain no raw D-symbols; the JSON editor is tested as the explicit code-editing exception.
- Existing hex and Wonderdraft alignment tests remain green, including interaction with previously saved tester maps and expeditions after deployment; no interim phase removes the working map UI.

## Phase 20 — Ruleset/map mismatch reconciliation

**Owner:** Tile Crawl; Surveyor only supplies observations.  
**Depends on:** Phases 16–19, including completed generalized Surveyor detection in Phase 16.  
**Goal:** resolve imported-map/ruleset mismatches directly where safe, without settings-menu detours or silent destructive changes.

### Implementation

- Retrieve the expected canonical D-symbol and any geometry restrictions from the **applicable selected materialized ruleset/procedure when one exists**, and the observed canonical D-symbol plus measured realization from Surveyor. World/source-map authoring may occur **before** expedition creation or procedure selection, so absence of a ruleset is a first-class state: compare the observed pattern with established world geometry, not an invented procedure requirement. Perform structural equivalence, metric compatibility, and authoritative-world contradiction checks inside Tile Crawl. Avoid false mismatches caused only by alternative legitimate symmetry representations, and do not treat equal structural identity as automatic proof that metric requirements match. Do not require Surveyor to know which ruleset is being used.
- Resolve human-readable names through Tile Crawl's **optional data-driven tiling nomenclature catalog** where the canonical identity and any required geometric qualifiers match. Otherwise derive descriptions from actual topology and measured geometry. Use "hexagons", "squares", "triangles", a familiar named tiling, or "a repeating mix of shapes" only when justified by evidence; a four-sided cell is not necessarily a square. No names or aliases participate in authoritative identity comparison. The absence of a familiar name must not send the user to the JSON editor or force them to handle a D-symbol.
- Add an authoritative server-side conflict assessment that distinguishes:
  - a fresh unused default topology (safe to replace);
  - existing expeditions and pinned procedure snapshots using old cell identities;
  - accepted incompatible source-map registrations;
  - geometry-dependent features/locations, route state and other stored spatial references;
  - unrelated nonspatial content that creates no contradiction.
- For a validated mismatch with **no blockers**, show a direct button such as **Use square cells for this world**. When a campaign-owned procedure is applicable, the command must create the correct new procedure revision (never mutate preset templates or historical pinned snapshots), update world topology/registration where legitimate, and persist the change transactionally with expected versions. **When no procedure is yet selected, update only the authoritative world/topology/registration data that actually exist; do not fabricate a procedure revision.** Preserve provenance and user ownership. At any later procedure selection, verify that its requirements remain compatible with the accepted world topology.
- If the world/ruleset edit cannot be completed atomically across required aggregates, provide an application-level transaction/recovery strategy *before* exposing the button. A failed or stale action must leave all authoritative state consistent.
- When contradictions exist, use plain English to say what shape the imported map has, what shape the existing world expects, and *which existing objects* prevent conversion. **Never expose raw D-symbols in a named-pattern contradiction or ordinary technical-details expansion.** Only an actual error involving a **supported but unnamed** pattern may include a labeled D-symbol identifier beneath a plain-English explanation; this error must not ask the user to manually configure the notation.
- Offer appropriate alternatives, such as keeping the current configuration and using another map, adjusting an unsaved draft, or starting a separate world. Never offer a one-click conversion if runtime/geometry realization is not actually implemented.
- Distinguish low-confidence, unsupported, gridless and technically failed analyses. They should offer manual placement/retry where safe, not a false ruleset switch.

### Acceptance gate

- Matching image/ruleset (including provably equivalent symmetry representations): proceed normally after metric checks. Inconclusive equivalence: request review, not a false mismatch. Genuine mismatch on unused default: show one-click option and persist correct new revision/world geometry without requiring settings navigation. **No procedure selected:** retain/use observed world topology without inventing a revision, then check future procedure selection for compatibility.
- Mismatch on established expedition: block with an explanation citing the expedition and preserved position. Map already accepted with incompatible registration: block or require an explicit safe migration.
- Nonspatial procedure does not become an artificial blocker. Stale world/procedure versions produce a safe conflict and reload; no half-updated rule/world state.
- All UI messages use ordinary shape vocabulary or a well-supported catalog name. Catalog misses and geometrically ambiguous names fall back to derived descriptions, and adding a catalog name changes no detection or traversal behavior. **Assert that the only UI surfaces containing raw D-symbols are the explicitly selected JSON editor and an error for a supported but unnamed pattern**; all other flows use ordinary descriptions, including Advanced editing, empty states, map mismatches, and routine successful detection. The special unnamed-pattern error explains the problem first and provides code only as a secondary labeled identifier. Accessibility and narrow-screen interaction are reviewed.
- The same reconciliation path handles a held-out unfamiliar polygon mix with no pattern-specific conditional branches.

## Phase 21 — Tile Crawl cutover, cleanup, and stabilization

**Owners:** Tile Crawl and Surveyor.  
**Depends on:** completed Phases 16–20 and their accepted migrations.  
**Goal:** make the generalized system the sole supported path and complete the product transition.

### Implementation

- Delete superseded hex-only *authoritative* APIs, selectors, adapters, schema fields, dedicated navigators and duplicated logic once no live consumer needs them. Retain hex-specific optimizations only behind general interfaces, with proofs of behavioral equivalence.
- Rename product-facing Hex Crawl to **Tile Crawl** across navigation, help, copy, capability documentation, and deployment surfaces. Plan repository/service/package renaming separately with deliberate redirects and infrastructure checks; do not break existing URLs, auth, assets, deploy secrets, CI or external integrations by mechanically renaming a repository.
- Complete all **non-destructive** schema/data migrations and confirm that pre-Phase-16 tester worlds, expeditions, procedure histories and external map assets survive. Verify backup/restore and deployment recovery strategy, live compatibility, and the exact removal conditions of any temporary transition adapter. Data loss is not part of ordinary cutover acceptance.
- Reaudit Surveyor API discovery and downstream consumers, the world/ruleset/source-map authority boundaries, and the absence of a hard-coded **pattern-implementation** catalog. Retain the separate, optional data-driven tiling-name catalog as presentation only.
- Reconcile all authoritative documentation and eliminate stale instructions suggesting a required expectedDsSymbol hint, a fixed six-neighbor runtime, or a three-entry catalog. Preserve completed Phase 15 design and architecture documents as history.
- Run full CI and manual visual/workflow review at exact commit heads for each repository. Test deployments together and perform live signed-in user flows against the deployed services before calling the project complete.
- Preserve the Phase 15/15.1.1 emphasis on understandable language, directly actionable error repair, progressive disclosure, and accessible focused workflows. Tiling generalization is not a reason to overwhelm the UI with advanced geometry controls. Audit all visible copy/controls so code appears only in the JSON editor or the specifically allowed supported-but-unnamed tiling error.

### Final acceptance gate — definition of Tile Crawl complete

- A new structurally valid supported Euclidean periodic tiling can be **constructed into an operational translation-periodic cell system**, given a valid or explicitly parameterized metric realization, and **loaded, stored, rendered, navigated and replayed without a source image or new pattern-registration/code path**. Importing a suitable image of such a tiling must also work when Surveyor provides sufficient evidence.
- Surveyor can infer the equivalent topology from sufficiently informative unfamiliar raster patterns without a tiling hint, and can explicitly decline when evidence or capability is insufficient.
- A ruleset mismatch is handled entirely in Tile Crawl, with safe one-click conversion only when there are no real authoritative contradictions.
- Existing hexagonal maps and campaigns, their travel procedures, saved expeditions, and nonspatial journeys survive the transition without regressions.
- Geometry, image alignment and semantics remain independent; no duplicate notation, magic hard-coded shape dispatch, or silent unsafe migration remains.
- Both repositories pass validation on the actual merged commits; coordinated deployment and relevant live UI verification succeed. The product is called **Tile Crawl**.

## Cross-cutting acceptance matrix

The following are **test categories**, not supported-pattern registrations:

| Category | Required cases |
| --- | --- |
| Topology | regular triangle, square, hexagon; rhombille; mixed motif; a held-out newly generated periodic tiling |
| Identity | equivalent chamber relabelings; multi-chamber D-symbols; alternative symmetry quotients; valid Euclidean symbols; non-Euclidean/axiom-invalid inputs; invalid metric witnesses; inconclusive equivalence |
| Periodicity | rotated/scaled/cropped/noisy motifs; distant regions; variable offsets; misleading local periodicity; independent holdout set and predeclared operating envelope |
| Geometry | unequal cell sizes, varying side/adjacency counts, T-junctions, corner/vertex ambiguity, reciprocal boundaries, nonoverlap |
| Movement | continuous distance, cell steps, partial progress, boundary exit, lost/veer, deliberate double-back, encounter pause and deterministic replay |
| Data | pre-Phase-16 tester database and map assets; saved hex worlds/expeditions, pinned revisions, feature overlays, stored history, asset registrations, uninterrupted/retried migrations, backup/restore rehearsals, concurrent writes and version conflicts |
| Sessions | world-bound with and without any imported image; mapless abstract, no-interval journey, nonspatial rulesets |
| Reconciliation | matched patterns, world imported before ruleset selection, later ruleset selection, fresh default mismatch, established-world contradiction, unsupported detection, low confidence, stale save, canceled action |
| Tiling terminology | known names and aliases; geometry-qualified names; alternate symmetry conventions; unknown mixed motifs; accurate plain-English fallback; raw D-symbol visible **only** in JSON editor or supported-but-unnamed error |
| UI | desktop/mobile/embedded, pointer/keyboard, light/dark, source image alignment, selection, error recovery, Guided/Compact/Advanced/JSON |

For any applicable visual and numerical acceptance check, specify expected geometry, coordinate space, error tolerance, and source-of-truth ownership in the phase test rather than merely asserting that the UI rendered.

## Development and merge protocol

1. Work from the latest main of the relevant repository. Create **one focused feature branch per phase**, or separate coordinated Surveyor/Tile Crawl branches when a phase spans services. Keep unrelated merged changes.
2. Before coding each phase, inventory affected APIs/types/schema and write the change/migration plan **against actual tester data and currently deployed versions**, including backup, recovery, version overlap and the effect of a failed upgrade. Treat both the phase's acceptance gate and the mandatory continuous-usability/data-preservation gate as the implementation contract. Update this roadmap's status only when evidence exists.
3. Preserve **all live tester workflows**, with existing hex functionality as an automated regression oracle until the general runtime proves parity. Any phase that changes an authority must still deploy as a complete working increment. Do **not** expand an interim catalog of known patterns as a shortcut toward the target.
4. Keep API contract changes coordinated. Prefer expand/migrate/cutover/contract in an explicitly versioned API and schema, with old/new conformance and rollback tests. Avoid depending on a live deployment containing half of an incompatible change. Maintain and test the old operational hex path until the new client/server/runtime path is verified; intermediate phases may merge behind **non-user-exposed capability gates**, never leaving testers with an unusable tool or advertising unsupported tilings.
5. Run complete appropriate suites (Surveyor parser/raster/worker/API/container; Tile Crawl domain/application/integration/PostgreSQL/client/embedded/container), **realistic pre-upgrade snapshot migration and recovery tests**, security/ownership/concurrency tests and representative rendered visual checks. Continue internal tester feedback and bounded user-facing stabilization **during every phase**; use post-deployment signed-in existing-data smoke checks rather than deferring all repairs until Phase 21.
6. Conduct a self-review for hidden hex assumptions, pattern-specific branching, incorrect identity claims, unstable IDs, geometry drift, performance blow-ups, regression in nonspatial flows and overly technical UI language.
7. Report exact repository heads, test failures, live compatibility and migration/backups/recovery evidence. **Do not merge without explicit authorization.** After authorization merge coordinated branches as close together as practical, verify both main heads, post-merge validation, compatible deployment and signed-in tester-data workflows. A pair of GitHub merges is coordinated but not mathematically atomic: define rollback/compatibility strategy before breaking changes. **Merge authorization never implies authorization for database reset or data deletion**.
8. No phase is complete while its acceptance gates are red, while deployed tester workflows are broken, while data migrations or recovery remain unverified, while user data can be silently corrupted, or while a claim of arbitrary-tiling support rests only on three familiar examples. A genuinely unavoidable destructive reset requires separate explicit owner approval and tester communication; it is never the default exit from a failed migration.

## Phase tracker

| Phase | Focus | State |
| --- | --- | --- |
| 16 | D-symbol, operational topology and Surveyor motif detection | Not started |
| 17 | World/cell model and persistence | Not started |
| 18 | Spatial traversal/runtime | Not started |
| 19 | Rendering, UI and map registration | Not started |
| 20 | Ruleset/map reconciliation and one-click change | Not started |
| 21 | Tile Crawl cutover and full stabilization | Not started |

### Reference architecture

- Tile Crawl (currently Hex Crawl): docs/architecture.md, docs/generic-procedure-architecture.md, docs/runtime-engine.md, docs/shared-map-processing.md, docs/postgresql-persistence.md.
- Completed UX/design evidence: docs/phase-15-design-architecture.md, docs/phase-15-implementation-notes.md, docs/phase-15.1-guided-experience.md, docs/phase-15.1.1-coverage.md.
- Surveyor: [architecture](https://github.com/LegendMaster03/dorks-and-dice-surveyor/blob/main/docs/architecture.md), [API](https://github.com/LegendMaster03/dorks-and-dice-surveyor/blob/main/docs/api.md).
- Previous roadmap: docs/generic-procedure-development-plan.md is intentionally deleted, not retained as an alternate source of requirements. Git history preserves it if historical investigation is needed.
