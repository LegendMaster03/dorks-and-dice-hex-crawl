# Generic Procedure Architecture

This document describes the current generic procedure architecture through Phase 8 of `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not a runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure embeds complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, resolved parameters, and campaign overrides. `ProcedureOriginMetadata` is optional informational provenance and is never consulted to determine runtime, participant-assignment, or movement-composition behavior.

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

A focused operation may interpret one supported stored contract without binding unrelated structural mechanics. This is still procedure-snapshot authority, not a second procedure model. Phase 7 uses this boundary for `party.activities` policy projection and focused non-spatial interval-duration bookkeeping. Phase 8 uses it for `movement.budget` and `movement.terrain` composition policy.

## Runtime authority

`StoredExpedition.CampaignProcedure` is required current-format data. Full runtime procedure resolution has one path:

`StoredExpedition.CampaignProcedure -> GenericProcedureRuntime.Bind -> deterministic runtime behavior`

Focused projections likewise begin from `StoredExpedition.CampaignProcedure` and read only the selected stored module/mechanic/parameters required by that operation.

A missing campaign procedure is invalid current data. There is no historical-profile fallback or synthesized compatibility procedure.

Origin metadata remains optional and informational. Runtime execution, participant-activity policy, and movement-composition policy remain unchanged when origin metadata is removed and when the source preset no longer exists.

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

Phase 7 does not execute downstream foraging, camping, resources, fatigue/effects, or journey events. Later mechanics may consume `participant.activity-state` only through explicit generic contracts.

## Movement capability composition

Phase 8 adds a focused movement-composition layer without making structural movement mechanics automatically executable. Ownership is:

```text
CampaignProcedure
    owns movement.budget and movement.terrain policy
        ↓
CrawlPartySheet / expedition state
    owns explicit/manual movement contributors and PartyMovementReference
        ↓
Optional providers
    may supply a missing resolved capability
        ↓
MovementCapabilityComposer
    derives current effective capability, limiting source, provenance, and optional watch-distance suggestion
        ↓
Deterministic runtime
    receives resolved movement input and never calls a provider
```

`MovementCompositionPolicyResolver` reads only the exact materialized movement modules stored on the pinned `CampaignProcedure`. Preset identity, origin metadata, and current catalog defaults are not inputs. Unknown mechanic versions or unsupported stored semantics remain unsupported rather than being replaced with current definitions.

`MovementCapabilityContributor` is expedition-owned generic state. Contributor kinds cover participants, mounts, vehicles, loads, travel modes, terrain/route resolutions, resolved environment consequences, resolved persistent-effect consequences, and DM override. Operations distinguish base/replacement, multiplier, additive change, cap, floor, cost, and symbolic limit. Contributor keys and movement-unit identities are generic strings rather than named-system enums.

Participant movement respects `CountsTowardPartyMovement`. Mounts and vehicles can replace the movement unit of assigned riders/passengers; replaced participants do not also independently limit the same party, and unassigned conveyances do not affect the party. Compatible physical units are converted only through explicit `DistanceUnit` metadata; custom or incompatible conversion is not guessed.

Load and travel-mode contributors compose in deterministic stages. A travel-mode contributor is selected by the current generic pace/mode key. Environment and persistent-effect contributors are accepted only as already resolved movement consequences in Phase 8; Phase 9 owns generalized environment context and Phase 10 owns effect lifecycle/evaluation.

Pinned terrain semantics are preserved rather than flattened. Numeric `multiplier` and `distance-per-hour-multiplier` relationships can compose numerically. `activity-cost`, `movement-points-per-distance`, `hexes-per-quarter-day`, and `terrain-difficulty` remain their own budget/cost semantics and require explicit resolution where needed. `maximum-pace` remains symbolic; in particular, D&D 2024 `arctic=fast-if-appropriately-equipped` is retained as a conditional limit and is not converted into an unconditional numeric factor.

`PartyMovementReference` remains valid. When no composition policy/capability is available it can be the authoritative explicit movement source; when automatic composition is incomplete it can be a fallback; when a typed capability already resolves movement it is retained as informational reference and is not double-counted. An explicit DM replacement contributor has final precedence and retains pre-override value and provenance.

Optional provider adapters are input suppliers, not alternate movement engines. `ProcedureResolutionProviderEnricher` first honors explicit expected-distance input and locally resolved/manual composition. A provider may supply a missing generic physical capability and a semantically compatible resolved terrain factor; the result is fed back through `MovementCapabilityComposer`. Provider unavailability can fall back to an explicit party movement reference where one is usable. Provider status and provenance remain explicit.

Physical movement may produce a server-side watch-distance suggestion. Per-hour values use the exact pinned interval or active-watch remaining duration. Non-distance budgets such as Hexploration activities, quarter-day budgets, movement points, and journey progress remain explicitly non-distance. The One Ring journey-progress proof never fabricates a repeating interval or physical watch distance.

## Persistence and restart reproducibility

PostgreSQL stores one authoritative procedure representation for expeditions:

- `expeditions.procedure_json` — required serialized `CampaignProcedure` snapshot.

Campaign procedure revisions also store their `CampaignProcedure` snapshot in `campaign_procedure_revisions.procedure_json`.

Current typed participant assignments and explicit/manual movement contributors remain inside the existing expedition party state. Active spatial/non-spatial interval assignment snapshots remain inside runtime state. Generated procedure resolutions retain their own resolved values and provenance when later party edits change current movement state. There is no provider cache and no second movement persistence service.

The pre-release schema may reject earlier development shapes and require a reset rather than carrying retired navigator, free-form activity, or superseded movement compatibility infrastructure.

Restarting the application reloads the exact persisted generic module/mechanic snapshots, party assignments, movement contributors, explicit movement reference, generated-resolution history, and active interval snapshots. Mechanic versions, handlers, automation levels, parameters, source requirements, overrides, and origin metadata remain pinned.

## HTTP representation

Current HTTP surfaces represent procedure state with `CampaignProcedureContract`. The contract exposes generic procedure identity, revision, materialized modules, handler/version metadata, automation levels, and parameters.

When the snapshot can bind to the currently supported deterministic runtime, the API may additionally expose a derived `ProcedureRuntimeContract`. Structural, declarative, incomplete, and future snapshots remain representable even when runtime binding is unavailable.

Expedition detail also exposes `ParticipantActivityPolicyContract` and `MovementCapabilityCompositionContract` as derived projections of the exact pinned `CampaignProcedure` plus current expedition-owned state. Party responses include typed movement contributors and explicit `PartyMovementReference`; active interval assignment snapshots remain typed runtime state. These are not alternate procedure definitions.

The browser consumes the server-derived movement composition. It may convert an already-resolved physical suggestion into the displayed session unit, but it does not independently recompute party limiting, watch duration, terrain semantics, provider precedence, or reference fallback.

## Rules Core and Character Sheet

Rules Core remains optional enrichment. Generic procedure execution, participant activity state, and movement composition do not depend on Rules Core. The travel/environment provider boundary can resolve optional missing inputs, but procedure policy is read from the pinned `CampaignProcedure`, and missing external results are never invented by the runtime.

Character Sheet remains optional. `ExternalCharacterId` is only an optional capability lookup key. Hex Crawl movement contracts do not import Character Sheet DTOs, inventories, or stat blocks; a DM can enter movement contributors or an explicit party movement reference directly.

## Current scope boundary

Implemented through Phase 8 are the generic procedure/preset foundation, native current-core execution, proof catalog, Procedure Composer, generated procedure reference, optional provider boundary, typed participant activity/role state with active-interval snapshots, and generic movement capability composition with server-derived suggestions and provenance.

Still deferred are Phase 9 generalized environment context/execution, Phase 10 generalized consequence/effect lifecycle, Phase 11 forced-travel/resource/survival execution, activity-driven foraging/camping effects, Phase 12 multi-stage journey execution, expanded encounter runtime, and battle-map ownership.
