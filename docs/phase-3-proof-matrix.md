# Phase 3 Generic Procedure Proof Matrix

## Purpose

Phase 3 stress-tests the campaign-owned generic procedure architecture against materially different wilderness, travel, and journey procedures. Named systems exist only as creation-time preset metadata and research references. Runtime behavior is defined by the materialized `CampaignProcedure` snapshot and must not depend on preset identity.

The implementation uses original Hex Crawl descriptions and structured behavior. It does not import or reproduce proprietary rulebook text, tables, art, or layout. Where exact primary material was not legally/indexably available, only corroborated behavior is represented and uncertain details remain Manual/Assisted.

## Architectural result

Phase 3 proved that a preset does **not** need to fabricate the six-module legacy `CrawlProcedureProfile` shape. A preset materializes only the generic modules supported by the research and intended implementation.

`CampaignProcedure` is authoritative. `CrawlProcedureProfile` is a nullable compatibility projection retained only when the pinned generic snapshot contains the complete supported legacy-compatible shape. PostgreSQL therefore permits `procedure_json` to be null while preserving `campaign_procedure_json` as the authoritative snapshot.

The six Phase 2 compatibility modules remain:

- `time.interval`;
- `movement.resolution`;
- `movement.hex-progress`;
- `navigation.check`;
- `encounters.cadence`;
- `procedure.helpers`.

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

All Phase 3 mechanics use `procedure.declarative-contract` version 1. That handler means the snapshot contains an intentionally non-executable contract that this runtime version recognizes and preserves. It does not synthesize behavior. A declarative mechanic can not be `Automatic`.

A partial procedure can validate and persist without being a complete `GenericProcedureRuntime`. Native binding still requires every execution handler needed by the current runtime engine. Unknown handlers and unsupported versions remain hard failures rather than being silently reinterpreted.

### Module shell versus selected behavior contract

A Phase 3 module is a capability slot. It describes the category, outputs, compatible mechanic types, configuration shape, and presentation metadata. It does **not** assert every input that every possible mechanic in that slot might consume.

The selected `MechanicDefinition.InputContract` is the authoritative declaration of behavior-specific reads. This distinction is necessary because two mechanics in the same module family can have materially different data dependencies. For example:

- interval movement budgeting consumes `time.interval-duration`;
- journey-progress budgeting does not;
- budget-backed participant activities consume `movement.budget`;
- journey-role assignment does not consume a repeating interval or movement budget;
- activity-based camping consumes participant activity state;
- interval camping consumes the selected interval instead.

Phase 3 module shells therefore have no broad `Reads` entries. This prevents an unused generic possibility from becoming a false dependency of a selected behavior.

## Dependency-source model

Dependency validation evaluates the selected mechanic's genuine input contract together with any Phase 1/2 module reads. A missing selected-module producer is an error unless the selected mechanic explicitly permits a fallback source in the pinned snapshot.

Supported source classifications are:

- `SelectedModule` — another selected generic module produces the value;
- `Dm` / `Manual` — the DM supplies or adjudicates the value;
- `OptionalProvider` — a later or optional provider can supply the value;
- `ExternalState` / `RuntimeState` — campaign/runtime state supplies the value.

A multi-source unresolved input is reported as `UnresolvedInput`, with the complete `AllowedInputSources` flag set and a message that lists every permitted resolution source. It is not mislabeled as a manual requirement merely because `Dm` is one allowed option.

`MissingRequiredProducer` remains an error only when no selected producer exists and no fallback source is allowed.

The Phase 3 catalog no longer uses broad `ExternalInputSources` allowances to make unrelated reads disappear. An external/manual/provider allowance is attached only to a real selected-behavior input. The principal current example is `navigation.check-result`: `navigation-outcome-policy` genuinely consumes a resolved navigation check, while the actual check engine is deferred. That result may therefore come from a selected producer, DM adjudication, an optional provider, or external/runtime state without claiming that some unrelated interval or activity value is needed.

Explicit `ProcedureInputRequirement` data is copied into the pinned mechanic snapshot and round-trips PostgreSQL with the rest of the campaign-owned procedure.

## Terrain relationship contract

`movement.terrain` no longer assumes every terrain relationship is a numeric cost or multiplier. Its selected mechanic uses:

- `adjustmentModel` — the semantic relationship, such as `multiplier`, `activity-cost`, `maximum-pace`, `terrain-difficulty`, or another generic model;
- `terrainAdjustments` — a `map<string>` from terrain tags to the model-specific value or state;
- `routeAdjustmentModel`;
- `weatherAdjustmentModel`.

Numeric models remain valid by storing numeric values as the map values. Symbolic models can instead use values such as `fast`, `normal`, `slow`, or `special`. Phase 3 records this relationship only; generalized environment lookup and execution remain deferred to the later environment phase.

## Native-module proof matrix

The following table is the authoritative Phase 3 statement of which Phase 2 executable modules are actually selected by each proof preset. Structural Phase 3 modules are listed separately in each preset recipe.

| Preset | Phase 2 native modules selected | Complete compatibility profile? |
| --- | --- | --- |
| B/X | `time.interval`, `encounters.cadence` | No |
| AD&D 2e | `time.interval` | No |
| D&D 3.5e | `time.interval` | No |
| D&D 5.5e / 2024 | `time.interval` | No |
| Pathfinder 2e Hexploration | `time.interval` | No |
| Forbidden Lands | `time.interval` | No |
| Worlds Without Number | `time.interval` | No |
| The One Ring 2e | none | No |
| The Alexandrian | all six compatibility modules | Yes |
| Mixed House Rule | all six compatibility modules | Yes |

This matrix is enforced by automated tests. It prevents a familiar ruleset name from causing unverified movement, navigation, progress, encounter, helper, or time behavior to be invented merely to satisfy the old profile shape.

## Preset evidence and mappings

### B/X and Old-School Essentials

The `bx` preset represents day-scale wilderness travel, terrain-sensitive movement budgeting, lost/navigation outcomes, daily encounter cadence, resources, and foraging. The native Phase 2 portion is limited to the verified day interval and per-day encounter cadence; movement budgeting, terrain, navigation outcome, resources, and foraging remain structural contracts.

Its terrain mechanic uses a numeric multiplier map. Its navigation-outcome mechanic consumes a genuine `navigation.check-result`, which remains a deferred resolvable input rather than pretending that a selected navigation core exists.

The `ose-classic-fantasy` preset intentionally reuses the exact B/X `GenericProcedurePresetRecipe`. OSE remains a separate catalog identity only. No duplicate generic implementation exists.

Evidence basis: Old-School Essentials SRD wilderness material as a legally accessible B/X-compatible reference, with secondary B/X cross-checking.

### AD&D 2e

The preset represents a day-scale interval plus movement-budget, terrain-cost, navigation-outcome, and contextual encounter-schedule contracts. Only the time interval is native. Exact wilderness encounter cadence and other insufficiently verified details remain manual/configurable rather than being encoded as a fabricated core cadence.

The contextual encounter schedule consumes the actual selected interval but does not pretend that a Phase 2 encounter-cadence module exists.

Evidence basis: legally accessible secondary AD&D 2e reference material for overland movement and getting-lost concepts. Exact primary-source details that could not be responsibly verified were not asserted.

### D&D 3.5e

The preset represents an hourly interval plus speed-derived movement budget, terrain multiplier behavior, getting-lost outcome, foraging, forced travel, and persistent fatigue/nonlethal consequences. Only the hourly interval is native in Phase 3.

Forced travel produces the transient consequence consumed by the persistent-effect contract. Resource consumption is not falsely modeled as a required source of fatigue.

Evidence basis: D&D 3.5 SRD movement, Survival, and wilderness rules from d20srd.org.

### D&D 5.5e / 2024

The preset represents an hourly interval plus pace/speed budgeting, terrain-limited pace, participant travel activities, forced travel after the ordinary limit, and exhaustion as a generic persistent effect family. Only the hourly interval is native in Phase 3.

SRD 5.2 / the official 2024 travel material models predominant terrain as determining the maximum travel pace. The preset therefore uses:

- `adjustmentModel = maximum-pace`;
- terrain tags mapped directly to `fast`, `normal`, `slow`, or `special` states;
- good roads represented as improving the maximum pace by one step;
- environment-specific weather behavior left structural.

The represented terrain mapping is:

- Arctic → Fast;
- Coastal → Normal;
- Desert → Normal;
- Forest → Normal;
- Grassland → Fast;
- Hill → Normal;
- Mountain → Slow;
- Swamp → Slow;
- Underdark → Normal;
- Urban → Normal;
- Waterborne → Special.

This is deliberately **not** encoded as `fast=1;normal=2;slow=3`, because pace states are outputs of the terrain relationship, not terrain tags or numeric terrain costs.

Evidence basis: official D&D Free Rules 2024 / SRD 5.2 travel material, including `https://www.dndbeyond.com/sources/dnd/br-2024/dms-toolbox`.

### Pathfinder 2e Hexploration

The preset represents a day-scale interval plus speed-derived Hexploration activity budgeting, terrain activity cost, participant/group activities, contextual getting-lost outcomes, Subsist/foraging, and Fortify Camp-style camping. Only the time interval is native in Phase 3; the activity and movement behavior remains structural.

Terrain uses a numeric activity-cost mapping. Foraging and camping use activity-backed variants because those selected behaviors genuinely consume participant activity state.

Evidence basis: Archives of Nethys GM Core Hexploration rules.

### Forbidden Lands

The preset represents a six-hour quarter-day interval plus activity budgeting, terrain travel cost, participant journey activities, navigation/mishap outcomes, supply-die resources, foraging, camping, forced travel, and persistent effects. Only the interval is native in Phase 3. Exact mishap tables, numerical modifiers, and other details not established from a fully accessible primary source remain manual.

Foraging and camping consume the selected activity state. Forced travel produces transient fatigue/mishap consequences, and the persistent-effect contract consumes those consequences.

Evidence basis: Free League publisher material plus independent procedure summaries, without reproducing proprietary tables.

### Worlds Without Number

The preset represents a ten-hour expedition-day interval plus terrain-adjusted movement budgeting, contextual travel/camp encounter scheduling, supplies, foraging, and camping. Only the interval is native in Phase 3. Separate camp encounter execution and resource state remain deferred.

The terrain relationship remains a numeric distance-per-hour multiplier. The encounter schedule consumes the selected interval without inventing a Phase 2 encounter cadence. Camping is interval-based rather than activity-backed.

Evidence basis: Worlds Without Number SRD wilderness exploration and overland travel material.

### The One Ring 2e

The preset is intentionally **not** forced through the legacy hexcrawl runtime profile. It contains no Phase 2 native modules, no `time.interval` module, and no compatibility profile.

Its selected structural graph is behavior-specific:

1. `journey-progress-budget` represents journey-leg/route progress and consumes **no repeating interval**.
2. `terrain-movement-policy` applies route/terrain difficulty to that progress representation.
3. `journey-role-activity-policy` establishes Guide/Hunter/Look-out/Scout role state and consumes **no repeating interval or movement-budget input**.
4. `multi-stage-expedition-process` consumes role state and terrain adjustment and produces `journey.progress`.
5. `progress-triggered-journey-event-policy` consumes that journey progress, role state, and terrain adjustment, and produces both `journey.event` and `effects.transient`.
6. `progressive-expedition-effect` consumes the transient event consequence and represents persistent fatigue.

There is therefore no manual or external `time.interval-duration` diagnostic. There is also no fabricated `resource.consumed` dependency for fatigue. The event/fatigue relationship is expressed directly in the selected graph.

Evidence basis: Free League publisher material plus a secondary Chapter 6 journey summary. Exact event tables, distances, modifiers, and fatigue values are not encoded.

### The Alexandrian

`alexandrian-advanced` remains the fully native Phase 2 proof. It contains all six supported compatibility modules and continues to execute its four-hour interval, continuous variable-distance movement, intra-hex progress, navigation/persistent veer, per-watch encounter cadence, and deterministic helpers from the pinned generic snapshot.

Evidence basis: The Alexandrian hexcrawl watch checklist and later 5E watch checklist.

### Mixed House Rule

The original Dorks & Dice mixed preset intentionally combines a complete existing native core with Phase 3 structural contracts from otherwise independent families: activity budgeting, terrain activity costs, participant activities, navigation outcomes, supply-die resources, activity-based foraging/camping, forced travel, persistent effects, and journey events.

Forced travel is the selected source of transient fatigue consequences; the persistent-effect contract consumes that output. Journey events consume real participant/terrain state rather than persistent effects that they would themselves help cause.

It proves that a complete executable core can coexist with recognized non-executable structural contracts without runtime dispatch depending on preset identity.

## Compatibility, persistence, and legacy HTTP behavior

`CampaignProcedureCompatibilityProjector` remains deliberately narrow. It projects only a complete supported six-module legacy shape. Extra structural modules do not expand the legacy profile.

For projectable procedures, persistence stores both the authoritative generic snapshot and the retained compatibility profile and verifies that they agree. For non-projectable generic procedures, `procedure_json` is null and `campaign_procedure_json` remains authoritative. Historical profile-only rows remain supported through the explicit legacy compatibility boundary.

Pinned snapshots survive PostgreSQL restart with mechanic version, automation level, parameters, input-source requirements, module definitions, and origin metadata intact. A later preset revision or removal of the originating preset does not mutate an existing campaign-owned procedure revision.

The current web/client workbench contract still requires a legacy `RuntimeProfileContract`; Phase 4 owns replacement of that public surface. Phase 3 therefore uses an explicit compatibility boundary instead of fabricating a profile:

- legacy HTTP creation endpoints resolve/materialize the requested preset **before persistence**;
- a non-projectable preset is rejected with HTTP `409 Conflict` before an expedition row is created;
- projectable preset creation continues unchanged;
- if a generic-only expedition already exists because it was created through the supported application/persistence path, the legacy detail `GET /api/expeditions/{id}` returns an explicit `409 Conflict` explaining that the generic `CampaignProcedure` can not be represented by the legacy workbench contract;
- collection summaries remain safe because they do not require `RuntimeProfileContract` conversion.

This guarantees that the HTTP creation request can not successfully persist a generic-only expedition and then fail while serializing a nonexistent compatibility profile.

## Re-audit guarantees

The Phase 3 automated proof suite re-checks the following after the contract refinements:

- The One Ring has no fabricated interval/core requirement and no compatibility profile;
- D&D 2024 terrain tags map honestly to maximum-pace states;
- selected structural mechanics declare only inputs their behavior actually consumes;
- broad external-source allowances do not mask false dependencies;
- unresolved multi-source inputs expose their full permitted source set;
- no named-system identity appears in generic module keys, mechanic keys, handler identities, or implementation type names;
- non-projectable snapshots round-trip PostgreSQL without fabricated profiles;
- projectable snapshots enforce generic/compatibility consistency;
- historical profile-only rows remain loadable/executable through the compatibility boundary;
- `Automatic` plus `procedure.declarative-contract` remains invalid;
- unknown/future handlers and versions are preserved but unsupported by the current runtime;
- preset revision or removal does not mutate an already pinned procedure.

## Runtime leakage audit

Generic module keys, mechanic keys, handler identities, and implementation type names are tested against proof-system identity tokens. Named-system identity is limited to preset/catalog metadata, research documentation, attribution/disclaimer text, and tests that intentionally select presets.

Runtime dispatch uses only embedded generic handler identity and mechanic version. It does not branch on preset key, edition, publisher, or product name.

## Later-phase deferrals

Phase 3 intentionally does not implement the Procedure Composer, typed participant-activity UX/state, full movement-capability composition, generalized environment context, resource inventory/consumption execution, generalized effects, complete camp/forage/forced-travel engines, the full multi-stage journey engine, expanded encounter handoff, or battle-map ownership changes.

Those later subsystems can add execution behind generic contracts without turning named preset identity into a runtime dependency.
