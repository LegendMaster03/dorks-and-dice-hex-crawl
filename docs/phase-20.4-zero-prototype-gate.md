# Phase 20.4 — zero V1 / zero hex-specific implementation gate

**Owner directive (2026-10-12):** the initial hex prototype must leave no executable source trace. Hexagonal worlds and the three existing hex-oriented procedure presets must remain functional **as data-driven instances of the single generalized polygonal tiling engine**, not through hex-specific algorithms, adapters, runtime structures, rendering, start forms, snapshots, or fallback handlers. This requirement supersedes the previous Phase 20.3 readiness assessment.

**Release status:** BLOCKED for dev/testing deployment, merge and WorkChat signed-in acceptance until the criteria below are demonstrated. The previous green CI at `a76aa430` proves the then-present code compiled; it does not prove absence of V1 or special-case geometry.

## Mandatory removal inventory

| Layer | Known remaining source to eliminate or rewrite | Required replacement |
| --- | --- | --- |
| Domain movement | `Runtime/ExpeditionState.cs`, `HexTraversalState.cs`, `CrawlRuntimeEngine.cs` V1 overloads, `Engine/CrawlRuntimeEngine.Movement.cs`, `Navigation.cs`, `WatchLifecycle.cs`, `CrawlAssistantActions.cs` and `Assistants/*` | One `CellExpeditionState`, `PeriodicCellTraversal`, atomic interface-based navigation, generalized watch/action contracts |
| Procedure binding | `GenericProcedureRuntime` V1 branches, `TravelResolutionMode.HexSteps`, `ProcedureHexProgressRuntime`, `CrawlProcedureCatalog` and `GeometricNativeProcedureMaterializer` hex-specific recipes | Version-2 generalized movement rules; data-only preset records with periodic D-symbol/realization, no compiled special-case hex constructor |
| Application | `ExpeditionStartService` V1 stubs/factory, `CrawlSessionService`, `ExpeditionAssistantService`, `HexCrawlService.Expeditions` V1 overloads, `ExpeditionWorldComposition`, V1 part of `EnvironmentContextResolver` | V2 cell starts/advances/helpers/knowledge, shared nonspatial services with no V1 spatial branches |
| World/spatial authority | `OverworldDefinition.Grid`, `LegacyHexTilingCompatibility`, `HexCoordinate` / `HexGridDefinition` / `HexGeometry` as production source, `AbstractCellSessionGeometry.CreateHexWorld`, `AbstractHexCrawlSessionContext`, axial environment/knowledge scopes | Persisted `PeriodicWorldTiling` and polygon/world-point/qualified-cell addressing everywhere; generalized mapless construction; migrate existing worlds and map registrations to this representation |
| HTTP/wire | `WorkbenchExpeditionStateContract.CurrentHex/HexProgress`, old V1 advance/assistant request types and methods, hex-specific start requests, any old-route error shim | Only typed V2 spatial and independent nonspatial contracts; no null placeholders for retired fields |
| Browser | `expedition-view.ts` spatial fallback, `expedition-watch-controller.ts`, hex-focused assistant branch, retired methods in `api.ts`, `types.ts` hex fields and commands | Retain Phase 15 Guided/Compact shell backed only by cell APIs, qualified polygons/interfaces and nonspatial controls |
| Maps/authoring | `canvas-renderer.ts` axial grid branch, `map-surface.ts` q/r picking, `hex-math.ts`, `hex-grid-analysis.ts`, source-map hex alignment and editor q/r controls | Unified periodic-polygon render/pick/viewport/registration and tiling-agnostic motif analysis, with no second geometry engine |
| Persistence/migration | Explicit V1-discriminator readers and schema-10→11 startup deletion shim after its one verified dev DB application | V2/nonspatial snapshot codec only; safe data-preserving world/map/procedure migration; schema-11 constraint retained |
| Tests/docs | V1-only tests and assertions currently sustaining old code | Replace with generalized behavior tests including existing hex worlds and unfamiliar mixed motifs; no tests requiring removed APIs |

This inventory is grounded in the branch-level repository audit, not proof that every affected reference has been enumerated. Before claiming completion perform a **full tracked-source audit** (C#, TypeScript, shaders/scripts, native assets, API descriptions, generated source). Legitimate content or names describing a hexagonal *preset as data* do not exempt compiled hex-specific behavior.

## Nonnegotiable implementation rules

- No compatibility shim, deprecated endpoint that throws, duplicate engine, historical runtime serializer, synthetic axial fields, hex progress approximator, six-direction control, or bespoke hex rendering shortcut.
- Do not introduce square/triangle feature toggles or new public preset families before the owner's geometric system review; this does not justify retaining hex special cases.
- Preserve retained user-authored worlds, source maps and binaries, map registration, accounts, saved procedure revisions, nonspatial histories and existing V2 sessions through a verified migration. V1 tester expeditions are disposable per owner decision.
- Clean deletion is preferred to keeping code behind an unreachable `if`. Remove obsolete tests and replace useful semantic coverage. Product branding and repository/service renaming must not be mistaken for actual geometry removal.
- Do not run the migration/deployment or merge without owner authorization, even when the cleanup is green.

## Verification gate

1. Static code audit shows **no surviving original V1 or hex-specialized compiled implementation**. Hexagon instances exist solely as ordinary periodic polygon data; dedicated geometry and presentation fast paths do not.
2. API/snapshot/store/wire contracts expose only current cell spatial and independent nonspatial representations; unknown records fail closed, and removed routes are truly absent.
3. A data-defined hex world and each of the three existing presets behave correctly through the **same** renderer, watch engine, and persistence path as a data-defined square, triangle and held-out mixed-tiling witness.
4. Full domain, application, HTTP, PostgreSQL/assets and frontend CI, restart/replay, backup/restore and rendered UI acceptance run at an exact reviewed commit. No skipped rendered matrix counts as a pass.
5. Only after 1–4: owner-controlled dev/testing deployment and WorkChat testing, using `docs/phase-20.3-workchat-dev-test-handoff.md`.
