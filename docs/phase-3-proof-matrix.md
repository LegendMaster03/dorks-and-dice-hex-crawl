# Phase 3 Generic Procedure Proof Matrix

## Purpose

Phase 3 stress-tests the campaign-owned generic procedure architecture against materially different wilderness, travel, and journey procedures. Named systems exist only as creation-time preset metadata and research references. Runtime behavior is defined by the materialized `CampaignProcedure` snapshot and must not depend on preset identity.

The implementation uses original Hex Crawl descriptions and structured behavior. It does not import proprietary rulebook text, tables, art, or layout. Where exact primary material was not legally or indexably available, only corroborated behavior is represented and uncertain details remain Manual or Assisted.

## Architectural result

Phase 3 proves that materially different systems can materialize only the generic modules actually required by their procedures. A proof preset does not need to invent an executable core merely to fit a fixed aggregate shape.

`CampaignProcedure` is the single authoritative procedure representation. PostgreSQL stores the exact materialized snapshot in required `expeditions.procedure_json`. Structural procedures persist normally even when one or more selected mechanics are intentionally non-executable in the current runtime.

The Phase 2 executable module families remain:

- `time.interval`;
- `movement.resolution`;
- `movement.hex-progress`;
- `navigation.check`;
- `encounters.cadence`;
- `procedure.helpers`.

Those six executable modules bind through generic behavior-oriented handler identities: `procedure.time.fixed-interval`, `procedure.movement.resolution`, `procedure.movement.hex-progress`, `procedure.navigation.check-policy`, `procedure.encounter.cadence`, and `procedure.resolution-helpers`. The handler/version pair is part of the pinned `CampaignProcedure`; no handler identity carries preset or retired profile terminology.

Phase 3 adds these system-neutral structural module families:

| Module | Representative mechanics | Phase 3 automation |
| --- | --- | --- |
| `movement.budget` | `movement-budget`, `journey-progress-budget` | Assisted |
| `movement.terrain` | `terrain-movement-policy` | Assisted |
| `party.activities` | `participant-activity-policy`, `journey-role-activity-policy` | Manual |
| `navigation.outcome` | `navigation-outcome-policy` | Assisted |
| `encounters.schedule` | `encounter-schedule-policy`, `contextual-encounter-schedule-policy` | Assisted |
| `survival.resources` | `resource-consumption-policy` | Manual |
| `exploration.foraging` | `foraging-policy`, `activity-foraging-policy` | Assisted |
| `survival.camping` | `camping-policy`, `activity-camping-policy` | Assisted |
| `time.forced-travel` | `forced-travel-policy` | Assisted |
| `effects.expedition` | `progressive-expedition-effect` | Manual |
| `journey.events` | `journey-event-policy`, `progress-triggered-journey-event-policy` | Manual |
| `journey.process` | `multi-stage-expedition-process` | Manual |

All Phase 3 structural mechanics use `procedure.declarative-contract` version 1. The handler means the snapshot contains an intentionally non-executable contract that this runtime version recognizes and preserves. It does not synthesize behavior. A declarative mechanic can not be `Automatic`.

A structural procedure can validate, persist, round-trip, and appear through current API contracts without being bindable to a complete `GenericProcedureRuntime`. Binding remains strict: unknown handlers and unsupported versions fail rather than being silently reinterpreted.

## Module shell versus selected behavior contract

A Phase 3 module is a capability slot. It describes the category, outputs, compatible mechanic types, configuration shape, and presentation metadata. It does not assert every input that every possible mechanic in that slot might consume.

The selected `MechanicDefinition.InputContract` is authoritative for behavior-specific reads. This distinction matters because mechanics in the same module family can have materially different dependencies. For example:

- interval movement budgeting consumes `time.interval-duration`;
- journey-progress budgeting does not;
- budget-backed participant activities consume `movement.budget`;
- journey-role assignment does not consume a repeating interval or movement budget;
- activity-based camping consumes participant activity state;
- interval camping consumes the selected interval instead.

Phase 3 module shells therefore have no broad `Reads` entries. An unused possibility must not become a false dependency of the selected behavior.

## Dependency-source model

Dependency validation evaluates the selected mechanic's genuine input contract together with any applicable executable-module reads. A missing selected-module producer is an error unless the selected mechanic explicitly permits another source in the pinned snapshot.

Supported source classifications are:

- `SelectedModule` — another selected generic module produces the value;
- `Dm` / `Manual` — the DM supplies or adjudicates the value;
- `OptionalProvider` — an optional provider can supply the value;
- `ExternalState` / `RuntimeState` — campaign/runtime state supplies the value.

A multi-source unresolved input is reported as `UnresolvedInput`, with the complete `AllowedInputSources` flag set and a message listing every permitted resolution source. It is not mislabeled as a manual-only requirement merely because `Dm` is one allowed source.

`MissingRequiredProducer` remains an error when no selected producer exists and no fallback source is permitted.

The Phase 3 catalog does not use broad `ExternalInputSources` allowances to make unrelated reads disappear. An external/manual/provider allowance is attached only to a real selected-behavior input. The principal current example is `navigation.check-result`: `navigation-outcome-policy` genuinely consumes a resolved navigation check while the generalized check engine remains deferred. That value may therefore come from a selected producer, DM adjudication, an optional provider, or external/runtime state.

Explicit `ProcedureInputRequirement` data is copied into the pinned mechanic snapshot and round-trips PostgreSQL with the rest of the campaign-owned procedure.

## Terrain relationship contract

`movement.terrain` does not assume every terrain relationship is a numeric cost or multiplier. Its selected mechanic uses:

- `adjustmentModel` — semantic relationship such as `multiplier`, `activity-cost`, `maximum-pace`, `terrain-difficulty`, or another generic model;
- `terrainAdjustments` — a `map<string>` from terrain tags to the model-specific value or state;
- `routeAdjustmentModel`;
- `weatherAdjustmentModel`.

Numeric models store numeric values as map values. Symbolic models can use states such as `fast`, `normal`, `slow`, `special`, or a generic conditional state such as `fast-if-appropriately-equipped`.

Phase 3 records the relationship only. Generalized environment lookup and execution remain deferred to the environment phase.

## Phase 2 executable-module proof matrix

The following table records which already-executable Phase 2 modules are selected by each proof preset. Phase 3 structural modules are additional materialized contracts, not implied executable behavior.

| Preset | Phase 2 executable modules selected |
| --- | --- |
| B/X | `time.interval`, `encounters.cadence` |
| AD&D 2e | `time.interval` |
| D&D 3.5e | `time.interval` |
| D&D 5.5e / 2024 | `time.interval` |
| Pathfinder 2e Hexploration | `time.interval` |
| Forbidden Lands | `time.interval` |
| Worlds Without Number | `time.interval` |
| The One Ring 2e | none |
| The Alexandrian | all six Phase 2 executable modules |
| Mixed House Rule | all six Phase 2 executable modules |

The matrix prevents a familiar system name from causing unverified movement, navigation, progress, encounter, helper, or time behavior to be invented.

## Preset evidence and mappings

### B/X and Old-School Essentials

The `bx` preset represents day-scale wilderness travel, terrain-sensitive movement budgeting, lost/navigation outcomes, daily encounter cadence, resources, and foraging. The executable Phase 2 portion is limited to the verified day interval and per-day encounter cadence; movement budgeting, terrain, navigation outcome, resources, and foraging remain structural contracts.

Its terrain mechanic uses a numeric multiplier map. Its navigation-outcome mechanic consumes a genuine `navigation.check-result`, which remains a deferred resolvable input rather than pretending that a selected navigation core exists.

The `ose-classic-fantasy` preset intentionally reuses the exact B/X `GenericProcedurePresetRecipe`. OSE remains a separate catalog identity only. No duplicate generic implementation exists.

Evidence basis: Old-School Essentials SRD wilderness material as a legally accessible B/X-compatible reference, with secondary B/X cross-checking.

### AD&D 2e

The preset represents a day-scale interval plus movement-budget, terrain-cost, navigation-outcome, and contextual encounter-schedule contracts. Only the time interval is executable in the current runtime. Exact wilderness encounter cadence and other insufficiently verified details remain manual/configurable rather than being encoded as fabricated executable policy.

The contextual encounter schedule consumes the actual selected interval but does not pretend that an executable encounter-cadence module exists.

Evidence basis: legally accessible secondary AD&D 2e reference material for overland movement and getting-lost concepts. Exact primary-source details that could not be responsibly verified were not asserted.

### D&D 3.5e

The preset represents an hourly interval plus speed-derived movement budget, terrain multiplier behavior, getting-lost outcome, foraging, forced travel, and persistent fatigue/nonlethal consequences. Only the hourly interval is executable in Phase 3.

Forced travel produces the transient consequence consumed by the persistent-effect contract. Resource consumption is not falsely modeled as a required source of fatigue.

Evidence basis: D&D 3.5 SRD movement, Survival, and wilderness rules from d20srd.org.

### D&D 5.5e / 2024

The preset represents an hourly interval plus pace/speed budgeting, terrain-limited pace, participant travel activities, forced travel after the ordinary limit, and exhaustion as a generic persistent-effect family. Only the hourly interval is executable in Phase 3.

SRD 5.2 / official 2024 travel material models predominant terrain as determining maximum travel pace. The preset therefore uses:

- `adjustmentModel = maximum-pace`;
- terrain tags mapped directly to symbolic maximum-pace states rather than numeric terrain costs;
- good roads represented as improving the maximum pace by one step;
- environment-specific weather behavior left structural.

The represented terrain mapping is:

- Arctic → `fast-if-appropriately-equipped`;
- Coastal → `normal`;
- Desert → `normal`;
- Forest → `normal`;
- Grassland → `fast`;
- Hill → `normal`;
- Mountain → `slow`;
- Swamp → `slow`;
- Underdark → `normal`;
- Urban → `normal`;
- Waterborne → `special`.

The Arctic value deliberately preserves the rule that Fast travel requires appropriate equipment such as skis. It is not collapsed to unconditional `fast`.

The mapping is also deliberately not encoded as `fast=1;normal=2;slow=3`, because pace states are outputs of the terrain relationship, not terrain tags or numeric terrain costs. Phase 9 environment execution can later interpret generic conditional states without changing this materialized proof contract.

Evidence basis: official D&D Free Rules 2024 / SRD 5.2 travel material.

### Pathfinder 2e Hexploration

The preset represents a day-scale interval plus speed-derived Hexploration activity budgeting, terrain activity cost, participant/group activities, contextual getting-lost outcomes, Subsist/foraging, and camping. Only the time interval is executable in Phase 3; activity and movement behavior remains structural.

Terrain uses a numeric activity-cost mapping. Foraging and camping use activity-backed variants because those selected behaviors genuinely consume participant activity state.

Evidence basis: Archives of Nethys GM Core Hexploration rules.

### Forbidden Lands

The preset represents a six-hour quarter-day interval plus activity budgeting, terrain travel cost, participant journey activities, navigation/mishap outcomes, supply-die resources, foraging, camping, forced travel, and persistent effects. Only the interval is executable in Phase 3. Exact mishap tables, numerical modifiers, and details not established from sufficiently accessible material remain manual.

Foraging and camping consume selected activity state. Forced travel produces transient fatigue/mishap consequences, and the persistent-effect contract consumes those consequences.

Evidence basis: Free League publisher material plus independent procedure summaries, without reproducing proprietary tables.

### Worlds Without Number

The preset represents a ten-hour expedition-day interval plus terrain-adjusted movement budgeting, contextual travel/camp encounter scheduling, supplies, foraging, and camping. Only the interval is executable in Phase 3.

The terrain relationship remains a numeric distance-per-hour multiplier. The encounter schedule consumes the selected interval without inventing an executable encounter cadence. Camping is interval-based rather than activity-backed.

Evidence basis: Worlds Without Number SRD wilderness exploration and overland travel material.

### The One Ring 2e

The preset intentionally contains no Phase 2 executable modules and no `time.interval` module. Its selected structural graph is behavior-specific:

1. `journey-progress-budget` represents journey-leg/route progress and consumes **no repeating interval**.
2. `terrain-movement-policy` applies route/terrain difficulty to that progress representation.
3. `journey-role-activity-policy` establishes Guide/Hunter/Look-out/Scout role state and consumes **no repeating interval or movement-budget input**.
4. `multi-stage-expedition-process` consumes role state and terrain adjustment and produces `journey.progress`.
5. `progress-triggered-journey-event-policy` consumes that journey progress, role state, and terrain adjustment and produces both `journey.event` and `effects.transient`.
6. `progressive-expedition-effect` consumes the transient event consequence and represents persistent fatigue.

There is no `time.interval-duration` diagnostic. There is also no fabricated `resource.consumed` dependency for fatigue. The event/fatigue relationship is expressed directly in the selected graph.

Evidence basis: Free League publisher material plus a secondary Chapter 6 journey summary. Exact event tables, distances, modifiers, and fatigue values are not encoded.

### The Alexandrian

`alexandrian-advanced` remains the fully executable Phase 2 proof. It contains all six currently supported executable modules and continues to execute its four-hour interval, continuous variable-distance movement, intra-hex progress, navigation/persistent veer, per-watch encounter cadence, and deterministic helpers from the pinned generic snapshot.

Evidence basis: The Alexandrian hexcrawl watch checklist and later 5E watch checklist.

### Mixed House Rule

The Dorks & Dice mixed preset intentionally combines a complete executable core with Phase 3 structural contracts from otherwise independent families: activity budgeting, terrain activity costs, participant activities, navigation outcomes, supply-die resources, activity-based foraging/camping, forced travel, persistent effects, and journey events.

Forced travel is the selected source of transient fatigue consequences; the persistent-effect contract consumes that output. Journey events consume real participant/terrain state rather than persistent effects they would themselves help cause.

It proves that a complete executable core can coexist with recognized non-executable structural contracts without runtime dispatch depending on preset identity.

## Persistence and current API behavior

`CampaignProcedure` is the sole supported procedure snapshot for current-format expeditions.

PostgreSQL behavior is intentionally simple:

- `expeditions.procedure_json` is required and stores the exact `CampaignProcedure`;
- `campaign_procedure_revisions.procedure_json` stores exact campaign procedure revisions;
- there is no second procedure representation or fallback reconstruction path;
- earlier development procedure schemas may require a development database reset.

Pinned snapshots survive PostgreSQL restart with mechanic version, execution handler, automation level, parameters, input-source requirements, module definitions, overrides, and origin metadata intact. A later preset revision or removal of the originating preset does not mutate an existing snapshot.

Current HTTP surfaces use `CampaignProcedureContract` and can represent structural generic procedures directly. The contract exposes generic identity, revision, module/mechanic metadata, handler/version data, automation level, and parameters. When the current deterministic runtime can bind the procedure, the API may also expose a derived executable runtime projection.

A structural procedure is therefore not rejected merely because one of its mechanics is intentionally deferred. Persistence and representation succeed normally. An execution request fails clearly only when runtime binding is actually required and the selected handler/version set is not executable by the current runtime.

## Pre-release architecture policy

Hex Crawl does not preserve superseded development API, persistence, UI, or internal model shapes unless a specific requirement is deliberately approved.

Phase 3 therefore optimizes for the target architecture rather than parallel representations. Breaking development schema changes and database resets are acceptable at this stage. Future migration obligations for meaningful user data must be considered separately once such compatibility commitments exist.

## Re-audit guarantees

The Phase 3 proof suite re-checks the following after these refinements:

- The One Ring has no fabricated `time.interval` module or interval dependency.
- `journey-progress-budget` does not consume `time.interval-duration`.
- `journey-role-activity-policy` does not consume interval or movement-budget state.
- journey process produces `journey.progress`.
- progress-triggered journey events consume `journey.progress`.
- journey events produce `effects.transient`.
- persistent effects consume transient effects rather than fabricated resource inputs.
- D&D 2024 terrain tags map to model-specific symbolic maximum-pace states, including conditional Arctic Fast travel.
- D&D 2024 terrain remains terrain-tag-to-state data rather than a pace-name-to-number map.
- selected structural mechanics declare only inputs their behavior actually consumes.
- Phase 3 module shells introduce no broad false `Reads`.
- broad `ExternalInputSources` allowances do not mask false dependencies.
- unresolved multi-source inputs use `UnresolvedInput` and expose the complete allowed-source set.
- `MissingRequiredProducer` remains an error when no producer or permitted fallback exists.
- OSE and B/X share the same recipe object.
- mixed rules compose independent generic families.
- `procedure.declarative-contract` can not be `Automatic`.
- unknown/future handlers and unsupported versions remain preserved but unsupported.
- exact `CampaignProcedure` snapshots round-trip PostgreSQL normally.
- preset revision/removal does not mutate pinned procedures.
- executable generic procedures continue to execute from their pinned snapshots.
- runtime dispatch remains independent of preset/system identity.

## Deferred execution

Phase 3 stops at architectural proof for the new structural families. It does not implement:

- the full Procedure Composer UI;
- typed participant-activity execution;
- movement capability composition;
- generalized environment execution;
- generalized consequence/effect execution;
- survival/resource execution;
- multi-stage journey execution;
- expanded encounter runtime.

Those remain owned by later phases in `docs/generic-procedure-development-plan.md`.
