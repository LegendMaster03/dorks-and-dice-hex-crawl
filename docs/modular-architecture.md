# Hex Crawl modular architecture

## Purpose

Hex Crawl has reached the point where clean architectural layers alone are not enough to keep the implementation easy to navigate. The repository already separates Domain, Application, Infrastructure, and Web responsibilities, but several feature families have accumulated in shared composition files. The goal of this refactor is to preserve those dependency boundaries while making feature ownership visible from the directory tree.

A developer should be able to answer "where does this Hex Crawl capability live?" without searching a monolithic bootstrap file or editing unrelated sub-tools.

## Reference patterns

Two existing projects provide useful, complementary models.

### Dorks & Dice Site

The Site separates reusable framework contracts from mode and plugin implementations. Stable definitions and registries let the composition root discover capabilities without teaching shared framework code the internals of each one.

Hex Crawl adopts that principle for sub-tools: the framework owns module contracts and validation; feature modules own registration and routes.

### XnGine

XnGine centralizes genuinely shared game behavior in `GameXngine` and lets Arena, Battlespire, Daggerfall, and Redguard specialize it. Feature-specific classes remain physically close to the game that owns them.

Hex Crawl adopts the locality principle, but does not copy inheritance indiscriminately. Worlds, Expeditions, Source Maps, and reference catalogs are different capabilities rather than interchangeable implementations of one base object. Composition through a narrow module contract is therefore the default. Inheritance remains appropriate only when two implementations have a real is-a relationship and shared behavior.

## Architectural rules

The existing project dependency direction remains authoritative:

```text
HexCrawl.Domain
    ↑
HexCrawl.Application
    ↑
HexCrawl.Infrastructure
    ↑
HexCrawl.Web
```

Infrastructure and Web may also reference Domain where the existing solution requires it. The refactor does not introduce a second "core" assembly that duplicates Domain or Application responsibilities.

Within Web:

```text
Framework/
    HexCrawlModule.cs
    HexCrawlModuleCatalog.cs

Modules/
    Worlds/
    Expeditions/
    SourceMaps/
    ReferenceData/
```

Application follows the same feature ownership without changing its public namespace:

```text
HexCrawl.Application/
    HexCrawlService.cs
    Worlds/
        WorldCommands.cs
        HexCrawlService.Worlds.cs
        HexCrawlService.WorldObjects.cs
    Expeditions/
        ExpeditionCommands.cs
        ExpeditionWorkbenchService.cs
        ExpeditionAssistantService.cs
        ...
        HexCrawlService.Expeditions.cs
    SourceMaps/
        SourceMapCommands.cs
        SourceMapApplicationService.cs
        HexCrawlService.SourceMaps.cs
```

`HexCrawlService` remains a compatibility facade, but its implementation is now physically separated by capability. This minimizes call-site churn while making feature ownership visible. New focused application services should live with their owning feature rather than enlarging the facade.

The framework owns:

- the module contract;
- stable module identity;
- dependency validation;
- service-registration orchestration;
- API composition.

A feature module owns:

- the services specific to that capability;
- its endpoint registration;
- feature-local HTTP contracts and options;
- feature-local adapters and presentation pieces as they are migrated;
- explicit dependency declarations on other modules.

Web contracts used by more than one module live under `Web/Contracts/`; they are not copied into each feature.

The composition root should not contain a growing list of feature services or endpoints.

## Installed modules

### Worlds

Owns overworld, location, and semantic feature authoring. It registers the shared semantic-world application service used by dependent capabilities.

### Expeditions

Owns persisted crawl sessions, the expedition workbench, runtime advancement, discovery, and focused procedural assistants. It explicitly depends on Worlds because world-bound sessions use semantic world state.

### Source Maps

Owns source-map registration, raster assets, and import workflows such as Wonderdraft. It explicitly depends on Worlds because imported material is promoted into ordinary semantic world objects. Its HTTP surface keeps one route-composition entry point while raster/source-map handlers and Wonderdraft handlers live in separate partial files, so importer work does not require editing ordinary asset CRUD.

### Reference Data

Owns read-only procedure and presentation catalogs. It has no module dependency.

## What remains framework infrastructure

Authentication against the Dorks & Dice Tool Host, persistence implementation selection, filesystem asset-root configuration, health checks, and deployment-specific configuration remain host/framework composition concerns. A feature module should not acquire knowledge of TrueNAS, hostnames, reverse proxies, or deployment secrets.

## Assistant domain locality

The focused Travel/Watch, Navigation, and Encounter Cadence tools share the stable `CrawlAssistantActions` API, but their domain implementations live in separate partial files under `Domain/Runtime/Assistants/`. Shared event/provenance and distance helpers remain in one support partial. This mirrors the user-visible sub-tools without introducing separate runtime abstractions or changing deterministic state transitions.

## Deterministic runtime locality

`CrawlRuntimeEngine` remains one sealed deterministic engine with one public `Advance` entry point. Its implementation is physically divided under `Domain/Runtime/Engine/` into watch lifecycle, navigation/direction handling, movement, and support/event collection. This is intentionally a partial-class split rather than an object graph: it improves human navigation and limits routine edits without changing execution order, state ownership, or introducing replaceable runtime stages that the domain does not currently need.

## Next decomposition targets

This first slice deliberately preserves behavior and public routes. The next structural work should follow the same ownership boundaries instead of performing a broad rewrite:

1. Reduce the remaining `HexCrawlService` compatibility surface when focused services already provide the same operation and consumers can migrate without churn. Do not create replacement services merely to eliminate a partial class.
2. Evaluate whether the now physically separated `CrawlRuntimeEngine` responsibilities should remain one deterministic partial-class boundary or graduate into collaborators. Do not introduce collaborator interfaces until they provide a concrete testing, substitution, or dependency benefit.
3. Continue retiring compatibility-only Web contracts when no active route or test requires them. Keep genuinely shared spatial contracts under `Web/Contracts/` rather than duplicating them across modules.
4. Continue decomposing only where a concrete lifecycle boundary remains. Expedition read-only presentation lives in `expedition-presentation.ts`, and watch-form policy/submission lives in `expedition-watch-controller.ts`; the route view now owns map/discovery orchestration and navigation.
5. Evaluate whether the physically separated SQLite aggregate operations should eventually become independent stores. Keep `IHexCrawlStore` as the compatibility boundary until a narrower contract provides a concrete benefit; do not change storage semantics merely for type count.
6. Keep import formats such as Wonderdraft as adapters. They should produce reviewed semantic inputs rather than become core world types.

## Browser-client locality

The browser bootstrap now dispatches through a validated client-module catalog rather than importing and switching over every feature view directly. Home, Worlds, Expeditions, and Assistants own their route kinds and rendering composition. The catalog rejects duplicate or missing route ownership, which gives new client capabilities the same explicit composition model as server modules. Generic DOM/form helpers that were duplicated across several feature views now live under `Client/src/ui/`; feature-specific helpers remain with their owning view. Read-only expedition presentation is separated from watch mutation and discovery orchestration in `expedition-presentation.ts`. Stateful sub-workflows are also isolated when they have a real lifecycle boundary: source-map affine registration owns its control-point state, preview lifecycle, map-click interception, and save flow in `source-map-registration-controller.ts`; Wonderdraft inspection, candidate review, and semantic import selection live in `wonderdraft-import-controller.ts`.

## Format-adapter locality

Binary and image import code remains implementation-oriented infrastructure rather than becoming domain abstractions. `RasterImageInspector` keeps one public inspection API, while PNG, JPEG, and WebP structural validation live in separate partial files. Shared bounded stream-reading mechanics remain in the core inspector. `WonderdraftProjectInspector` likewise keeps its public inspection/candidate API together while the Godot Variant parser and GCPF payload reader live in separate partial files. This makes format-specific maintenance local without changing accepted formats, validation behavior, decompression limits, or the upload contract.

## Persistence locality

`SqliteHexCrawlStore` still implements the existing `IHexCrawlStore` contract and uses the same schema and serialized shapes. Its implementation is now separated into world persistence, expedition/event persistence, shared connection/serialization mechanics, runtime snapshots, and world snapshots. Snapshot types remain private implementation details, which keeps persisted compatibility concerns close to the SQLite adapter rather than leaking them into Domain.

## Guardrails

- Public route behavior and persisted data formats do not change merely for organization.
- Cross-module calls use stable application/domain contracts rather than reaching into another module's UI or implementation details.
- A module dependency must be explicit.
- Shared code is promoted to the framework only after at least two capabilities genuinely need the same behavior.
- Do not introduce base classes solely to reduce line count. Prefer composition unless derived types represent the same abstraction.
- New features should normally be added under one owning module so routine work edits the smallest practical set of files.
