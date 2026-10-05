# Journey processes and journey events

Phase 12 adds persistent, ruleset-neutral multi-stage journey/challenge execution without replacing the deterministic crawl runtime.

## Ownership

The ownership path is:

```text
CampaignProcedure
    owns exact pinned journey.process / journey.events contracts
        ↓
StoredExpedition.Journey
    owns active/closed processes, event occurrences, resolution history,
    consumed resolution identities, and observed runtime occurrences
        ↓
CrawlRuntimeEngine / focused watch mutations
    remain authoritative for ordinary crawl time and travel
```

Named-system identity and `ProcedureOriginMetadata` are not execution inputs. Preset recipes must contain their complete generic journey parameters before materialization. A persisted `CampaignProcedure` remains authoritative if its creating preset is renamed, changed, or removed.

Phase 12 is an application-layer overlay. `CrawlRuntimeEngine` remains the deterministic travel/time engine and does not gain repository, world, provider, environment, or journey-content dependencies.

## Process model

`JourneyProcessDefinition` describes a campaign/process-owned challenge using open generic keys:

- stable process key and display name;
- ordered stage definitions;
- explicit initial stage;
- optional destination, route, and location references;
- stage completion model;
- optional progress/success/failure/complication thresholds;
- optional approaches and external capability references;
- optional role keys;
- explicit sequential, explicit-target, or outcome-selected transitions.

`JourneyProcessExecutionSnapshot` pins the executable semantics sampled from the exact `journey.process` module when the process starts. It stores stage-transition behavior, progress representation/unit/bounds, completion behavior, role-assignment timing, interval integration, travel-blocking policy, and the selected mechanic handler/version.

A process can use numeric progress in an arbitrary declared unit or explicit symbolic state. No unit conversion, dice formula, target number, publisher event table, or failure consequence is inferred unless it is actually encoded in the pinned procedure or supplied as resolved input.

A failed attempt is not automatically a failed process. Failures and complications are retained as counters; process failure occurs only through explicit resolution or a configured generic limit that says the process fails at that limit.

## Stage resolution and idempotency

Each resolved attempt uses a stable `ResolutionId`. `ConsumedResolutionIds` prevents retries from applying progress, counters, events, or consequences twice.

Resolution may record:

- selected approach;
- current participant/role snapshot;
- resolved outcome key;
- numeric progress delta or explicit progress state;
- success/failure/complication deltas;
- stage completion/transition;
- process completion or failure;
- consequence IDs and generated event occurrence IDs;
- provenance.

Current participant/role assignments are read from the Phase 7 `CrawlPartySheet` at resolution time. The resulting participant/assignment snapshot is retained in history so later reassignment does not rewrite past resolutions. Ambiguous current role ownership requires explicit participant selection rather than choosing an arbitrary assignment.

External capability references are opaque references to optional provider-owned capabilities. They do not make Rules Core or Character Sheet authoritative for process state.

## Journey events

`journey.events` is a separate focused policy. It supports explicit generic trigger sources:

- process progress;
- stage transition;
- completed watch;
- landmark;
- explicit/manual;
- external.

A policy declares whether events are standalone, process-linked, or both. A trigger creates a durable `JourneyEventOccurrence`; it does not fabricate event content. The DM/provider resolves the event key/type, target, consequences, and optional note explicitly.

Stable occurrence IDs make event creation idempotent. Event history can therefore survive request retries and expedition restart without repeating resolved work.

The One Ring proof recipe uses process-linked progress events. The Mixed House Rule proof recipe uses standalone completed-watch, landmark, and explicit events. These are ordinary generic recipe parameters, not runtime branches on preset identity.

## Completed-watch integration

`JourneyRuntimeIntegration.ObserveCompletedWatches` observes a successful authoritative runtime transition after the runtime itself has advanced.

For each newly completed watch it can:

- create one stable standalone watch-triggered event opportunity when the pinned event policy requests it;
- create one stable pending process resolution opportunity for active processes whose pinned execution snapshot uses completed-watch integration.

It does not advance journey progress automatically, invent an event result, or mutate runtime time/travel state. `ObservedRuntimeOccurrenceIds` guarantees that replaying the observation does not duplicate opportunities.

## Environment integration

Journey event creation/resolution may snapshot effective Phase 9 environment facts. The snapshot records the dimensions/values/units/sources relevant at the time of the occurrence.

Environment truth does not become a hidden modifier. Phase 12 preserves environment context for explicit adjudication or provider resolution; it does not invent terrain/event formulas that are absent from the pinned procedure.

## Consequence integration

Journey processes and events do not own resource, survival, movement-effect, or persistent-effect mutation logic.

Resolved `ExpeditionConsequence` values flow through `ExpeditionConsequenceAggregateTransition`, which delegates to the existing Phase 10 effect pipeline and Phase 11 resource/survival consumers. This preserves one consequence identity/idempotency boundary and one owner for each durable subsystem.

Phase 12 retained encounter circumstances as structured deferred consequences rather than interpreting them as tactical combat state. Phase 14 subsequently added server-authoritative v2 encounter handoff projection for relevant pending circumstances and their linked effects, resources, journey provenance, and scenes. Block Initiative remains authoritative for tactical combat and consumption decisions.

## Persistence

PostgreSQL schema version 8 adds `expeditions.journey_state_json jsonb NOT NULL`.

The persisted journey aggregate includes:

- active processes;
- closed/terminal processes;
- event occurrences;
- resolution records;
- journey history;
- consumed resolution IDs;
- observed runtime occurrence IDs.

The process execution snapshot is stored with each process so catalog changes do not alter an in-progress challenge. Procedure authority remains separately pinned in `procedure_json`.

Older pre-release development schemas are reset rather than supported through compatibility reconstruction.

## API and DM UI

Typed HTTP operations expose journey state and focused mutations for:

- starting a process;
- resolving a process stage attempt;
- completing, failing, or abandoning a process;
- creating an event opportunity;
- resolving or skipping an event.

The TypeScript client uses the same contracts through `JourneyApi`. The expedition workbench includes a Journey / Challenge panel for process creation/resolution, event opportunities, structured consequences, and durable history. The browser does not edit raw journey JSON or implement a second process engine.

## Proof presets

### The One Ring 2e

The proof recipe materializes a role-driven three-stage process (`route -> events -> arrival`) with numeric `journey-progress`, sequential transitions, current-at-resolution role sampling, final-stage completion, process-progress event opportunities, and persistent fatigue as a generic downstream effect concept.

No repeating `time.interval` is fabricated. Exact event tables, distances, modifiers, dice formulas, and fatigue amounts remain explicit resolved input because the recipe does not encode them.

### Mixed House Rule

The proof recipe retains its four-hour deterministic watch runtime and adds standalone journey events triggered by completed watches, landmarks, or explicit DM action. Completed-watch observation is additive and idempotent; it does not replace watch bookkeeping.

The recipe also demonstrates Phase 11 forced-travel accounting using movement-budget unit `watch` and forced-travel unit `watches`; those open units are treated as the same simple singular/plural unit without general semantic conversion.

## Phase boundary

Phase 12 completes generic multi-stage journey/challenge state and journey-event opportunities. It intentionally does not add:

- publisher-specific event tables or formulas that are not encoded in the pinned procedure;
- a second travel engine;
- automatic environment modifiers without a stored contract;
- tactical encounter execution or battle-map ownership;
- named-system runtime branches;
- compatibility reconstruction for pre-release development databases.

Phase 14 subsequently added structured encounter handoff across the Hex Crawl/Block Initiative ownership boundary; it did not move tactical combat into Hex Crawl. Phase 14.5 is the next testing-readiness gate, while battle-map ownership remains deferred to Phase 15.
