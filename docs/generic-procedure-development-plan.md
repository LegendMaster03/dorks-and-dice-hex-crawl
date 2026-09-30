# Hex Crawl generic procedure and removable preset development plan

## Purpose

Hex Crawl is becoming a self-sufficient, ruleset-agnostic expedition engine. Published game systems, editions, third-party procedures, and named community procedures are represented only as **creation-time presets** that configure generic Hex Crawl mechanics.

Applying a preset materializes a complete campaign-owned procedure snapshot. From that point forward, the campaign and its expeditions execute the materialized generic procedure rather than consulting the originating preset.

The central product principle is:

> **Named systems are removable presets. Mechanics are generic. Procedures are campaign-owned snapshots. The runtime executes behavior, not branding.**

## Pre-release compatibility policy

Hex Crawl is still a pre-release development application. Development-era API, persistence, UI, and internal-model compatibility is not preserved unless a specific compatibility requirement is deliberately approved.

During pre-release development:

- obsolete representations should be removed rather than maintained beside the target architecture;
- the project should not carry adapters, dual persistence, fallback runtime paths, or migrations solely to keep throwaway development data readable;
- breaking development schema changes and database resets are acceptable;
- current architecture and intended product behavior take priority over preserving superseded implementation shapes.

Once Hex Crawl reaches a stage where real user-data compatibility commitments exist, meaningful migrations and compatibility guarantees must be evaluated separately and explicitly.

This policy applies to all phases of this plan.

## Current repository baseline

The current branch establishes these foundations:

- `CampaignProcedure` is the single authoritative procedure representation for current-format sessions;
- built-in procedure keys are creation-time presets rather than reload-time authorities;
- `CrawlRuntimeEngine` is deterministic and accepts resolved inputs rather than depending directly on Rules Core;
- runtime procedure binding is handler/version-aware and reads only the materialized generic snapshot;
- `WorldBound`, `AbstractHex`, and `NonSpatial` contexts separate expedition procedure from map ownership;
- `CrawlPartySheet` stores party members, marching order, watch rotation, standing orders, typed participant activity/role assignments, and explicit movement references;
- active intervals snapshot applicable typed participant assignments so later party edits do not rewrite active runtime state;
- generated/manual/external/DM-override provenance already exists;
- Block Initiative receives a versioned encounter handoff;
- `LocationDetailMapReference` provides an early location-to-detail-map relationship;
- PostgreSQL is the structured persistence backend;
- source-map/raster alignment is isolated enough to proceed independently of procedure architecture.

## Hard architectural invariants

1. No game-system, edition, publisher, product, or third-party procedure identity is required by the Hex Crawl execution engine.
2. Generic mechanic definitions contain no published-system identity.
3. Presets are creation-time recipes, not runtime dependencies.
4. Applying a preset materializes a standalone generic procedure snapshot owned by the campaign/session.
5. Removing, renaming, disabling, or revising a preset must not silently alter an existing materialized procedure.
6. Origin-preset metadata is informational only and may be removed without changing executable behavior.
7. Rules Core is optional enrichment and must not be required for core expedition execution.
8. No general rule-import, scraping, or mirrored-rulebook pipeline is required for preset support.
9. Hex Crawl implements expedition behavior as generic mechanics and configuration.
10. The Dorks & Dice Site owns account identity, authentication, campaign identity, permissions, hosting, and platform navigation.
11. Character Sheet owns character state and may optionally provide capabilities.
12. Block Initiative owns tactical combat.
13. Battle-map ownership remains intentionally undecided; Hex Crawl owns locations and encounter context and stores provider-neutral linked-scene references.
14. Raster/grid auto-alignment remains independent of procedure/preset development.
15. Runtime dispatch must depend on materialized mechanic handler/version contracts, never on named-system identity.
16. A missing `CampaignProcedure` in current-format expedition data is invalid current data, not a trigger for a fallback representation.
17. Do not merge development branches to `main` without explicit authorization.

## Product model

```text
Named Preset
    |
    | apply once
    v
CampaignProcedure
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
Handler/version-aware runtime binding
    |
    v
Expedition Runtime
```

A preset answers:

> Give me a complete familiar starting procedure.

A procedure module answers:

> Which part of expedition procedure does this control?

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

The materialized procedure is independent:

```text
CampaignProcedure
  procedureId
  revision
  key
  name
  modules[]
  overrides[]
```

Optional `ProcedureOriginMetadata` is stored adjacent to the procedure, not inside behavior selection. Runtime execution must remain unchanged if it is absent.

### Preset removal behavior

| Change | New materializations | Existing materialized procedures |
| --- | --- | --- |
| Preset removed | Can no longer select it | Continue unchanged |
| Preset renamed | See new catalog name | Execution unchanged |
| Attribution/disclaimer changed | See current metadata | Execution unchanged |
| Preset recipe corrected | New materializations use corrected recipe | Existing snapshots remain pinned |
| Origin metadata removed | No effect on behavior | Execution unchanged |

## Generic mechanic architecture

Executable mechanics are named for behavior rather than source.

Representative concepts include:

- fixed interval duration;
- terrain movement multiplier/cost/state;
- party-limiting movement;
- pace movement policy;
- scheduled encounter checks;
- navigation outcome policy;
- participant interval activity;
- travel-role assignment;
- forced travel;
- resource consumption;
- progressive expedition effects;
- multi-stage expedition processes.

The generic mechanic contract is centered on:

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

The mechanic contract does not contain publisher, game edition, product, or source-system identity.

### Execution handlers and versions

The persisted handler/version pair is authoritative for runtime dispatch. Current native executable handlers use generic `procedure.*` behavior-oriented identifiers; identifiers from retired development representations are not compatibility contracts.

- supported handler/version pairs may bind and execute;
- unknown handlers remain preserved but unsupported;
- known handlers with unsupported versions remain preserved but unsupported;
- recognized structural/declarative mechanics may persist without being executable;
- `procedure.declarative-contract` can not be `Automatic`.

No runtime implementation may inspect preset identity to choose behavior.

## Procedure module architecture

Modules describe **what part** of the procedure is being resolved. Mechanics describe **how** that selected part behaves.

Initial module families include:

| Family | Candidate modules |
| --- | --- |
| Time | Interval duration, extended/forced travel, stage duration |
| Movement | Base capability, pace/mode, terrain/route effects, mounts/vehicles, water travel, encumbrance |
| Party procedure | Marching order, participant activities, travel roles, watch rotation, navigator selection |
| Navigation | Check cadence, difficulty, outcome, lost state, directional error, recognition, reorientation |
| Exploration | Mapping, searching, scouting, tracking, foraging |
| Encounters | Cadence, schedule, probability, timing, distance, circumstances, handoff |
| Survival/resources | Food, water, light, ammunition, fuel, supplies, fatigue, exposure |
| Journey processes | Complex hazards, crossings, pursuits, journey challenges, other multi-stage procedures |

A module contract exposes category, purpose, execution stage, outputs, compatible mechanics, configuration shape, and presentation metadata.

### Module shell versus selected behavior

Module shells must not introduce broad reads merely because some compatible mechanic might need them.

The selected `MechanicDefinition.InputContract` is authoritative for behavior-specific reads. This is a Phase 3 architectural requirement.

Examples:

- interval-backed movement budgeting may consume `time.interval-duration`;
- journey-progress budgeting does not;
- budget-backed participant activities may consume `movement.budget`;
- journey-role assignment does not consume a repeating interval or movement budget;
- activity-backed camping may consume participant activity state;
- interval-backed camping may consume interval state.

## Dependency model

Generic mechanics declare what they consume and produce.

Dependency evaluation distinguishes:

- selected module producer;
- DM/manual input;
- optional provider;
- external/runtime state.

A selected input with no selected producer but one or more permitted fallback sources is reported as `UnresolvedInput` with the complete allowed-source set.

`MissingRequiredProducer` remains an error when no producer exists and no fallback source is permitted.

Broad fallback-source declarations must not be used to hide incorrect dependencies.

## Procedure persistence

`CampaignProcedure` is the single authoritative persisted procedure snapshot.

For expeditions:

- PostgreSQL `expeditions.procedure_json` is required and stores `CampaignProcedure`;
- there is no parallel procedure representation;
- there is no fallback reconstruction path from a retired development model.

For campaign procedure revisions:

- `campaign_procedure_revisions.procedure_json` stores the exact `CampaignProcedure` revision;
- origin metadata is stored separately and remains optional.

The current pre-release schema may reject earlier development schemas and require a development database reset.

Pinned snapshots must round-trip handler/version metadata, automation level, parameters, input-source requirements, module definitions, overrides, and origin metadata unchanged.

## HTTP/API representation

Current development API surfaces represent procedures using generic contracts derived from `CampaignProcedure`.

An endpoint may expose:

- procedure ID and revision;
- generic procedure key/name;
- materialized module/mechanic metadata;
- handler/version and automation level;
- parameters;
- an optional derived executable-runtime projection when the current runtime can bind the snapshot.

A generic procedure is not exceptional merely because some mechanics are structural or deferred. Persistence and representation remain valid; execution fails only when unsupported runtime behavior is actually invoked.

## Optional provider boundary

Hex Crawl remains fully functional without Rules Core or another external rules provider.

The application owns capability-oriented provider contracts describing what Hex Crawl needs. For travel/environment enrichment, `ITravelEnvironmentProvider` exposes provider metadata, availability, catalog/capability discovery, typed resolution, unresolved states, and provenance. Rules Core is the first concrete adapter for that contract; it is not the contract itself.

Provider identity terminates at the adapter/provenance boundary. Tool Host paths, Rules Core HTTP routes, delegation details, wire failures, and provider-specific transport DTOs do not appear in generic application or runtime contracts.

Conceptually:

```text
Hex Crawl application / procedure engine
    |
    +-- native generic procedure behavior
    +-- explicit DM/manual inputs
    +-- optional provider capability interfaces
            |
            +-- Rules Core adapter
            +-- future Character capability adapter
            +-- future providers
```

Provider selection is deterministic rather than vendor-switched. A single provider can be used directly; multiple providers require explicit selection or one unambiguous default. Phase 6 does not invent provider-precedence arbitration.

Provider absence is explicit feature state. The application distinguishes unavailable/not-configured, unsupported capability, input-required, not-applicable, adjudication/conflict, resolved, and provider failure states. A missing or failed provider never causes Hex Crawl to invent a rule.

Explicit DM values retain precedence. Provider enrichment is attempted only for missing values whose workflow requests provider-backed resolution, and provider results become resolved application inputs before deterministic runtime execution.

The runtime boundary remains:

```text
provider
    ↓
resolved external value
    ↓
application command/input
    ↓
generic deterministic runtime
```

The runtime does not call providers or HTTP services.

Provider identity remains useful provenance. An audit record may truthfully say `Provider: Rules Core`, but `Rules Core` is not procedure identity, mechanic identity, or runtime dispatch input.

## No rule-import requirement

Preset support does not depend on importing, scraping, or mirroring rulebooks.

Development workflow:

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

Use original Hex Crawl descriptions and generic structured data. Source expression, publisher prose, art, logos, and copied layout are not required to execute mechanics.

## Preset proof catalog

Phase 3 proof candidates include:

- B/X;
- Old-School Essentials Classic Fantasy as a shared/alias recipe with B/X unless a verified difference exists;
- AD&D 2e;
- D&D 3.5e;
- D&D 5.5e / 2024;
- Pathfinder 2e Hexploration;
- Forbidden Lands;
- Worlds Without Number;
- The One Ring 2e;
- The Alexandrian;
- a deliberately mixed house-rule procedure.

The proof matrix stresses hours, quarter-days, days, party-wide and participant-specific activities, navigation, terrain, encounter scheduling, resources, foraging, camping, forced travel, persistent effects, journey events, and higher-level journey processes.

## Terrain relationship model

Terrain is represented generically through:

- `adjustmentModel`;
- `terrainAdjustments` as `map<string>`;
- `routeAdjustmentModel`;
- `weatherAdjustmentModel`.

This supports numeric and symbolic relationships without system-specific runtime types.

For the D&D 2024 proof, `maximum-pace` maps terrain tags to symbolic pace states. Arctic terrain is represented as `fast-if-appropriately-equipped`, not unconditional `fast`.

## The One Ring proof graph

The One Ring proof must remain free of a fabricated repeating interval.

Required relationships:

1. journey-progress budgeting consumes no `time.interval-duration`;
2. journey-role assignment consumes no repeating interval or movement-budget state;
3. the journey process consumes role/terrain state and produces `journey.progress`;
4. progress-triggered journey events consume `journey.progress` and produce transient effects;
5. persistent effects consume those transient effects directly;
6. no false resource-consumption dependency is introduced.

## Preset versioning

Preset revisions never silently mutate active procedures.

```text
PresetRevision N
    ↓ materialize
CampaignProcedure Revision 1
    ↓ DM edits
CampaignProcedure Revision 2
    ↓ expedition pins
Exact expedition snapshot
```

A future update workflow may compare newer preset recipes at module level, but the DM explicitly chooses whether to accept changes.

## Procedure Composer UX

Phase 4 introduces a first-class DM Procedure Composer.

Start with recognizable presets, then organize customization around generic behavior:

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

Each module should explain what it controls, selected behavior, parameters, automation level, required inputs, outputs, dependencies, alternatives, campaign modifications, and origin preset only as secondary provenance.

The Composer edits the same `CampaignProcedure` model runtime and persistence consume. It must not introduce a separate UI procedure model.

## Generated procedure documentation

Phase 5 generates readable DM procedure documentation from the exact materialized snapshot. Do not maintain a second manually authored rules document that can drift from execution configuration.

Procedure reference generation remains provider-independent and does not fetch external capability data.

## Party activities and travel roles

Phase 7 establishes one typed, ruleset-neutral participant-assignment model without replacing `CrawlPartySheet`.

Ownership is:

```text
CampaignProcedure
    owns the exact materialized party.activities policy
        ↓
CrawlPartySheet
    owns current expedition participant activity/role assignments
        ↓
Active interval, when one exists
    owns an immutable snapshot of applicable assignments
        ↓
Later mechanics
    may consume participant.activity-state through explicit generic contracts
```

The `party.activities` policy projection is derived from the expedition's pinned `CampaignProcedure`, never from preset identity, origin metadata, or current catalog defaults. It preserves assignment scope, activity-budget model, activity keys, role keys, and selected mechanic metadata.

`CrawlPartySheet` stores stable typed assignments using generic `Party`, `Participant`, and `Role` scopes plus source-defined string activity/role keys. It can represent navigator, lookout, mapper, forager, scout, guide, hunter, quarter-day duties, party-wide activities, journey roles, and future campaign-defined keys without system-specific enums or code changes.

The former independently persisted `DefaultNavigatorMemberId` is removed. Navigator is ordinary role data when the stored procedure defines that role. Likewise, the former free-form watch `Activities: string[]` path is removed; a watch snapshots the current typed assignments rather than accepting a second unowned activity list.

A single stored `roleKeys = none` value used as a no-role sentinel is normalized at the generic policy interpretation boundary and is not offered as an assignable role. No exclusivity, mandatory-role, role-to-activity mapping, or activity-capacity rule is inferred unless a future generic contract explicitly encodes it.

Journey-role state is current expedition state and does not require a repeating interval. The One Ring proof therefore remains structural: guide, hunter, lookout, and scout assignments can be edited without adding `time.interval`, requiring `movement.budget`, or binding the complete structural procedure.

Phase 7 does not execute downstream foraging, camping, movement penalties, resources, effects, fatigue, or journey events. Those remain later-phase concerns.

## Movement capability composition

Phase 8 should compose movement contributors from participants, mounts/vehicles, encumbrance, travel mode, terrain/route, environment, persistent effects, and explicit DM override.

The current explicit `PartyMovementReference` remains a valid authoritative reference and fallback.

## Environment context

Phase 9 introduces ruleset-neutral environment context such as terrain tags, route tags, weather, visibility, elevation/depth, water/current, temperature, hazard tags, and regional effects.

World truth describes the environment. Generic mechanics decide what that environment means to the active procedure.

## Effects and consequences

Phase 10 introduces a generalized structured effect pipeline capable of representing time delay, movement changes, resources, exposure/fatigue, damage/endurance, navigation changes, encounter circumstances, and campaign/provider-defined consequences.

Do not overload `RuntimePauseReason` for persistent effects.

## Resources and survival

Phase 11 adds generic expedition resources and survival behavior after the effect/resource infrastructure is stable.

Possible resources include food, water, light, ammunition, fuel, medicine, generic supplies, mount/vehicle supplies, harvested materials, and campaign-defined resources.

## Multi-stage expedition processes

Phase 12 adds generic Journey Challenge / Complex Hazard support over ordinary expedition intervals.

This is an overlay, not a replacement for the deterministic core travel loop.

## Encounter handoff

Existing structured encounter handoff should be expanded rather than reinvented. Later additions may include surprise, start circumstances, delay, reinforcements, altered composition, depleted resources, route/location changes, linked scenes, and source process provenance.

Block Initiative remains authoritative for tactical combat.

## Battle maps and linked scenes

Battle-map ownership remains deliberately unresolved. Hex Crawl owns location and encounter context; tactical/scene maps remain provider-neutral linked resources until an explicit product decision is made.

## Parallel raster/grid auto-alignment

Raster and baked-grid auto-alignment work remains isolated from procedure/preset architecture. Procedure work should not alter grid-lattice detection, source-raster analysis, registration, or Wonderdraft alignment without a genuine shared contract need.

## Phased roadmap

### Phase 0 — preset identity and persistence foundation — complete

Established creation-time preset identity separation, informational origin metadata, PostgreSQL persistence, explicit session contexts, and deterministic runtime foundations.

Any temporary development scaffolding from this stage is not a compatibility commitment and may be removed when superseded.

### Phase 1 — generic procedure and preset foundation — complete

Introduced generic module/mechanic contracts, preset recipes, materialization, campaign procedure revisions, overrides, dependency metadata, persistence, and architecture tests.

### Phase 2 — native generic execution of current behavior — complete

Made materialized `CampaignProcedure` snapshots the runtime authority for currently executable behavior. Runtime binding now dispatches through persisted handler/version contracts.

### Phase 3 — preset catalog and proof matrix — complete

Implement enough generic structural primitives to represent materially different proof systems without system-specific runtime classes. Preserve current executable behavior where supported; later-phase engines remain deferred.

Phase 3 definition of done includes:

- all required proof presets materialize and validate;
- proof dependency graphs are behaviorally accurate;
- generic-only/structural procedures persist normally;
- executable procedures run through generic binding;
- unsupported/declarative behavior fails clearly when execution is attempted;
- PostgreSQL stores one authoritative procedure representation;
- current APIs represent generic procedure state directly;
- no runtime identity dependency exists;
- exact-head CI is green.

### Phase 4 — Procedure Composer UI — complete

Add preset picker, module review, generic behavior selection, parameter editing, modification count, provenance display, and dependency warnings.

### Phase 5 — generated procedure documentation — complete

Generate readable campaign procedure documentation directly from the materialized snapshot. Procedure references are derived from the exact immutable `CampaignProcedure` revision and remain independent of optional providers.

### Phase 6 — optional provider adapters — complete

External rules/capability sources now sit behind Hex Crawl-owned capability interfaces. Travel/environment resolution uses `ITravelEnvironmentProvider`; Rules Core is the first adapter, provider availability and unresolved states are explicit, DM/manual values bypass providers, provider identity is retained as provenance, and deterministic runtime/domain code remains provider-free.

### Phase 7 — typed participant activities — complete

Added typed party, participant, and role assignment state to `CrawlPartySheet`; exact pinned-procedure activity-policy projection; policy-driven party UI; typed spatial/non-spatial active-interval snapshots; PostgreSQL/API round-trip; removal of the independent default-navigator and free-form watch-activity authorities; and structural journey-role editing without fabricating an interval. Activity mechanics remain declarative/manual and downstream movement/effect/resource/journey execution remains deferred.

### Phase 8 — movement capability composition

Compose participant, mount, vehicle, environment, load, and effect contributors with manual fallback and DM override.

### Phase 9 — environment context

Introduce generic terrain/route/weather/elevation/water/hazard execution context.

### Phase 10 — generalized effect/consequence engine

Add structured expedition effects and provenance.

### Phase 11 — forced travel, survival, and generic resources

Add fatigue/exposure, food/water/supplies, weather/altitude, forced travel, and generic resources.

### Phase 12 — multi-stage journey processes

Add generic Journey Challenge / Complex Hazard execution over ordinary expedition intervals.

### Phase 13 — expanded encounter handoff

Carry structured circumstances, effects, composition changes, journey/hazard provenance, and linked-scene references.

### Phase 14 — battle-map ownership evaluation

Use actual product needs to decide whether tactical maps remain external, Hex Crawl gains a map surface, or both coexist.

## Development workflow

Before each implementation phase or major review pass:

1. fetch current `main`;
2. inspect current validation state and active related work;
3. compare the working branch against current `main`;
4. preserve unrelated changes;
5. update from `main` when required before final validation;
6. run relevant local/CI validation;
7. verify the complete exact-head workflow before requesting merge review.

Do not modify or merge `main` without explicit authorization.

## Architecture definition of done

The target architecture is reached when a DM can:

1. select a recognizable preset;
2. receive a complete generic campaign procedure;
3. understand each major part in plain language;
4. replace individual behaviors with alternatives or house rules;
5. run the resulting executable procedure without the original preset being present;
6. persist structural procedures even when some later execution engines are not yet implemented;
7. run core expedition behavior without Rules Core;
8. use Rules Core, Character Sheet, Block Initiative, and map providers only when they add value;
9. save and reuse customized procedure revisions;
10. generate a readable procedure reference from the exact executable configuration;
11. preserve pinned campaign/session behavior across later preset revisions;
12. evolve pre-release architecture by removing obsolete development representations rather than accumulating parallel compatibility layers.