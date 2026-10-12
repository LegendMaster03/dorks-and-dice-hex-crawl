# Phase 20.4 zero-prototype implementation handoff

**Objective:** No V1 source or hex-specific logic anywhere in the production build; retain hexagonal geometry and existing preset identities purely as generalized tiling and procedure **data**. This is an owner-imposed **pre-dev/testing deployment** requirement, not a deferred post-deployment cleanup.

**Repository:** `LegendMaster03/dorks-and-dice-hex-crawl`  
**Unmerged, undeployed feature branch:** `feature/tile-crawl-phase-20.2-application-persistence`  
**Last green baseline:** `cc79753e25549d73e88677d423e8f9c8d05f9d41`, [CI 38219426704](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38219426704). **Last source-cleanup commit before documentation updates:** `bd7d6940ffc3ef57830b32cb6b42249670bb687e`; its CI and the final documentation-inclusive head are not yet verified. Verify latest branch ref and CI before further work.
**Release gate:** `docs/phase-20.4-zero-prototype-gate.md`  
**Authoritative plan:** `docs/tile-crawl-development-plan.md`

## October 12 incremental cleanup and concrete remaining dependencies

Source commits on this feature branch after the last green baseline:

- `61644f7d`: delete obsolete V1 `HexEntered` / `HexExited` event kinds, explicitly preserving the numeric value of the next event kind to protect serialized histories.
- `a229d0ed`: update qualified-cell boundary regression assertions accordingly.
- `e02dea65`: delete unused `WatchAdvanceResult`, whose payload referenced the V1 `ExpeditionState` type.
- `b5cc652c`, `bd7d6940`: remove the unreferenced axial `expedition-watch-ledger.ts` module and its tests. That module read legacy `event.hex.q/r`; the active V2 view maintains its own event history.

**CI:** The previous successful check is [38219426704](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38219426704) at `cc79753e`; do not label cleanup commits green without checking [the latest branch runs](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions). No local .NET or browser acceptance run is claimed.

Confirmed source coupling requiring coordinated edits:

- `src/HexCrawl.Domain/Runtime/ExpeditionState.cs`, `HexTraversalState.cs`, `GenericProcedureRuntime.cs` (`ProcedureHexProgressRuntime`, mechanic-v1 handler), `CrawlSessionContext.cs` (`AbstractHexCrawlSessionContext`) and `CrawlRuntimeEvent.cs` (`HexCoordinate? Hex` event payload). Removing classes without replacing all consumers is unsafe.
- `src/HexCrawl.Web/Modules/Expeditions/ExpeditionApiContracts.cs` still emits axial `CurrentHex`, `HexProgress`, direction and position placeholders for `CellExpeditionState` and `NonSpatialSessionState`; corresponding client union in `src/HexCrawl.Web/Client/src/types.ts` is V1-shaped.
- `src/HexCrawl.Domain/World/OverworldDefinition.cs` still has fallback `Grid` plus axial environment scopes; `src/HexCrawl.Infrastructure/Persistence/Snapshots/PostgresHexCrawlStore.WorldSnapshots.cs` still reads versions 1/2 using that grid.
- `src/HexCrawl.Domain/Runtime/AbstractCellSessionGeometry.cs`, `src/HexCrawl.Application/Expeditions/ExpeditionStartService.cs`, `src/HexCrawl.Application/Expeditions/HexCrawlService.CellExpeditions.cs`, and mapless `/api/expeditions/cells` still construct geometry using the legacy axial bridge.
- `src/HexCrawl.Web/Client/src/modules/expeditions/expedition-view.ts` routes spatial sessions into V2 but retains approximately 2,500 lines of dormant spatial and live nonspatial/shared Phase 15 code; `expedition-watch-controller.ts` remains imported there. Extract nonspatial/shared functionality before deleting dormant branches; adjust legacy source tests without weakening V2 behavioral assertions.
- Old frontend world-preview/source-map alignment code and `HexGridDefinition`/`HexCoordinate` utilities have not been removed; they remain outside the generalized persisted-tiling renderer.

**Migration/deployment boundary:** schema 10→11 startup migration remains unexecuted on the real dev database and transactionally removes disposable V1 expeditions only. An additional verified one-time conversion of retained authored world JSON, environment scopes, map registrations, and asset references is required before eliminating all read-time grid conversion. Rehearse PostgreSQL plus filesystem asset backup/restore together. Run only with owner authorization. After verified cutover, remove temporary upgrade code from the application while keeping schema 11 and its V2-only constraint. Do not merge, deploy, or conduct signed-in acceptance until the zero-prototype gate is demonstrably passed.

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
- Further verified removals: `CrawlSessionService.cs`, `CrawlSessionContextResolver.cs`, `ExpeditionWorldComposition.cs`, the V1 movement/navigation/watch lifecycle engine files and V1 spatial assistant navigation source. These are absent on current feature-branch tree; the surviving engine and assistant paths retain generalized cells and independent nonspatial capabilities.
- Focused browser assistant `expedition-assistant-view.ts` is now nonspatial-only. It routes **every spatial session** into the generalized expedition page; V1 six-direction navigation, hex-travel, veer-step, progress and resulting-hex forms have been deleted. Nonspatial interval, encounter cadence, provenance, history and Block Initiative handoff remain. Frontend source tests were updated to test the current contract. Code commit `cf05640d` passed full CI.
- **The zero-prototype gate remains blocked.** `ExpeditionState.cs`, `HexTraversalState.cs`, old browser `expedition-view.ts` and `expedition-watch-controller.ts`, old world grid/authoring, mapless context and V1 wire fields remain. No deployment or merge was authorized.

## Remaining mandatory work

- Delete remaining `ExpeditionState` and `HexTraversalState` runtime models and their serialization/wire dependencies; the old movement/navigation/watch implementation files were already removed. Remove any remaining V1 assistant and procedure branches. Keep `CrawlRuntimeEngine` only for generalized cell work.
- Replace `OverworldDefinition.Grid`, hex-specific environment scopes and `LegacyHexTilingCompatibility` with a data migration of saved worlds to native `PeriodicWorldTiling`. Move mapless session creation from `AbstractHexCrawlSessionContext` and `HexCoordinate` onto generic tiling/position inputs.
- Replace the hex authoring/preview, source-map registration and axial projection files (including `hex-math.ts`, `hex-grid-analysis.ts`, old preview in `canvas-renderer.ts` and `map-surface.ts`) with generic observed periodic-pattern interfaces. Preserve source map import and registration capabilities.
- Remove legacy HTTP fields `CurrentHex`, `HexProgress`, `HexSteps`, old `RuntimeAdvanceRequest`, any retired API methods, and the dormant 2,593-line spatial branch in `expedition-view.ts` / `expedition-watch-controller.ts`. The focused browser assistant is already nonspatial-only. Preserve nonspatial/party/encounter/journey UX when deleting the remaining old page.
- Clean all stale source-level tests while keeping or replacing meaningful verification for sessions, movement, results, geometry, knowledge, environments, pinned procedure revisions, source maps, authorization, stale versions, asset/database restart and backup/restore.
- Audit **all** C# and TypeScript sources for a special case involving a six-direction grid or axial q/r. Do not mistake the presence of a generalized engine alongside V1 for compliance. Validate a held-out periodic mixed motif as well as square, triangle and hex *using the same code*.
- Rename product-facing Hex Crawl to Tile Crawl, but do not let branding changes masquerade as removing old execution code.
- Run full .NET, frontend, HTTP, PostgreSQL/maps, container, rendered signed-in acceptance and WorkChat tests on exact commit hashes. Do not merge or deploy before the zero-prototype gate passes.

**Do not say that Phase 20.4 is complete or that dev/testing deployment may proceed while any of these source paths remain.**
