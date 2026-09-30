# Hex Crawl architecture

## Boundaries

Hex Crawl keeps world truth, crawl/session state, player knowledge, presentation policy, and procedure configuration as independent state axes.

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, and source-map representations.
2. **Crawl/session state** — explicit `CrawlSessionContext` plus `ExpeditionState` or `NonSpatialSessionState`.
3. **Player knowledge** — `PlayerKnowledgeState` records subject-specific knowledge and party presentation state for world-bound sessions.
4. **Presentation policy** — `MapPresentationPolicy` and projections decide what knowledge changes may happen automatically and how authoritative data is presented.
5. **Procedure configuration** — `CampaignProcedure` is the single authoritative procedure representation for current-format sessions.

Named systems exist only as removable creation-time presets. Runtime behavior depends on materialized generic module/mechanic snapshots, never on preset identity.

## Pre-release compatibility policy

Hex Crawl is pre-release. Development-era API, persistence, UI, and internal model compatibility is not preserved unless a specific requirement is explicitly approved.

Obsolete representations should be removed instead of maintained beside the target architecture. Breaking development migrations and database resets are acceptable while no compatibility commitment exists for real user data.

Future user-data migrations must be evaluated separately when the product reaches a stage where such commitments exist.

## Project structure

- `HexCrawl.Domain` owns spatial, world, knowledge, presentation, source-map registration math, generic procedure contracts, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, procedure materialization/revision, runtime orchestration, optional provider integration, and persistence/blob ports.
- `HexCrawl.Infrastructure` implements PostgreSQL persistence, filesystem map assets, and Tool Host authentication redemption.
- `HexCrawl.Web` owns HTTP contracts, authentication middleware, route hosting, and the TypeScript application.

## Procedure architecture

`CrawlProcedurePresetDefinition` contains creation-time preset identity, display metadata, revision, and a `GenericProcedurePresetRecipe`.

Applying a preset materializes a standalone `CampaignProcedure` containing complete selected module/mechanic snapshots and parameters. `ProcedureOriginMetadata` is optional provenance only.

The runtime path is:

`CampaignProcedure -> GenericProcedureRuntime.Bind -> CrawlRuntimeEngine`

Runtime binding dispatches on embedded handler/version contracts. It does not consult system identity, origin preset identity, or the current catalog.

Unknown handlers and unsupported versions remain preserved but unsupported. Recognized declarative structural mechanics are persisted and exposed without pretending they are executable.

## Continuous overworld and semantic geometry

An `OverworldDefinition` is one continuous world coordinate space. The hex grid is mathematical; creating a world does not pre-populate stored hex rows.

The grid supports pointy-top/flat-top orientation, axial coordinates, configurable origin/rotation, world-space radius, and configurable physical center distance/unit.

Point, line, and region features are stored as world-space semantic geometry. Categories are strings so terrain, roads, rivers, borders, and campaign-specific semantics remain extensible without coupling world truth to a raster or edition.

## Session contexts

The three current context forms are deliberately distinct:

- `WorldBound` stores a real Overworld ID and may persist player knowledge/presentation state.
- `AbstractHex` stores its own name, orientation, and physical crawl scale without an Overworld.
- `NonSpatial` stores procedure/time/history state without spatial fields.

The deterministic runtime receives only the context data actually required for the active behavior.

## Persistence and asset architecture

PostgreSQL remains behind `IHexCrawlStore`; application/domain code has no Npgsql dependency.

The current pre-release schema is version 4. Earlier development procedure schemas are intentionally rejected with a reset instruction rather than upgraded through retired compatibility paths.

The `expeditions` table stores one required procedure representation:

- `procedure_json` — serialized `CampaignProcedure`.

There is no parallel retired procedure JSON column or profile-only fallback. `campaign_procedure_revisions.procedure_json` likewise stores `CampaignProcedure` revisions.

Runtime history remains in `expedition_events`; persistence is aggregate snapshot plus retained ordered history, not event sourcing.

Map assets remain behind `IMapAssetStore` and are filesystem-backed at `/data/assets` in the initial production implementation. Domain records store provider-relative opaque asset keys rather than filesystem paths.

## Ownership and authentication

Persistent APIs require stable identity. Hosted requests use the Dorks & Dice Tool Host authentication contract; standalone development can enable an explicit configured identity.

Every world and crawl session is owner-scoped. Enumeration, direct loads, source-map mutation, asset retrieval, session creation, and session mutation enforce that ownership.

## Optimistic concurrency

Worlds and crawl sessions have monotonically increasing aggregate versions. Mutations carry `ExpectedVersion`; stale writes return conflicts rather than overwriting newer state.

Grid changes remain blocked once sessions depend on that world geometry.

## API contracts

The Web layer exposes resource DTOs rather than persistence rows.

Procedure-facing endpoints use generic contracts:

- preset catalog endpoints expose `ProcedurePresetContract` with a materialized `CampaignProcedureContract`;
- expedition detail/workbench responses expose the persisted `CampaignProcedureContract`;
- executable snapshots may additionally expose derived `ProcedureRuntimeContract` data;
- structural/declarative/incomplete snapshots remain representable even when current runtime binding is unavailable.

Generic procedures are not rejected merely because a retired contract shape can not represent them.

World and expedition routes retain the existing product organization, including world-bound creation, standalone session creation, full workbench routes, mapless trackers, and focused assistant mutations.

## Frontend and routing

Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation goes through `RenderLifecycle`; `MutationObserver` is not used.

`/` is the DM tools home. World authoring, full world-bound workbench routes, mapless session routes, and focused assistant routes remain separate product surfaces.

The browser renders authoritative persisted procedure/session state. It does not create a second procedure model.

## Source-map boundary

A `SourceMapRepresentation` is evidence/presentation for part of one continuous overworld, never authoritative terrain or procedure state.

Registration, raster rendering, map import, and automatic grid-alignment work remain independent from generic procedure architecture.

## Rules Core and other tools

Rules Core remains optional enrichment and may provide resolved inputs or canonical content, but it does not own the expedition procedure or runtime.

Character Sheet may provide capabilities. Block Initiative remains authoritative for tactical combat after encounter handoff. Neither tool changes Hex Crawl runtime ownership.

## Phase 3 structural proof

Phase 3 proves that materially different travel/journey systems can be represented with generic module/mechanic contracts without system-specific runtime classes.

Important retained guarantees include:

- removable named presets;
- campaign-owned pinned snapshots;
- no runtime branching on system identity;
- handler/version-aware dispatch;
- explicit unresolved-input source sets;
- no broad false dependency reads;
- The One Ring journey graph without a fabricated repeating interval;
- D&D 2024 terrain represented as terrain-tag-to-symbolic maximum-pace state, including conditional Arctic Fast travel.

Later phases own the full Procedure Composer, typed participant activities, movement capability composition, environment execution, generalized effects/consequences, survival/resource execution, journey execution, and expanded encounter runtime.

See `docs/generic-procedure-architecture.md`, `docs/generic-procedure-development-plan.md`, and `docs/phase-3-proof-matrix.md`.
