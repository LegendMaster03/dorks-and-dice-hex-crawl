# Generic Procedure Architecture

This document describes the Phase 1 procedure-composition architecture implemented from `docs/generic-procedure-development-plan.md`.

## Architectural boundary

Named systems remain creation-time preset metadata in `CrawlProcedureCatalog`. A preset is not an executable runtime authority. It contains a `GenericProcedurePresetRecipe`, which selects generic modules, versioned generic mechanics, and parameters.

Applying a preset materializes a campaign-owned `CampaignProcedure`. The materialized procedure contains complete snapshots of every selected `ProcedureModuleDefinition` and `MechanicDefinition`, plus resolved parameters and campaign overrides. `ProcedureOriginMetadata` remains optional informational provenance and is not consulted by runtime execution.

The current runtime compatibility path is intentionally preserved:

`GenericProcedurePresetRecipe -> CampaignProcedure -> CrawlProcedureProfile -> CrawlRuntimeEngine`

`CrawlRuntimeEngine` therefore remains unchanged in Phase 1. No system-specific runtime classes are introduced.

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

Embedding the selected definitions is deliberate. Persistence does not require the current global module catalog or mechanic catalog to reconstruct a saved procedure. Removing, renaming, or revising a creation-time preset therefore does not alter an existing campaign procedure.

The Phase 1 generic catalog covers the behavior already representable by `CrawlProcedureProfile`:

- travel-interval duration;
- movement resolution and actual-distance policy;
- intra-hex progress and direction-change behavior;
- navigation and persistent veer behavior;
- encounter-check cadence;
- deterministic travel, navigation, and encounter resolution helpers.

Later roadmap phases can add generic modules and mechanics without introducing named-system runtime classes.

## Compatibility projection

`CampaignProcedureCompatibilityProjector` is the explicit compatibility boundary. It can:

1. capture an existing `CrawlProcedureProfile` as a generic campaign procedure;
2. compile a materialized `CampaignProcedure` back into the current `CrawlProcedureProfile` runtime shape;
3. create generic preset recipes from existing profile definitions during the migration period.

Projection uses the embedded module and mechanic snapshots. It does not resolve the origin preset.

If persisted data contains a future mechanic that this runtime version does not know how to project, persistence still preserves the complete mechanic snapshot. Projection reports an unsupported handler instead of deleting or rewriting the unknown mechanic.

## Campaign overrides and revisions

`CampaignProcedureOverride` targets a generic module. An override can change parameters or select another compatible generic mechanic/version. Applying overrides creates a new `CampaignProcedure` revision while retaining the same `ProcedureId`; the previous revision remains unchanged.

`CampaignProcedureService` provides the Phase 1 application boundary for:

- materializing a standalone campaign procedure from a preset;
- creating a new revision with optimistic expected-revision checking;
- loading an exact revision;
- loading the latest revision.

This is revision infrastructure, not the Phase 3 composer UI.

## Dependency metadata

A materialized procedure can evaluate its declared module dependencies. Missing required modules and incompatible mechanic selections are errors. Produced outputs that no selected module consumes are retained as diagnostics rather than destructive normalization.

The initial dependency metadata is intentionally structural. The broad authoring/composer dependency graph, richer compatibility UX, and later-phase generated explanation surfaces are not part of Phase 1.

## Persistence

PostgreSQL schema version 2 adds:

- `campaign_procedure_revisions`, keyed by `(procedure_id, revision)`, containing the full materialized procedure JSON and optional origin metadata;
- nullable `expeditions.campaign_procedure_json`, which pins a full generic procedure snapshot to an expedition.

The existing `expeditions.procedure_json` column remains the authoritative compatibility projection consumed by the current runtime. Existing rows remain valid because the new generic snapshot column is nullable. Loading a historical row with no generic snapshot does not fabricate origin metadata.

Newly constructed `StoredExpedition` instances materialize a generic snapshot from their compatibility profile by default. Creation paths that use `MaterializeGeneric` can supply that exact materialized snapshot directly. When a historical expedition is loaded from PostgreSQL, its persisted nullable `campaign_procedure_json` value is respected as-is.

No SQLite migration infrastructure is added or restored by Phase 1.

## Rules Core

Rules Core remains optional enrichment. The generic procedure model does not require Rules Core and does not import Rules Core runtime state into Hex Crawl. Existing explicit Rules Core travel adapters continue to operate at their current boundary.

## Phase 1 limits

Phase 1 does not implement:

- the edition/preset behavior proof matrix from Phase 2;
- the visual procedure composer from Phase 3;
- native module execution replacing `CrawlProcedureProfile` from Phase 4;
- expanded environment/effects/resources/activity mechanics from later phases;
- battle-map ownership changes;
- raster/grid-alignment changes.

Those remain intentionally deferred so the generic composition and revision foundation can be validated independently.
