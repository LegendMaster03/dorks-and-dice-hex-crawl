# Generic Procedure Architecture

This document describes the current generic procedure architecture through Phase 9 of `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not a runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure embeds complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, resolved parameters, and campaign overrides. `ProcedureOriginMetadata` is optional informational provenance and is never consulted to determine runtime, participant-assignment, movement-composition, or environment-evaluation behavior.

The authoritative procedure path is:

`GenericProcedurePresetRecipe -> CampaignProcedure -> handler/version-aware binding or focused stored-contract projection -> crawl-session state/results`

`CampaignProcedure` is the only supported persisted procedure representation for current-format expeditions.

## Pre-release compatibility policy

Hex Crawl is pre-release development software. Development-era API, persistence, UI, and internal-model compatibility is not preserved unless a specific compatibility requirement is deliberately approved.

Retired dual models are removed instead of retained as fallback infrastructure. Current PostgreSQL schema version 5 rejects older development schemas with an explicit reset/reinitialize instruction rather than maintaining compatibility columns or alternate procedure/environment representations.

## Generic modules and mechanics

A procedure is composed from generic modules and mechanics whose keys describe behavior rather than game identity. Mechanic definitions carry versioned semantics and, where executable, explicit handler/version contracts.

Runtime and focused application projections follow the contracts embedded in the pinned `CampaignProcedure`. They do not ask which named preset originally produced it.

Unknown handlers, future versions, declarative-only mechanics, and unsupported semantic values remain representable. Hex Crawl reports them as unsupported/unresolved rather than replacing them with a current catalog default.

This distinction lets the same materialized procedure remain authoritative after presets change or are removed.

## Runtime binding

`GenericProcedureRuntime.Bind` is the deterministic runtime binding boundary for mechanics owned by `CrawlRuntimeEngine`.

The runtime assembly receives only the data required for deterministic crawl progression. It does not own repositories, Rules Core clients, world lookup, Character Sheet lookup, or optional environment providers.

Focused application operations may project one supported contract from the stored procedure without binding the entire procedure. This is used where behavior is not a deterministic crawl-engine transition, including Procedure Composer validation, participant activity state, movement capability composition, and environment-to-movement interpretation.

Focused projection is still pinned-procedure evaluation. It is not preset dispatch.

## Campaign-owned procedure revisions

Campaign procedure revisions persist complete `CampaignProcedure` snapshots. Expeditions persist their own complete pinned procedure snapshot as `procedure_json`.

A later edit or catalog change therefore does not silently mutate an existing expedition. Explicit revision/application operations are required to change procedure authority.

Origin metadata may identify the recipe or source that created a snapshot, but origin is never used as a runtime switch.

## Procedure Composer

The Procedure Composer operates on generic modules, mechanics, dependencies, parameters, and revisions.

It allows a DM to create and revise campaign procedures without selecting a named system at runtime. Validation reports missing dependencies, unsupported contracts, or incomplete values directly instead of forcing every procedure into one preset-shaped schema.

The browser edits application contracts. Server/application code remains authoritative for materialization and validation.

## Generated procedure reference

Generated reference content is derived from the pinned generic procedure. It explains the mechanics that the expedition actually owns and retains source/provenance information where available.

Reference generation does not become a second rules model. Structural/declarative mechanics can be described even when no current executable handler exists.

## Typed participant activities and roles

Participant assignment is generic and procedure-owned.

Typed activity/role state records participant choices against the contracts embedded in the pinned procedure. Active-interval snapshots preserve the inputs used for an interval so later party edits do not retroactively rewrite completed or in-progress resolution history.

Participant state does not dispatch on named-system identity and does not import Character Sheet objects. `ExternalCharacterId` remains an optional external lookup key only.

## Movement capability composition

Phase 8 introduced one generic movement-composition boundary: `MovementCapabilityComposer`.

The composer reads movement policy from the exact stored `CampaignProcedure` and combines typed current capability/consequence inputs. Inputs can represent participants, mounts/vehicles, load consequences, pace/mode, terrain/route, environment, persistent effects, and an explicit DM final override.

Composition preserves non-distance semantics. Activity costs, movement points, quarter-day budgets, terrain difficulty, symbolic maximum pace, and journey progress are not flattened into physical speed unless the pinned contract explicitly provides a safe physical interpretation.

`PartyMovementReference` remains a supported explicit/fallback authority with a declared role in each result rather than being silently added to composed movement.

Provider-backed physical rates or factors are converted into explicit typed contributors and still pass through `MovementCapabilityComposer`. Providers do not own final movement composition.

See `docs/movement-capability-composition.md`.

## Environment context and evaluation

Phase 9 adds a generic environment layer above deterministic runtime and upstream of Phase 8 movement composition.

Environment truth is ruleset-neutral. `EnvironmentFact` uses an open dimension string and either a tag or an explicit numeric measurement/unit. Common dimensions include terrain, route, weather, visibility, elevation, depth, water, current, temperature, hazard, and regional effect, but the set is intentionally extensible.

### Ownership

Static environment truth belongs to `OverworldDefinition.EnvironmentAnnotations`. An annotation explicitly targets the world, one hex, or one spatial feature.

Transient/current conditions and explicit DM decisions belong to `StoredExpedition.Environment`:

- `CurrentFacts` for current/transient conditions;
- `Overrides` for explicit DM overrides.

`ExpeditionState` remains traversal/runtime state. Static world truth is not copied into it.

### Resolution

`EnvironmentContextResolver` computes the effective context from the current authoritative state.

For world-bound sessions it considers world annotations, current-hex annotations, and annotations on spatial features intersecting the current hex. Abstract-hex and non-spatial sessions can resolve expedition-current facts without an overworld.

Precedence is deterministic:

1. static world/hex/spatial-feature facts;
2. expedition current/transient facts;
3. DM overrides.

Higher precedence supersedes lower facts for the same dimension, while the lower facts remain visible as non-effective provenance. Compatible same-authority tags can coexist. Incompatible value kinds or disagreeing same-authority scalar measurements produce explicit conflicts and require adjudication instead of an arbitrary tie-break.

Moving to a new hex recomputes applicable static world truth. Effective context is derived, not persisted.

### Pinned-procedure interpretation

`EnvironmentProcedureEvaluator` maps effective environment facts into generic movement inputs using the expedition's exact pinned `CampaignProcedure`.

It currently interprets terrain, route, and weather for movement where the stored procedure defines a safe contract. It preserves unknown facts and reports unsupported semantics rather than deleting them.

The evaluator deliberately requires adjudication when competing facts can not be combined safely. Examples include materially different terrain mappings, a mixture of understood and unknown competing terrain, route ambiguity, relevant scalar conflicts, and weather behavior for which no safe local formula exists.

Conditional semantics such as “fast if appropriately equipped” remain conditional. Environment truth does not fabricate equipment/capability state.

### Movement handoff

The normal Phase 9 path is:

`authoritative environment state -> EnvironmentContextResolver -> EnvironmentProcedureEvaluator -> MovementCompositionInput -> MovementCapabilityComposer`

This is the same path used by ordinary expedition projection and the environment workbench. Phase 9 does not add a separate environment movement calculator.

Source provenance and the pinned terrain/mechanical adjustment are retained in the resulting movement explanation.

See `docs/environment-context.md`.

## Optional travel/environment providers

Rules Core remains optional enrichment. Generic procedure execution, participant state, environment authority, and movement composition do not depend on Rules Core.

`ITravelEnvironmentProvider` exposes typed provider catalog/resolution contracts. Provider states keep unavailable, failed, unsupported, input-required, not-applicable, requires-adjudication, and resolved cases distinct.

Provider input/output boundaries preserve units, time bases, factor semantics, missing-input keys, provider identity, and source attribution. Hex Crawl refuses to reinterpret output whose declared semantics do not match the requested generic operation.

Provider output is converted into explicit application inputs/contributors. Provider-native state is not persisted as procedure or environment authority.

## Character Sheet boundary

Character Sheet remains optional. `ExternalCharacterId` is only an optional capability lookup key.

Hex Crawl procedure, participant, movement, and environment contracts do not import Character Sheet DTOs, inventories, or stat blocks. A DM can enter movement contributors, party references, current environment facts, and overrides directly.

A procedure condition that refers to equipment does not prove the party has that equipment. Capability must come from explicit local state, an approved external lookup/provider, or DM adjudication.

## Persistence boundaries

The current PostgreSQL schema version is 5.

Procedure authority is stored as complete `CampaignProcedure` JSON snapshots.

Phase 9 environment authority is stored separately:

- `overworlds.world_json` contains static environment annotations as part of `OverworldDefinition`;
- `expeditions.environment_json` contains expedition current facts and DM overrides.

Effective environment context, conflicts, provider responses, environment evaluation, and current movement composition are derived and are not persisted as competing authorities.

Generated/consumed resolution history retains the values and provenance that were actually used at the time of resolution, while optimistic concurrency prevents stale automatic results from being committed against newer aggregate state.

See `docs/postgresql-persistence.md`.

## API and UI boundaries

HTTP contracts expose application/domain concepts rather than persistence rows.

The Web module catalog now includes the `environment-context` module. It depends on both `worlds` and `expeditions` because Phase 9 owns static world annotation authoring plus expedition current/override and workbench operations.

World and expedition mutations continue to use aggregate optimistic concurrency.

The TypeScript client presents server-derived procedure, participant, movement, and environment projections. It does not implement a second precedence resolver, procedure interpreter, or movement composer in browser code.

## Preset-proof behavior

The generic architecture is intentionally tested against procedures with materially different travel semantics.

Examples retained across the proof set include:

- ordinary physical distance/rate procedures;
- D&D 2024 symbolic maximum pace, including conditional Arctic Fast travel;
- Pathfinder Hexploration activity budgets;
- Forbidden Lands quarter-day activity budgets;
- AD&D-style movement-point semantics;
- The One Ring journey progress without a fabricated repeating time interval;
- Worlds Without Number numeric party-rate composition without preset dispatch.

Phase 9 proves that environment truth can feed these stored semantics without forcing them into a single miles-per-hour model.

## Provenance and adjudication

The architecture treats provenance and unresolved semantics as first-class output.

Procedure origin, source attribution, environment fact provenance, effective-source information, movement contributor provenance, provider attribution, and generated-resolution provenance remain explanatory data. They never replace the pinned mechanical contracts as authority.

When the system lacks a safe generic rule, it reports missing input, unsupported semantics, or adjudication. It does not choose a “worst” terrain, invent a weather formula, infer equipment, reinterpret provider semantics, or dispatch to a named-system special case.

## Current scope boundary

Implemented through Phase 9 are:

- generic preset/materialization and campaign-owned procedure snapshots;
- native generic runtime binding;
- preset-proof structural/mechanical catalog;
- Procedure Composer and generated procedure reference;
- optional travel/environment provider boundary;
- typed participant activity/role state with interval snapshots;
- generic movement capability composition with server-derived suggestions and provenance;
- generalized environment facts, static annotations, current state, DM overrides, precedence/conflicts, pinned-procedure environment evaluation, persistence, HTTP/UI workbench support, and movement handoff.

Still deferred are:

- Phase 10 generalized consequence/effect lifecycle;
- Phase 11 forced-travel/resource/survival execution and activity-driven foraging/camping effects;
- Phase 12 multi-stage journey execution;
- expanded encounter runtime;
- battle-map ownership.

Mechanics within the Phase 9 environment domain remain manual/provider-resolved when no safe executable generic contract exists. That is an intentional boundary, not permission to add named-system special cases.

## Invariants

The following invariants apply across the current generic procedure architecture:

- named systems are removable creation-time presets;
- `CampaignProcedure` is the only mechanical procedure authority for current-format expeditions;
- runtime/application behavior does not branch on preset identity;
- execution/projection honors embedded handler/version and semantic contracts;
- unsupported/future data is preserved rather than silently replaced;
- external tools are optional input providers, not authorities;
- static environment truth belongs to the world;
- transient environment state and DM overrides belong to the expedition aggregate;
- deterministic runtime has no world/provider dependency;
- `MovementCapabilityComposer` remains the sole final movement composer;
- unknown or ambiguous environment semantics are surfaced, not guessed;
- derived provider/environment/composition output is not persisted as competing truth.
