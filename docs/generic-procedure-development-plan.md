# Hex Crawl generic procedure and removable preset development plan

## Purpose

Hex Crawl should become a self-sufficient, ruleset-agnostic expedition engine. Published game systems, editions, third-party procedures, and named community procedures are represented only as **presets** that configure generic Hex Crawl mechanics.

A preset is a creation-time recipe, not a runtime dependency. Applying a preset materializes a complete campaign-owned procedure snapshot. From that point forward, the campaign and its expeditions execute the materialized generic procedure rather than consulting the originating preset.

This preserves familiar starting points for DMs while allowing any part of a procedure to be replaced with another generic implementation or a campaign house rule.

The central product principle is:

> **Named systems are removable presets. Mechanics are generic. Procedures are campaign-owned snapshots. The runtime executes behavior, not branding.**

## Current repository baseline

This plan is designed as an evolution of the current architecture rather than a rewrite.

The current repository already provides several important foundations:

- persisted crawl sessions store a complete `CrawlProcedureProfile` snapshot;
- built-in procedure keys are already treated as creation-time presets rather than reload-time authorities;
- `CrawlRuntimeEngine` is deterministic and accepts resolved inputs instead of depending directly on Rules Core;
- `WorldBound`, `AbstractHex`, and `NonSpatial` session contexts separate expedition procedure from map ownership;
- `CrawlPartySheet` already stores party members, marching order, watch rotation, standing orders, a default navigator, and explicit movement references;
- generated/manual/external/DM-override provenance already exists;
- Block Initiative already receives a versioned encounter handoff;
- `LocationDetailMapReference` already provides an early location-to-detail-map relationship;
- the modular source layout already separates Worlds, Expeditions, Source Maps, and Reference Data;
- source-map/raster auto-alignment work is localized enough to proceed in parallel with procedure architecture.

The primary architectural mismatch is the current `CrawlProcedureProfile`, which presently combines preset identity, user-visible naming, fixed procedure configuration, helper configuration, and runtime behavior switches. The new architecture separates those concerns without initially replacing the deterministic runtime.

## Hard architectural invariants

1. No game-system, edition, publisher, product, or third-party procedure identity is required by the Hex Crawl execution engine.
2. Generic mechanic definitions contain no published-system identity.
3. Presets are recipes, not runtime dependencies.
4. Applying a preset materializes a standalone generic procedure snapshot owned by the campaign.
5. Removing, renaming, or disabling a preset must not invalidate, alter, or prevent execution of an existing campaign procedure or expedition.
6. Origin-preset metadata is informational only and may be removed without changing executable behavior.
7. Rules Core is optional enrichment and must not be required for core expedition execution.
8. No general rule-import, scraping, or mirrored-rulebook pipeline is required for preset support.
9. Hex Crawl implements functional expedition behavior directly as generic mechanics and configuration.
10. The Dorks & Dice Site remains responsible for account identity, authentication, campaign identity, permissions, hosting, and platform navigation.
11. Character Sheet remains authoritative for character-owned state and may optionally provide capabilities.
12. Block Initiative remains authoritative for tactical combat.
13. Battle-map ownership remains intentionally undecided; Hex Crawl owns locations and encounter context and stores provider-neutral linked scene references.
14. Raster/grid auto-alignment remains independent of procedure/preset development.
15. Do not merge development branches to `main` without explicit authorization.

## Product model

The user-facing model is:

```text
Named Preset
    |
    | apply once
    v
Generic Campaign Procedure
    |
    +-- Time modules
    +-- Movement modules
    +-- Party procedure modules
    +-- Navigation modules
    +-- Exploration modules
    +-- Encounter modules
    +-- Survival / resource modules
    +-- Environment / effect modules
    +-- Multi-stage journey modules
    |
    v
Expedition Runtime
```

A preset answers:

> Give me a complete familiar starting procedure.

A procedure module answers:

> Which part of the expedition procedure does this control?

A generic mechanic answers:

> How does that part behave?

A campaign override answers:

> What did this DM change?

## Preset containment

Game-system and product names should exist only in preset/catalog metadata and related non-executable presentation metadata.

Conceptually:

```text
PresetDefinition
  presetKey
  displayName
  description
  attribution?
  disclaimer?
  presetRevision
  moduleSelections[]
  mechanicParameters[]
```

The recipe points only to generic mechanic keys and generic parameter values.

The materialized procedure is independent:

```text
CampaignProcedure
  procedureId
  procedureRevision
  optionalOriginPresetMetadata
  modules[]
  mechanics[]
  parameters[]
  overrides[]
  dependencyState
```

`optionalOriginPresetMetadata` must never be needed for execution.

### Preset removal behavior

| Change | New users | Existing campaigns |
| --- | --- | --- |
| Preset removed | Can no longer select it | Continue unchanged |
| Preset renamed | See new catalog name | Execution unchanged |
| Attribution/disclaimer changed | See current metadata | Execution unchanged |
| Preset recipe corrected | New applications use corrected recipe | Existing procedures remain pinned unless explicitly updated |
| Preset identity must be stripped | Catalog entry disappears | Origin metadata may be removed without touching mechanics |

## Generic mechanic architecture

Executable mechanics should be named for behavior rather than source.

Examples include:

- `FixedIntervalDuration`
- `TerrainMovementMultiplier`
- `TerrainMovementCost`
- `PartyLimitingMovement`
- `PaceMovementModifier`
- `ScheduledEncounterChecks`
- `PerIntervalEncounterCheck`
- `TerrainEncounterProbability`
- `NavigationDifficultyByEnvironment`
- `NavigationFailurePersistentVeer`
- `NavigationFailureRandomDirection`
- `RecognizeLostCheck`
- `ReorientationCheck`
- `ParticipantIntervalActivity`
- `TravelRoleAssignment`
- `ForcedTravelCheck`
- `ResourceConsumptionPerInterval`
- `EncounterDistanceByEnvironment`
- `ProgressiveExpeditionEffect`
- `MultiStageExpeditionProcess`

A generic mechanic contract should converge toward something similar to:

```text
MechanicDefinition
  key
  displayName
  description
  inputContract
  outputContract
  parameterSchema
  executionHandler
  compatibilityTags[]
  automationLevel
  version
```

The mechanic contract should not contain fields such as publisher, game edition, product, or source system.

## Procedure module architecture

Modules describe **what part** of the expedition procedure is being resolved. Mechanics describe **how** the module behaves.

Initial module families:

| Family | Candidate modules |
| --- | --- |
| Time | Interval duration, travel day, rest, extended travel, forced travel, stage duration |
| Movement | Base capability, pace/mode, terrain/route effects, mounts/vehicles, water travel, encumbrance |
| Party procedure | Marching order, participant activities, travel roles, watch rotation, standing orders, navigator selection |
| Navigation | Check cadence, difficulty, resolution, failure state, directional error, recognize lost, reorientation |
| Exploration | Mapping, searching, scouting, tracking, foraging, other interval activities |
| Encounters | Check cadence, probability, timing, distance, composition, circumstances, handoff |
| Survival/resources | Food, water, light, ammunition, fuel, supplies, fatigue, exposure, weather consequences |
| Journey processes | Complex hazards, crossings, pursuits, journey challenges, other multi-stage procedures |

A module contract should eventually expose at least:

```text
ProcedureModuleDefinition
  key
  category
  displayName
  purpose
  executionStage
  reads[]
  produces[]
  requiredDependencies[]
  optionalDependencies[]
  compatibleMechanicTypes[]
  configurationSchema
  presentationMetadata
```

## Phase 0 — compatibility normalization before generic procedure composition

Phase 0 is a compatibility-focused migration stage that must complete before the broader generic module system. It has two separately reviewable checkpoints that combine for the final Phase 0 review. Neither checkpoint changes game/runtime behavior.

### Phase 0A — separate preset identity from execution

Goal: separate creation-time preset identity from executable procedure state while retaining `CrawlProcedureProfile` as the compatibility projection consumed by the deterministic runtime.

Required work:

1. Introduce a preset/catalog concept outside the executable Domain procedure model.
2. Introduce nullable informational `ProcedureOriginMetadata`.
3. Move named-system construction out of `CrawlProcedureProfile` Domain factories.
4. Remove the invariant that a customized executable procedure key must equal the selected preset key.
5. Persist the complete executable snapshot independently of origin metadata.
6. Prove an expedition remains executable if its originating preset or origin metadata disappears.
7. Keep Rules Core optional and avoid runtime catalog re-resolution.
8. Preserve old persisted shapes and add regression coverage.

Phase 0A is implemented on its dedicated checkpoint branch and remains the behavioral base for Phase 0B.

### Phase 0B — normalize production persistence on PostgreSQL

Goal: move structured Hex Crawl production persistence from SQLite to PostgreSQL without redesigning the domain, application contracts, or runtime.

Required work:

1. Keep `IHexCrawlStore` as the application persistence boundary and implement it with Npgsql plus hand-written PostgreSQL SQL; do not introduce Entity Framework merely for platform consistency.
2. Use PostgreSQL-native UUID, bigint, timestamptz, boolean where applicable, and structured JSON storage while retaining complete aggregate snapshots rather than normalizing runtime/domain state into relational tables.
3. Preserve atomic optimistic concurrency, owner scoping, foreign-key/delete semantics, deterministic ordering, and atomic snapshot-plus-history persistence.
4. Add deterministic, idempotent, transactional, version-tracked PostgreSQL schema initialization that fails clearly instead of resetting production data.
5. Provide a one-time read-only SQLite → PostgreSQL migration capability supporting deployed historical SQLite generations, including databases with and without `procedure_origin_json`. Missing origin is valid and must remain missing.
6. Preserve IDs, owners, versions, timestamps, world/source-map metadata, every crawl-session snapshot field, every retained event and sequence, AutomaticRoll/provenance records, and all existing relationships.
7. Verify migration semantically: row counts, IDs, versions, event order/uniqueness, relationships, snapshot equality, and real application-path deserialization/read behavior.
8. Keep raster/source-map binaries filesystem-backed through `IMapAssetStore` at `/data/assets`; the existing data volume remains required and the legacy SQLite database remains a rollback artifact during cutover.
9. Make `/ready` verify PostgreSQL connectivity/schema readiness while `/health` remains process health.
10. Make normal CI, container restart tests, deployment smoke tests, Compose configuration, and deployment use PostgreSQL rather than SQLite.
11. Re-prove every Phase 0A preset-origin invariant on the combined branch.
12. Document the operator-controlled backup, migration, verification, cutover, and rollback sequence.

### Phase 0 non-goals

- Do not rewrite `CrawlRuntimeEngine`.
- Do not introduce the full module/mechanic dependency graph.
- Do not implement new published-system presets.
- Do not remove optional Rules Core travel/environment support.
- Do not implement the generalized effect/resource engine.
- Do not change battle-map ownership.
- Do not modify raster/grid alignment algorithms.
- Do not merge to `main` without explicit authorization.

Phase 1 begins only after the combined Phase 0A + Phase 0B result is reviewed and explicitly authorized for merge.

## Phase 1 — generic procedure and preset foundation

After Phase 0, introduce the real generic composition model.

### Scope

- `ProcedureModuleDefinition`
- `MechanicDefinition`
- generic preset recipe schema
- materialization service
- standalone campaign-procedure revisions
- optional origin-preset metadata
- generic campaign overrides
- input/output/dependency metadata
- version-safe persistence
- compatibility projection into `CrawlProcedureProfile`
- architecture tests and documentation

### Compatibility projection

Do not replace the deterministic runtime immediately.

Initially use:

```text
Generic CampaignProcedure
        |
        | compile / project
        v
CrawlProcedureProfile
        |
        v
existing CrawlRuntimeEngine
```

This allows the new architecture to mature while preserving current runtime semantics and regression coverage.

As generic modules become more expressive than `CrawlProcedureProfile`, individual runtime responsibilities can later migrate to module-native execution deliberately.

## Rules Core boundary

Hex Crawl should remain fully functional without Rules Core.

The current runtime already has the right dependency direction: it accepts resolved values and does not call Rules Core directly. The current application layer does, however, expose explicitly Rules-Core-shaped contracts such as `IRulesCoreTravelGateway`, `TravelEnvironmentMechanicKeys`, `ProcedureResolutionRulesCoreAdapter`, and Rules Core-specific travel UI.

Do not delete this work. Move it behind a generic optional provider boundary over time.

Conceptually:

```text
Hex Crawl Procedure Engine
    +-- native generic mechanics
    +-- campaign/custom mechanics
    +-- optional Rules Core provider adapter
    +-- optional Character capability adapter
    +-- future providers
```

Rules Core may continue to supply useful canonical content such as creature data, competencies, tools, conditions, or source-backed calculations. It does not own the expedition procedure or runtime.

If an optional provider is unavailable, Hex Crawl should continue with materialized/native behavior when possible or request explicit DM input. It must not invent a result.

## No rule-import requirement

Preset support should not depend on importing, scraping, or mirroring external rulebooks.

The intended development workflow is:

```text
Research a procedure
    ↓
Identify functional behavior
    ↓
Can existing generic mechanics represent it?
   / \
 yes  no
  |    |
configure   add/generalize generic mechanic
   \   /
    ↓
Named preset recipe
```

Development should use original Hex Crawl descriptions and generic structured data. Source expression, publisher prose, artwork, logos, and copied layout are not required to execute mechanics.

## Preset catalog planning

Named presets are catalog metadata only. None should create system-specific runtime classes.

Initial planning/proof candidates:

- OD&D (1974)
- B/X
- Old-School Essentials Classic Fantasy, as a shared/alias recipe with B/X unless a verified procedure difference exists
- BECMI / Rules Cyclopedia
- AD&D 1e
- AD&D 2e
- D&D 3e
- D&D 3.5e
- D&D 4e
- D&D 5e (2014)
- D&D 5.5e / 2024
- The Alexandrian
- Pathfinder 1e
- Pathfinder 2e Hexploration
- Forbidden Lands
- Worlds Without Number
- The One Ring 2e

Shipping priority may be narrower. The purpose of the broader set is to stress the abstraction before its contracts are considered stable.

### Minimum architecture proof matrix

Before the generic composition contracts are treated as mature, prove they can represent without system-specific runtime classes:

- B/X
- AD&D 2e
- D&D 3.5e
- D&D 5.5e / 2024
- Pathfinder 2e Hexploration
- Forbidden Lands
- Worlds Without Number
- The One Ring 2e
- The Alexandrian
- one deliberately mixed house-rule procedure

This matrix exercises hours, watches, quarter-days, days, party-wide and participant-specific actions, navigation, getting lost, terrain, encounter scheduling, resource consumption, foraging, camping, forced travel, persistent fatigue/effects, journey events, and higher-level journey processes.

## Preset versioning

Preset revisions must never silently mutate an active campaign.

Preferred flow:

```text
PresetRevision N
    ↓ materialize
CampaignProcedureRevision 1
    ↓ DM edits
CampaignProcedureRevision 2
    ↓ expedition pins
ExpeditionProcedureSnapshot
```

If a newer preset revision becomes available, offer an explicit comparison/update workflow at module level. The DM chooses whether to accept any changes.

An expedition should retain the exact procedure revision used for its resolutions so audit history remains reproducible after the campaign procedure evolves.

## Procedure Composer UX

The Procedure Composer is a first-class DM surface, not a developer/debug editor.

Start with recognizable presets, then organize customization around understandable behavior:

```text
Start with:
  B/X | AD&D 2e | 3.5e | 5.5e | Pathfinder 2e | Custom | ...

Review / Customize:
  Time
  Movement
  Party Organization
  Navigation
  Exploration
  Encounters
  Survival
  Journey Processes
```

Each module should explain:

- what it controls;
- which generic behavior is selected;
- current parameters;
- automation level;
- required inputs;
- state/output produced;
- dependencies and compatibility warnings;
- available alternatives;
- whether the campaign modified it;
- preset origin only as secondary informational metadata.

### Generated executable documentation

The procedure snapshot that drives execution should also generate the DM-facing procedure reference. Do not maintain a separate rules document that can drift from configuration.

For example:

```text
During each travel interval:
1. Choose travel mode.
2. Assign participant activities or roles.
3. Determine effective movement.
4. Resolve navigation if required.
5. Advance spatial state.
6. Resolve encounter checks.
7. Consume resources.
8. Apply environmental and expedition effects.
9. Complete the interval.
```

## Dependency and compatibility graph

Generic mechanics should declare what they consume and produce.

The composer should distinguish:

- missing required producer → error or explicit DM-input fallback;
- produced but unused output → warning;
- incompatible writers → explicit conflict/adjudication;
- manually resolvable but not automatable mechanic → allowed with manual resolution status;
- optional provider unavailable → use materialized/native behavior or request explicit input;
- unknown future mechanic key → preserve during persistence and report unsupported state rather than deleting data.

Do not silently rewrite the DM's choices to make a configuration valid.

## Party activities and travel roles

Do not replace `CrawlPartySheet`. Extend it.

The existing party sheet is already the correct foundation for:

- members;
- external character references;
- marching order;
- watch rotation;
- standing orders;
- default navigator;
- movement reference and limiting member.

Add a generalized participant-activity model capable of representing a navigator, lookout, mapper, forager, scout, quarter-day activity, or another ruleset-defined expedition action without system-specific enums.

Conceptually:

```text
ParticipantActivityAssignment
  participantId
  activityKey
  intervalOrStage
  parameters
  exclusivityGroup?
  capacity?
  provenance
```

## Movement capability composition

Movement should eventually compose contributions from:

- participant/creature movement capability;
- limiting party member;
- carried load or encumbrance;
- mounts and vehicles;
- travel mode or pace;
- terrain and route;
- weather, elevation, visibility, and water/current context;
- persistent expedition effects;
- explicit DM override.

The current explicit `PartyMovementReference` remains a valid authoritative reference and manual fallback.

## Environment context

Introduce a ruleset-neutral environment context rather than edition-specific terrain profiles.

Conceptually:

```text
EnvironmentContext
  terrainTags[]
  routeTags[]
  weather
  visibility
  elevationOrDepth
  waterCurrent
  temperature
  hazardTags[]
  regionalEffects[]
```

World truth describes the environment. Generic mechanics decide what that environment means to the active procedure.

## Generalized effects and consequences

After the composition model is stable, add a generic structured effect pipeline.

It should be able to represent:

- time delay;
- movement reduction or route blockage;
- food/water/light/fuel/ammunition/material changes;
- exposure, fatigue, dehydration, disease, corruption, or campaign-defined conditions;
- damage/endurance effects on participants, mounts, vehicles, or expedition-owned state;
- lost/veer/reorientation changes;
- encounter surprise, reinforcements, composition changes, or positional disadvantage;
- custom provider/campaign-defined structured consequences.

Do not overload `RuntimePauseReason` for persistent expedition effects. Pause reasons explain why the current deterministic transition can not continue; effects describe ongoing expedition state.

## Multi-stage expedition processes

Journey Challenges, Complex Hazards, difficult crossings, pursuits, and similar procedures should be represented by one generic model rather than a 4e-specific runtime feature.

Conceptually:

```text
MultiStageExpeditionProcess
  goal
  stages[]
  progress
  failures
  complications
  availableApproaches[]
  mechanicReferences[]
  activeEffects[]
  completionCriteria
  failureCriteria
  auditTrail
```

This process overlays the ordinary expedition/watch loop. It does not replace the existing core Travel → Watch → Navigation → Encounter procedure.

## Resources and survival

After the effect/resource infrastructure is stable, support generic expedition resources such as:

- food;
- water;
- light;
- ammunition;
- fuel;
- medicine;
- generic supplies;
- vehicle/mount supplies;
- harvested/crafting materials;
- campaign-defined resources.

Loot Tavern integration, if added, should layer onto the generic resource model rather than dictate the core schema.

## Encounter handoff

Structured encounter handoff already exists and should be **expanded**, not reinvented.

Current Block Initiative handoff already includes expedition identity, context, watch/day, encounter outcome, timing, hex/location context, optional notes, and optional structured combatants.

Future additions may include:

- surprise/loss of surprise;
- advantageous/disadvantaged start circumstances;
- delayed arrival;
- reinforcements;
- altered encounter composition;
- depleted resources;
- route/location changes;
- linked scene reference;
- source journey/hazard identifier;
- provenance for carried consequences.

Block Initiative remains the owner of tactical combat.

## Battle maps and linked scenes

Battle-map ownership remains deliberately unresolved.

Hex Crawl owns the world location and encounter context. Tactical/scene maps are linked resources until an explicit product decision is made.

The existing `LocationDetailMapReference` is a useful seed and should evolve toward a provider-neutral concept such as:

```text
LinkedSceneReference
  id
  providerKey
  resourceId
  displayName?
  sceneKind
  optionalUri?
  metadata?
```

Possible scene kinds include battle map, dungeon/interior map, regional map, handout/image, or custom scene.

Hex Crawl may own the location association, approach/entry context, time, weather, route/environment state, encounter circumstances, and return path.

Do not prematurely make Hex Crawl authoritative for initiative rounds, tactical token movement, line of sight, walls/doors, lighting/fog, combat-grid enforcement, or tactical combat effects.

## Parallel raster/grid auto-alignment

Raster and baked-hex-grid auto-alignment work is active in parallel and should remain isolated from procedure/preset architecture.

Procedure work should avoid changes to grid-lattice detection, source-raster analysis, source-map registration, canonical source-resolution handling, and Wonderdraft-specific map alignment unless a genuine shared contract is required.

The modularized repository already supports this separation well.

Before each implementation phase:

1. fetch current `main`;
2. inspect active alignment branches/PRs and current validation state;
3. compare the working branch against current `main`;
4. preserve unrelated changes;
5. rebase/merge current `main` when necessary before final validation.

## Phased roadmap

### Phase 0 — separate preset identity from execution

Compatibility migration described above. No runtime rewrite.

### Phase 1 — generic procedure and preset foundation

Create generic module/mechanic contracts, preset recipes, campaign procedure revisions, materialization, dependencies, persistence, and `CrawlProcedureProfile` compatibility projection.

### Phase 2 — preset catalog and proof matrix

Implement enough generic primitives to represent the proof systems without system-specific runtime classes.

### Phase 3 — Procedure Composer UI

Add preset picker, module review, generic behavior selection, parameter editing, modification count, provenance display, and dependency warnings.

### Phase 4 — migrate current runtime behavior onto generic modules

Map current watch length, movement, pace, navigation, persistent veer/lost state, encounter cadence, marching order, participant activities, and automatic helper configuration onto generic modules while preserving outcomes.

### Phase 5 — generated procedure documentation

Generate a readable campaign procedure directly from the executable snapshot.

### Phase 6 — optional provider adapters

Put the existing Rules Core travel/environment integration behind generic optional interfaces and add other provider adapters only where they provide real value.

### Phase 7 — typed participant activities

Add structured participant roles/activities while retaining free-text compatibility.

### Phase 8 — movement capability composition

Compose participant, mount, vehicle, environment, load, and effect contributors with manual fallback and DM override.

### Phase 9 — environment context

Introduce generic terrain/route/weather/elevation/water/hazard context.

### Phase 10 — generalized effect/consequence engine

Add structured expedition effects and provenance.

### Phase 11 — forced travel, survival, and generic resources

Add fatigue/exposure, food/water/supplies, weather/altitude, forced travel, and generic resources.

### Phase 12 — multi-stage journey processes

Add generic Journey Challenge / Complex Hazard support over ordinary expedition intervals.

### Phase 13 — expanded encounter handoff

Carry structured circumstances, effects, composition changes, journey/hazard provenance, and linked scene references.

### Phase 14 — battle-map ownership evaluation

Use actual product needs to decide whether tactical maps remain external, Hex Crawl gains a map surface, or both models coexist.

## Phase 0 branch recommendation

Create a focused branch from current `main`, for example:

```text
feature/preset-identity-decoupling
```

Do not modify `main` directly for implementation work and do not merge until explicitly authorized.

## Required Phase 0 validation

Tests should prove at least:

- executable procedure behavior does not require a preset/product/system identifier;
- named preset identity can be moved outside the Domain executable profile;
- existing persisted `CrawlProcedureProfile` snapshots remain readable;
- a customized procedure can differ from its origin preset without sharing the preset key as executable identity;
- an expedition loads and advances when its originating preset no longer exists in the current catalog;
- origin metadata can be removed without changing execution;
- preset changes do not silently mutate an existing expedition;
- runtime output is equivalent before and after the compatibility migration for existing built-in profiles;
- Rules Core availability is not introduced as a new runtime requirement;
- auto-alignment behavior and source-map tests remain unaffected.

## Architecture definition of done

The target architecture is reached when a DM can:

1. select a recognizable preset;
2. receive a complete generic campaign procedure;
3. understand each major part in plain language;
4. replace individual behaviors with alternatives or house rules;
5. run the resulting procedure without the original preset being present;
6. run core expedition procedure without Rules Core being available;
7. use Rules Core, Character Sheet, Block Initiative, and map providers only when they add value;
8. save and reuse the customized procedure;
9. generate a readable procedure reference from the exact executable configuration;
10. continue an existing campaign unchanged even if the originating preset is removed from the product.
