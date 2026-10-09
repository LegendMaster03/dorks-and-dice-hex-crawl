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

Hex Crawl remains pre-release, but **human testing began after Phase 15**. The earlier disposable-development-data policy no longer governs ongoing releases.

**Every Tile Crawl development phase must leave the deployed tester application usable**, including already supported hex/world/expedition, map, procedure, and nonspatial operations. Temporary compatibility adapters and disabled feature gates are appropriate when needed to ship a working increment; obsolete models should be retired after the safe cutover rather than kept permanently.

**Preserving tester PostgreSQL data and independently stored map assets is the default.** Use versioned, verified migrations, pre-deployment backup/recovery planning, and existing-data regression tests. Neither deployment nor startup may automatically reset/drop/reinitialize a database. Unsupported schema versions must fail safely without modifying user data, with guidance for recovering or migrating them.

A destructive reset is possible only as an **exceptional last resort** when targeted migration/data repair and backup/restore alternatives have been assessed. It requires explicit project-owner authorization *for that reset* and a clear explanation to affected testers. Phase approval or merge approval is not reset authorization.

The active operational release gates and data policy are in `docs/tile-crawl-development-plan.md`.

## Project structure

- `HexCrawl.Domain` owns spatial, world, knowledge, presentation, source-map registration math, generic procedure contracts, environment fact types, expedition effect/resource/survival/journey state types, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, procedure materialization/revision, environment resolution/evaluation, movement composition, consequence/resource/survival/journey orchestration, optional provider integration, and persistence/blob ports.
- `HexCrawl.Infrastructure` implements PostgreSQL persistence, filesystem map assets, and Tool Host authentication redemption.
- `HexCrawl.Web` owns HTTP contracts, authentication middleware, route hosting, and the TypeScript application.

## Procedure architecture

`CrawlProcedurePresetDefinition` contains creation-time preset identity, display metadata, revision, and a `GenericProcedurePresetRecipe`.

Applying a preset materializes a standalone `CampaignProcedure` containing complete selected module/mechanic snapshots, parameters, a procedure-schema version, and the ruleset's canonical Delaney-Dress symbol. `ProcedureOriginMetadata` is optional provenance only.

The current procedure schema is **v1.2**. Built-in presets use the hexagonal
Delaney-Dress symbol `<1:1,1,1:6,3>` as their sole tiling identity.
The existing hexagonal runtime is unchanged and does not yet support other
spatial topologies. Older procedure revisions and expedition snapshots are
upgraded to v1.2 when loaded, and PostgreSQL schema migration 9 rewrites
the stored JSON from the previous notation to the new field.


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

The current application PostgreSQL schema is version **9** (see `PostgresSchemaMigrator.CurrentVersion`). Version 8 is upgraded to 9 transactionally; other unsupported historical versions currently fail with a legacy reset-oriented diagnostic. **That existing diagnostic is not the target policy for post-Phase-15 tester data** and must be replaced by actionable, non-destructive recovery guidance during the transition. Do not reset tester databases to resolve unsupported schema versions.

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

Phase 15 adds authoritative canonical-procedure JSON draft/validation/save endpoints. JSON editing reconstructs the same `CampaignProcedure`, runs domain validation, preserves procedure identity and optimistic concurrency, and stores ordinary immutable campaign procedure revisions rather than introducing a parallel JSON authority.

## Frontend and routing

Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation goes through `RenderLifecycle`; `MutationObserver` is not used.

Phase 15 presents one coherent expedition product rather than requiring the DM to assemble separate map/tracker/subsystem screens mentally. Spatial expeditions use a map-centered workspace with a primary current-action/status surface and contextual access to party, current map context, history, resources/effects, and journeys. Nonspatial and journey-oriented procedures adapt the primary surface to their actual stored procedure and do not fabricate map or interval state.

Procedure authoring exposes Compact, Advanced, and JSON presentations over the same authoritative `CampaignProcedure`. Compact uses tabletop concepts; Advanced exposes generic mechanic/contracts and diagnostics; JSON exposes the canonical representation through server-authoritative validation and revision commands. Preset browsing is a first-class discovery surface rather than only a select box.

Existing typed controllers remain authoritative for mutations. The richer workspace composes their state and actions without recreating environment precedence, movement composition, survival policy, consequence ownership, journey transitions, or runtime rules in presentation code.

See `docs/phase-15-design-architecture.md` for the Phase 15 interaction architecture, Compact persona, tracking-sheet research, responsive/accessibility rules, and Phase 15.1 extension points.

## Source-map boundary

A `SourceMapRepresentation` is evidence/presentation for part of one continuous overworld, never authoritative terrain or procedure state.

Registration, raster rendering, map import, and automatic grid-alignment work remain independent from generic procedure architecture. Source-map or feature categories are not silently promoted into environment facts; environment truth is explicitly annotated.

## Rules Core and other tools

Rules Core remains optional enrichment and may provide resolved inputs or canonical content, but it does not own the expedition procedure, environment authority, runtime, resources, or journey process state.

Character Sheet may provide capabilities. Block Initiative remains authoritative for tactical combat after encounter handoff. Neither tool changes Hex Crawl runtime ownership.

A future Battle Map tool and cross-tool tactical-map ownership/integration are outside the Hex Crawl roadmap. Phase 15 does not invent that product or its ownership model.

## Generic procedure implementation through Phase 15

The generic procedure work includes removable named presets, campaign-owned pinned snapshots, generic runtime binding, proof procedures, Procedure Composer, typed participant activities, Phase 8 movement capability composition, Phase 9 generalized environment context/evaluation, Phase 10 generalized consequences/effects, Phase 11 resources/survival/forced travel, Phase 12 multi-stage journey/process execution, Phase 13 shared Surveyor extraction, Phase 14 comprehensive remediation/encounter handoff, and the Phase 15 core UX/presentation architecture.

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
- journey processes overlay deterministic travel rather than replacing it;
- Compact, Advanced, and JSON remain presentations over one canonical `CampaignProcedure`;
- spatial and nonspatial workspaces follow stored procedure behavior rather than named preset identity.

Phase 13 extracted shared raster preparation and lattice detection into Surveyor. Phase 14 completed the broad architecture/correctness/security/performance review and expanded the server-authoritative v2 encounter handoff while Block Initiative remained tactical-combat authority. Phases 15, 15.1 and 15.1.1 established the current user-facing, tester-oriented procedure experience. The active next roadmap is **Tile Crawl Phases 16–21** in docs/tile-crawl-development-plan.md, including stabilization; the previously proposed separate Phase 15.5 milestone is superseded.

Battle Map ownership evaluation has been removed from the Hex Crawl roadmap and belongs to a future Battle Map roadmap once that product exists and is sufficiently mature.

See `docs/generic-procedure-architecture.md`, `docs/environment-context.md`, `docs/movement-capability-composition.md`, `docs/survival-resources.md`, `docs/journey-processes.md`, `docs/postgresql-persistence.md`, `docs/tile-crawl-development-plan.md`, `docs/phase-3-proof-matrix.md`, and `docs/phase-15-design-architecture.md`.

## Shared map processing (Phase 13)

Raster decoding, bounded grayscale preparation, and hex-lattice detection are owned by the headless Dorks & Dice Surveyor service. The browser calls only the Hex Crawl API. Hex Crawl authorizes the world/source map, loads the authoritative stored raster, calls Surveyor through `IMapAnalysisService`, validates source dimensions/media type, and returns a typed observation to the browser.

Surveyor is computation-only. Hex Crawl retains physical scale, Wonderdraft reconciliation, preview, explicit Apply, grid identity, expedition safety, optimistic concurrency, and final persistence. Surveyor unavailability does not prevent Hex Crawl startup or manual map registration.
