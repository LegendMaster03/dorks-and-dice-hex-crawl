# Phase 20.4 zero-prototype implementation handoff

**Objective:** No V1 source or hex-specific logic anywhere in the production build; retain hexagonal geometry and existing preset identities purely as generalized tiling and procedure **data**. This is an owner-imposed **pre-dev/testing deployment** requirement, not a deferred post-deployment cleanup.

**Repository:** `LegendMaster03/dorks-and-dice-hex-crawl`  
**Unmerged, undeployed feature branch:** `feature/tile-crawl-phase-20.2-application-persistence`  
**Partial cleanup code head:** `6ee805a009b5df209c0d0464a784ba0a2a347fca`  
**Release gate:** `docs/phase-20.4-zero-prototype-gate.md`  
**Authoritative plan:** `docs/tile-crawl-development-plan.md`

## Verified implementation decisions

1. The user explicitly rejects residual V1 and hex-specialized code. **Do not retain compatibility wrappers or throw-only endpoint stubs**, even temporarily for a later phase. The deployed test environment is still unchanged and is not authorized to cut over until full cleanup.
2. Spatial starts, watch execution, navigation, map geometry, snapshots and player knowledge should all use qualified cells and generalized periodic polygons. Nonspatial journeys remain supported independently.
3. Keep the existing three hex-origin presets *as data*; their now-native version-2 recipes no longer require conversion from hex-progress mechanics. Do not introduce a square/triangle selector or new preset family before owner review.
4. Existing authored worlds, map binaries, source-map registrations and saved procedures must be converted to periodic data rather than silently dropped. Previously disposable V1 tester expeditions are an explicitly accepted exception. The one-time 10→11 migration is **not deployed**, and must retain the verified preflight/rollback safeguards until an authorized dev deployment.
5. The Phase 15 Guided/Compact UI remains the presentation authority. Remove its dormant V1 spatial branch and hex-only watch/assistant modules after extracting any nonspatial behavior they still own; do not rewrite the entire UI unnecessarily.

## Code already modified

- Deleted obsolete browser world-start methods and input type.
- Existing authored map tilings now drive polygon drawing and selection, even where older worlds still carry a grid projection. Preview authoring is still old hex code, so this is not complete map unification.
- The three original spatial preset recipes are native version-2 movement with explicit cell or physical-distance resolutions and the generic spatial-turn module. `GeometricNativeProcedureMaterializer.cs` has been deleted, and `CampaignProcedureMaterializer.MaterializeExecutableSpatial` generically pins any qualified version-2 preset. Existing V1 tests were removed/replaced where they asserted the retired conversion or V1 movement executor.
- Deleted `ExpeditionStartService.StartWorldBoundAsync`, its legacy `StartStandaloneAsync` path, `CreateAbstractState` and `RequireLegacyHexMovement`. A dedicated nonspatial start remains.
- Current code changes span commits `ffd46f10`, `2328a5ac`, `8c523178`, `bc67b872`, `6c1e5211`, `26820e5c`, `04a8d73e`, `5caba90a`, `6ee805a0`; some intermediate CI runs failed during migration and were repaired in subsequent commits. **Recheck CI at the final branch head**, not an earlier passing run.

## Remaining mandatory work

- Delete the original `ExpeditionState`, `HexTraversalState`, hex navigation/movement/watch engine and `CrawlAssistantActions` implementations; remove remaining V1 references from application services, commands, procedure binder, UI and tests. Keep `CrawlRuntimeEngine` only for generalized cell work.
- Replace `OverworldDefinition.Grid`, hex-specific environment scopes and `LegacyHexTilingCompatibility` with a data migration of saved worlds to native `PeriodicWorldTiling`. Move mapless session creation from `AbstractHexCrawlSessionContext` and `HexCoordinate` onto generic tiling/position inputs.
- Replace the hex authoring/preview, source-map registration and axial projection files (including `hex-math.ts`, `hex-grid-analysis.ts`, old preview in `canvas-renderer.ts` and `map-surface.ts`) with generic observed periodic-pattern interfaces. Preserve source map import and registration capabilities.
- Remove legacy HTTP fields `CurrentHex`, `HexProgress`, `HexSteps`, old `RuntimeAdvanceRequest`, helper/assistant commands, and the dormant 2,593-line spatial branch in `expedition-view.ts` / `expedition-watch-controller.ts`. Preserve unrelated nonspatial/party/encounter/journey features.
- Clean all stale source-level tests while keeping or replacing meaningful verification for sessions, movement, results, geometry, knowledge, environments, pinned procedure revisions, source maps, authorization, stale versions, asset/database restart and backup/restore.
- Audit **all** C# and TypeScript sources for a special case involving a six-direction grid or axial q/r. Do not mistake the presence of a generalized engine alongside V1 for compliance. Validate a held-out periodic mixed motif as well as square, triangle and hex *using the same code*.
- Rename product-facing Hex Crawl to Tile Crawl, but do not let branding changes masquerade as removing old execution code.
- Run full .NET, frontend, HTTP, PostgreSQL/maps, container, rendered signed-in acceptance and WorkChat tests on exact commit hashes. Do not merge or deploy before the zero-prototype gate passes.

**Do not say that Phase 20.4 is complete or that dev/testing deployment may proceed while any of these source paths remain.**
