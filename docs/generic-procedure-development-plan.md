# Hex Crawl generic procedure and removable preset development plan

## Purpose

Hex Crawl is becoming a self-sufficient, ruleset-agnostic expedition engine. Published game systems, editions, third-party procedures, and named community procedures are represented only as **creation-time presets** that configure generic Hex Crawl mechanics.

Applying a preset materializes a complete campaign-owned procedure snapshot. From that point forward, the campaign and its expeditions execute the materialized generic procedure rather than consulting the originating preset.

The central product principle is:

> **Named systems are removable presets. Mechanics are generic. Procedures are campaign-owned snapshots. The runtime executes behavior, not branding.**

Phase 15 adds the presentation principle:

> **Organize the interface around the DM's tabletop workflow and information needs, not around backend modules or persistence boundaries.**

## Pre-release compatibility policy

Hex Crawl is still a pre-release development application. Development-era API, persistence, UI, and internal-model compatibility is not preserved unless a specific compatibility requirement is deliberately approved.

During pre-release development:

- obsolete representations should be removed rather than maintained beside the target architecture;
- the project should not carry adapters, dual persistence, fallback runtime paths, or migrations solely to keep throwaway development data readable;
- breaking development schema changes and database resets are acceptable before the internal-testing compatibility horizon;
- current architecture and intended product behavior take priority over preserving superseded implementation shapes.

### Compatibility horizon for internal human testing

For this roadmap, **Phase 15 — core UX and presentation architecture** is the gate after which internal human testers are expected to begin accumulating campaign and expedition data that should create a meaningful preference for migration over reset.

Before Phase 15 is accepted:

- database resets are an accepted development tool when they simplify or improve the target architecture;
- there is no requirement to migrate development-era data merely to preserve temporary local/test state;
- there is no requirement to retain legacy persistence shapes, API contracts, runtime models, compatibility projections, fallback loaders, or other obsolete representations;
- a superseded development representation should normally be removed rather than supported beside its replacement;
- implementation should optimize for the intended final architecture and coherent UX rather than avoiding a reset;
- compatibility or migration work should be added only when a specific requirement is explicitly approved.

After Phase 15 acceptance and the opening of internal human testing:

- Hex Crawl remains pre-release and testers are not guaranteed permanent data retention;
- a reset remains acceptable when an architectural correction genuinely requires it;
- before making a breaking persisted-data change, development should evaluate whether a straightforward migration can preserve tester data without compromising the architecture;
- when migration is simple and architecture-preserving, migration is preferred because tester data now has a human cost;
- this preference must not turn internal-testing data into a permanent legacy-compatibility burden or justify parallel obsolete models.

Release-level migration and compatibility guarantees remain a separate future decision. Internal testing changes the default cost calculation for resets; it does not create a release compatibility contract.

This policy applies to all phases of this plan.

## Current repository baseline

The current architecture establishes these foundations:

- `CampaignProcedure` is the single authoritative procedure representation for current-format sessions;
- built-in procedure keys are creation-time presets rather than reload-time authorities;
- `CrawlRuntimeEngine` is deterministic and accepts resolved inputs rather than depending directly on Rules Core;
- runtime procedure binding is handler/version-aware and reads only the materialized generic snapshot;
- `WorldBound`, `AbstractHex`, and `NonSpatial` contexts separate expedition procedure from map ownership;
- `CrawlPartySheet` stores party members, marching order, watch rotation, standing orders, typed participant activity/role assignments, and explicit movement references;
- active intervals snapshot applicable typed participant assignments so later party edits do not rewrite active runtime state;
- generated/manual/external/DM-override provenance exists throughout resolution paths;
- Block Initiative receives a server-authoritative versioned encounter handoff;
- `LocationDetailMapReference` and linked-scene references provide provider-neutral location/scene relationships without making Hex Crawl a tactical-map product;
- PostgreSQL is the structured persistence backend;
- Surveyor owns shared image/grid computation while Hex Crawl remains authoritative for accepted map/grid/world state;
- Phase 15 presents Compact, Advanced, and JSON over the same canonical `CampaignProcedure` and unifies expedition state around the DM's current workflow.

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
13. Battle Map product design, tactical-map ownership, and cross-tool tactical-map integration are outside the Hex Crawl roadmap. Existing provider-neutral linked-scene references do not predetermine that future architecture.
14. Raster/grid auto-alignment remains independent of procedure/preset development.
15. Runtime dispatch must depend on materialized mechanic handler/version contracts, never on named-system identity.
16. A missing `CampaignProcedure` in current-format expedition data is invalid current data, not a trigger for a fallback representation.
17. Do not merge development branches to `main` without explicit authorization.
18. Before Phase 15 acceptance, development-era persistence/API/runtime/UI compatibility is not a requirement unless explicitly approved; obsolete shapes should not be retained merely to avoid a database reset.
19. Hex Crawl durable map/grid state must not depend on whether image-processing work executes locally or through the shared headless processing resource.
20. The shared map-processing resource owns computation only; each consuming tool remains authoritative for the accepted domain state produced from those results.
21. Compact, Advanced, JSON, and future Guided presentation must not become competing procedure authorities.
22. Browser presentation must not recreate authoritative procedure, movement, environment, survival, journey, or consequence calculations solely to avoid an application/server query.
23. Nonspatial and no-interval procedures remain first-class and must not be forced into fabricated map/watch/interval state.

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
    +--> Compact presentation
    +--> Advanced presentation
    +--> JSON presentation
    |
    v
Handler/version-aware runtime binding / focused stored-contract projection
    |
    v
Expedition Runtime / focused application operations
```

Phase 15.1 later adds Guided presentation around the same canonical procedure and Compact interaction architecture.

A preset answers:

> Give me a complete familiar starting procedure.

A procedure module answers:

> Which part of expedition procedure does this control?

A generic mechanic answers:

> How does that part behave?

A campaign override answers:

> What did this DM change?

A presentation answers:

> How should this authoritative behavior be organized so the DM can understand and operate it efficiently?

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

Phase 15 preset browsing may use current catalog metadata to help a user choose a starting point. Once materialized, authoring and runtime behavior must remain understandable without a live preset lookup.

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

The selected `MechanicDefinition.InputContract` is authoritative for behavior-specific reads.

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

Compact presentation should translate those technical diagnostics into domain-facing actionable language. Advanced may expose underlying keys and graph relationships; JSON exposes the raw canonical representation. Genuine invalid procedure state must never be hidden.

## Procedure persistence

`CampaignProcedure` is the single authoritative persisted procedure snapshot.

For expeditions:

- PostgreSQL `expeditions.procedure_json` is required and stores `CampaignProcedure`;
- there is no parallel procedure representation;
- there is no fallback reconstruction path from a retired development model.

For campaign procedure revisions:

- `campaign_procedure_revisions.procedure_json` stores the exact `CampaignProcedure` revision;
- origin metadata is stored separately and remains optional.

Pinned snapshots must round-trip handler/version metadata, automation level, parameters, input-source requirements, module definitions, overrides, and origin metadata unchanged.

Phase 15 JSON editing creates ordinary campaign procedure revisions. It does not write directly to storage or establish a JSON-only authority.

## HTTP/API representation

API surfaces represent procedures using generic contracts derived from `CampaignProcedure`.

An endpoint may expose:

- procedure ID and revision;
- generic procedure key/name;
- materialized module/mechanic metadata;
- handler/version and automation level;
- parameters;
- an optional derived executable-runtime projection when the current runtime can bind the snapshot.

A generic procedure is not exceptional merely because some mechanics are structural or deferred. Persistence and representation remain valid; execution fails only when unsupported runtime behavior is actually invoked.

Phase 15 canonical JSON endpoints may create a server-produced canonical draft, validate canonical JSON, create a new procedure, or create an immutable revision. Domain-invalid JSON, identity changes, stale revisions, and semantic no-op revisions are rejected through authoritative application/server checks.

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

Provider selection is deterministic rather than vendor-switched. A single provider can be used directly; multiple providers require explicit selection or one unambiguous default.

Provider absence is explicit feature state. A missing or failed provider never causes Hex Crawl to invent a rule.

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

Provider identity remains useful provenance but is not procedure identity, mechanic identity, or runtime dispatch input.

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

Representative proof procedures include:

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
- a deliberately mixed house-rule procedure;
- Custom/no-origin procedures.

The proof matrix stresses hours, quarter-days, days, party-wide and participant-specific activities, navigation, terrain, encounter scheduling, resources, foraging, camping, forced travel, persistent effects, journey events, and higher-level journey processes.

Phase 15 must exercise materially different proof families through presentation, not only the easiest D&D preset.

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

Phase 15 presentation must preserve this graph: a no-interval journey is journey/process dominant and does not receive a fabricated watch or map requirement.

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

## Procedure authoring UX

Phase 4 introduced the original first-class Procedure Composer. Phase 15 replaces its default interaction architecture while retaining useful low-level generic editing capabilities for Advanced mode.

Phase 15 authoring begins with a first-class preset browser and a readable description of **what the procedure does**, then supports explicit modes:

```text
Compact | Advanced | JSON
```

- Compact uses ordinary tabletop terminology and focused procedure-area editors.
- Advanced progressively exposes mechanic/version, execution support, parameters, input/output contracts, dependencies, alternatives, provenance, and diagnostics.
- JSON exposes the same canonical `CampaignProcedure` through safe server validation and optimistic concurrency.

Compact is not Advanced with less text, and Advanced is not the old giant settings page. All modes share one coherent product architecture and one procedure authority.

Phase 15.1 later adds Guided as the eventual default, wrapping/enriching Compact rather than forking procedure state.

See `docs/phase-15-design-architecture.md`.

## Generated procedure documentation

Readable DM procedure documentation is generated from the exact materialized snapshot. Do not maintain a second manually authored rules document that can drift from execution configuration.

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

The `party.activities` policy projection is derived from the expedition's pinned `CampaignProcedure`, never from preset identity, origin metadata, or current catalog defaults.

`CrawlPartySheet` stores stable typed assignments using generic `Party`, `Participant`, and `Role` scopes plus source-defined activity/role keys. It can represent navigator, lookout, mapper, forager, scout, guide, hunter, quarter-day duties, party-wide activities, journey roles, and future campaign-defined keys without system-specific enums or code changes.

Navigator and watch activities are ordinary typed assignment data rather than parallel authorities.

Journey-role state is current expedition state and does not require a repeating interval. The One Ring proof therefore remains structural: guide, hunter, lookout, and scout assignments can be edited without adding `time.interval`, requiring `movement.budget`, or binding the complete structural procedure.

## Movement capability composition

Phase 8 composes movement contributors from participants, mounts/vehicles, encumbrance, travel mode, terrain/route, environment, persistent effects, and explicit DM override through one generic composition boundary.

The current explicit `PartyMovementReference` remains a valid authoritative reference and fallback.

## Environment context

Phase 9 provides ruleset-neutral environment context such as terrain tags, route tags, weather, visibility, elevation/depth, water/current, temperature, hazard tags, and regional effects.

World truth describes the environment. Generic mechanics decide what that environment means to the active procedure.

## Effects and consequences

Phase 10 provides a generalized structured effect pipeline capable of representing time delay, movement changes, resources, exposure/fatigue, damage/endurance, navigation changes, encounter circumstances, and campaign/provider-defined consequences.

Persistent effects remain separate from `RuntimePauseReason`.

## Resources and survival

Phase 11 provides generic expedition resources, forced-travel accounting/resolution, exposure tracking, foraging/camping operations, and survival behavior on top of Phase 10 consequence ownership.

Supported resource shapes include counted, abstract, supply-die, and external/manual resources without assuming one published inventory model.

## Multi-stage expedition processes

Phase 12 provides generic Journey Challenge / Complex Hazard execution as an overlay, not a replacement for the deterministic core travel loop.

Processes persist explicit definitions, execution snapshots, stage state, progress, success/failure/complication counters, approaches, role snapshots, transitions, terminal history, and stable resolution identities. `journey.events` separately models standalone/process-linked trigger opportunities and explicit event resolution. Completed-watch observation is idempotent and can create pending journey work without automatically advancing process progress or fabricating event content.

Journey consequences reuse Phase 10/11 aggregate mutation boundaries, and journey event environment context snapshots reuse Phase 9 authority without inventing modifiers.

See `docs/journey-processes.md`.

## Internal human testing boundary

The Phase 0–14 backend/application architecture is complete and reviewed, but the old collection-of-panels interaction model is not the intended human-testing baseline.

Phase 15 is therefore the internal-human-testing gate. It builds the coherent production interaction model: unified expedition workspace, Compact, Advanced, JSON, preset browsing, procedure-aware presentation, responsive/focused workspace patterns, and accessibility foundations.

**After Phase 15 acceptance, internal human testing begins.**

Testing continues while Phase 15.1 adds the Guided experience. Phase 15.5 later consolidates problems found during Phase 15/15.1 testing and establishes the stable pre-release baseline.

## Encounter handoff

Phase 14 expanded the structured encounter handoff rather than reinventing it. The v2 handoff carries structured circumstances, effects, composition changes, journey/hazard provenance, depleted resources, route/location changes, linked scenes, and other expedition context needed for combat handoff while preserving Block Initiative ownership of tactical combat.

Phase 15 preserves that server-authoritative behavior while presenting the interrupted encounter coherently in the unified expedition workspace.

Block Initiative remains authoritative for tactical combat.

## Battle Map scope

Battle Map is removed from the Hex Crawl roadmap.

A Battle Map tool has not yet been designed or implemented. Phase 15 does not invent that product, decide future tactical-map ownership, or generalize Hex Crawl into a tactical-map application. Cross-tool tactical-map ownership/integration belongs to a future Battle Map roadmap once that tool exists and is sufficiently mature.

Hex Crawl may continue to carry existing provider-neutral linked-scene references and encounter context. Those current integration surfaces do not predetermine a future Battle Map architecture.

## Shared image-processing boundary

Phase 13 extracted image-processing and grid-recognition functionality into the shared headless Surveyor resource.

The extraction is an infrastructure separation, not a Hex Crawl product-state rewrite:

```text
Hex Crawl map/grid workflows
        ↓
Hex Crawl-owned stable map-processing client/capability boundary
        ↓
Surveyor shared computation
        ↓
validated result DTO
        ↓
Hex Crawl acceptance/domain operation
        ↓
Hex Crawl-owned durable map/grid state
```

Surveyor owns computation. Hex Crawl remains authoritative for accepted Hex Crawl map/grid/world state. Other consuming tools remain authoritative for their own accepted state.

Surveyor availability, timeout/cancellation, malformed responses, validation, trust, diagnostics, timing, retry/idempotency, and resource limits are explicit service-boundary concerns. Surveyor does not imply automatic terrain/road recognition, semantic-map interpretation, new map mechanics, or AI/model identity as runtime rule authority.

## Comprehensive Phase 14 review boundary

Phase 14 completed the broad corrective review of the Phase 0–13 architecture before Phase 15 rebuilt the core interaction model.

Its review covered architecture/state ownership, persistence integrity, backend/API correctness, optional-provider and external-tool boundaries, Surveyor failure behavior, security/trust boundaries, measurement-driven performance, comprehensive UI/UX defects, cross-phase integration, test quality, obsolete scaffolding, and expanded encounter handoff.

Representative Phase 14 scenarios covered interval/watch travel, activity-budget travel, quarter-day travel, mapless/nonspatial journeys, forced travel, resources, environment-driven survival consequences, persistent effects, foraging/camping/recovery, multi-stage journeys, journey-event consequences, restart/continue behavior, and encounter handoff. The One Ring no-interval path received dedicated review to protect it from fabricated watch dependencies.

## Phase 15 core UX and presentation architecture

Phase 15 is a major product-design/frontend-architecture phase, not a cosmetic pass.

Its target Compact persona is an experienced traditional-hexcrawl DM who understands tabletop procedure but should never need to learn Hex Crawl's implementation vocabulary.

Phase 15 definition of done includes:

- one coherent expedition product rather than disconnected map/tracker/configuration experiences;
- a map-centered spatial workspace where map and current expedition state interact;
- an equally intentional nonspatial/journey-first workspace;
- current action and blockers visible as primary state;
- scannable status/stat-block summaries that group related raw and derived information;
- interactive domain summaries for party, movement, navigation, resources, effects, environment, journey, encounter, and history as applicable;
- a first-class preset browser and understandable procedure summaries;
- Compact procedure editing that does not require implementation keys;
- Advanced editing that exposes exact generic mechanics/contracts without abandoning the coherent product architecture;
- expert canonical JSON editing with formatting, validation, useful diagnostics, safe unsaved/reload behavior, authoritative server save, and stale-revision handling;
- all modes operating on the same `CampaignProcedure`;
- origin/provenance understandable to humans but non-authoritative;
- no live preset identity dispatch for presentation or runtime;
- consistent disclosure/context/focused-workspace interaction patterns;
- responsive behavior across wide desktop, ordinary laptop, embedded Site width, tablet-like width, and narrow/mobile width;
- keyboard/focus/touch/accessibility behavior built into reusable components;
- loading, mutation-pending, provider-unavailable, unsupported/manual, empty, validation, stale/conflict, missing-reference, and retry states remaining understandable;
- no significant regression of Phase 14 performance/lifecycle work;
- representative procedures and complete DM workflows exercised through browser and server boundaries;
- actual rendered visual review, not unit tests alone;
- exact-head CI green.

Tracking-sheet research and the detailed interaction architecture are documented in `docs/phase-15-design-architecture.md`.

## Required Phase 15 procedure families

At minimum, presentation validation should cover:

1. OSE/B/X — interval/watch-oriented traditional crawl;
2. Alexandrian Advanced — dense traditional hexcrawl state;
3. D&D 5.5e/2024 — simpler modern-D&D baseline;
4. Pathfinder 2e Hexploration — activity-oriented proof;
5. Forbidden Lands or Worlds Without Number — survival/resource-oriented proof;
6. The One Ring 2e — mandatory no-fabricated-interval journey proof;
7. Mixed House Rule — materialized behavior must drive UI rather than named preset assumptions;
8. Custom — no preset origin.

## Required Phase 15 end-to-end scenarios

Representative acceptance includes:

- preset discovery/inspection/materialization;
- Compact customization and understandable modification/provenance state;
- Advanced low-level customization and round-trip back to Compact;
- canonical JSON syntax failure, domain failure, valid save, stale conflict, and cross-mode round-trip;
- spatial expedition travel and map context through the unified workspace;
- forced-travel/survival resolution and resulting resource/effect visibility;
- The One Ring/nonspatial journey roles, progress, events, consequences, and completion without fake map/watch/interval state;
- encounter interruption and Phase 14 Block Initiative handoff;
- failure/stale/provider/Surveyor/restart/missing-reference/slow-request states.

## Required Phase 15 visual review

Rendered visual review must inspect representative states at:

- wide desktop;
- ordinary laptop;
- embedded Site width;
- tablet-like width;
- narrow/mobile width.

Review should include the default Compact expedition, expanded status/context, focused workspace, preset browser, Compact procedure overview, Advanced procedure editor, JSON editor, nonspatial journey, loading/error/conflict/empty states, long content, and scrolling/off-screen controls.

## Phase 15.1 — Guided Hex Crawl experience

Phase 15.1 adds the eventual default experience for users who understand tabletop RPGs/D&D but do not already know hexcrawling or Hex Crawl.

Guided should wrap/enrich Compact through reusable help/explanation/recommendation/next-action affordances. It may add beginner-friendly preset discovery, examples, "Why?" explanations, consequence explanations, and coaching, but it must not fork procedure authority or runtime state.

Internal human testing continues during Phase 15.1.

## Phase 15.5 — internal-testing stabilization and pre-release hardening

Phase 15.5 resolves issues discovered during Phase 15/15.1 human testing and establishes the stable pre-release baseline.

It should prioritize real tester blockers, persistence/restart/migration issues, diagnostics, cross-tool failures, procedure-proof regressions, accessibility/responsive defects, and performance problems found in actual use. It does not convert temporary tester data into a permanent legacy-model burden.

## Phased roadmap

### Phase 0 — preset identity and persistence foundation — complete

Established creation-time preset identity separation, informational origin metadata, PostgreSQL persistence, explicit session contexts, and deterministic runtime foundations.

### Phase 1 — generic procedure and preset foundation — complete

Introduced generic module/mechanic contracts, preset recipes, materialization, campaign procedure revisions, overrides, dependency metadata, persistence, and architecture tests.

### Phase 2 — native generic execution of current behavior — complete

Made materialized `CampaignProcedure` snapshots the runtime authority for currently executable behavior. Runtime binding dispatches through persisted handler/version contracts.

### Phase 3 — preset catalog and proof matrix — complete

Implemented enough generic structural primitives to represent materially different proof systems without system-specific runtime classes while preserving current executable behavior where supported.

### Phase 4 — Procedure Composer UI — complete

Added the original generic Composer, including preset selection, module review, behavior selection, parameter editing, provenance, modification count, and dependency diagnostics. Phase 15 later replaces its default interaction architecture while retaining useful low-level capabilities for Advanced mode.

### Phase 5 — generated procedure documentation — complete

Generates readable campaign procedure documentation directly from the exact materialized snapshot.

### Phase 6 — optional provider adapters — complete

External rules/capability sources sit behind Hex Crawl-owned capability interfaces; Rules Core is optional enrichment and deterministic runtime/domain code remains provider-free.

### Phase 7 — typed participant activities — complete

Added typed party, participant, and role assignment state, exact pinned activity-policy projection, typed interval snapshots, and structural journey-role editing without fabricating an interval.

### Phase 8 — movement capability composition — complete

Added one generic movement-composition boundary for participant, mount/vehicle, load, mode/pace, environment, persistent-effect, party-reference, and DM-override contributors.

### Phase 9 — environment context — complete

Added generic static/current/override environment facts, deterministic precedence/conflict handling, spatial resolution, pinned-procedure evaluation, provider composition, persistence, HTTP/UI support, and movement handoff.

### Phase 10 — generalized effect/consequence engine — complete

Added stable structured expedition consequences, typed components, persistent effects, lifecycle/idempotency/provenance, persistence, and focused HTTP/UI contracts.

### Phase 11 — forced travel, survival, and generic resources — complete

Added generic counted/abstract/supply-die/external resources, audit history, forced-travel accounting/checks, exposure, camping/foraging support, consequence reuse, persistence, and DM-facing operations.

### Phase 12 — multi-stage journey processes — complete

Added generic Journey Challenge / Complex Hazard definitions/instances, progress, approaches, role-driven resolution, counters/transitions, terminal state/history, stable identities, journey events, completed-watch observation, consequence handoff, persistence/restart, typed contracts, and DM UI.

### Phase 13 — shared headless map-processing extraction — complete

Extracted image/grid computation behind the Surveyor service while retaining Hex Crawl authority for accepted map/grid/world state and explicit failure/trust/validation boundaries.

### Phase 14 — comprehensive architecture/correctness/integration review — complete

Completed the broad Phase 0–13 review/remediation, including persistence, API, security, performance, UI/UX defects, cross-phase integration, test quality, stale/dead paths, and the expanded server-authoritative encounter handoff.

### Phase 15 — core UX and presentation architecture — current

Build the coherent production interaction model, including:

- unified expedition workspace;
- Compact mode;
- Advanced mode;
- expert JSON procedure editing;
- preset browsing;
- procedure-aware presentation;
- responsive interaction model;
- reusable UI primitives for later guidance;
- tracking-sheet-informed information architecture;
- accessibility/focus foundations;
- representative visual and end-to-end review.

After Phase 15 acceptance, internal human testing begins.

### Phase 15.1 — Guided Hex Crawl experience — later

Add the beginner-facing Guided layer over the Compact architecture while internal human testing continues.

### Phase 15.5 — internal-testing stabilization and pre-release hardening — later

Resolve real human-testing problems and establish the stable pre-release baseline.

### Battle Map — separate future roadmap

Battle Map product design, tactical-map ownership, and cross-tool integration are not a Hex Crawl phase. Address them through a future Battle Map roadmap once that product exists and is sufficiently mature.

## Development workflow

Before each implementation phase or major review pass:

1. fetch current `main`;
2. inspect current validation state and active related work;
3. compare the working branch against current `main`;
4. preserve unrelated changes;
5. update from `main` when required before final validation;
6. run relevant local/CI validation;
7. verify the complete exact-head workflow before requesting merge review.

Compatibility handling follows the roadmap horizon:

- before Phase 15 acceptance, do not add migrations, legacy loaders, dual representations, or compatibility adapters merely to avoid development database resets;
- after Phase 15 acceptance opens internal testing, evaluate migration before resetting persisted tester data and prefer migration when it is straightforward and architecture-preserving;
- at all stages, explicit architectural requirements override accidental compatibility with superseded development formats.

Do not modify or merge `main` without explicit authorization.

## Architecture definition of done

The target architecture is reached when a DM can:

1. browse and select a recognizable preset;
2. receive a complete generic campaign procedure;
3. understand each major part in tabletop language;
4. replace individual behaviors with alternatives or house rules;
5. run the resulting executable procedure without the original preset being present;
6. persist structural procedures even when some later execution engines are not implemented;
7. run core expedition behavior without Rules Core;
8. use Rules Core, Character Sheet, Block Initiative, map providers, and Surveyor only when they add value;
9. save and reuse customized procedure revisions;
10. generate a readable procedure reference from the exact executable configuration;
11. preserve pinned campaign/session behavior across later preset revisions;
12. keep Hex Crawl durable map/grid/world state independent of Surveyor implementation details;
13. use Compact, Advanced, and JSON as coherent presentations over the same canonical procedure;
14. run spatial and nonspatial/journey procedures from a workflow-oriented expedition workspace without fabricated state;
15. recover from validation, provider, service, and optimistic-concurrency failures without losing authoritative context;
16. evolve pre-release architecture by removing obsolete development representations rather than accumulating parallel compatibility layers.
