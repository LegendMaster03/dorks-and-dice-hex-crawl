# Phase 19 — independent WorkChat review handoff

**Repository:** `LegendMaster03/dorks-and-dice-hex-crawl`  
**Review branch:** `feature/tile-crawl-phase-19` (check the current exact head before tests)  
**Baseline main:** `134147fa42302f2fcd6bda76508bd6ad326ce1e5`  
**Surveyor:** Phase 16 v3 experimental motif investigation is consumed; no Surveyor source changes were made in this Phase 19 branch. Existing v2 hex detector remains required.  
**Decision:** Candidate for independent review; not authorization to merge, deploy or reset tester data.

## Purpose

Independently find correctness, data preservation, security, concurrency, and UX defects in Phase 19 generalized world authoring, polygon rendering, map registration, and ruleset compatibility. WorkChat should report reproducible issues with paths, requests/screenshots and test evidence. Fix review findings on this branch; do not merge or deploy without a separate instruction.

## Major changes to review

1. **Existing data / old hex:** persisted v8→9→10 data and current legacy hex/Wonderdraft maps/procedures/expeditions must still open, align, save, reload and move. No schema change or destructive migration is introduced by Phase 19.
2. **Image-free generalized worlds:** construct square, triangle, rhombille, mixed square/triangle, and subdivided non-edge-to-edge examples using the same authoritative geometry API. Verify bounded viewport, server containment (including ambiguous shared boundaries), selected-cell terrain, locations, routes, feature intersection, keyboard selection, image-free map, pan/zoom/reset, and geometry-neutral navigator.
3. **Expeditions:** create pinned editable cell-step procedures, start cell expeditions from a selected qualified address, cross actual reciprocal interfaces, pause for course and encounter choices, resume, reload, and verify knowledge, party, resources, environment, effects and survival. Ensure old hex and nonspatial/journey sessions are unaffected.
4. **Analysis vs authority:** Surveyor v3 returns independent experimental observations, never world truth. Test supported, matching, incompatible, ambiguous, unsupported, malformed and unavailable outcomes; legacy Surveyor v2 behavior; alternative symmetry quotient and unfamiliar observed motif. No raw D-symbol should surface in routine navigation, mismatch, map dialogs or Guided/Compact/Advanced screens (except the explicitly selected JSON editor and narrowly permitted unnamed-pattern error).
5. **Matching image registration:** on a generalized world with saved source raster, choose Check cell pattern. The server must independently test all motif vertices, primitive translation mapping and reciprocal atomic interfaces. Multiple symmetry-equivalent placements must be exposed for visual choice, not autoaccepted. Preview, accept, restart and verify source coverage; rejection/stale/foreign-user/altered-candidate requests must leave state unchanged. The optional fourth landmark in manual three-point placement must never change the transform fit; an attempted failed check disables that save.
6. **Mismatch / correction:** on a fresh uncalibrated generalized world with no spatial dependents, verify a mismatch produces a correction option *only* if replacement realization and pixel registration both pass. Confirm in one operation that new tiling id and revision, the chosen source alignment and world version persist with original image metadata/assets preserved. A second stale request must fail without partial updates. With an established expedition, annotation, feature, location, existing aligned map, physical scale or incompatible selected procedure, verify no direct correction is offered.
7. **Concurrency:** race starting a world-bound cell expedition against correction. One operation must be rejected cleanly; never persist an expedition pointing at the old tiling after a successful replacement. Inspect lock order and PostgreSQL transaction failure semantics.
8. **Metrics and names:** try nonuniform U/V period lengths and included angle, invalid/out-of-range/unsatisfiable combinations, and unfamiliar patterns. Confirm labels only when justified by geometric qualifiers, reliable fallback descriptions for unnamed mixes, and no shape-specific implementation dispatch.
9. **Responsive/accessibility:** browser test signed-in existing data on desktop, mobile/tablet, compact/guided, light/dark and embedded. Inspect source imagery/transforms/selected polygon overlays, relative interface labels, current cell, keyboard alternative, and world-editor overflow. Verify no full-canvas drift, stale async selection, lost route overlays or accidental exposure of player-hidden information.

## Automated evidence and required execution

Run the exact feature-branch head's normal GitHub Actions workflow (frontend unit/build/typecheck, .NET/domain/application, PostgreSQL-backed HTTP integration, container, persistence and isolated backup/restore smoke). Existing tests of interest: `Phase19WorldGeometryEndpointsTests`, `Phase19MixedWorldHttpTests`, `Phase19RasterRegistrationTests`, `Phase19UnusedWorldCorrectionTests`, `Phase19MotifCompatibilityTests`, Phase 18 cell decision/environment/knowledge tests, `phase19-generalized-geometry.test.mjs`, and `phase19-affine-holdout.test.mjs`.

**Deployment/data gate:** no database schema migration introduced here. Check the actual deployed schema and Surveyor capabilities before any staging/test deployment. Preserve PostgreSQL and independent source-map binary backups. Validate against *existing* signed-in tester worlds and their assets after a controlled restart; do not reset an existing database to make a test pass. CI success does not imply successful deployment.

## Deliberate safety limits to evaluate, not bypass

- A valid D-symbol alone never registers an image. Differing translation-cover sizes, inconclusive measured geometry and unprovable metric/phase relationships fall back to manual placement. The solver currently limits candidate enumeration and only auto-registers a one-to-one motif correspondence.
- Selected incompatible procedure revisions cannot be atomically rewritten in the existing correction operation. The button is disabled for that situation instead of altering historical revisions or leaving world/procedure half-updated. Review whether the remaining UX is sufficient for Phase 19 acceptance; call out any required follow-up explicitly.
- The correction preview displays the **old** cells until the replacement commits, and labels that limitation. Verify the choice is understandable and does not claim to preview new authoritative polygons before they exist.
- A single successful unused-world correction has no bespoke one-click undo; the operation is atomic and limited to worlds without spatial dependents. Verify this limitation is acceptable and that rollback/backup guidance is clear.
- This handoff does not attest to live browser/deployment testing. WorkChat must perform it and report results before this phase can be accepted.

## Review output

Return a pass/fail verdict with exact SHA, CI URL, tests performed, signed-in screenshots/observations, security and concurrency analysis, source-image registration correctness, UX/accessibility findings, data preservation and deployment/recovery readiness. List defects by severity, provide concrete reproduction, and keep the phase unmerged until the owner approves.
