# Hex Crawl architecture

## Boundaries

Hex Crawl keeps world truth, crawl/session state, environment context, player knowledge, presentation policy, and procedure configuration as independent state axes.

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, source-map representations, and static environment annotations.
2. **Crawl/session state** — explicit `CrawlSessionContext` plus `ExpeditionState` or `NonSpatialSessionState`. Deterministic runtime state does not own world lookup or provider integration.
3. **Environment context** — `StoredExpedition.Environment` owns current/transient facts and explicit DM overrides. Static world/hex/spatial-feature facts remain on `OverworldDefinition` and are resolved against current position by the application layer.
4. **Player knowledge** — `PlayerKnowledgeState` records subject-specific knowledge and party presentation state for world-bound sessions.
5. **Presentation policy** — `MapPresentationPolicy` and projections decide what knowledge changes may happen automatically and how authoritative data is presented.
6. **Procedure configuration** — `CampaignProcedure` is the single authoritative procedure representation for current-format sessions.
7. **Expedition consequences/resources/survival** — Phase 10/11 aggregate state owns applied/pending consequences, persistent effects, generic resources, forced-travel progress, exposure, and camp state.
8. **Journey/process state** — `StoredExpedition.Journey` owns active/closed multi-stage processes, journey-event occurrences, resolutions, history, and idempotency identities.

Named systems exist only as removable creation-time presets. Runtime behavior depends on materialized generic module/mechanic snapshots, never on preset identity.

## Pre-release compatibility policy

Hex Crawl is pre-release. Development-era API, persistence, UI, and internal model compatibility is not preserved unless a specific requirement is explicitly approved.

Obsolete representations should be removed instead of maintained beside the target architecture. Breaking development migrations and database resets are acceptable while no compatibility commitment exists for real user data.

Future user-data migrations must be evaluated separately when the product reaches a stage where such commitments exist.

## Project structure

- `HexCrawl.Domain` owns spatial, world, knowledge, presentation, source-map registration math, generic procedure contracts, environment fact types, expedition effect/resource/survival/journey state types, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, procedure materialization/revision, environment resolution/evaluation, movement composition, consequence/resource/survival/journey orchestration, optional provider integration, and persistence/blob ports.
- `HexCrawl.Infrastructure` implements PostgreSQL persistence, filesystem map assets, and Tool Host authentication redemption.
- `HexCrawl.Web` owns HTTP contracts, authentication middleware, route hosting, and the TypeScript application.

## Procedure architecture

`CrawlProcedurePresetDefinition` contains creation-time preset identity, display metadata, revision, and a `GenericProcedurePresetRecipe`.

Applying a preset materializes a standalone `CampaignProcedure` containing complete selected module/mechanic snapshots and parameters. `ProcedureOriginMetadata` is optional provenance only.

The deterministic runtime path is:

`CampaignProcedure -> GenericProcedureRuntime.Bind -> CrawlRuntimeEngine`

Runtime binding dispatches on embedded handler/version contracts. It does not consult system identity, origin preset identity, or the current catalog.

Unknown handlers and unsupported versions remain preserved but unsupported. Recognized declarative structural mechanics are persisted and exposed without pretending they are executable.

Focused application operations may interpret one supported contract from the pinned procedure without binding unrelated structural mechanics. Participant activities, movement capability composition, environment-to-movement evaluation, survival/resource operations, and journey process/event operations use this boundary.

Phase 12 preset recipes declare their complete generic `journey.process` / `journey.events` parameters before materialization. There is no creation-time preset-identity completion pass and no runtime preset dispatch.

## Continuous overworld and semantic geometry

An `OverworldDefinition` is one continuous world coordinate space. The hex grid is mathematical; creating a world does not pre-populate stored hex rows.

The grid supports pointy-top/flat-top orientation, axial coordinates, configurable origin/rotation, world-space radius, and configurable physical center distance/unit.

Point, line, and region features are stored as world-space semantic geometry. Categories are strings so terrain, roads, rivers, borders, and campaign-specific semantics remain extensible without coupling world truth to a raster or edition.

Static environment annotations are explicit world data, not inferred from feature category. An annotation may target the world, one hex, or one spatial feature and carries typed tag or measurement facts with optional provenance and notes.

## Environment context

Phase 9 provides a ruleset-neutral environment layer. Environment dimensions are open strings; common dimensions include terrain, route, weather, visibility, elevation, depth, water, current, temperature, hazard, and regional effect. Values are either tags or measurements with explicit units.

Ownership is deliberately split:

- `OverworldDefinition.EnvironmentAnnotations` owns static world, hex, and spatial-feature truth.
- `StoredExpedition.Environment.CurrentFacts` owns transient/current session conditions.
- `StoredExpedition.Environment.Overrides` owns explicit DM overrides.
- `ExpeditionState` remains traversal/runtime state and does not receive world lookup or provider dependencies.

`EnvironmentContextResolver` computes an effective context above deterministic runtime. Static world/hex/feature facts have precedence 0, expedition-current facts precedence 1, and DM overrides precedence 2. Lower-precedence facts remain visible for provenance but are not effective for the same dimension. Equally authoritative compatible tag facts may coexist; incompatible value kinds or disagreeing scalar measurements produce explicit conflicts and require adjudication.

For world-bound sessions, only annotations applicable to the current hex and intersecting spatial features are considered. Moving to another hex recomputes world truth rather than copying static facts into expedition state. Abstract-hex and non-spatial sessions can use expedition-current facts without an overworld.

`EnvironmentProcedureEvaluator` reads the expedition's pinned `CampaignProcedure` and maps effective environment facts to Phase 8 movement inputs. It does not dispatch on preset identity or current catalog defaults. Unknown values remain valid environment truth even when the pinned procedure can not interpret them.

Automatic interpretation is conservative. Multiple materially different terrain values, partially understood competing terrain, conflicting route inputs, unresolved scalar conflicts, conditional symbolic semantics, and weather models without a safe local formula produce explicit adjudication rather than an invented rule. Final movement is always composed by `MovementCapabilityComposer`; Phase 9 does not introduce a second movement engine.

Optional Rules Core integration remains an input provider above runtime. Provider results must preserve declared units, time bases, factor semantics, status, and source attribution. Provider output is converted into explicit movement contributors and passed through `MovementCapabilityComposer`; unsupported semantics are not reinterpreted locally.

See `docs/environment-context.md` for the complete ownership, precedence, conflict, provenance, persistence, and phase-boundary contract.

## Effects, resources, and survival

Phase 10 owns generalized `ExpeditionConsequence` lifecycle and persistent expedition effects. Structured consequences can represent time, movement, resource, exposure/fatigue, damage/endurance, navigation, encounter circumstances, persistent-effect changes, and campaign-defined output without overloading runtime pause state.

Phase 11 owns generic expedition resources and survival state. Resource and survival consumers receive explicit structured consequences rather than duplicating consequence logic inside later features. Forced-travel accounting observes successful authoritative travel mutations and converts elapsed travel only when the pinned procedure establishes a safe unit relationship.

Journey/process code delegates consequences through `ExpeditionConsequenceAggregateTransition`; it does not directly own resource, survival, or persistent-effect mutation.

See `docs/survival-resources.md`.

## Journey/process overlay

Phase 12 adds generic Journey Challenge / Complex Hazard state as an application-layer overlay over ordinary expedition execution.

A `JourneyProcessInstance` stores its process definition, an execution snapshot sampled from the exact pinned `journey.process` policy, current stage, per-stage progress/counters, pending actions, start/end references, and provenance. Process history remains valid if the current catalog changes because execution semantics are stored with the process.

Journey resolution supports numeric or explicit-state progress, arbitrary declared progress units, explicit approaches, current role/participant sampling, success/failure/complication counters, stage transitions, explicit process completion/failure/abandonment, and stable resolution identities. Failed attempts do not automatically mean failed processes.

`journey.events` is a separate focused policy supporting standalone and/or process-linked trigger opportunities. Triggering creates a durable occurrence but does not invent event content; event type, target, consequences, and notes remain explicit resolved input unless encoded by the stored contract.

`JourneyRuntimeIntegration` may observe newly completed authoritative watches and create stable pending opportunities. It never replaces or independently advances `CrawlRuntimeEngine`. Re-observation is idempotent through retained occurrence identities.

Journey events may retain Phase 9 environment snapshots for provenance/adjudication, but environment truth does not become an inferred modifier. Structured consequences are handed to the existing Phase 10/11 aggregate transition.

See `docs/journey-processes.md`.

## Session contexts

The three current context forms are deliberately distinct:

- `WorldBound` stores a real Overworld ID and may persist player knowledge/presentation state.
- `AbstractHex` stores its own name, orientation, and physical crawl scale without an Overworld.
- `NonSpatial` stores procedure/time/history state without spatial fields.

The deterministic runtime receives only the context data actually required for the active behavior.

## Persistence and asset architecture

PostgreSQL remains behind `IHexCrawlStore`; application/domain code has no Npgsql dependency.

The current pre-release schema is version 8. Earlier development schemas are intentionally rejected with a reset instruction rather than upgraded through retired compatibility paths.

The `overworlds` table stores the complete `OverworldDefinition` in `world_json`, including static environment annotations.

The `expeditions` table stores authoritative aggregate state, including:

- `procedure_json` — serialized pinned `CampaignProcedure`;
- `environment_json` — current/transient environment facts and explicit DM overrides;
- `effects_json` — Phase 10 consequence/effect authority;
- `resources_json` and `survival_json` — Phase 11 resource/survival authority;
- `journey_state_json` — Phase 12 process/event/resolution/history authority;
- `state_json`, `party_json`, `knowledge_json`, and generated-resolution state owned by their existing boundaries.

Derived effective environment context, conflict resolution projections, movement composition, provider output, and catalog reinterpretation are not persisted as competing authorities. `campaign_procedure_revisions.procedure_json` stores complete `CampaignProcedure` revisions.

Runtime history remains in `expedition_events`; persistence is aggregate snapshot plus retained ordered history, not event sourcing.

Map assets remain behind `IMapAssetStore` and are filesystem-backed at `/data/assets` in the initial production implementation. Domain records store provider-relative opaque asset keys rather than filesystem paths.

See `docs/postgresql-persistence.md`.

## Ownership and authentication

Persistent APIs require stable identity. Hosted requests use the Dorks & Dice Tool Host authentication contract; standalone development can enable an explicit configured identity.

Every world and crawl session is owner-scoped. Enumeration, direct loads, source-map mutation, environment authoring, asset retrieval, session creation, and session mutation enforce that ownership.

## Optimistic concurrency

Worlds and crawl sessions have monotonically increasing aggregate versions. Mutations carry `ExpectedVersion`; stale writes return conflicts rather than overwriting newer state.

Environment authoring follows the same aggregate boundaries: static annotation changes use the world version, while current facts and DM overrides use the expedition version. Consequence, resource, survival, and journey mutations use the expedition aggregate version.

Grid changes remain blocked once sessions depend on that world geometry.

## API contracts

The Web layer exposes resource DTOs rather than persistence rows.

Procedure-facing endpoints use generic contracts:

- preset catalog endpoints expose `ProcedurePresetContract` with a materialized `CampaignProcedureContract`;
- expedition detail/workbench responses expose the persisted `CampaignProcedureContract`;
- executable snapshots may additionally expose derived `ProcedureRuntimeContract` data;
- structural/declarative/incomplete snapshots remain representable even when current runtime binding is unavailable.

Generic procedures are not rejected merely because a retired contract shape can not represent them.

Environment endpoints expose typed facts, static annotation authoring, expedition current/override editing, effective context, conflicts, provenance, and the environment-to-movement evaluation. Ordinary expedition movement projections resolve the same effective context and use the same composition path as the environment workbench.

Survival/resource and journey endpoints expose focused typed operations against the same stored expedition aggregate. Journey endpoints start/resolve/close processes and create/resolve event opportunities; clients do not upload replacement raw aggregate JSON.

World and expedition routes retain the existing product organization, including world-bound creation, standalone session creation, full workbench routes, mapless trackers, and focused assistant mutations.

## Frontend and routing

Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation goes through `RenderLifecycle`; `MutationObserver` is not used.

`/` is the DM tools home. World authoring, full world-bound workbench routes, mapless session routes, and focused assistant routes remain separate product surfaces.

The expedition UI includes DM environment, survival/resource, and Journey / Challenge panels. The browser consumes server-derived policy/state and typed mutation contracts; it does not recreate environment precedence, movement composition, survival policy, consequence ownership, or journey transition logic.

The browser renders authoritative persisted procedure/session state. It does not create a second procedure model.

## Source-map boundary

A `SourceMapRepresentation` is evidence/presentation for part of one continuous overworld, never authoritative terrain or procedure state.

Registration, raster rendering, map import, and automatic grid-alignment work remain independent from generic procedure architecture. Source-map or feature categories are not silently promoted into environment facts; environment truth is explicitly annotated.

## Rules Core and other tools

Rules Core remains optional enrichment and may provide resolved inputs or canonical content, but it does not own the expedition procedure, environment authority, runtime, resources, or journey process state.

Character Sheet may provide capabilities. Block Initiative remains authoritative for tactical combat after encounter handoff. Neither tool changes Hex Crawl runtime ownership.

## Generic procedure implementation through Phase 12

The generic procedure work now includes removable named presets, campaign-owned pinned snapshots, generic runtime binding, proof procedures, Procedure Composer, typed participant activities, Phase 8 movement capability composition, Phase 9 generalized environment context/evaluation, Phase 10 generalized consequences/effects, Phase 11 resources/survival/forced travel, and Phase 12 multi-stage journey/process execution.

Important retained guarantees include:

- removable named presets and self-contained generic preset recipes;
- campaign-owned pinned snapshots;
- no runtime or focused-operation branching on system identity;
- handler/version-aware dispatch;
- explicit unresolved-input source sets;
- no broad false dependency reads;
- The One Ring journey graph without a fabricated repeating interval;
- D&D 2024 terrain represented as terrain-tag-to-symbolic maximum-pace state, including conditional Arctic Fast travel;
- environment truth independent from named-system identity;
- deterministic environment precedence/conflict reporting;
- no silent invention of unsupported terrain, route, weather, equipment, journey event, or provider semantics;
- one movement composition boundary through `MovementCapabilityComposer`;
- one structured consequence/application boundary reused by resources, survival, and journeys;
- stable journey resolution/event/runtime-occurrence identities for retry and restart safety;
- journey processes overlay deterministic travel rather than replacing it.

Still deferred are expanded encounter runtime/tactical circumstance consumption and battle-map ownership. Phase 12.5 is the next internal-human-testing readiness gate.

See `docs/generic-procedure-architecture.md`, `docs/environment-context.md`, `docs/movement-capability-composition.md`, `docs/survival-resources.md`, `docs/journey-processes.md`, `docs/postgresql-persistence.md`, `docs/generic-procedure-development-plan.md`, and `docs/phase-3-proof-matrix.md`.

## Shared map processing (Phase 13)

Raster decoding, bounded grayscale preparation, and hex-lattice detection are owned by the headless Dorks & Dice Surveyor service. The browser calls only the Hex Crawl API. Hex Crawl authorizes the world/source map, loads the authoritative stored raster, calls Surveyor through `IMapAnalysisService`, validates source dimensions/media type, and returns a typed observation to the browser.

Surveyor is computation-only. Hex Crawl retains physical scale, Wonderdraft reconciliation, preview, explicit Apply, grid identity, expedition safety, optimistic concurrency, and final persistence. Surveyor unavailability does not prevent Hex Crawl startup or manual map registration.
