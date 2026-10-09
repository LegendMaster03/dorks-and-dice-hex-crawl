# Generic Procedure Architecture

This document records the implemented generic procedure architecture established through Phase 15. It remains architectural reference material, not an active development roadmap. The current transition plan is `docs/tile-crawl-development-plan.md`; completed implementation history remains available through Git history and the focused Phase 15 design/implementation documents.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not a runtime authority. It contains a `GenericProcedurePresetRecipe` selecting generic modules, versioned mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure embeds complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, resolved parameters, and campaign overrides. `ProcedureOriginMetadata` is optional informational provenance and is never consulted to determine runtime, participant-assignment, movement-composition, environment-evaluation, survival/resource, journey, or Phase 15 presentation behavior.

The authoritative procedure path is:

`GenericProcedurePresetRecipe -> CampaignProcedure -> handler/version-aware binding or focused stored-contract projection -> crawl-session state/results`

`CampaignProcedure` is the only supported persisted mechanical procedure representation for current-format expeditions.

Preset recipes must contain complete generic behavior parameters before materialization. The materializer copies those values into a standalone campaign-owned snapshot; there is no preset-specific completion layer at runtime.

## Phase 15 presentation boundary

Compact, Advanced, and JSON are three presentations over the same canonical `CampaignProcedure`:

```text
CampaignProcedure
      |
      +-- Compact
      +-- Advanced
      +-- JSON
```

Compact presents ordinary tabletop concepts. Advanced progressively exposes mechanic identity/version, execution support, automation level, parameters, contracts, dependencies, provenance, and diagnostics. JSON exposes the canonical procedure representation for expert editing.

None is a separate procedure authority. Browser presentation state can organize or explain the procedure, but authoritative procedure changes still pass through application/server validation and immutable campaign procedure revision creation.

The JSON path parses with persistence-compatible enum/string conventions, validates the reconstructed domain model, protects procedure identity, applies optimistic concurrency, and rejects semantically unchanged revisions.

Phase 15.1 may later add Guided presentation around/enriching Compact. It must not introduce a separate procedure model.

See `docs/phase-15-design-architecture.md`.

## Pre-release compatibility policy

Hex Crawl remains pre-release.

Before Phase 15 acceptance, obsolete development API/persistence/UI/internal representations should be removed rather than preserved through fallback infrastructure, and development database resets remain acceptable.

After Phase 15 acceptance, internal human testing begins. Tester data has a human cost, so a straightforward architecture-preserving migration should be preferred before breaking persisted tester data where practical. This preference does not justify parallel obsolete models or permanent compatibility infrastructure, and resets remain possible when architecture warrants them.

## Generic modules and mechanics

A procedure is composed from generic modules and mechanics whose keys describe behavior rather than game identity. Mechanic definitions carry versioned semantics and, where executable, explicit handler/version contracts.

Runtime and focused application projections follow contracts embedded in the pinned `CampaignProcedure`. They do not ask which named preset originally produced it.

Unknown handlers, future versions, declarative-only mechanics, and unsupported semantic values remain representable. Hex Crawl reports them as unsupported/unresolved instead of replacing them with a current catalog default.

## Runtime binding

`GenericProcedureRuntime.Bind` is the deterministic binding boundary for mechanics owned by `CrawlRuntimeEngine`.

The runtime assembly receives only data required for deterministic crawl progression. It does not own repositories, Rules Core clients, world lookup, Character Sheet lookup, environment providers, resource stores, or journey content.

Focused application operations may project one supported contract from the stored procedure without binding the entire procedure. This is used for participant activity state, movement capability composition, environment-to-movement interpretation, resources/survival, journey process/event execution, focused interval policy, and procedure authoring/validation.

Focused projection is still pinned-procedure evaluation. It is not preset dispatch.

## Campaign-owned procedure revisions

Campaign procedure revisions persist complete `CampaignProcedure` snapshots. Expeditions persist their own complete pinned procedure snapshot as `procedure_json`.

A later edit or catalog change therefore does not silently mutate an existing expedition. Explicit revision/application operations are required to change procedure authority.

Origin metadata may identify the recipe or source that created a snapshot, but origin is never used as a runtime or presentation switch.

## Procedure authoring

The Procedure workspace begins with a first-class preset browser and supports Compact, Advanced, and JSON editing.

Compact organizes customization around tabletop domains such as time/travel structure, movement, party organization, navigation, exploration, encounters, survival, and journeys. It translates dependency problems into domain-facing actionable messages rather than requiring implementation-key literacy.

Advanced reuses generic mechanic/parameter/dependency capabilities where technical control is appropriate. JSON edits the same canonical procedure and never bypasses server validation or storage boundaries.

Preset provenance is useful to humans but remains secondary metadata. An existing materialized procedure remains renderable and executable if its originating preset later changes or disappears.

## Generated procedure reference

Generated reference content is derived from the pinned generic procedure. It explains the mechanics that the expedition actually owns and retains source/provenance information where available.

Reference generation does not become a second rules model. Structural/declarative mechanics can be described even when no current executable handler exists.

## Typed participant activities and roles

Participant assignment is generic and procedure-owned.

Typed activity/role state records participant choices against contracts embedded in the pinned procedure. Active-interval snapshots preserve inputs used for an interval so later party edits do not retroactively rewrite completed or in-progress resolution history.

Participant state does not dispatch on named-system identity and does not import Character Sheet objects. `ExternalCharacterId` remains an optional external lookup key only.

Role-driven journey resolution samples current typed assignments at the explicit resolution boundary and retains participant/assignment snapshots in journey history. Ambiguous role ownership requires explicit participant selection.

## Movement capability composition

Phase 8 introduced one generic movement-composition boundary: `MovementCapabilityComposer`.

The composer reads movement policy from the exact stored `CampaignProcedure` and combines typed current capability/consequence inputs. Inputs can represent participants, mounts/vehicles, load consequences, pace/mode, terrain/route, environment, persistent effects, and an explicit DM final override.

Composition preserves non-distance semantics. Activity costs, movement points, quarter-day budgets, terrain difficulty, symbolic maximum pace, and journey progress are not flattened into physical speed unless the pinned contract explicitly provides a safe physical interpretation.

`PartyMovementReference` remains a supported explicit/fallback authority with a declared role in each result rather than being silently added to composed movement.

Provider-backed physical rates or factors are converted into explicit typed contributors and still pass through `MovementCapabilityComposer`. Providers do not own final movement composition.

See `docs/movement-capability-composition.md`.

## Environment context and evaluation

Phase 9 provides a generic environment layer above deterministic runtime and upstream of Phase 8 movement composition.

Environment truth is ruleset-neutral. `EnvironmentFact` uses an open dimension string and either a tag or explicit numeric measurement/unit. Common dimensions include terrain, route, weather, visibility, elevation, depth, water, current, temperature, hazard, and regional effect, but the set is intentionally extensible.

### Ownership

Static environment truth belongs to `OverworldDefinition.EnvironmentAnnotations`. An annotation explicitly targets the world, one hex, or one spatial feature.

Transient/current conditions and explicit DM decisions belong to `StoredExpedition.Environment`:

- `CurrentFacts` for current/transient conditions;
- `Overrides` for explicit DM overrides.

`ExpeditionState` remains traversal/runtime state. Static world truth is not copied into it.

### Resolution

`EnvironmentContextResolver` computes effective context from current authoritative state.

For world-bound sessions it considers world annotations, current-hex annotations, and annotations on spatial features intersecting the current hex. Abstract-hex and nonspatial sessions can resolve expedition-current facts without an overworld.

Precedence is deterministic:

1. static world/hex/spatial-feature facts;
2. expedition current/transient facts;
3. DM overrides.

Higher precedence supersedes lower facts for the same dimension, while lower facts remain visible as non-effective provenance. Compatible same-authority tags can coexist. Incompatible value kinds or disagreeing same-authority scalar measurements produce explicit conflicts and require adjudication rather than an arbitrary tie-break.

Moving to a new hex recomputes applicable static world truth. Effective context is derived, not persisted.

### Pinned-procedure interpretation

`EnvironmentProcedureEvaluator` maps effective environment facts into generic movement inputs using the expedition's exact pinned `CampaignProcedure`.

It interprets terrain, route, and weather for movement only where the stored procedure defines a safe contract. Unknown facts remain valid environment truth and unsupported semantics remain explicit.

Conditional semantics such as "fast if appropriately equipped" remain conditional. Environment truth does not fabricate equipment/capability state.

The normal path is:

`authoritative environment state -> EnvironmentContextResolver -> EnvironmentProcedureEvaluator -> MovementCompositionInput -> MovementCapabilityComposer`

Journey events may capture effective environment facts as historical resolution context. This is provenance/adjudication context, not a competing current-environment authority.

See `docs/environment-context.md`.

## Generalized effects and consequences

Phase 10 provides one structured consequence lifecycle. `ExpeditionConsequence` carries stable identity, category, target, typed components, provenance, source reference, and lifecycle status.

Consequences can describe time delay, movement change, resource change, exposure/fatigue, damage/endurance, navigation change, encounter circumstance, persistent-effect change, or custom output. Later subsystems reuse this pipeline rather than copying consequence logic.

Persistent effects are expedition state and remain distinct from deterministic runtime pause reasons.

## Resources, survival, and forced travel

Phase 11 adds generic resource inventory/audit state plus forced-travel, exposure, and camp state.

Resource/survival operations are focused projections over exact pinned generic contracts. They do not infer publisher-specific rates or tables from preset identity. When the stored procedure lacks a complete formula, the DM/provider supplies explicit resolved input.

`ForcedTravelAccounting` observes successful authoritative travel mutations. It converts elapsed travel only when the pinned procedure establishes a safe relationship to the forced-travel unit.

See `docs/survival-resources.md`.

## Multi-stage journey processes

Phase 12 adds generic Journey Challenge / Complex Hazard execution without adding a second crawl engine.

`journey.process` focused policy projects stage/progress/completion/role/watch-integration behavior from the expedition's exact pinned procedure. Starting a process persists both the process definition and a `JourneyProcessExecutionSnapshot`, so an in-progress process does not change when catalog recipes later change.

Process definitions use open stage, approach, role, outcome, destination, route, and location keys. Progress can be numeric in any explicit unit or an explicit symbolic state. Stage completion and transition behavior remain explicit stored semantics.

Resolution IDs are stable/idempotent. Resolutions preserve progress before/after, counter deltas, participant/role snapshots, transitions, consequence IDs, event IDs, and provenance. A failed attempt does not automatically fail a process.

`journey.events` is an independent focused policy. Triggering creates a durable unresolved occurrence; event content and consequences remain explicit resolved input unless the pinned procedure actually encodes them.

`JourneyRuntimeIntegration.ObserveCompletedWatches` runs only after an authoritative runtime mutation. It can create stable event/process-resolution opportunities but does not advance runtime or process progress itself. Retained runtime-occurrence identities prevent duplicate opportunities on retry/restart.

Journey-generated consequences flow through the existing Phase 10/11 aggregate transition. Phase 14 projects relevant pending circumstances and linked expedition context through the server-authoritative v2 encounter handoff without moving tactical-combat authority into Hex Crawl.

See `docs/journey-processes.md`.

## Optional travel/environment providers

Rules Core remains optional enrichment. Generic procedure execution, participant state, environment authority, movement composition, resources, and journey state do not depend on Rules Core.

`ITravelEnvironmentProvider` exposes typed provider catalog/resolution contracts. Provider states keep unavailable, failed, unsupported, input-required, not-applicable, requires-adjudication, and resolved cases distinct.

Provider input/output boundaries preserve units, time bases, factor semantics, missing-input keys, provider identity, and source attribution. Hex Crawl refuses to reinterpret output whose declared semantics do not match the requested generic operation.

Provider output is converted into explicit application inputs/contributors. Provider-native state is not persisted as procedure or environment authority.

## Character Sheet boundary

Character Sheet remains optional. `ExternalCharacterId` is only an optional capability lookup key.

Hex Crawl procedure, participant, movement, environment, resource, and journey contracts do not import Character Sheet DTOs, inventories, or stat blocks. A DM can enter movement contributors, party references, current environment facts, overrides, process resolutions, and consequences directly.

A procedure condition or journey approach that refers to an external capability does not prove the party has that capability. Capability must come from explicit local state, an approved external lookup/provider, or DM adjudication.

## Persistence boundaries

The current PostgreSQL schema version is 8.

Procedure authority is stored as complete `CampaignProcedure` JSON snapshots.

Expedition subsystem authority is explicitly separated:

- `environment_json` — current/transient facts and DM overrides;
- `effects_json` — generalized consequence/effect lifecycle;
- `resources_json` — generic resource inventory and audit history;
- `survival_json` — forced-travel/exposure/camp state;
- `journey_state_json` — active/closed processes, event occurrences, resolutions, history, and idempotency identities.

Static environment annotations remain in `overworlds.world_json`. Effective environment context, conflicts, provider responses, environment evaluation, movement composition, and current catalog interpretation are derived and are not persisted as competing authorities.

Generated/consumed resolution history retains values and provenance actually used at resolution time, while optimistic concurrency prevents stale automatic results from being committed against newer aggregate state.

See `docs/postgresql-persistence.md`.

## API and UI boundaries

HTTP contracts expose application/domain concepts rather than persistence rows.

The Web modules expose typed focused operations for environment, resources/survival, journey/process state, procedure authoring, and canonical JSON validation/revision. World and expedition mutations continue to use aggregate optimistic concurrency.

The TypeScript client presents server-derived procedure, participant, movement, environment, survival, and journey projections. It does not implement a second precedence resolver, procedure interpreter, movement composer, resource engine, process engine, or browser-only rule authority.

Phase 15 combines those projections in one workflow-oriented workspace. Current action/status is presentation derived from authoritative state; existing controllers/commands remain responsible for durable changes.

## Preset-proof behavior

The generic architecture is tested against procedures with materially different travel and journey semantics.

Examples include:

- ordinary physical distance/rate procedures;
- D&D 2024 symbolic maximum pace, including conditional Arctic Fast travel;
- Pathfinder Hexploration activity budgets;
- Forbidden Lands quarter-day activity budgets and supply-die resources;
- AD&D-style movement-point semantics;
- The One Ring role-driven journey progress without a fabricated repeating time interval;
- a synthetic mixed procedure combining watch travel with standalone journey-event opportunities;
- Worlds Without Number numeric party-rate composition without preset dispatch;
- Custom procedures with no preset origin.

The One Ring preset and the synthetic mixed-procedure fixture contain their complete generic journey parameter sets directly. Runtime behavior does not identify either composition by preset key to finish its contract; the synthetic mixed procedure has no production preset identity at all.

## Provenance and adjudication

Procedure origin, source attribution, environment fact provenance, effective-source information, movement contributor provenance, provider attribution, consequence provenance, and journey resolution/event provenance remain explanatory data. They never replace pinned mechanical contracts as authority.

When the system lacks a safe generic rule, it reports missing input, unsupported semantics, or adjudication. It does not choose a "worst" terrain, invent a weather or journey-event formula, infer equipment, reinterpret provider semantics, or dispatch to a named-system special case.

## Current scope boundary

Implemented architecture now includes:

- generic preset/materialization and campaign-owned procedure snapshots;
- native generic runtime binding;
- preset-proof structural/mechanical catalog;
- procedure browsing/composition and generated reference;
- Compact, Advanced, and canonical JSON procedure presentations;
- optional travel/environment provider boundary;
- typed participant activity/role state with interval snapshots;
- generic movement capability composition;
- generalized environment context/evaluation;
- generalized consequences and persistent effects;
- generic resources, forced travel, exposure/camp state, and focused survival/resource operations;
- multi-stage journey processes/events with persistence/restart;
- Surveyor computation-only map-processing boundary;
- Phase 14 server-authoritative v2 encounter handoff;
- Phase 15 unified procedure-aware expedition workspace with spatial and no-interval/nonspatial adaptation.

Phase 15 established the internal-human-testing gate, followed by Guided and subsequent UI refinements. The former separate Phase 15.5 stabilization proposal has been superseded by the Tile Crawl transition's final validation/stabilization phase (`docs/tile-crawl-development-plan.md`).

A Battle Map tool and tactical-map ownership/integration architecture are outside the Hex Crawl roadmap and remain work for a future Battle Map roadmap once that product exists.

## Invariants

The following invariants apply across the current generic procedure architecture:

- named systems are removable creation-time presets;
- preset recipes are complete creation inputs, not runtime dependencies;
- `CampaignProcedure` is the only mechanical procedure authority for current-format expeditions;
- Compact, Advanced, JSON, and future Guided presentations must not become competing procedure authorities;
- runtime/application behavior does not branch on preset identity;
- execution/projection honors embedded handler/version and semantic contracts;
- unsupported/future data is preserved rather than silently replaced;
- external tools are optional input providers, not authorities;
- static environment truth belongs to the world;
- transient environment state and DM overrides belong to the expedition aggregate;
- deterministic runtime has no world/provider/journey dependency;
- `MovementCapabilityComposer` remains the sole final movement composer;
- generalized consequences remain the shared downstream mutation contract;
- journey process/event execution overlays deterministic runtime rather than replacing it;
- stable consequence/resolution/event/runtime-occurrence identities prevent duplicate application;
- unknown or ambiguous environment, resource, provider, or journey semantics are surfaced, not guessed;
- derived provider/environment/composition output is not persisted as competing truth;
- nonspatial/no-interval procedures remain first-class and are not forced into spatial watch presentation;
- application-owned DOM uses explicit state transitions rather than `MutationObserver` synchronization.
