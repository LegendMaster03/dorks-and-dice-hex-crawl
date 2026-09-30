# Generic Procedure Architecture

This document describes the current generic procedure architecture through Phase 7 of `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not a runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure embeds complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, resolved parameters, and campaign overrides. `ProcedureOriginMetadata` is optional informational provenance and is never consulted to determine runtime or participant-assignment behavior.

The authoritative procedure path is:

`GenericProcedurePresetRecipe -> CampaignProcedure -> handler/version-aware binding or focused stored-contract projection -> crawl-session state/results`

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

Module shells remain narrow. A module family does not claim every input that any possible mechanic might consume. The selected mechanic input contract is authoritative for behavior-specific reads.

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

The current deterministic executable handler identities are behavior-oriented contracts persisted exactly with their mechanic versions:

- `procedure.time.fixed-interval`;
- `procedure.movement.resolution`;
- `procedure.movement.hex-progress`;
- `procedure.navigation.check-policy`;
- `procedure.encounter.cadence`;
- `procedure.resolution-helpers`.

These identifiers describe executable behavior. They do not encode a preset, system, edition, or retired procedure representation.

Recognized declarative mechanics use `procedure.declarative-contract` version 1. They may validate and persist as structural contracts, but they are intentionally non-executable and can not be `Automatic`.

The deterministic `CrawlRuntimeEngine` executes a fully bindable `CampaignProcedure` through the generic binding. Existing movement, navigation, watch lifecycle, encounter timing, pause/resume, and event-transition logic remains reusable, while behavior selection comes from the materialized generic snapshot.

A focused operation may interpret one supported stored contract without binding unrelated structural mechanics. This is still procedure-snapshot authority, not a second procedure model. Phase 7 uses this boundary for `party.activities` policy projection and for focused non-spatial interval-duration bookkeeping.

## Runtime authority

`StoredExpedition.CampaignProcedure` is required current-format data. Full runtime procedure resolution has one path:

`StoredExpedition.CampaignProcedure -> GenericProcedureRuntime.Bind -> deterministic runtime behavior`

Focused projections likewise begin from `StoredExpedition.CampaignProcedure` and read only the selected stored module/mechanic/parameters required by that operation.

A missing campaign procedure is invalid current data. There is no historical-profile fallback or synthesized compatibility procedure.

Origin metadata remains optional and informational. Runtime execution and participant-activity policy remain unchanged when origin metadata is removed and when the source preset no longer exists.

## Campaign overrides and revisions

`CampaignProcedureOverride` targets a generic module. An override can change parameters or select another compatible generic mechanic/version.

Applying overrides creates a new `CampaignProcedure` revision while retaining the same `ProcedureId`. Previous revisions remain unchanged. Existing expeditions keep their pinned snapshot until explicitly updated.

`CampaignProcedureService` provides the application boundary for materializing, revising, and loading procedure revisions. The Procedure Composer edits that same `CampaignProcedure` model rather than maintaining a second UI procedure authority.

## Dependency model

A materialized procedure evaluates selected behavior contracts rather than broad family assumptions.

- a required selected-module producer that is absent and has no permitted fallback produces `MissingRequiredProducer`;
- a real unresolved input with permitted sources produces `UnresolvedInput`;
- unresolved diagnostics expose the complete allowed-source flag set;
- optional provider, DM/manual, and external/runtime-state sources are attached only to inputs that genuinely allow those sources;
- broad fallback declarations are not used to hide incorrect dependency graphs.

Produced-but-unused outputs remain diagnostics rather than destructive normalization.

## Structural proof mechanics

The generic structural families cover movement budgets, terrain relationships, participant activities, navigation outcomes, encounter scheduling, resources, foraging, camping, forced travel, persistent effects, journey events, and multi-stage journey processes.

These contracts prove that materially different systems can be represented without system-specific runtime classes. Later execution engines remain deferred according to the development plan.

The One Ring proof is intentionally independent of a fabricated repeating interval: journey progress feeds progress-triggered events, transient event effects feed persistent effects, and role assignment does not require interval or movement-budget state.

The D&D 2024 terrain proof uses a generic `maximum-pace` relationship. Terrain tags map to symbolic pace states, including `arctic=fast-if-appropriately-equipped`, rather than using pace names as terrain keys or numeric costs.

## Typed participant activity state

Phase 7 turns the structural `participant.activity-state` output into real typed expedition state without changing the activity mechanics to automatic execution.

The exact selected `party.activities` module in the expedition's pinned `CampaignProcedure` defines the policy. A supported policy projection preserves:

- selected mechanic key/version and handler;
- `assignmentScope`;
- `activityBudgetModel`;
- `activityKeys`;
- `roleKeys`.

The projection does not consult origin preset identity or current catalog parameter values. A future/unsupported mechanic remains distinguishable from no policy and from the two currently supported generic activity-policy mechanics.

`CrawlPartySheet` owns current mutable assignment state. `ParticipantActivityAssignment` uses a stable ID, generic `Party`/`Participant`/`Role` scope, an optional real participant reference where structurally appropriate, generic string activity/role keys, and optional context note. The domain does not encode named role enums or infer role-to-activity mappings, exclusivity, required roles, or activity capacity.

A party-wide assignment requires no synthetic participant. Participant and role assignments reference existing party members. Member removal can cascade through the UI, while domain validation independently rejects dangling references.

The former separately persisted `DefaultNavigatorMemberId` is removed. A navigator is an ordinary role assignment when the pinned procedure exposes that key.

The free-form watch activity list is also removed. Spatial watches and real non-spatial intervals snapshot the current typed assignments when the interval begins. Later edits to the party sheet do not mutate that active snapshot.

Journey-role state remains party/session state and does not require an interval. The One Ring guide/hunter/lookout/scout roles can therefore be edited without fabricating `time.interval`, requiring `movement.budget`, or binding the complete structural procedure.

Phase 7 does not execute downstream movement penalties, foraging, camping, resources, fatigue/effects, or journey events. Later mechanics may consume `participant.activity-state` only through explicit generic contracts.

## Persistence and restart reproducibility

PostgreSQL stores one authoritative procedure representation for expeditions:

- `expeditions.procedure_json` — required serialized `CampaignProcedure` snapshot.

Campaign procedure revisions also store their `CampaignProcedure` snapshot in `campaign_procedure_revisions.procedure_json`.

Current typed participant assignments remain inside the existing expedition party state. Active spatial/non-spatial interval assignment snapshots remain inside runtime state. There is no second activity persistence service or dual generic/retired procedure storage.

The pre-release schema may reject earlier development shapes and require a reset rather than carrying retired navigator or free-form activity compatibility infrastructure.

Restarting the application reloads the exact persisted generic module/mechanic snapshots, party assignments, and active interval snapshots. Mechanic versions, handlers, automation levels, parameters, source requirements, overrides, and origin metadata remain pinned.

## HTTP representation

Current HTTP surfaces represent procedure state with `CampaignProcedureContract`. The contract exposes generic procedure identity, revision, materialized modules, handler/version metadata, automation levels, and parameters.

When the snapshot can bind to the currently supported deterministic runtime, the API may additionally expose a derived `ProcedureRuntimeContract`. Structural, declarative, incomplete, and future snapshots remain representable even when runtime binding is unavailable.

Expedition detail also exposes `ParticipantActivityPolicyContract` as a derived projection of the exact pinned `CampaignProcedure`, plus typed current party assignments and typed active-interval assignment snapshots. These are not alternate procedure definitions.

## Rules Core and Character Sheet

Rules Core remains optional enrichment. Generic procedure execution and participant activity state do not depend on Rules Core. The travel/environment provider boundary can resolve optional external inputs, but procedure policy is read from the pinned `CampaignProcedure`, and missing external results are never invented by the runtime.

Character Sheet remains optional. `ExternalCharacterId` may link a party member to external character state, but Phase 7 does not require that service, copy character statistics into Hex Crawl, or perform Phase 8 movement capability composition.

## Current scope boundary

Implemented through Phase 7 are the generic procedure/preset foundation, native current-core execution, proof catalog, Procedure Composer, generated procedure reference, optional provider boundary, and typed participant activity/role state with active-interval snapshots.

Still deferred are generalized movement capability composition, environment execution, generalized consequence/effect execution, survival/resource execution, activity-driven foraging/camping effects, multi-stage journey execution, expanded encounter runtime, and battle-map ownership.