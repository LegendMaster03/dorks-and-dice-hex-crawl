# Phase 20.4 — source deletion checkpoint

**Objective:** Delete the initial hex-specific prototype completely. There is no production release or compatibility requirement. Keep the established hexagon-oriented presets only as **data** used by the one periodic polygon engine. Document/schema revision numbers are not runtime implementation switches.

**Repository:** `LegendMaster03/dorks-and-dice-hex-crawl`  
**Feature branch (unmerged, undeployed):** `feature/tile-crawl-phase-20.2-application-persistence`  
**Last fully green automated head:** [`707f5667`](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/commit/707f56673fc671b75cf5fd3ead4f5f59c455d847), [CI 38288740680](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38288740680), **successful**. This is automated validation, **not** completion of the architectural deletion gate or rendered browser acceptance.

## Deleted from the feature branch

- Obsolete `ExpeditionState`, `HexTraversalState`, `HexDirection`, axial watch inputs and watch lifecycle/movement/navigation implementation. Current spatial execution uses `CellExpeditionState`, `PeriodicCellTraversal` and realized boundaries; nonspatial sessions have their own state.
- V1 movement-progress mechanic and runtime binder, `HexSteps` travel primitive, mechanic-version selection overload, compatibility branches, obsolete scalar procedure-resolution/assistant paths, axial knowledge progression and old wire `currentHex`/hex-progress fields.
- Old axial runtime event enumerators, unused browser watch ledger, retired spatial expedition watch/controller UI and its source tests. Current spatial UX routes to the periodic-cell workbench; the separate nonspatial Guided/Compact workspace remains.
- Browser rendering fallback that inferred an expedition world position from axial q/r instead of its authoritative spatial position.
- The axial `resolveHexStartCell` browser module. Home and world-editor **world-bound** start forms now accept only qualified motif-cell addresses plus integer lattice displacement. Server world contracts expose `SpatialTiling` for existing hex-authored and new generalized worlds; procedure assessment does not branch on legacy grids.
- Obsolete axial selected-cell authoring panel, event handler and q/r session storage: [`d3a43d4`](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/commit/d3a43d4c530da931e0e39c354e6d0dd21f8c44df) removed 549 lines in `world-editor-view.ts`. Cell, feature and world-scoped environment facts are edited via the generalized polygon panel; the old `selected-cell-authoring.ts` helper lost 184 lines of hex-specific geometry/fact functions and now has generic polygon tests at [`707f5667`](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/commit/707f56673fc671b75cf5fd3ead4f5f59c455d847).
- A stale PostgreSQL smoke test expectation was changed to assert that removed `currentHex` is **absent**, not reintroduced. CI was green at `096a58b2` and `9164f77a` before the latest world-authoring removals.

## Remaining source-level blockers — delete, do not accommodate

1. **World data and geometry:** `OverworldDefinition.Grid`, `HexCoordinate`, `HexGridDefinition`, `HexGeometry`, `LegacyHexTilingCompatibility`, old `WorldSnapshots` grid readers, old hex-environment scopes and the world-grid update API. Existing authored worlds, semantic features, map registrations, binaries and accounts must survive a verified one-time conversion to periodic data.
2. **Mapless starts:** `AbstractHexCrawlSessionContext`, `AbstractCellSessionGeometry.CreateHexWorld/CreateNewHexSession`, the mapless q/r/orientation HTTP and home-form inputs. Replace with realized, data-defined periodic mapless session geometry and qualified starting cells; do not hide axial handling in a helper.
3. **Map preview/registration:** `hex-math.ts`, `hex-grid-analysis.ts`, remaining hex grid drawing/picking in `canvas-renderer.ts`/`map-surface.ts`, old source-map grid alignment and world-grid editing. Preserve calibrated registrations and map raster assets while replacing special-case geometry, not the assets.
4. **Data/wire cleanup:** remove old hex references from retained presentation policies, event/snapshot payloads, `types.ts` and any old authoring routes/parameters once their surviving consumers and persisted authored records have been converted. Confirm there are no obsolete code-path selectors, fallbacks, stubs or tests; do not let passing CI substitute for static source elimination.
5. **Final regressions:** keep strong periodic/held-out mixed motif, authoritative scale, movement, navigation, encounters, effects, discovery, nonspatial journey, authentication/ownership, concurrency, persistence, restart and asset backup/recovery coverage. Rendered Phase 15 Guided/Compact review remains unperformed.

## Deployment and testing boundary

The transactional, guarded schema 10→11 upgrade is **not executed against the real development database**. A separate, verified, data-preserving migration for authored worlds, scope annotations, map registrations and saved procedure revisions is required before removing all old readers. Disposable short-lived prototype expeditions may be deleted in the authorized cutover. Do not merge, deploy, run migrations against live development data or start WorkChat acceptance until the owner authorizes it **after** the sole generalized workflow and zero-prototype source gate are complete.

The controlling checklist is [`phase-20.4-zero-prototype-gate.md`](phase-20.4-zero-prototype-gate.md). The development plan is [`tile-crawl-development-plan.md`](tile-crawl-development-plan.md).
