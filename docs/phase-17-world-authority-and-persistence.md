# Phase 17 — world authority and persistence contract

**Status:** implementation on `feature/tile-crawl-phase-17`; release acceptance requires CI, an isolated pre-upgrade PostgreSQL rehearsal, recovery verification, and independent WorkChat review. No shared tester data has been migrated by this branch.

## Authority and identity

- **Ruleset/procedure**: immutable, revision-pinned `tilingDsSymbol` declares the structural requirement. It does not specify physical world geometry. This field is neither replaced nor rewritten.
- **World**: either a validated `PeriodicWorldTiling` (topology witness, metric witness, units, translation basis, pose, stable ID and revision) or a pre-existing `HexGridDefinition` that is projected through `LegacyHexTilingCompatibility`. There is no extra pattern implementation catalog, image prerequisite, or Surveyor write path.
- **Source map**: separately stores asset key, affine/projective registration and coverage. Raster observations cannot modify world topology/geometry.
- **Semantic layers**: features, locations, source maps, environment annotations and player knowledge retain their own identities and coordinate/ownership semantics; polygon intersections use actual boundaries.
- **Cell identity**: `WorldCellId(tilingId, PeriodicCellAddress(motifCellId, LatticeDisplacement(U,V)))`. The motif ID and translation basis are part of the address convention. Topology or translation-basis changes must never reuse the same identity/revision while silently reinterpreting existing cell addresses.

World-authored generalized geometry must pass `PeriodicTopologyWitness.ValidateAdjacency()` and `PeriodicMetricWitnessValidator.Validate()` before persistence. Structural validation proves the claimed translation-cover incidence and primitive lattice; metric validation proves concrete polygon embedding and constraints within bounded limits. The D-symbol alone cannot substitute for the metric witness.

`PeriodicWorldTiling` exposes `Resolve`, `Boundaries`, `Reciprocal`, `Neighbors`, `Containing`, `Nearby`, and `Intersecting`. Lookup handles polygon-boundary ambiguity explicitly; region enumeration is inverse-lattice bounded with a configurable hard ceiling of 100,000 candidate cells. Interfaces are atomic; multiple interfaces may refer to one geometric polygon side, so movement must not assume whole unsplit sides. Enumeration does not materialize an infinite plane.

## Existing-hex identity and compatibility

Existing `HexGridDefinition.Id` and `OverworldDefinition.Id` are retained. A legacy axial `HexCoordinate(Q,R)` corresponds bijectively to `PeriodicCellAddress("hex", (Q,R))`. The old `HexId` maps to `WorldCellId` with the **same grid UUID**. The translation basis, polygon corners, physical units, rotation and origin are derived from the actual persisted grid by `HexGeometry`, not generated anew from a superficially equivalent canonical D-symbol. This preserves pointy/flat orientation, scale, world pose and physical cell positions. Legacy `HexTraversalState`, directional intent, entry/last direction, progress, pending encounters, events, player knowledge and procedure snapshots remain in their existing representations until Phase 18 provides equivalent generalized movement.

`OverworldDefinition.Grid` is a temporary compatibility accessor for hex-only consumers. New generalized worlds have `Tiling` and no Grid. Attempting to use the hex runtime for generalized geometry is blocked; Phase 18 must remove this dependency after migration and replay parity tests. There is no change to ordinary frontend world creation.

## Persistence inventory and representation

Relational schema stays at **9**: no new columns, index changes, or destructive SQL migrations are required for the additive world JSON format. The existing transaction advisory lock and optimistic aggregate version checks remain unchanged.

| Existing owner/field | Phase 17 behavior |
| --- | --- |
| `overworlds.id/owner_user_id/version/timestamps` | Unchanged; owner isolation and optimistic concurrency retained |
| `world_json.id/name/grid` | Legacy **format 1** read unchanged; old grid ID/pose remain authoritative historical input |
| `world_json.formatVersion/tiling` | **Format 2** carries validated generalized tiling; format 2 requires tiling and no legacy Grid |
| `world_json.features/locations/sourceMaps` | Unchanged, including all identities, geometry, transforms, raster asset keys |
| `world_json.environmentAnnotations` | Existing world/hex/feature scopes preserved; additive qualified cell scope |
| `expeditions.context_json/state_json` | Unchanged, including current hex, directional state and partial progress |
| `expeditions.knowledge_json/party_json/environment_json/effects_json/resources_json/survival_json/journey_state_json/generated_resolutions_json` | Unchanged; the expedition snapshot is not rewritten |
| `expeditions.procedure_json/procedure_origin_json` | Unchanged immutable pinned revision, source and tiling requirement |
| `campaign_procedure_revisions` | Unchanged, including owners, revision IDs, pinned procedure JSON |
| `expedition_events` | Unchanged sequence and payloads; no event replay/renumbering |
| External filesystem map assets | Untouched; asset storage and map IDs remain separate from PostgreSQL |

**Upgrade strategy:** schema 9 format-1 legacy snapshots are read as-is and lazily projected into validated periodic geometry. Generalized worlds are written as format 2 and validated on every store admission/reload. Existing format-1 worlds are not batch-converted or rewritten at application startup. This no-write path is idempotent and interruption-safe. Unsupported format versions, malformed witnesses and unknown top-level world JSON properties fail closed with the original record still present for repair. Existing tester world/expedition IDs and aggregate versions do not increment merely because newer code reads them.

**Old/new application coexistence:** previous code remains able to open legacy format-1 hex worlds. The previous code must **not** be expected to open new format-2 generalized worlds; these are feature-gated from ordinary authoring pending Phase 18/19. Do not introduce format-2 records into a database that must roll back to pre-Phase-17 code without a controlled restore/migration plan. Procedure revisions and nonspatial sessions are not changed. If legacy worlds are later permanently migrated to format 2, implement a separate explicit, reversible conversion that preserves legacy IDs, stored grid parameters and history; do not silently rewrite historical data.

## Operational preflight and recovery (release gates)

1. Inventory the **actual** deployed schema version and world JSON format distribution under read-only credentials, including map assets, owner counts and active expeditions. If variants outside the documented format are found, stop and prepare a targeted migration; do not reset.
2. Stop incompatible writers or establish a known compatible write window before introducing format 2.
3. Establish a recoverable consistent PostgreSQL backup using the environment's approved snapshot/pg_dump approach, plus a separate map-asset backup including all asset keys referenced by source-map records. Preserve credentials and access controls; do not commit backup files.
4. Rehearse restoring **both** backups in an isolated environment; compare world/expedition/procedure/event counts, selected IDs and map asset hashes. A backup command without a verified restore does not satisfy the release gate.
5. Rehearse fresh/generalized, legacy format-1, unknown-version and mixed-data loading; run concurrent startup/stale-write tests. Restore the old application image on the isolated copy and prove it can reopen unchanged legacy worlds.
6. Roll out only after independent review and specific merge/deployment authorization. If the new application fails before writing format 2, rollback to the previous application is safe for existing schema-9 legacy data. If format 2 has been written, coordinate a controlled cutover or restore the verified backup; do not start the older application against unsupported format-2 records.
7. Complete signed-in real deployed smoke tests after approved merge: old world open/edit, saved expedition resume and travel, map images/registration, pinned procedures, history and nonspatial sessions. Record evidence in the release review.

**Unverified here:** current live tester database inventory, backup/asset recovery, real deployment smoke tests and release authorization. Do not mark those gates complete based only on CI tests.

## Phase 18 integration

Consume `WorldCellId`, `PeriodicCellAddress`, polygon geometry, and reciprocal `WorldCellBoundary` from `PeriodicWorldTiling`. Introduce cell-based runtime state **additively** with preserved historical axial state during cutover. Define movement distances from actual boundary/route geometry and explicit ambiguous-vertex handling. Prove that old hex traversal, progress, navigation and event history replay unchanged before retiring the hex adapter. Phase 19 should replace legacy hex-only canvas geometry using bounded generalized world polygon enumeration.
