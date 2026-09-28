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

Phase 3 adds these system-neutral structural contracts:

| Module | Mechanic | Phase 3 automation |
| --- | --- | --- |
| `movement.budget` | `movement-budget` | Assisted |
| `movement.terrain` | `terrain-movement-policy` | Assisted |
| `party.activities` | `participant-activity-policy` | Manual |
| `navigation.outcome` | `navigation-outcome-policy` | Assisted |
| `encounters.schedule` | `encounter-schedule-policy` | Assisted |
| `survival.resources` | `resource-consumption-policy` | Manual |
| `exploration.foraging` | `foraging-policy` | Assisted |
| `survival.camping` | `camping-policy` | Assisted |
| `time.forced-travel` | `forced-travel-policy` | Assisted |
| `effects.expedition` | `progressive-expedition-effect` | Manual |
| `journey.events` | `journey-event-policy` | Manual |
| `journey.process` | `multi-stage-expedition-process` | Manual |

All twelve use `procedure.declarative-contract` version 1. That handler means the snapshot contains an intentionally non-executable contract that this runtime version recognizes and preserves. It does not synthesize behavior. A declarative mechanic can not be `Automatic`.

A partial procedure can validate and persist without being a complete `GenericProcedureRuntime`. Native binding still requires every execution handler needed by the current runtime engine. Unknown handlers and unsupported versions remain hard failures rather than being silently reinterpreted.

## Dependency-source model

Dependency validation now checks both module reads and mechanic input contracts. A missing selected-module producer is an error unless the mechanic explicitly permits another source in the pinned snapshot.

Supported source classifications are:

- `SelectedModule` — another selected generic module produces the value;
- `Dm` / `Manual` — the DM supplies or adjudicates the value;
- `OptionalProvider` — a later or optional provider can supply the value;
- `ExternalState` / `RuntimeState` — campaign/runtime state supplies the value.

Materialization converts catalog source declarations into explicit `ProcedureInputRequirement` entries in the pinned mechanic snapshot. This makes the fallback contract part of persisted procedure data rather than an implicit catalog convention.

Diagnostics distinguish missing required producers from manual, optional-provider, and external-state inputs. Only unresolved inputs with no permitted source are dependency errors.

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

The `ose-classic-fantasy` preset intentionally reuses the exact B/X `GenericProcedurePresetRecipe`. OSE remains a separate catalog identity only. No duplicate generic implementation exists.

Evidence basis: Old-School Essentials SRD wilderness material as a legally accessible B/X-compatible reference, with secondary B/X cross-checking.

### AD&D 2e

The preset represents a day-scale interval plus movement-budget, terrain-cost, navigation-outcome, and encounter-schedule contracts. Only the time interval is native. Exact wilderness encounter cadence and other insufficiently verified details remain manual/configurable rather than being encoded as a fabricated core profile.

Evidence basis: legally accessible secondary AD&D 2e reference material for overland movement and getting-lost concepts. Exact primary-source details that could not be responsibly verified were not asserted.

### D&D 3.5e

The preset represents an hourly interval plus speed-derived movement budget, terrain effects, getting-lost outcome, foraging, forced travel, and persistent fatigue/nonlethal consequences. Only the hourly interval is native in Phase 3.

Evidence basis: D&D 3.5 SRD movement, Survival, and wilderness rules from d20srd.org.

### D&D 5.5e / 2024

The preset represents an hourly interval plus pace/speed budgeting, terrain limits, participant travel activities, forced travel after the ordinary limit, and exhaustion as a generic persistent effect family. Only the hourly interval is native in Phase 3.

Evidence basis: official D&D Free Rules 2024 / SRD 5.2 travel material.

### Pathfinder 2e Hexploration

The preset represents a day-scale interval plus speed-derived Hexploration activity budgeting, terrain activity cost, participant/group activities, contextual getting-lost outcomes, Subsist/foraging, and Fortify Camp-style camping. Only the time interval is native in Phase 3; the activity and movement behavior remains structural.

Evidence basis: Archives of Nethys GM Core Hexploration rules.

### Forbidden Lands

The preset represents a six-hour quarter-day interval plus activity budgeting, terrain travel cost, participant journey activities, navigation/mishap outcomes, supply-die resources, foraging, camping, forced travel, and persistent effects. Only the interval is native in Phase 3. Exact mishap tables, numerical modifiers, and other details not established from a fully accessible primary source remain manual.

Evidence basis: Free League publisher material plus independent procedure summaries, without reproducing proprietary tables.

### Worlds Without Number

The preset represents a ten-hour expedition-day interval plus terrain-adjusted movement budgeting, travel/camp encounter scheduling, supplies, foraging, and camping. Only the interval is native in Phase 3. Separate camp encounter execution and resource state remain deferred.

Evidence basis: Worlds Without Number SRD wilderness exploration and overland travel material.

### The One Ring 2e

The preset is intentionally **not** forced through the legacy hexcrawl runtime profile. It contains no Phase 2 native modules and therefore has no compatibility profile.

It represents role-driven participant activity, journey-progress movement budgeting, terrain influence, fatigue, journey events, and a multi-stage route/events/arrival process entirely through Manual/Assisted structural contracts. Missing interval/activity/effect inputs are explicit dependency-source diagnostics rather than fabricated one-day movement or navigation behavior.

Evidence basis: Free League publisher material plus a secondary Chapter 6 journey summary. Exact event tables, distances, modifiers, and fatigue values are not encoded.

### The Alexandrian

`alexandrian-advanced` remains the fully native Phase 2 proof. It contains all six supported compatibility modules and continues to execute its four-hour interval, continuous variable-distance movement, intra-hex progress, navigation/persistent veer, per-watch encounter cadence, and deterministic helpers from the pinned generic snapshot.

Evidence basis: The Alexandrian hexcrawl watch checklist and later 5E watch checklist.

### Mixed House Rule

The original Dorks & Dice mixed preset intentionally combines a complete existing native core with Phase 3 structural contracts from otherwise independent families: activity budgeting, terrain costs, participant activities, navigation outcomes, supply-die resources, foraging, camping, forced travel, persistent effects, and journey events.

It proves that a complete executable core can coexist with recognized non-executable structural contracts without runtime dispatch depending on preset identity.

## Compatibility and persistence behavior

`CampaignProcedureCompatibilityProjector` remains deliberately narrow. It projects only a complete supported six-module legacy shape. Extra structural modules do not expand the legacy profile.

For projectable procedures, persistence stores both the authoritative generic snapshot and the retained compatibility profile and verifies that they agree. For non-projectable generic procedures, `procedure_json` is null and `campaign_procedure_json` remains authoritative. Historical profile-only rows remain supported through the explicit legacy compatibility boundary.

Pinned snapshots survive PostgreSQL restart with mechanic version, automation level, parameters, input-source requirements, module definitions, and origin metadata intact. A later preset revision or removal of the originating preset does not mutate an existing campaign-owned procedure revision.

The legacy web/client `RuntimeProfileContract` remains non-null because Phase 4 owns replacement of that UI/API surface. If a legacy contract is asked to represent an expedition that has no compatibility projection, it fails explicitly rather than inventing a profile.

## Runtime leakage audit

Generic module keys, mechanic keys, handler identities, and implementation type names are tested against proof-system identity tokens. Named-system identity is limited to preset/catalog metadata, research documentation, attribution/disclaimer text, and tests that intentionally select presets.

Runtime dispatch uses only embedded generic handler identity and mechanic version. It does not branch on preset key, edition, publisher, or product name.

## Later-phase deferrals

Phase 3 intentionally does not implement the Procedure Composer, typed participant-activity UX/state, full movement-capability composition, generalized environment context, resource inventory/consumption execution, generalized effects, complete camp/forage/forced-travel engines, the full multi-stage journey engine, expanded encounter handoff, or battle-map ownership changes.

Those later subsystems can add execution behind generic contracts without turning named preset identity into a runtime dependency.
