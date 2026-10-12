# Phase 20.4 — zero V1 / zero hex-specific implementation gate

**Owner directive (2026-10-12):** the initial hex prototype must leave no executable source trace. Hexagonal worlds and the three existing hex-oriented procedure presets must remain functional **as data-driven instances of the single generalized polygonal tiling engine**, not through hex-specific algorithms, adapters, runtime structures, rendering, start forms, snapshots, or fallback handlers. This requirement supersedes the previous Phase 20.3 readiness assessment.

**Release status (2026-10-12):** BLOCKED for merge, dev/testing deployment, and WorkChat signed-in acceptance. Last fully green code baseline: `d20a71867bdd8cf98042fe771d1f256f9cf7832d` ([CI 38231135438](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38231135438), successful). Later removal head: `3e12d18c6c333f58789772ecdb12a7888ae5849b` ([CI 38232849985](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38232849985), awaiting final result). The CI renderer still skips Phase 15 visual acceptance, and the zero-V1 source audit still fails.

## First implementation slices completed — not the release gate

- Deleted the retired browser world-bound hex start methods and `StartExpeditionInput` type. The world editor/home still start through the qualified-cell API.
- Persisted map grids now render and pick from `PeriodicWorldTiling` server polygon records regardless of the world's legacy grid projection. The old hex preview/registration authoring path still exists and must be replaced.
- Converted the three existing spatial preset recipes to native mechanic-version-2 cell/distance modules and generic turn-cost modules. Deleted `GeometricNativeProcedureMaterializer.cs`; the generic campaign procedure materializer now accepts any executable version-2 spatial preset data without translating a V1 recipe. The existing preset keys remain **data identifiers**.
- Removed the obsolete world-start and abstract-hex-state constructors from `ExpeditionStartService`. Nonspatial creation uses `StartNonSpatialAsync`, with the original cell starts remaining separate. Tests that required the V1-only preset materializer or V1 spatial advance were removed or rewritten against V2.
- Removed redundant V1 application session and context services and old movement/navigation/watch lifecycle engine files. Rewrote the focused browser assistant to retain only nonspatial interval/encounter work and route all spatial sessions into the V2 cell view; removed its V1 hex-steps/direction/forms. [CI 38218781220](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38218781220) passed at code commit `cf05640d`.
- **The release gate is still blocked**: both V1 runtime state classes, the original browser expedition and watch controller, axial world/authoring, mapless start context, and legacy wire responses still remain. Passing CI on this partial migration is not zero-prototype acceptance. No deploy or merge is authorized.

### Further confirmed source removals (October 12)

- Obsolete `HexExited` and `HexEntered` enumerators removed; surviving event ids retain their original numeric values; generalized cell event tests now assert `Cell` identifiers.
- The no-longer-used V1 `WatchAdvanceResult` domain payload was removed.
- The unused axial `expedition-watch-ledger.ts` and its V1-only unit tests were removed. Active V2 travel history remains in `periodic-expedition-workbench.ts`.
- The live branch still contains `ExpeditionState`, `HexTraversalState`, legacy procedure movement and old HTTP state placeholders, an axial mapless constructor, persisted grid/world fallback, dormant V1 expedition page/controller, and hex-only map authoring. **Do not mistake incremental deletion for a passing source gate.**
- The development database still requires an owner-authorized, verified one-time schema 10→11 migration. Old world snapshots and map registrations additionally need a data-preserving conversion before removing grid readers. No dev database or map-asset migration has been performed.

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
