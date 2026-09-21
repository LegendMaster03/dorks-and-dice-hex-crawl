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
- feature-local adapters and presentation pieces as they are migrated;
- explicit dependency declarations on other modules.

The composition root should not contain a growing list of feature services or endpoints.

## Installed modules

### Worlds

Owns overworld, location, and semantic feature authoring. It registers the shared semantic-world application service used by dependent capabilities.

### Expeditions

Owns persisted crawl sessions, the expedition workbench, runtime advancement, discovery, and focused procedural assistants. It explicitly depends on Worlds because world-bound sessions use semantic world state.

### Source Maps

Owns source-map registration, raster assets, and import workflows such as Wonderdraft. It explicitly depends on Worlds because imported material is promoted into ordinary semantic world objects.

### Reference Data

Owns read-only procedure and presentation catalogs. It has no module dependency.

## What remains framework infrastructure

Authentication against the Dorks & Dice Tool Host, persistence implementation selection, filesystem asset-root configuration, health checks, and deployment-specific configuration remain host/framework composition concerns. A feature module should not acquire knowledge of TrueNAS, hostnames, reverse proxies, or deployment secrets.

## Next decomposition targets

This first slice deliberately preserves behavior and public routes. The next structural work should follow the same ownership boundaries instead of performing a broad rewrite:

1. Reduce the remaining `HexCrawlService` compatibility surface when focused services already provide the same operation and consumers can migrate without churn. Do not create replacement services merely to eliminate a partial class.
2. Split `CrawlRuntimeEngine` into explicit travel, navigation, encounter, and watch-transition collaborators only where doing so preserves the deterministic runtime boundary.
3. Move Web API contracts next to their owning modules when shared-contract analysis shows that doing so does not create duplication.
4. Give the browser client the same feature locality. Route handlers should be registered by client modules rather than accumulated in `app.ts`, and large views such as expedition and source-map workspaces should be decomposed into focused components.
5. Split SQLite persistence by aggregate/concern behind the existing `IHexCrawlStore` contract before changing storage semantics.
6. Keep import formats such as Wonderdraft as adapters. They should produce reviewed semantic inputs rather than become core world types.

## Guardrails

- Public route behavior and persisted data formats do not change merely for organization.
- Cross-module calls use stable application/domain contracts rather than reaching into another module's UI or implementation details.
- A module dependency must be explicit.
- Shared code is promoted to the framework only after at least two capabilities genuinely need the same behavior.
- Do not introduce base classes solely to reduce line count. Prefer composition unless derived types represent the same abstraction.
- New features should normally be added under one owning module so routine work edits the smallest practical set of files.
