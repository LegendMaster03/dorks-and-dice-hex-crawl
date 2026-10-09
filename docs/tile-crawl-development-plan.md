# Tile Crawl transition — development plan

**Status:** proposed; implementation has not started  
**Roadmap:** Phases 16–22 (the successor to the completed Hex Crawl / generic-procedure roadmap)  
**Primary repository:** LegendMaster03/dorks-and-dice-hex-crawl  
**Analysis-service repository:** LegendMaster03/dorks-and-dice-surveyor  
**Planning baseline:** Hex Crawl main at 70629398cf1b30c43addef5299ce1ad23efbfd7a; Surveyor main at e9ad1cc9f04ce375fa1baf241894357abc116e09 (9 October 2026)

## Objective

Complete the product and architectural transition from **Hex Crawl** to **Tile Crawl**. The existing expedition and ruleset engine becomes independent of hexagonal geometry. A world can use a periodic tiling composed of any supported collection of cell shapes and adjacencies, without installing a new named tiling or hard-coding a new pattern-specific runtime. Surveyor independently observes image structure; Tile Crawl interprets that observation against its own world and ruleset.

This is **one coordinated project with separately validated and merged phases**, not one enormous development cycle. The old generic-procedure development plan is superseded and removed. Existing architecture, proof, and completed Phase 15 design documents remain historical/architectural references, not competing roadmaps.

**Completion is not the ability to select hexagons, squares, or triangles from a list.** Completion is the ability to ingest a structurally valid supported periodic tiling and its suitable geometric realization, address and render its cells, cross their actual boundaries, and run the relevant generic procedures **without adding pattern-specific source code**. Surveyor must additionally discover the structure of supported unfamiliar periodic tilings from sufficient image evidence without being told which pattern to find.

## Architectural decisions — authoritative for this plan

1. **Delaney–Dress is the only periodic-tiling notation.** Neither Cundy–Rollett nor GomJau–Hogg returns as a persisted, public, or parallel notation. Standard D-symbols are not a custom Tile Crawl syntax; avoid Tegula-specific wire formats and importing restrictive third-party implementations.
2. **No hard-coded catalog of supported pattern implementations or per-pattern engine dispatch.** Example tilings are test fixtures, not the set of patterns the system can recognize or traverse. A **data-only catalog of familiar tiling names, aliases, and descriptions is encouraged** as a nonauthoritative presentation aid; its entries must never determine recognition, validity, realizability, or support. General-purpose image-analysis operations, mathematical constraints, and optimized numerical kernels are allowed; an unfamiliar valid tiling must not require a named shape-specific code path.
3. **Surveyor observes; Tile Crawl decides.** Surveyor receives an image and analysis options, without an expected D-symbol or information about the selected ruleset. It derives and reports a supported detected D-symbol, measured geometry, evidence, and uncertainty. Tile Crawl compares that observation with its ruleset and world state locally.
4. **A D-symbol is structural, not a complete geometric map.** It encodes incidence/symmetry information under an explicitly chosen group and convention. It does not by itself specify the chosen translational cover, an integer-addressed fundamental motif, or a unique straight-edge metric embedding. Exact polygon coordinates, chosen geometric embedding, metric scale, distortions, image alignment, and registration are separately represented *measured or realized geometry*, not alternative tiling notations or attributes stuffed into the D-symbol.
5. **World topology, ruleset requirements, and source images are distinct authorities.** A procedure/ruleset expresses the expected tiling. The world owns its accepted spatial topology and stable cells. The source map holds its image and alignment. Surveyor cannot silently change any of them.
6. **Map semantics remain separate.** Terrain, biome, labels, regions, roads, rivers, structures, locations, visibility, encounters, and gameplay state must not become part of a tiling identity or geometric motif.
7. **Existing runtime behavior is a regression contract.** Preserve movement budgeting, navigation/lost/veer, partial traversal, double-back, discoveries, encounters, environment, effects, survival, and the current user-oriented Guided/Compact UI. Nonspatial and no-interval procedures remain first-class.
8. **Incompatible data is never silently rewritten.** Existing world/expedition geometry, procedure revisions, anchored content, and tester data require explicit consistency checks and an intentional migration. No auto-reset and no silent conversion from an established hex world into a different tiling.
9. **No invented certainty.** A parseable string, a mathematically valid D-symbol, a theorem-backed Euclidean periodic tiling, a *particular admissible metric realization*, and a tiling actually observed in an image are different claims. The standard two-dimensional zero-curvature criterion applies to fully valid D-symbols; it does **not** certify that arbitrary chosen polygon coordinates or image evidence match. APIs, validators, and UI must distinguish these claims.
10. **Shared contract, independent services.** The two repositories must pass the same D-symbol and geometry conformance fixtures, but Tile Crawl may not call Surveyor simply to compare a detected pattern with the selected ruleset.
11. **The tiling code is not ordinary user-facing language.** Normal users interact with named tilings and understandable shape descriptions, not D-symbols. **Raw Delaney–Dress notation may be exposed only in the explicit JSON editor, or in an error that identifies a *supported* tiling for which Tile Crawl cannot resolve a reliable human-readable name.** No ordinary selector, Guided/Compact/Advanced authoring screen, map-alignment prompt, navigation display, help card, success notification, or named-pattern contradiction message may require users to read, type, paste, or understand the notation. Developer logs and machine-readable API/persistence formats remain unaffected by this presentation restriction.

## Scope and definitions

A **pattern** is the combinatorial periodic tiling; its authoritative identity is a canonical standard D-symbol. A **geometric realization** supplies cell polygons, edges, periodic translations, and coordinates for that pattern. A **fit** registers that realization against a particular source image, with confidence and residual evidence. A **cell address** identifies a specific cell independently of its polygon side count. A **traversal interface** is an actual shared boundary (which may comprise multiple segments) from one cell to another, not a number from 0 through 5.

The initial mathematically explicit target is **two-dimensional Euclidean periodic polygonal tilings with a finite repeating fundamental domain**, including mixed polygon types. The design must account for subdivided edges and more than one cell orbit. Treat non-edge-to-edge incidences explicitly; do not silently force T-junctions into one-edge/one-neighbor assumptions. The plan does not claim that every non-polygonal, fractal, aperiodic, curved-surface, or arbitrarily illustrated pattern is automatically reconstructible. When a tiling class is unsupported or the evidence insufficient, return a specific unsupported/inconclusive state rather than a fabricated identity.

A standard D-symbol may not uniquely determine metric geometry; different choices of symmetry group can also alter the symbol used to describe a visually similar arrangement. Phase 16 must establish and test **one documented identity/symmetry convention** and distinguish chamber-relabeling isomorphism, equivalence under alternative group presentations, and metric-shape compatibility. Two strings can describe the same underlying periodic cell structure under different valid group choices, while identical combinatorics can have metrically incompatible realizations. Do not assume raw D-symbol string equality alone establishes all ruleset/world compatibility, and do not equate visually similar polygons without checking their incidence. Test independently derived examples instead of assuming the current three one-chamber symbols prove general correctness.

### Human-readable tiling nomenclature catalog

Tile Crawl should maintain an **optional, editable/versionable data catalog** for recognizable tiling names, not a catalog of implemented shapes. It belongs in Tile Crawl's presentation/reference layer, not in Surveyor's detector dispatch.

- **Lookup:** use a canonical D-symbol under the documented symmetry convention as the initial lookup key; where a familiar name requires additional geometric evidence (for example, square versus general quadrilateral, or rhombus versus general parallelogram), attach and validate explicit geometric qualifiers before using that name. Do not assume an arbitrary abstract D-symbol uniquely fixes metric shape or everyday nomenclature.
- **Entry:** preferred everyday name, optional established mathematical name, aliases/search terms, concise explanation, possible localization identifier, source/provenance, and optional geometric qualifiers. Do not store terrain, encounters, map annotations, or gameplay settings in this catalog.
- **Many-to-one and one-to-many:** multiple aliases may name the same structure, and a visually familiar family may have variant symbol representations depending on symmetry convention. Treat ambiguous matches explicitly; never use display-name equality to establish ruleset compatibility.
- **Fallback:** if no catalog entry applies, derive accurate plain-language descriptions from the observed/realized cell geometry and topology (side counts, repeated shape mixes, and actual angles/lengths where known), such as "a repeating pattern of triangles and four-sided cells." A four-sided polygon is not necessarily a square. Use that description in all ordinary, successful, or routine mismatch interfaces; lack of a catalog name does not make a valid tiling unsupported.
- **Only permitted user-facing notation surfaces:** the explicitly selected JSON editor, which represents raw authoritative procedure data, and a genuine *error* describing a tiling that the system supports but cannot resolve to a reliable human-readable name. In that narrowly scoped error, explain in plain English first; a labeled D-symbol may appear as a secondary identifier for reporting/identifying the otherwise unnamed pattern. Do not use the exception for regular unnamed-pattern confirmations, selections, comparisons, authoring, or successful analysis results. Unrecognized/invalid/unsupported patterns do not get raw code merely to fill a missing message.
- **Responsibility:** catalog names are for readable explanations, search, ruleset browsing, and mismatch dialogs only. The canonical D-symbol and authoritative topology determine structure and compatibility. An unlisted tiling remains fully usable. The UI must not require code entry as a workaround for missing names outside the JSON editor.
- **Validation:** allow new catalog data entries without changing algorithms; verify aliases, ambiguous identities, geometric qualifiers, and localization-safe fallback wording. A missing or invalid catalog entry must never corrupt a world or prevent traversal.

### Target system flow

1. User selects a ruleset/procedure and imports an image into Tile Crawl.
2. Tile Crawl sends **only raster data and detector options** to Surveyor. Remove the currently optional expectedDsSymbol request parameter; no replacement shape/category/side-count selector.
3. Surveyor searches for periodic structure, derives a validated observed D-symbol and geometric realization/fit when supported, or reports uncertainty with grounded diagnostics.
4. Tile Crawl locally compares the observed identity and **documented structural-equivalence/metric constraints** with the selected procedure/ruleset's expected tiling and the world's established topology. No Surveyor round-trip is needed to do this comparison.
5. If patterns agree and geometry is compatible, provide the normal map alignment workflow.
6. If they differ, the server evaluates authoritative contradictions. With none, offer a **one-click change of the selected campaign-owned procedure/ruleset and applicable world geometry**. With blockers, explain them concretely in ordinary shape language; do not modify data.
7. Recheck versions and compatibility at commit time. An accepted change updates its legitimate authorities atomically and records the resulting procedure revision/provenance. A canceled, stale, unsupported, or failed action makes no changes.

**Important:** All freshly created worlds currently have a default hex grid. A mere unused default is **not** a contradiction. An expedition using hex addresses, grid-dependent feature anchoring, previously accepted incompatible map registrations, or immutable active procedure snapshots may be. Determine the actual dependency, not merely the presence of a grid object.

## Phase 16 — D-symbol and operational topology contracts

**Owners:** Tile Crawl domain and Surveyor periodic-tiling resource.  
**Prerequisite:** merged Surveyor v2 / procedure schema 1.2 baseline.  
**Goal:** establish one mathematically sound, versionable cross-service contract before changing production spatial authority.

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

### Acceptance gate

- Independent D-symbol conformance corpus includes equivalent chamber relabelings, **axiom-invalid inputs that happen to give an apparent zero-curvature sum**, valid zero-curvature Euclidean symbols, invalid or self-intersecting *metric witnesses*, nontrivial multi-chamber mixed motifs, and alternative realizations/group descriptions for the same underlying periodic structure. No test may presume a fully valid zero-curvature 2D D-symbol is topologically nonrealizable.
- The fundamental-domain model can represent triangle, quadrilateral, hexagon, rhombille, and mixed-polygon fixtures **through input data, with no required registration or pattern-specific source paths**. Optional nomenclature entries do not affect whether these fixtures execute.
- Deriving the translation cover and finite motif from nontrivial D-symbols produces reciprocal adjacency and stable translation-relative cell addresses, round-tripping deterministically with and without source imagery. If the translation cover cannot be constructed within supported limits, Phase 16 cannot be accepted. No terrain, labels, or ruleset semantics appear in the structural contract.
- All existing Surveyor and Tile Crawl tests pass; contract changes are versioned and documented before either service is deployed.

## Phase 17 — Generalize world/cell authority and persistence

**Owner:** Tile Crawl.  
**Depends on:** Phase 16 contracts.  
**Goal:** replace the hex-only world/spatial data model with cell- and tiling-based truth while preserving saved state.

### Implementation

- Replace the authoritative dependence on HexGridDefinition, HexId, and axial HexCoordinate with generalized TilingDefinition, CellId/CellAddress, and topology/realization abstractions. These are conceptual target names, not a requirement to mechanically rename every existing class.
- Address any cell by motif-cell index plus lattice translations, including tilings with multiple distinct cells per repeat. Ensure canonical/stable identifiers across pagination, reload, projection, and unchanged topology revisions.
- Make polygon lookup, nearest/containing cell, neighbor enumeration, crossed boundary and feature intersection generic. Do not silently equate cell center distance with polygon edge length or world travel distance.
- Update source-map registration and world spatial semantics to reference world topology plus realization/transform independently. Avoid tying unrelated feature, terrain, or player knowledge state to the tiling symbol.
- Design and implement a tested persistence migration for current hex worlds and already saved expeditions. Preserve stable IDs, locations, route/selection state, source map transforms, relevant semantic features and ownership checks wherever possible. Explicitly classify records that cannot be migrated automatically. No blanket database reset.
- Keep standalone and nonspatial procedures/sessions operational without a fabricated spatial topology. Ensure changing ruleset default tiling does not silently mutate pinned expedition procedure snapshots.
- Maintain optimistic concurrency, owners' authorization, and existing protections against geometry changes while dependent expeditions or spatial records exist. During the phased transition, **feature-gate creation/acceptance of nonhex worlds** until generalized traversal and rendering are operational; internal data-only fixtures may precede those capabilities, but production users must never be offered a nonfunctional tiling.

### Acceptance gate

- Existing hex worlds/expeditions produce identical cell identity, neighbors, feature intersection and coordinate conversion after migration; stored data round-trips and replay remains deterministic.
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

- Existing hex procedural replay and movement fixtures remain byte/semantically equivalent where the serialization contract permits.
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
- Present tiling names, cell shapes, and boundary labels in ordinary language; no raw D-symbol or code-entry control in normal map interaction or Guided/Compact/Advanced editors. Provide keyboard/accessible alternatives to map-click selection. On smaller viewports, show only relevant operations; retain improved Phase 15.1.1 layout, progressive disclosure and procedural guidance.
- Update UI/domain variable names only when the generalized behavior is proven; avoid broad cosmetic renaming that obscures architecture defects.

### Acceptance gate

- The same UI and renderer can handle hexagonal, quadrilateral, triangular, rhombille and mixed-cell fixtures **including a no-image world**, without tiling-specific UI components. Unlisted patterns with missing metric parameters receive a guided, understandable configuration path rather than a forced JSON edit.
- Polygon hit testing, boundary previews, zoom/pan, transformed source imagery and selected-cell actions agree; no map shift/drift from repeated local fitting.
- Mobile/tablet/desktop, embedded, light/dark, keyboard and pointer review covers no map, unrecognized map, geometry mismatch, current cell, travel, encounter and nonspatial states. Normal selectors, map dialogs, navigation, error recovery, and Advanced screens contain no raw D-symbols; the JSON editor is tested as the explicit code-editing exception.
- Existing hex and Wonderdraft alignment tests remain green.

## Phase 20 — Surveyor general periodic-motif discovery

**Owner:** Surveyor.  
**Depends on:** Phase 16 contracts; can proceed in parallel with Tile Crawl Phases 17–19.  
**Goal:** discover unknown supported periodic structures from raster evidence, instead of selecting a known tiling and running its detector.

### Implementation

- Remove the public expectedDsSymbol request hint and the fixed triangular/square/hexagonal tiling catalog as an authority for discovery. Do not replace them with a bigger registry of named patterns.
- Generalize reusable edge/line detection, orientation analysis, autocorrelation, translation-basis search, repeating offset/motif extraction, vertex and edge junction reconstruction, polygon/cell incidence and symmetry reduction.
- Infer a candidate fundamental-domain topology **from the image**, derive and validate its D-symbol and geometric realization, and then verify the *entire* inferred periodic motif against the original raster.
- Search/verify multiple hypotheses where evidence is ambiguous. Separate insufficient evidence, supported-but-inconclusive, structurally invalid, and presently unsupported image geometry; never manufacture a symbol to satisfy a hint.
- Preserve the proven detector's multi-shape, multi-region, continuous-spacing, and distant/global-rigid-fit checks. All candidate components share one jointly evaluated fit. **Do not iteratively refine transformed output by repeated local corrections**, which previously caused geometric drift.
- Bound image preparation, worker CPU/memory, candidate complexity, motif size, search horizon, timeout and cancellation. Prevent the unknown-pattern search from becoming an unbounded exhaustive tiling enumerator.
- Publish a versioned observed-pattern response containing D-symbol, measured realization/motif, image transform, uncertainty and evidence. Keep all terrain, labels, icons, game semantics and accepted world state out of Surveyor.
- Retain proven regular-pattern detection as optional *generic numerical optimization kernels* only; no catalog-based pattern identity or shape-specific gate in the final public algorithm. Add cross-version adapter tests during the coordinated rollout, then delete obsolete public selectors/contracts.

### Acceptance gate

- Synthetic and representative authored raster fixtures include regular hex/square/triangle, rhombille, mixed polygon motifs, variable offsets, transformed/noisy/cropped maps, distractors and gridless imagery.
- At least one **held-out unfamiliar periodic motif**, generated after the implementation without registering its name or code path, is recovered with correct D-symbol, topology, and useful global alignment within measured tolerances.
- Incorrect but locally plausible candidates are rejected by distant-region evidence. False identification, ambiguity, confidence calibration, elapsed time and resource use are measured, with stated thresholds and regression cases.
- Outputs that cannot be reconstructed reliably are explicitly inconclusive/unsupported, not spuriously "detected".
- Tile Crawl performs the expected-pattern comparison locally; Surveyor receives no expected shape, D-symbol, ruleset or campaign data.

## Phase 21 — Ruleset/map mismatch reconciliation

**Owner:** Tile Crawl; Surveyor only supplies observations.  
**Depends on:** Phases 17–20.  
**Goal:** resolve imported-map/ruleset mismatches directly where safe, without settings-menu detours or silent destructive changes.

### Implementation

- Retrieve the expected canonical D-symbol and any geometry restrictions from the **selected materialized ruleset/procedure**, and the observed canonical D-symbol plus measured realization from Surveyor. Perform structural equivalence, metric compatibility, and authoritative-world contradiction checks inside Tile Crawl. Avoid false mismatches caused only by alternative legitimate symmetry representations, and do not treat equal structural identity as automatic proof that the metric requirements match. Do not require Surveyor to know which ruleset is being used.
- Resolve human-readable names through Tile Crawl's **optional data-driven tiling nomenclature catalog** where the canonical identity and any required geometric qualifiers match. Otherwise derive descriptions from actual topology and measured geometry. Use "hexagons", "squares", "triangles", a familiar named tiling, or "a repeating mix of shapes" only when justified by evidence; a four-sided cell is not necessarily a square. No names or aliases participate in authoritative identity comparison. The absence of a familiar name must not send the user to the JSON editor or force them to handle a D-symbol.
- Add an authoritative server-side conflict assessment that distinguishes:
  - a fresh unused default topology (safe to replace);
  - existing expeditions and pinned procedure snapshots using old cell identities;
  - accepted incompatible source-map registrations;
  - geometry-dependent features/locations, route state and other stored spatial references;
  - unrelated nonspatial content that creates no contradiction.
- For a validated mismatch with **no blockers**, show a direct button such as **Use square cells for this world**. The command must create the correct campaign-owned procedure revision (never mutate preset templates or historical pinned snapshots), update world topology/registration where legitimate, and persist the change transactionally with expected versions. Preserve provenance and user ownership.
- If the world/ruleset edit cannot be completed atomically across required aggregates, provide an application-level transaction/recovery strategy *before* exposing the button. A failed or stale action must leave all authoritative state consistent.
- When contradictions exist, use plain English to say what shape the imported map has, what shape the existing world expects, and *which existing objects* prevent conversion. **Never expose raw D-symbols in a named-pattern contradiction or ordinary technical-details expansion.** Only an actual error involving a **supported but unnamed** pattern may include a labeled D-symbol identifier beneath a plain-English explanation; this error must not ask the user to manually configure the notation.
- Offer appropriate alternatives, such as keeping the current configuration and using another map, adjusting an unsaved draft, or starting a separate world. Never offer a one-click conversion if runtime/geometry realization is not actually implemented.
- Distinguish low-confidence, unsupported, gridless and technically failed analyses. They should offer manual placement/retry where safe, not a false ruleset switch.

### Acceptance gate

- Matching image/ruleset (including provably equivalent symmetry representations): proceed normally after metric checks. Inconclusive equivalence: request review, not a false mismatch. Genuine mismatch on unused default: show one-click option and persist correct new revision/world geometry without requiring settings navigation.
- Mismatch on established expedition: block with an explanation citing the expedition and preserved position. Map already accepted with incompatible registration: block or require an explicit safe migration.
- Nonspatial procedure does not become an artificial blocker. Stale world/procedure versions produce a safe conflict and reload; no half-updated rule/world state.
- All UI messages use ordinary shape vocabulary or a well-supported catalog name. Catalog misses and geometrically ambiguous names fall back to derived descriptions, and adding a catalog name changes no detection or traversal behavior. **Assert that the only UI surfaces containing raw D-symbols are the explicitly selected JSON editor and an error for a supported but unnamed pattern**; all other flows use ordinary descriptions, including Advanced editing, empty states, map mismatches, and routine successful detection. The special unnamed-pattern error explains the problem first and provides code only as a secondary labeled identifier. Accessibility and narrow-screen interaction are reviewed.
- The same reconciliation path handles a held-out unfamiliar polygon mix with no pattern-specific conditional branches.

## Phase 22 — Tile Crawl cutover, cleanup, and stabilization

**Owners:** Tile Crawl and Surveyor.  
**Depends on:** completed Phases 16–21 and their accepted migrations.  
**Goal:** make the generalized system the sole supported path and complete the product transition.

### Implementation

- Delete superseded hex-only *authoritative* APIs, selectors, adapters, schema fields, dedicated navigators and duplicated logic once no live consumer needs them. Retain hex-specific optimizations only behind general interfaces, with proofs of behavioral equivalence.
- Rename product-facing Hex Crawl to **Tile Crawl** across navigation, help, copy, capability documentation, and deployment surfaces. Plan repository/service/package renaming separately with deliberate redirects and infrastructure checks; do not break existing URLs, auth, assets, deploy secrets, CI or external integrations by mechanically renaming a repository.
- Complete schema/data migrations and confirm old persisted data survives; verify backup/rollback strategy and the exact removal conditions of any temporary transition adapter.
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
| Periodicity | rotated/scaled/cropped/noisy motifs; distant regions; variable offsets; misleading local periodicity |
| Geometry | unequal cell sizes, varying side/adjacency counts, T-junctions, corner/vertex ambiguity, reciprocal boundaries, nonoverlap |
| Movement | continuous distance, cell steps, partial progress, boundary exit, lost/veer, deliberate double-back, encounter pause and deterministic replay |
| Data | pre-migration hex worlds, persisted expedition positions, pinned procedure revisions, feature overlays, asset registrations, concurrency conflicts |
| Sessions | world-bound with and without any imported image; mapless abstract, no-interval journey, nonspatial rulesets |
| Reconciliation | matched patterns, fresh default mismatch, established-world contradiction, unsupported detection, low confidence, stale save, canceled action |
| Tiling terminology | known names and aliases; geometry-qualified names; alternate symmetry conventions; unknown mixed motifs; accurate plain-English fallback; raw D-symbol visible **only** in JSON editor or supported-but-unnamed error |
| UI | desktop/mobile/embedded, pointer/keyboard, light/dark, source image alignment, selection, error recovery, Guided/Compact/Advanced/JSON |

For any applicable visual and numerical acceptance check, specify expected geometry, coordinate space, error tolerance, and source-of-truth ownership in the phase test rather than merely asserting that the UI rendered.

## Development and merge protocol

1. Work from the latest main of the relevant repository. Create **one focused feature branch per phase**, or separate coordinated Surveyor/Tile Crawl branches when a phase spans services. Keep unrelated merged changes.
2. Before coding each phase, inventory affected APIs/types/schema and write the change/migration plan. Treat the phase's acceptance gate as the implementation contract. Update this roadmap's status only when evidence exists.
3. Preserve the existing default hex functionality as an automated regression oracle until the general runtime proves parity. Do **not** expand an interim catalog of known patterns as a shortcut toward the target.
4. Keep API contract changes coordinated. Prefer add-and-cutover in an explicitly versioned API with conformance tests; avoid depending on a production deployment containing half of an incompatible change.
5. Run complete appropriate suites (Surveyor parser/raster/worker/API/container; Tile Crawl domain/application/integration/PostgreSQL/client/embedded/container), migration round trips, security/ownership/concurrency tests and representative rendered visual checks.
6. Conduct a self-review for hidden hex assumptions, pattern-specific branching, incorrect identity claims, unstable IDs, geometry drift, performance blow-ups, regression in nonspatial flows and overly technical UI language.
7. Report exact repository heads, test failures and caveats. **Do not merge without explicit authorization.** After authorization merge coordinated branches as close together as practical, verify both main heads, post-merge validation and deployment. A pair of GitHub merges is coordinated but not mathematically atomic: define rollback/compatibility strategy before breaking changes.
8. No phase is complete while its authoritative acceptance gate is red, while user data can be silently corrupted, or while a claim of arbitrary-tiling support rests only on three familiar examples.

## Phase tracker

| Phase | Focus | State |
| --- | --- | --- |
| 16 | D-symbol and topology contracts | Not started |
| 17 | World/cell model and persistence | Not started |
| 18 | Spatial traversal/runtime | Not started |
| 19 | Rendering, UI and map registration | Not started |
| 20 | General image-pattern discovery in Surveyor | Not started |
| 21 | Ruleset/map reconciliation and one-click change | Not started |
| 22 | Tile Crawl cutover and full stabilization | Not started |

### Reference architecture

- Tile Crawl (currently Hex Crawl): docs/architecture.md, docs/generic-procedure-architecture.md, docs/runtime-engine.md, docs/shared-map-processing.md, docs/postgresql-persistence.md.
- Completed UX/design evidence: docs/phase-15-design-architecture.md, docs/phase-15-implementation-notes.md, docs/phase-15.1-guided-experience.md, docs/phase-15.1.1-coverage.md.
- Surveyor: [architecture](https://github.com/LegendMaster03/dorks-and-dice-surveyor/blob/main/docs/architecture.md), [API](https://github.com/LegendMaster03/dorks-and-dice-surveyor/blob/main/docs/api.md).
- Previous roadmap: docs/generic-procedure-development-plan.md is intentionally deleted, not retained as an alternate source of requirements. Git history preserves it if historical investigation is needed.
