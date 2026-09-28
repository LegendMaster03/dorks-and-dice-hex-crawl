# Generic Procedure Architecture

This document describes the generic procedure architecture implemented through Phase 2 of `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not an executable runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned generic mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure contains complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, plus resolved parameters and campaign overrides. `ProcedureOriginMetadata` remains optional informational provenance and is not consulted by runtime execution.

For newly materialized sessions, the runtime path is now:

`GenericProcedurePresetRecipe -> CampaignProcedure -> native generic procedure execution -> crawl-session state/results`

`CrawlProcedureProfile` remains persisted as compatibility data while historical rows still exist, but it is not the runtime authority when a pinned `CampaignProcedure` is present.

## Generic definitions

`ProcedureModuleDefinition` describes a composable procedure slot. It carries:

- a generic key and category;
- display name and purpose;
- execution stage;
- declared inputs and outputs;
- required and optional module dependencies;
- compatible mechanic keys;
- configuration schema;
- presentation metadata.

`MechanicDefinition` describes reusable executable behavior. It carries:

- a generic key and display metadata;
- input and output contracts;
- parameter schema;
- execution-handler identifier;
- compatibility tags;
- automation level;
- an explicit mechanic version.

Neither type contains preset identity or named-system identity.

## Materialized campaign procedures

`CampaignProcedure` is the campaign-owned executable definition. It has a stable `ProcedureId` and an immutable revision number. Each materialized module embeds its complete module definition, mechanic definition, and parameter set.

Embedding the selected definitions is deliberate. Persistence does not require the current global module catalog or mechanic catalog to reconstruct or execute a saved procedure. Removing, renaming, or revising a creation-time preset therefore does not alter an existing campaign procedure.

The current generic modules cover the behavior already supported by Hex Crawl:

- travel-interval/watch duration;
- movement resolution and actual-distance policy;
- intra-hex progress, entry/exit factors, direction-change cost, and deliberate double-back behavior;
- navigation checks, persistent veer, lost state, and reorientation behavior;
- encounter-check cadence and encounter timing;
- deterministic travel, navigation, and encounter resolution helpers.

Travel-mode pace keys, participant activities, marching/party state, provenance, and DM overrides continue through their existing runtime/application contracts while procedure-owned policy comes from the pinned generic snapshot.

## Native generic execution

`GenericProcedureRuntime.Bind` creates an ephemeral execution binding from a pinned `CampaignProcedure`. Runtime support is explicit for the embedded mechanic's execution-handler identifier and mechanic version. The current runtime registers each supported `(ExecutionHandler, Version)` combination directly and then reads that materialized module's parameters. It does not resolve the origin preset, call `CrawlProcedureCatalog`, or substitute a current global mechanic definition for the persisted snapshot.

The runtime support registry is deliberately separate from `GenericProcedureCatalog`: the catalog remains creation-time material, while the execution runtime decides whether it knows how to execute the exact persisted mechanic snapshot.

The binding exposes module-oriented runtime policy (`Time`, `Movement`, `HexProgress`, `Navigation`, `Encounters`, and optional resolution helpers) rather than rebuilding another named or edition-specific profile object.

`CrawlRuntimeEngine` has a native `CampaignProcedure` entry point. Its deterministic movement, navigation, watch lifecycle, encounter timing, pause/resume, and event-transition logic remains reusable, but behavior selection and configuration come from the generic runtime binding. The older profile entry point is retained only as an explicit compatibility adapter for profile-only historical data and existing compatibility tests.

The native `CampaignProcedure` entry point also enforces the pinned movement policy before shared deterministic movement runs. Fixed continuous-distance mechanics use one effective distance; application services normalize that value into matching expected/actual fields for the existing `ResolvedTravelAmount` shape, and the domain rejects mismatched values. Variable-resolved continuous-distance mechanics require both expected and actual values. Hex-step mechanics accept only a non-negative step count. The historical profile-only compatibility entry point retains its legacy structural behavior rather than redefining persisted historical semantics.

Unsupported execution handlers or unsupported versions of otherwise-known handlers are not discarded, rewritten, or downgraded. Their snapshots remain persisted intact; attempting native execution raises `UnsupportedProcedureMechanicException` with the module, mechanic, handler, and mechanic version that this runtime does not support.

## Application execution boundary

`ExpeditionProcedureExecutionResolver` is the deliberate historical-data boundary:

- if an expedition has `CampaignProcedure`, services bind and execute that pinned generic snapshot;
- if `CampaignProcedure` is absent, services adapt the historical `CrawlProcedureProfile` into the generic runtime policy for compatibility.

The full workbench, legacy expedition advance service, focused travel/watch assistants, automatic procedure-resolution helpers, procedure requirement checks, and the optional Rules Core travel adapter all use this boundary or the resulting generic runtime policy.

New world-bound workbench sessions now pin the exact generic materialization just like the existing standalone and world-bound creation paths. Abstract-hex and non-spatial sessions therefore use the same procedure authority even though their spatial context differs.

## Encounter cadence correction

The legacy runtime had split responsibility for `PerDay` cadence. The application workbench suppressed an otherwise-due engine encounter check by constructing a temporary profile with `EncounterCadence.None`, while the engine itself treated any non-`None` cadence as due when entered directly.

Native execution centralizes cadence evaluation against persisted runtime history. `None`, per-watch/custom, and per-day cadence now have one deterministic requirement calculation used by the engine, workbench, focused assistants, and automatic-resolution helper. This is an intentional correction of inconsistent legacy behavior, not an attempt to preserve the temporary profile rewrite.

## Compatibility projection

`CampaignProcedureCompatibilityProjector` remains a serialization/UI compatibility boundary. It can:

1. capture an existing `CrawlProcedureProfile` as a generic campaign procedure;
2. project a known materialized `CampaignProcedure` into the compatibility `CrawlProcedureProfile` shape;
3. create generic preset recipes from existing profile definitions during migration.

Projection uses embedded module and mechanic snapshots. It does not resolve the origin preset. Runtime execution of a new generic expedition no longer requires this projection.

If persisted data contains a future mechanic that native execution does not support, persistence still preserves the complete mechanic snapshot. Native execution rejects unsupported handler/version combinations rather than deleting, rewriting, downgrading, or replacing the persisted mechanic. Compatibility projection remains a separate non-runtime boundary.

## Campaign overrides and revisions

`CampaignProcedureOverride` targets a generic module. An override can change parameters or select another compatible generic mechanic/version. Applying overrides creates a new `CampaignProcedure` revision while retaining the same `ProcedureId`; the previous revision remains unchanged.

Native execution reads the revised materialized parameters, so procedure revisions and expedition-pinned snapshots affect behavior without runtime lookup of the originating preset.

`CampaignProcedureService` provides the application boundary for materializing, revising, and loading campaign procedure revisions. The visual Procedure Composer remains a later phase.

## Dependency metadata

A materialized procedure can evaluate its declared module dependencies. Missing required modules and incompatible mechanic selections are errors. Produced outputs that no selected module consumes are retained as diagnostics rather than destructive normalization.

The current dependency metadata remains structural. Broader authoring/composer dependency UX belongs to later phases.

## Persistence and restart reproducibility

PostgreSQL stores both:

- nullable `expeditions.campaign_procedure_json`, the authoritative pinned generic procedure for new sessions;
- `expeditions.procedure_json`, the retained compatibility profile used by historical profile-only rows and compatibility surfaces.

Known generic/profile pairs are validated for consistency on persistence. A historical row with no generic snapshot remains valid and is not silently rewritten. Restarting the application reloads and executes the persisted generic module/mechanic snapshots rather than reconstructing procedure behavior from the current preset or mechanic catalogs.

No SQLite migration infrastructure is added or restored.

## Rules Core

Rules Core remains optional enrichment. Native generic execution does not depend on Rules Core. The explicit Rules Core travel/navigation adapter can resolve external inputs, but procedure interval and applicability policy are read from the pinned generic procedure, and missing external results are not invented by the runtime.

## Phase 2 limits

Phase 2 does not add the later proof-matrix mechanics, Procedure Composer UI, generated documentation, generalized provider architecture, expanded participant-activity model, generalized movement/environment/effects composition, forced resource/survival systems, multi-stage journeys, expanded encounter handoff, battle-map ownership, or raster/grid auto-alignment work.
