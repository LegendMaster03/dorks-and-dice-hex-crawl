# Phase 3 Generic Procedure Proof Matrix

## Purpose

Phase 3 stress-tests the campaign-owned generic procedure architecture against materially different wilderness, travel, and journey procedures. This document records the research basis and the architectural mapping. It is a proof artifact only; executable truth remains the versioned preset recipe and the `CampaignProcedure` snapshot produced from it.

Named systems appear here and in preset metadata so a DM can select a familiar starting point. Generic module keys, mechanic keys, handler identities, dependencies, parameters, and runtime dispatch remain system-neutral.

## Evidence policy

The implementation uses original Hex Crawl descriptions and structured behavior. It does not import, scrape, mirror, or reproduce rulebook prose, tables, art, or layout.

Where an official SRD, free rule, or publisher page was available, it was preferred. Where exact primary material was not legally/indexably available, the preset encodes only behavior that could be corroborated and marks uncertain details as Manual or Assisted rather than inventing rules.

## Generic Phase 3 primitives

Phase 2's executable primitives remain unchanged:

- fixed interval duration;
- movement resolution and actual-distance policy;
- intra-hex progress;
- navigation-check policy and the existing persistent-veer behavior;
- encounter cadence;
- deterministic resolution helpers.

Phase 3 adds the following system-neutral contracts:

| Module | Mechanic | Purpose | Automation in Phase 3 |
| --- | --- | --- | --- |
| `movement.budget` | `movement-budget` | Distance/activity/point budget and limiting scope | Assisted |
| `movement.terrain` | `terrain-movement-policy` | Terrain, route, and weather movement adjustment | Assisted |
| `party.activities` | `participant-activity-policy` | Party, participant, or role activity assignment | Manual |
| `navigation.outcome` | `navigation-outcome-policy` | Check trigger, lost state, directional error, recognition, reorientation | Assisted |
| `encounters.schedule` | `encounter-schedule-policy` | Travel/camp/terrain/event encounter schedules beyond the core cadence | Assisted |
| `survival.resources` | `resource-consumption-policy` | Resource kinds, inventory style, and consumption cadence | Manual |
| `exploration.foraging` | `foraging-policy` | Forage resolution, time cost, and movement tradeoff | Assisted |
| `survival.camping` | `camping-policy` | Camp setup, time cost, and watch model | Assisted |
| `time.forced-travel` | `forced-travel-policy` | Normal limit, check model, and failure consequence | Assisted |
| `effects.expedition` | `progressive-expedition-effect` | Persistent effect families, accumulation, scope, recovery | Manual |
| `journey.events` | `journey-event-policy` | Event placement, target selection, terrain influence, consequences | Manual |
| `journey.process` | `multi-stage-expedition-process` | Stage/progress/completion model for higher-level journeys | Manual |

All twelve use the versioned generic handler `procedure.declarative-contract` version 1. The handler means the current runtime understands the snapshot as an intentionally non-executable Manual/Assisted contract and may continue executing other supported modules. It does not fabricate results. Unknown handlers or unsupported future versions still fail native binding and remain preserved.

## Required proof matrix

### B/X

- **Preset:** `bx` / B/X.
- **Evidence:** Old-School Essentials SRD wilderness procedure as a legally accessible B/X-compatible reference where available, plus cross-checking against secondary B/X procedure summaries. OSE SRD reference: `https://oldschoolessentials.necroticgnome.com/srd/index.php/Wilderness_Adventuring`.
- **Time structure:** day-scale wilderness travel.
- **Movement:** party-limited daily movement modified by terrain.
- **Party organization / activities:** the verified Phase 3 slice does not require a role engine.
- **Navigation / lost behavior:** navigation may create a lost state with a random/off-course direction; recognition and reorientation are represented separately from the check.
- **Terrain/environment:** multiplier-style generic terrain cost.
- **Encounters:** daily, terrain-sensitive scheduling is represented; exact table probabilities remain data/manual input rather than copied tables.
- **Resources / survival:** food and water consumption concepts; counted inventory.
- **Foraging / camping:** foraging is represented; no separate camp subsystem is asserted by this preset.
- **Forced travel / persistent effects:** not asserted by the verified preset slice.
- **Journey events / process:** not applicable.
- **Generic modules:** Phase 2 core plus movement budget, terrain movement, navigation outcome, encounter schedule, resources, foraging.
- **Native execution:** day interval, core movement, and core per-day cadence. Conditional lost checks and random-direction resolution remain Assisted.
- **Deferred:** generalized environment, resources, and richer lost-state execution.

### Old-School Essentials Classic Fantasy alias

- **Preset:** `ose-classic-fantasy`.
- **Behavior:** exactly reuses the B/X generic recipe.
- **Reason:** Phase 3 found no verified Hex Crawl-relevant procedural difference that justified an artificial second implementation.
- **Architectural proof:** two named catalog identities can materialize the same system-neutral recipe.

### AD&D 2e

- **Preset:** `adnd-2e`.
- **Evidence:** legally accessible secondary transcription/reference material for AD&D 2e DMG overland movement and getting-lost concepts, including `https://adnd2e.fandom.com/wiki/Movement_(DMG)` and related getting-lost references.
- **Time structure:** day-scale overland travel.
- **Movement:** movement-point/distance style with terrain cost.
- **Party organization / activities:** no participant activity engine is required for the verified slice.
- **Navigation / lost behavior:** explicit lost/off-course state with recognition/reorientation contract.
- **Terrain/environment:** movement-point-per-distance representation.
- **Encounters:** exact wilderness encounter cadence was not responsibly verified from a primary open source. The preset therefore uses a contextual/manual schedule contract and does not claim a copied cadence.
- **Resources / foraging / camping / forced travel / effects / journey process:** intentionally not asserted by the verified Phase 3 slice.
- **Generic modules:** Phase 2 core plus movement budget, terrain movement, navigation outcome, encounter schedule.
- **Native execution:** day interval and continuous movement policy. Conditional navigation triggering, movement points, and lost outcomes remain Assisted.
- **Uncertainty:** encounter scheduling and exact terrain values remain manual/configurable until stronger evidence is available.

### D&D 3.5e

- **Preset:** `dnd-3-5e`.
- **Evidence:** D&D 3.5 SRD movement, Survival, and wilderness rules: `https://www.d20srd.org/srd/movement.htm`, `https://www.d20srd.org/srd/skills/survival.htm`, and `https://www.d20srd.org/srd/wilderness.htm`.
- **Time structure:** hourly travel, with an eight-hour ordinary daily limit before forced travel.
- **Movement:** speed-derived distance with difficult-terrain multipliers.
- **Party organization / activities:** foraging can trade movement rate for resource gathering.
- **Navigation / lost behavior:** checks can create a lost state; off-course direction, recognition, and reorientation are separate concepts.
- **Terrain/environment:** generic normal/difficult multiplier model.
- **Encounters:** no exact copied encounter tables or schedule are encoded.
- **Resources:** foraging produces food/water conceptually; a complete inventory engine is deferred.
- **Foraging:** Assisted skill/check contract with half-speed tradeoff.
- **Camping:** not asserted as a distinct procedure.
- **Forced travel:** escalating checks after the normal travel limit.
- **Persistent effects:** fatigue/nonlethal consequences represented structurally.
- **Journey events / process:** not applicable.
- **Native execution:** hourly interval and fixed continuous movement. Conditional navigation triggering remains Assisted.
- **Deferred:** skill resolution, effect engine, inventory/resource state.

### D&D 5.5e / 2024

- **Preset:** `dnd-2024`.
- **Evidence:** official D&D Free Rules (2024), DM's Toolbox / Travel Pace: `https://www.dndbeyond.com/sources/dnd/br-2024/dms-toolbox` and the SRD 5.2 Gameplay Toolbox.
- **Time structure:** hourly travel; ordinary travel is eight hours before extended-travel checks.
- **Movement:** speed/pace-derived, with slowest-traveler limitation.
- **Party organization / activities:** participant travel activities are represented as a generic activity contract.
- **Navigation:** environment supplies navigation difficulty; the check and detailed outcome remain Assisted.
- **Terrain/environment:** maximum pace by terrain, roads improving the effective limit, environment-specific adjustments.
- **Encounters:** no copied encounter table or encounter-generation subsystem.
- **Resources / foraging / camping:** full resource/camp systems are not part of this preset proof.
- **Forced travel:** escalating Constitution-save model after eight hours.
- **Persistent effects:** Exhaustion is represented as a generic persistent effect family, not as a D&D-specific runtime class.
- **Journey events / process:** not required.
- **Native execution:** hourly interval and continuous-distance movement. Navigation/activity detail remains Assisted.
- **Deferred:** movement capability composition, environment engine, effect engine.

### Pathfinder 2e Hexploration

- **Preset:** `pathfinder-2e-hexploration`.
- **Evidence:** Archives of Nethys, GM Core Hexploration: `https://2e.aonprd.com/Rules.aspx?ID=3103`.
- **Time structure:** day-scale Hexploration activities.
- **Movement:** speed-derived activity budget; Travel cost varies by open/difficult/greater difficult terrain.
- **Party organization / activities:** group Travel/Reconnoiter and individual Hexploration activities.
- **Navigation / lost behavior:** mapping and getting-lost concepts are represented structurally; exact resolution remains Assisted.
- **Terrain/environment:** activity-cost model; roads improve travel terrain one step.
- **Encounters:** daily random-encounter cadence is represented at the core level; terrain probability remains structural.
- **Resources / survival:** Subsist is represented through foraging rather than a full inventory.
- **Foraging:** Subsist-style activity replacing other Hexploration activity capacity.
- **Camping:** Fortify Camp-style activity contract.
- **Forced travel / persistent effects / journey process:** not required by the verified Hexploration proof.
- **Native execution:** daily interval, whole-hex movement, and per-day core encounter cadence. Contextual getting-lost behavior remains Assisted.
- **Deferred:** speed-to-activity calculation, typed activity state, environment/context engine.

### Forbidden Lands

- **Preset:** `forbidden-lands`.
- **Evidence:** Free League product/quickstart availability plus independent procedure summaries. Publisher entry: `https://freeleaguepublishing.com/games/forbidden-lands/`. Exact journey/mishap tables are not reproduced.
- **Time structure:** four quarter-days per day; the preset uses a six-hour interval.
- **Movement:** hex travel measured in quarter-day activities with terrain affecting effort.
- **Party organization / activities:** participant activities including travel, leading the way, keeping watch, foraging, hunting/fishing, camp, rest, and sleep are represented as generic keys.
- **Navigation / lost behavior:** leading-the-way failure can produce mishap/off-course state; exact mishap results remain manual.
- **Terrain/environment:** generic activity/hex-cost model.
- **Encounters:** per-quarter-day travel cadence is represented by the native per-watch cadence.
- **Resources:** food, water, ammunition/light concepts use a generic supply-die inventory model.
- **Foraging / camping:** explicit activity contracts.
- **Forced travel:** travel beyond the ordinary quarter-day limit has a generic Endurance/check contract.
- **Persistent effects:** fatigue/mishap consequences are structural.
- **Journey events / process:** no separate higher-level journey process is required.
- **Native execution:** six-hour interval, whole-hex movement, navigation requirement, per-interval encounter cadence.
- **Uncertainty:** exact mishap tables, roll modifiers, and some numerical terrain values remain manual because a fully accessible primary rules source was not available during this phase.

### Worlds Without Number

- **Preset:** `worlds-without-number`.
- **Evidence:** Worlds Without Number SRD wilderness exploration and overland travel, including `https://wwn.quadrifons.com/2.0%20The%20Rules%20of%20the%20Game/2.12.0%20Wilderness%20Exploration%20and%20Expeditions.html` and the adjacent Overland Travel section.
- **Time structure:** ordinary overland travel is modeled as a ten-hour expedition day.
- **Movement:** terrain-dependent distance per hour, with route/weather adjustments.
- **Party organization / activities:** no mandatory role system is asserted.
- **Navigation / lost behavior:** no navigation mechanic is asserted beyond verified source evidence used in this phase.
- **Terrain/environment:** distance-per-hour terrain model.
- **Encounters:** separate travel and camp opportunities are represented by a generic schedule contract; the native core check handles the travel-interval portion.
- **Resources:** food, water, shelter/fire concepts with counted supplies.
- **Foraging:** half/full-day tradeoff represented structurally.
- **Camping:** separate night/camp state and encounter opportunity.
- **Forced travel / persistent effects:** not asserted by the verified slice.
- **Journey events / process:** not required.
- **Native execution:** ten-hour interval, continuous movement, per-interval travel encounter check.
- **Deferred:** separate camp encounter execution, resource engine, terrain/environment provider.

### The One Ring 2e

- **Preset:** `the-one-ring-2e`.
- **Evidence:** Free League publisher material (`https://freeleaguepublishing.com/shop/the-one-ring/core-rules-2/`) plus a secondary procedural summary explicitly scoped to Chapter 6 of the Third Printing (`https://theroleplayersguild.com/articles/the-one-ring-rpg/journeys-travel/journey-rules`).
- **Time structure:** higher-level journey legs and events rather than a simple repeating hexcrawl watch. A one-day core interval exists only as a compatibility/native fallback.
- **Movement:** route/journey progress.
- **Party organization / activities:** role-based Guide, Hunter, Look-out, and Scout assignments.
- **Navigation:** route decisions are role/journey-process concerns rather than a Phase 2 persistent-veer model.
- **Terrain/environment:** terrain/roads influence event and journey difficulty.
- **Encounters:** journey events are not treated as ordinary random encounter checks.
- **Resources / foraging / camping:** role responsibilities are represented, but detailed resource/camp subsystems are not claimed.
- **Forced travel:** not encoded without stronger exact evidence.
- **Persistent effects:** Fatigue is represented as a persistent generic effect family.
- **Journey events:** Guide-driven progress places events; events target travel roles and can add fatigue.
- **Higher-level process:** route -> repeated progress/events -> arrival.
- **Native execution:** only the generic fallback interval/movement can execute in Phase 3.
- **Manual/Assisted:** role assignments, marching progress, event placement/targeting, fatigue accumulation.
- **Deferred:** the full multi-stage journey engine and generalized effects.
- **Uncertainty:** exact distances, modifiers, event tables, and fatigue values are intentionally not encoded or claimed complete.

### The Alexandrian

- **Preset:** `alexandrian-advanced`.
- **Evidence:** The Alexandrian, "Hexcrawl – Part 6: Watch Checklist" (`https://thealexandrian.net/wordpress/17349/roleplaying-games/hexcrawl-part-6-watch-checklist`) and the later 5E watch checklist (`https://thealexandrian.net/wordpress/46229/roleplaying-games/5e-hexcrawl-part-6-watch-checklists`).
- **Time structure:** four-hour watches.
- **Movement:** continuous distance with variable actual distance.
- **Party organization / activities:** navigator/watch actions exist in the source procedure; typed participant activity ownership is deferred.
- **Navigation / lost behavior:** navigation checks, persistent veer, recognition, and reorientation.
- **Terrain/environment:** average distance can vary by terrain/conditions; generalized environment composition is deferred.
- **Encounters:** per-watch checks with timing helper.
- **Resources / foraging / camping / forced travel / persistent effects / journey process:** not required by the existing advanced preset.
- **Generic modules:** Phase 2 native core.
- **Native execution:** all currently encoded Alexandrian behavior remains native through pinned generic mechanics.
- **Deferred:** typed activity and generalized environment layers.

### Mixed House Rule

- **Preset:** `mixed-house-rule`.
- **Source:** original Dorks & Dice proof composition; it is intentionally not a published ruleset.
- **Time structure:** four-hour native travel intervals.
- **Movement:** continuous fixed-distance native core plus an activity/distance budget and activity-cost terrain contract.
- **Party organization / activities:** per-participant watch activities with navigator/lookout/forager/scout-style generic roles.
- **Navigation:** native core navigation is disabled; a separate Assisted contract supplies watch-triggered lost/veer, recognition, and reorientation behavior without changing runtime dispatch.
- **Terrain/environment:** activity-cost terrain with route improvement.
- **Encounters:** no native random encounter cadence, proving structural modules do not require encounter generation.
- **Resources:** supply-die food/water/light contract.
- **Foraging / camping:** activity-based contracts.
- **Forced travel:** escalating check after a watch limit.
- **Persistent effects:** level-like fatigue family with safe-rest recovery.
- **Journey events:** role-targeted watch/landmark events.
- **Higher-level process:** no full journey-process module is selected; this deliberately shows individual primitives can compose without taking an entire named procedure family.
- **Native execution:** four-hour interval and fixed continuous movement execute directly from the pinned snapshot.
- **Architectural proof:** the procedure combines activity budgeting, terrain costs, supply-die resources, activity-based camp/forage, forced travel, persistent effects, assisted navigation outcomes, and role-targeted events in a combination no named proof preset uses.

## Preset revision and snapshot guarantees

Phase 3 retains the lifecycle:

`Preset revision -> materialize -> CampaignProcedure revision -> DM revision -> expedition-pinned snapshot`.

Tests cover:

- preset revision changes producing a new materialization without mutating the prior procedure;
- campaign procedure revisions retaining the same `ProcedureId` while incrementing revision;
- expeditions retaining their exact pinned revision;
- origin metadata remaining informational;
- execution after the originating preset is absent;
- PostgreSQL/restart round trips preserving all module/mechanic definitions, versions, automation levels, and parameters.

No preset revision is looked up at runtime.

## Dependency and execution conclusions

The proof matrix exposed one Phase 2 gap: native binding previously rejected every selected mechanic whose execution handler was not executable, even when the mechanic was intentionally Manual/Assisted. That made it impossible to pin a structurally complete future-facing procedure while still executing supported portions.

Phase 3 resolves this with one explicit versioned declarative handler. It is not a wildcard and it does not execute or synthesize results. Only mechanics deliberately authored with `procedure.declarative-contract` version 1 are accepted as non-executable contracts. Unknown handlers and future versions continue to raise `UnsupportedProcedureMechanicException`.

The existing compatibility projector is not expanded for Phase 3 structural mechanics. It still projects only the six legacy-compatible core modules. Extra Phase 3 modules remain authoritative in `CampaignProcedure` and survive persistence independently.

The existing `CrawlProcedureCatalog.All` collection remains the three-profile legacy runtime compatibility view so Phase 3 does not broaden the old runtime-profile UI/API surface. `CrawlProcedureCatalog.Catalog` is the authoritative creation-time preset catalog and `Resolve` uses it. Phase 4 owns replacement of that compatibility presentation surface with the Procedure Composer/preset UX.

## Runtime leakage audit

Phase 3 generic module keys, mechanic keys, execution handlers, Domain type names, and Application implementation type names are covered by tests against proof-system identity tokens. The only named-system identities introduced by Phase 3 are preset/catalog metadata, research documentation, attribution/disclaimer text, and tests that intentionally select a preset by key.

Runtime binding dispatches exclusively on embedded generic execution-handler identity plus mechanic version. It does not switch on preset key, publisher, product, or edition.

## Later-phase deferrals

Phase 3 intentionally does not implement:

- the Procedure Composer;
- typed participant-activity UX/state;
- full movement capability composition;
- generalized environment context;
- generalized resource inventory/consumption execution;
- generalized effect/consequence execution;
- complete camp/forage engines;
- complete forced-travel execution;
- the multi-stage journey engine;
- expanded encounter handoff;
- battle-map ownership changes.

The Phase 3 contracts are structured so those phases can add execution without changing named preset identity into a runtime dependency.
