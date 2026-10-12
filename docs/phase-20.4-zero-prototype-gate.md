# Phase 20.4 — zero-prototype source and release gate

**Owner directive, October 12, 2026:** There is one spatial implementation. Delete all prototype axial/hex-specific execution, authoring, rendering, contracts, compatibility selectors, fallbacks and obsolete tests. Hexagonal tilings and the three established spatial presets remain fully supported **as data** for the shared periodic-polygon framework. No V1/V2 runtime choice, production-compatibility shim or dual-authority path is permitted. Procedure/schema revision history is separate and remains legitimate.

**Status: BLOCKED** for merge, development deployment and signed-in WorkChat acceptance. **Last verified green automated head before this deletion:** [`5907d314`](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/commit/5907d314d78ca151a931b230dc3547a4dac2d67c), [CI 38297955265](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38297955265), success. Automated validation does **not** prove source elimination or rendered UI quality. No owner-approved database migration, merge or deployment has happened.

## Completed deletion categories

The obsolete axial spatial runtime classes, hex-progress mechanics, version-gated execution paths, hex-step travel inputs, legacy axial watch/controller, obsolete spatial UI, and currentHex-style wire placeholders were deleted. World-bound expedition starts now use periodic cell addresses for both older hex-authored and generalized worlds; the unnecessary q/r lookup module was removed. The world editor now selects polygonal cells and edits cell/world/feature scoped facts through one panel, deleting the axial selected-hex UI and helper algorithms. [Implementation checkpoint](phase-20.4-code-removal-checkpoint.md) provides source details.

## Open architectural blockers

| Area | Must be removed or replaced | Acceptance |
| --- | --- | --- |
| World model and persistence | `OverworldDefinition.Grid`, `HexCoordinate`, `HexGridDefinition`, `HexGeometry`, `LegacyHexTilingCompatibility`, axial environment annotations and old grid snapshot readers | Existing authored world semantics, terrain, objects, registrations and raster assets are converted and reloaded using only periodic topology and realized geometry |
| Mapless context | `AbstractHexCrawlSessionContext`, hex-specific `AbstractCellSessionGeometry` constructors, mapless q/r/orientation start requests/forms | A periodic data-defined geometry/context with a qualified start cell; no hex constructor or fixed six-direction assumption |
| Map rendering and source alignment | Hex drawing/picking/analysis in `canvas-renderer.ts`, `map-surface.ts`, `hex-math.ts`, `hex-grid-analysis.ts`, source-map registration and grid editing | One renderer, picker, preview and registration model for realized periodic cells; existing map registrations retained |
| Residual contracts | Obsolete hex fields in presentation, environmental, event and browser DTOs, and historical update/start APIs | Only authoritative qualified cells, physical world geometry and independent nonspatial state survive; no null legacy placeholders |
| Database cutover | Guarded schema 10→11 startup migration and authored-world/scope/registration/procedure data conversion | Transactionally tested migration and backup/restore, explicit owner-authorized development cutover, then deletion of one-time migration scaffolding |
| Tests/UI | Obsolete source tests; remaining visual checks | Strong behavior tests and held-out mixed motifs, plus rendered Phase 15 Guided/Compact desktop/mobile/embedded checks and signed-in workflows **after** sole spatial path |

## Nonnegotiable implementation policy

- Do not restore deleted prototype code just to satisfy compiler errors or old tests. Convert tests to current periodic behavior; delete tests for intentionally removed behavior.
- Do not introduce a public nonhex geometry selector or square/triangle presets before the owner's review. This has no bearing on the framework's generic geometry capabilities.
- Keep existing hexagonal *data* and original preset identities, not hexagon-specific executable algorithms or shape-authoring assumptions.
- Preserve retained worlds, map images, registration, authored semantics, saved procedure revisions, existing periodic-cell sessions, accounts and nonspatial histories. Short-lived V1 tester expeditions may be disposed of in the owner-authorized cutover.
- Perform complete source/contract/asset audit at the final head. Source terms referring to a hexagon-shaped **data instance** are allowed; compiled special-case hex behavior is not.
- Code commits/CI success are not permission to merge, migrate, deploy or launch WorkChat.

## Completion checklist

1. Static tracked-source audit finds **zero** original hex-specific execution, authoring/rendering/registration implementation, V1 selectors, endpoints, fallback codecs, or tests.
2. Every spatial session uses periodic topology/realized metric geometry, qualified cells and reciprocal interfaces. Nonspatial journeys remain independent. All three hex-origin presets use this same path.
3. World/map/procedure/asset retained data conversion is rehearsed, fails closed on unsupported inputs, and preserves ownership, revisions, geometry and histories. No original world data is silently discarded.
4. Full domain/application/PostgreSQL/HTTP/frontend/container CI, restart/replay, and asset recovery succeed on the exact reviewed commit with representative hex, square, triangle and unfamiliar mixed-motif cases.
5. Phase 15 rendered, keyboard, responsive, Guided/Compact and signed-in behavior is reviewed with WorkChat **only after** the sole workflow is present and the user authorizes the testing.
6. Owner authorizes merge and development cutover separately; do not infer it from green tests.

**Pending-encounter contract deletion (October 12; verified green at `28820231`):** Removed the obsolete axial coordinate from pending encounter domain and snapshot records, construction sites, and dependent tests. Spatial encounters retain their qualified cell; nonspatial encounters remain cell-free.

**Runtime event and handoff contract deletion (verified CI 38296378851):** Removed axial `Hex` fields from runtime event serialization and encounter handoff world context, all associated constructors and stale event assertions. Qualified `WorldCellId` is the only spatial event/handoff identity; nonspatial events remain cell-free. **Browser selection cleanup (verified CI 38297158435):** Removing the obsolete axial `selectedHex` renderer state, hex-picking callback, and q/r click behavior. Map alignment preview is retained as an overlay, and authored map positions continue as world points. **Knowledge-state deletion (verified CI 38297955265):** Remove the unused axial `PlayerKnowledgeState.KnownHexes` list and stale fixtures/assertions. Qualified `KnownCells` remains authoritative; the legacy map-presentation policy setting requires a separate preserved-semantics conversion. **Next geometry helper deletion (pending CI):** Remove the redundant axial feature-intersection overload, polygon-bridging `IntersectsHex` helper and unused six-neighbor/distance routines; convert retained tests to generalized periodic polygon intersection. The legacy axial coordinate **data record** remains until saved world and environment-scope cutover. The remaining world model, mapless context, hex grid preview geometry, and data conversion blockers above remain open.
