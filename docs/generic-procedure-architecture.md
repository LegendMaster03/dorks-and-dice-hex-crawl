# Generic Procedure Architecture

This document describes the current generic procedure architecture through Phase 3 of `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not a runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure embeds complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, resolved parameters, and campaign overrides. `ProcedureOriginMetadata` is optional informational provenance and is never consulted to determine runtime behavior.

The authoritative runtime path is:

`GenericProcedurePresetRecipe -> CampaignProcedure -> handler/version-aware generic binding -> crawl-session state/results`

`CampaignProcedure` is the only supported persisted procedure representation for current-format expeditions.

## Pre-release compatibility policy

Hex Crawl is pre-release development software. Development-era API, persistence, UI, and internal-model compatibility is not preserved unless a specific compatibility requirement is deliberately approved.

When an obsolete representation conflicts with the target architecture, remove it rather than maintaining parallel models, migration adapters, or compatibility projections. Breaking development migrations and database resets are acceptable while no user-data compatibility commitment exists.

Meaningful migrations for real user data must be evaluated separately once the product reaches a stage where compatibility commitments exist.

## Generic definitions

`ProcedureModuleDefinition` describes a composable procedure slot. It carries:

- a generic key and category;
- display name and purpose;
- execution stage;
- declared outputs and any truly module-wide dependencies;
- compatible mechanic keys;
- configuration schema;
- presentation metadata.

`MechanicDefinition` describes reusable behavior. It carries:

- a generic key and display metadata;
- behavior-specific input and output contracts;
- parameter schema;
- execution-handler identifier;
- compatibility tags;
- automation level;
- explicit mechanic version.

Neither type contains preset identity or named-system identity.

Phase 3 intentionally keeps module shells narrow. A module family does not claim every input that any possible mechanic might consume. The selected mechanic input contract is authoritative for behavior-specific reads.

## Materialized campaign procedures

`CampaignProcedure` is the campaign-owned procedure definition. It has a stable `ProcedureId`, an explicit revision, a generic key/name, materialized modules, and campaign overrides.

Each selected module embeds its complete module definition, mechanic definition, and parameter set. Persistence therefore does not require the current global module catalog, mechanic catalog, or originating preset to reconstruct a saved procedure.

Removing, renaming, or revising a preset does not mutate an existing procedure. A campaign revision remains pinned until explicitly revised.

## Generic execution

`GenericProcedureRuntime.Bind` creates an ephemeral runtime binding from a pinned `CampaignProcedure` when every mechanic required for the current deterministic runtime is executable.

Runtime support is explicit for the embedded `(ExecutionHandler, Version)` combination. The runtime does not:

- resolve the origin preset;
- branch on system or edition identity;
- replace a persisted mechanic with the current catalog definition;
- silently reinterpret an unknown handler;
- silently downgrade an unsupported version.

Unsupported handlers and versions remain preserved as procedure data and fail clearly when execution is attempted.

Recognized declarative Phase 3 mechanics use `procedure.declarative-contract` version 1. They may validate and persist as structural proof contracts, but they are intentionally non-executable. A declarative mechanic can not be `Automatic`.

The deterministic `CrawlRuntimeEngine` executes an executable `CampaignProcedure` through the generic binding. Existing movement, navigation, watch lifecycle, encounter timing, pause/resume, and event-transition logic remains reusable, while behavior selection comes from the materialized generic snapshot.

## Runtime authority

`StoredExpedition.CampaignProcedure` is required current-format data. Runtime procedure resolution has one path:

`StoredExpedition.CampaignProcedure -> GenericProcedureRuntime.Bind -> deterministic runtime behavior`

A missing campaign procedure is invalid current data. There is no historical-profile fallback or synthesized compatibility procedure.

Origin metadata remains optional and informational. Runtime execution remains valid when origin metadata is removed and when the source preset no longer exists.

## Campaign overrides and revisions

`CampaignProcedureOverride` targets a generic module. An override can change parameters or select another compatible generic mechanic/version.

Applying overrides creates a new `CampaignProcedure` revision while retaining the same `ProcedureId`. Previous revisions remain unchanged. Existing expeditions keep their pinned snapshot until explicitly updated.

`CampaignProcedureService` provides the application boundary for materializing, revising, and loading procedure revisions. The full visual Procedure Composer remains Phase 4 work.

## Dependency model

A materialized procedure evaluates selected behavior contracts rather than broad family assumptions.

- a required selected-module producer that is absent and has no permitted fallback produces `MissingRequiredProducer`;
- a real unresolved input with permitted sources produces `UnresolvedInput`;
- unresolved diagnostics expose the complete allowed-source flag set;
- optional provider, DM/manual, and external/runtime-state sources are attached only to inputs that genuinely allow those sources;
- broad fallback declarations are not used to hide incorrect dependency graphs.

Produced-but-unused outputs remain diagnostics rather than destructive normalization.

## Phase 3 structural proof mechanics

Phase 3 adds generic structural families for movement budgets, terrain relationships, participant activities, navigation outcomes, encounter scheduling, resources, foraging, camping, forced travel, persistent effects, journey events, and multi-stage journey processes.

These contracts prove that materially different systems can be represented without system-specific runtime classes. Later execution engines remain deferred according to the development plan.

The One Ring proof is intentionally independent of a fabricated repeating interval: journey progress feeds progress-triggered events, transient event effects feed persistent effects, and role assignment does not require interval or movement-budget state.

The D&D 2024 terrain proof uses a generic `maximum-pace` relationship. Terrain tags map to symbolic pace states, including `arctic=fast-if-appropriately-equipped`, rather than using pace names as terrain keys or numeric costs.

## Persistence and restart reproducibility

PostgreSQL stores one authoritative procedure representation for expeditions:

- `expeditions.procedure_json` — required serialized `CampaignProcedure` snapshot.

Campaign procedure revisions also store their `CampaignProcedure` snapshot in `campaign_procedure_revisions.procedure_json`.

There is no dual generic/retired procedure storage, consistency projection, or profile-only recovery path.

The pre-release schema is version 4. Databases from earlier development procedure schemas are intentionally rejected with a reset instruction instead of carrying obsolete migration complexity forward.

Restarting the application reloads the exact persisted generic module/mechanic snapshots. Mechanic versions, handlers, automation levels, parameters, source requirements, overrides, and origin metadata remain pinned.

## HTTP representation

Current HTTP surfaces represent procedure state with `CampaignProcedureContract`. The contract exposes generic procedure identity, revision, materialized modules, handler/version metadata, automation levels, and parameters.

When the snapshot can bind to the currently supported deterministic runtime, the API may additionally expose a derived `ProcedureRuntimeContract`. Structural, declarative, incomplete, and future snapshots remain representable even when runtime binding is unavailable.

An API contract does not fabricate a second procedure representation merely because a generic snapshot is not currently executable.

## Rules Core

Rules Core remains optional enrichment. Generic procedure execution does not depend on Rules Core. The existing travel/navigation adapter can resolve optional external inputs, but procedure policy is read from the pinned `CampaignProcedure`, and missing external results are never invented by the runtime.

## Scope boundary

Phase 3 proves the architecture and supplies structural mechanics. It does not implement the full Procedure Composer, typed participant-activity execution, generalized movement capability composition, environment execution, generalized consequence/effect execution, survival/resource execution, journey-process execution, expanded encounter runtime, or battle-map ownership.
