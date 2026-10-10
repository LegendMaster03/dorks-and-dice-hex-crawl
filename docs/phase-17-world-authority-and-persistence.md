# Phase 17 — world authority and persistence contract

**Status:** full transactional upgrade implementation on `feature/tile-crawl-phase-17`; release acceptance requires CI, an isolated pre-upgrade PostgreSQL rehearsal, recovery verification, and independent WorkChat review. No shared tester data has been migrated by this branch.

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

PostgreSQL schema increments from **9 to 10** (with existing verified 8→9 support), and procedure schema increments from **1.2 to 1.3**. No additional relational columns are required. A locked, transactional **full-database conversion** adds the validated periodic tiling to every existing world snapshot, preserving each legacy grid as a read-only-compatible projection. All stored campaign procedure revisions and expedition-pinned procedure snapshots advance to schema 1.3 without changing their IDs, revision numbers or D-symbol identity. The existing advisory lock and optimistic aggregate version checks remain unchanged.

| Existing owner/field | Phase 17 behavior |
| --- | --- |
| `overworlds.id/owner_user_id/version/timestamps` | Unchanged; owner isolation and optimistic concurrency retained |
| `world_json.id/name/grid` | Existing **format 1** converted transactionally to format 2; old grid ID/pose retained and checked against new authoritative tiling |
| `world_json.formatVersion/tiling` | **Format 2** carries validated generalized tiling; optional retained Grid is a verified legacy adapter for hex clients |
| `world_json.features/locations/sourceMaps` | Unchanged, including all identities, geometry, transforms, raster asset keys |
| `world_json.environmentAnnotations` | Existing world/hex/feature scopes preserved; additive qualified cell scope |
| `expeditions.context_json/state_json` | Unchanged, including current hex, directional state and partial progress |
| `expeditions.knowledge_json/party_json/environment_json/effects_json/resources_json/survival_json/journey_state_json/generated_resolutions_json` | Unchanged; the expedition snapshot is not rewritten |
| `expeditions.procedure_json/procedure_origin_json` | Only `procedure_json.schemaVersion` advances from 1.2 to 1.3; pinned revision, source, origin and D-symbol requirement are retained |
| `campaign_procedure_revisions` | Only `procedure_json.schemaVersion` advances from 1.2 to 1.3; owners, revision IDs and all other procedure fields are retained |
| `expedition_events` | Unchanged sequence and payloads; no event replay/renumbering |
| External filesystem map assets | Untouched; asset storage and map IDs remain separate from PostgreSQL |

**Upgrade strategy:** before accepting normal requests, the schema 9→10 migration locks the database, inspects every world, validates its prior grid, constructs its exact polygonal geometry, and writes both a format-2 marker and the periodic witness using JSONB updates that retain every unrelated field. A malformed world, world-snapshot/row ID mismatch, unsupported snapshot version, mismatched existing topology, or unexpected pinned-procedure schema aborts **the entire transaction**, leaving all records on schema 9. The migration upgrades every stored procedure schemaVersion 1.2 to 1.3 in `campaign_procedure_revisions` and `expeditions`, with `tilingDsSymbol` unchanged. The transaction records schema 10 only after all conversions succeed. A repeat invocation is a no-op. Existing world and expedition aggregate versions, ownership, timestamps and event sequences do not change solely due to schema migration.

New hex worlds also serialize the generalized tiling plus legacy grid projection, and newly authored nonhex worlds serialize only the tiling. Format-2 snapshots with a legacy grid must pass cross-checks confirming identical physical cell locations and topology, so there is only one authoritative geometry.

**Old/new application coexistence:** schema 9 applications reject schema 10 during startup, so the deploy requires a controlled single-writer cutover. If startup or postdeploy acceptance fails, restore the verified schema-9 PostgreSQL and matching map-asset backup before starting the previous application; the previous image alone is not a viable schema-10 rollback. Never attempt mixed writes from both versions.

## Operational preflight and recovery (release gates)

1. Inventory the **actual** deployed schema version and world JSON format distribution under read-only credentials, including map assets, owner counts and active expeditions. If variants outside the documented format are found, stop and prepare a targeted migration; do not reset.
2. Stop incompatible writers or establish a known compatible write window before introducing format 2.
3. Establish a recoverable consistent PostgreSQL backup using the environment's approved snapshot/pg_dump approach, plus a separate map-asset backup including all asset keys referenced by source-map records. Preserve credentials and access controls; do not commit backup files.
4. Rehearse restoring **both** backups in an isolated environment; compare world/expedition/procedure/event counts, selected IDs and map asset hashes. A backup command without a verified restore does not satisfy the release gate.
5. Rehearse fresh/generalized, legacy format-1, unknown-version and mixed-data loading; run concurrent startup/stale-write tests. Restore the old application image on the isolated copy and prove it can reopen unchanged legacy worlds.
6. Roll out only after independent review and specific merge/deployment authorization. If the new application fails before writing format 2, rollback to the previous application is safe for existing schema-9 legacy data. If format 2 has been written, coordinate a controlled cutover or restore the verified backup; do not start the older application against unsupported format-2 records.
7. Complete signed-in real deployed smoke tests after approved merge: old world open/edit, saved expedition resume and travel, map images/registration, pinned procedures, history and nonspatial sessions. Record evidence in the release review.

**Unverified here:** current live tester database inventory, backup/asset recovery, real deployment smoke tests and release authorization. Do not mark those gates complete based only on CI tests.

## One-time schema-10 deployment attestation

The main-branch deploy workflow deliberately fails **before touching the running service** unless an operator has created a private recovery attestation at `/mnt/HDDs/www/dorks-and-dice-hex-crawl/phase17-recovery-attestation.json` (or has configured `PHASE17_RECOVERY_MANIFEST` on the runner). No backup files or credentials belong in Git. This is a **cutover-specific fail-closed gate**, to be removed after authorized schema-10 deployment and live regression acceptance.

The operator must actually restore the database and map assets into an isolated environment, verify sample retained IDs/versions, procedure and event counts, and compare restored map-asset hashes. Only then record the following fields in the private manifest, using absolute backup artifact paths and their verified SHA-256 hashes:

```json
{
  "migration": "hex-crawl-9-to-10",
  "deploymentSha": "exact-main-commit-sha-to-deploy",
  "postgresBackupPath": "/secure/backups/hex-crawl-pre-phase17.dump",
  "postgresBackupSha256": "64-hex-digit-sha256",
  "mapAssetsBackupPath": "/secure/backups/hex-crawl-map-assets.tar.gz",
  "mapAssetsBackupSha256": "64-hex-digit-sha256",
  "isolatedPostgresRestoreVerified": true,
  "isolatedMapAssetRestoreVerified": true,
  "authorizedForCutover": true
}
```

The deployment checks the exact commit and both backup checksums. The attestation represents **operator evidence of a completed recovery rehearsal**, not a replacement for doing one. CI also runs an isolated `pg_dump` / `pg_restore` and map-archive extraction/diff against disposable smoke data; this is separate from and does not replace the live pre-cutover backup and recovery gate.

**Image-only rollback is disabled in the Phase 17 deploy workflow.** A failed cutover retains the previous image under the `pre-deploy` tag but does not restart it automatically, because the prior app expects schema 9. If a schema-10 deployment fails after database upgrade, stop writers, restore the verified schema-9 database and associated asset backup under controlled downtime, and only then relaunch the previous image. An operator must diagnose and perform the recovery; the workflow never automatically restores or deletes the actual tester database.

## Phase 18 integration

Consume `WorldCellId`, `PeriodicCellAddress`, polygon geometry, and reciprocal `WorldCellBoundary` from `PeriodicWorldTiling`. Introduce cell-based runtime state **additively** with preserved historical axial state during cutover. Define movement distances from actual boundary/route geometry and explicit ambiguous-vertex handling. Prove that old hex traversal, progress, navigation and event history replay unchanged before retiring the hex adapter. Phase 19 should replace legacy hex-only canvas geometry using bounded generalized world polygon enumeration.
