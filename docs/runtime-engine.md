# Crawl runtime engine

## Authority and state separation

`CrawlRuntimeEngine` is the authoritative deterministic full-watch transition boundary for behavior the current runtime can execute. It consumes a `CrawlRuntimeContext`, a materialized `CampaignProcedure`, crawl-session runtime state, a travel plan, and resolved inputs.

Runtime behavior is selected only from the pinned generic procedure snapshot through handler/version-aware binding. The engine does not depend on preset identity, publisher/system identity, the current preset catalog, an Overworld definition, source maps, player knowledge, or rendered map types.

The persisted session model keeps these axes independent:

- `CrawlSessionContext` identifies `WorldBound`, `AbstractHex`, or `NonSpatial` context.
- `CrawlRuntimeContext` supplies physical crawl scale for spatial sessions.
- `ExpeditionState` owns spatial crawl state.
- `NonSpatialSessionState` owns non-spatial procedure/time/history state.
- `CrawlPartySheet` owns current expedition party references and typed participant activity/role assignments.
- `CampaignProcedure` is the required persisted procedure snapshot and owns participant-activity policy.
- `OverworldDefinition` remains authoritative world/spatial truth outside the runtime engine.
- `PlayerKnowledgeState` remains world-specific party knowledge outside the runtime engine.
- presentation policy remains separate from procedure mechanics and world truth.

`ExpeditionWorldComposition` projects runtime state into authored world coordinates and subject-specific knowledge only when a real world-bound context exists.

## Procedure binding

The full executable runtime path is:

`CampaignProcedure -> GenericProcedureRuntime.Bind -> CrawlRuntimeEngine`

`GenericProcedureRuntime.Bind` recognizes explicit `(ExecutionHandler, Version)` combinations embedded in the materialized snapshot. It does not resolve a current global mechanic or inspect origin preset identity.

The current native executable handlers are `procedure.time.fixed-interval`, `procedure.movement.resolution`, `procedure.movement.hex-progress`, `procedure.navigation.check-policy`, `procedure.encounter.cadence`, and `procedure.resolution-helpers`. Handler identity is behavior-oriented and remains paired with the persisted mechanic version for dispatch.

Unknown execution handlers and unsupported versions are preserved in persistence but fail clearly when runtime execution is attempted. Recognized `procedure.declarative-contract` mechanics remain intentionally non-executable and can not be marked `Automatic`.

Focused operations do not have to bind an unrelated complete structural procedure merely to interpret one supported stored contract. Phase 7 derives participant-activity policy directly from the exact pinned `party.activities` module, and focused non-spatial interval bookkeeping can resolve a supported stored `time.interval` duration directly. These are ephemeral projections of `CampaignProcedure`, not second persisted procedure models or catalog lookups.

A current-format expedition without a `CampaignProcedure` is invalid current data. There is no alternate historical procedure resolution path.

## Executable procedure policy

The currently executable generic core covers the existing deterministic watch behavior:

- interval/watch duration;
- continuous-distance or hex-step movement;
- fixed or variable resolved continuous distance;
- intra-hex progress and exit factors;
- direction-change progress cost;
- deliberate double-back support;
- navigation checks and persistent lost/veer state;
- encounter cadence/timing;
- deterministic resolution-helper configuration where present.

Structural mechanics prove later behavior families without falsely claiming native execution. Phase 7 gives `participant.activity-state` a real typed expedition representation while the selected activity mechanics remain structural/manual. Persistence and API representation do not treat structural procedures as exceptional; full execution fails only when a caller attempts behavior the current runtime can not execute.

## Participant activity state

The exact materialized `party.activities` module in the pinned `CampaignProcedure` defines the participant-assignment policy. Supported Phase 7 policy projection preserves:

- assignment scope (`Party`, `Participant`, or `Role`);
- activity-budget model;
- generic activity keys;
- generic role keys;
- selected mechanic key/version and handler metadata.

Activity and role identity remains source-defined string data. The runtime does not contain enums for navigator, lookout, guide, hunter, forager, scout, or any other named duty. A single `none` value used as the generic no-role sentinel normalizes to an empty role list at the policy interpretation boundary.

`CrawlPartySheet.ActivityAssignments` is the authoritative current expedition assignment state. It supports party-wide activities without synthetic members, participant activities targeting real members, and role assignments targeting real members. The domain validates structural combinations and dangling references but does not invent exclusivity, cardinality, activity-to-role mappings, or movement/activity capacity rules that the procedure does not encode.

The retired independently writable default-navigator state is not an authority. A procedure that defines a `navigator` role uses the ordinary generalized role assignment.

When an applicable interval starts, its active state receives a copy of the current typed assignments. Subsequent party-sheet edits do not mutate the active interval. This snapshot principle applies to both spatial active watches and non-spatial active intervals that actually exist.

Journey roles are not interval-owned. A structural procedure such as The One Ring may expose and persist current guide/hunter/lookout/scout assignments while having no repeating `time.interval`, no movement-budget dependency for role assignment, and no fabricated active watch.

Phase 7 records and presents assignment state only. It does not make activity assignments automatically consume movement capacity, resolve foraging or camping, change resources, apply effects, or execute journey events.

## Abstract traversal

`ExpeditionState.Position` remains available for world rendering and future exact positioning. `WorldPositionPrecision` distinguishes exact positions from a `HexAnchor` produced by abstract procedure movement.

`HexTraversalState` tracks current hex, entry direction, last travel direction, abstract progress, and current exit requirement. It does not ray-cast movement through the rendered regular hex.

Near/far/back requirements remain scale-independent procedure parameters. For example, a procedure may use start `0.5`, near `0.5`, far `1.0`, and back `0.5` factors without making any physical hex scale globally authoritative.

## Watch transition model

A spatial transition is conceptually:

`crawl context + campaign procedure + expedition + travel plan + resolved inputs -> expedition + events + optional pause`

New watches record intended direction, pace/mode metadata, navigation aid, and a typed snapshot of the current applicable participant assignments. The runtime consumes explicit resolved travel/navigation/encounter inputs, derives actual direction from intended direction plus lost/veer state, and applies travel against abstract progress.

There is no parallel free-form `Activities: string[]` watch authority. Activity edits occur through the party sheet before a watch starts; the active watch then keeps its immutable typed snapshot.

A boundary can pause a watch with remaining time and an `ActiveWatchState`. The DM can review changed conditions and continue the same watch. Partially completed watches, including their typed assignment snapshot, are persisted exactly and survive restart.

The application workbench derives `CurrentDay` from elapsed procedure/travel time in 24-hour bands. This is not yet a general campaign calendar.

## Direction changes and double-back

Direction changes are procedure actions rather than literal geometric pivots. Executable movement mechanics may charge abstract progress for them.

Deliberate double-back handles the current hex's known entry boundary. Full multi-hex route unwinding remains deferred.

## Navigation, lost state, and veer

Navigation remains edition-neutral. `ResolvedNavigation` carries a resolved outcome and optional directional veer steps. The engine has no dependency on an edition-specific skill name, proficiency system, DC formula, or dice expression.

`NavigationRuntimeState` persists lost state and veer. Recognition/reorientation decisions are explicit transitions and survive persistence/restart.

A participant assigned a generic navigator role does not automatically replace the navigation engine. Later generic mechanics may consume `participant.activity-state` where an explicit contract says so.

## Resolved-input boundary

The engine does not perform consequential random rolls. `ResolutionProvenance` supports procedure-default, automatic, manual, external-system, and DM-override sources.

Automatic results are produced by the server-side procedure-resolution helper and are persisted with a generated-resolution identifier, watch/version context, resolved values, and audit sequence before application. Manually typed values can not falsely claim automatic provenance.

Rules Core or other tools may provide resolved values, but they do not become authoritative owners of expedition runtime state. Participant activity state is native Hex Crawl state and requires neither Rules Core nor Character Sheet.

## Encounters and discovery

Encounter cadence is executable procedure configuration; encounter content remains external.

`ExpeditionProcedureRequirements` centralizes whether an encounter resolution is due for the current persisted runtime state. The deterministic engine receives that resolved policy rather than reconstructing it from a named preset.

Crossing a hex boundary never reveals all content in that hex. Keyed-location encounter results use stable subject IDs, while `ExpeditionWorldComposition` validates and projects only the applicable authored subject into player knowledge.

Presentation policy is applied after the mechanical runtime transition.

## Runtime history and persistence

Important transitions append `CrawlRuntimeEvent` records for watch lifecycle, navigation/lost/veer changes, direction changes, travel, hex exits/entries, encounters, discoveries, decisions, provenance, and overrides.

Persistence remains snapshot plus retained history rather than event sourcing:

- the persisted expedition/session snapshot is authoritative current state;
- `CampaignProcedure`, party state including current typed assignments, applicable knowledge, generated-resolution verification state, pause reason, and remaining watch time live in the aggregate envelope;
- spatial and non-spatial active intervals persist typed assignment snapshots where applicable;
- ordered runtime history is retained in `expedition_events`;
- `(expedition_id, sequence)` is unique;
- restart reconstructs runtime history in sequence order before the next transition.

PostgreSQL stores the required `CampaignProcedure` in the single `expeditions.procedure_json` column. Party assignments remain in the existing party state and active snapshots remain in runtime state. No second activity backend or parallel retired procedure representation is supported.

## Persistent DM workflow

`CrawlSessionService` creates standalone `AbstractHex` and `NonSpatial` sessions without creating an Overworld. `CrawlSessionContextResolver` resolves world-bound scale from the authored world, abstract-hex scale from the persisted context, and no spatial context for non-spatial sessions.

`ExpeditionWorkbenchService` orchestrates executable full-watch transitions. Focused assistants mutate only their owned bookkeeping state and share the same persisted aggregate. Non-spatial interval bookkeeping reads the supported stored `time.interval` module without requiring unrelated structural modules to bind.

`ExpeditionPartyService` owns optimistic-concurrency mutation of the party sheet. Participant assignments use that existing boundary rather than a second persistence service, and policy validation is derived from the expedition's pinned procedure.

Every mutation carries an optimistic `ExpectedVersion`. Stale clients receive a conflict rather than silently overwriting newer state.

Grid geometry remains protected once an expedition exists so persisted spatial state is not reinterpreted against a different coordinate system.

## Procedure origins and revisions

Creation-time presets may supply optional `ProcedureOriginMetadata`, but origin identity never drives execution or participant activity behavior. Removing or renaming a preset does not change a pinned expedition.

Procedure revisions are explicit. Existing expeditions keep the exact `CampaignProcedure` revision they already contain unless an explicit update operation changes it. Later catalog changes do not rewrite stored assignment policy keys or active assignment state.

## Phase 3 proof constraints

The runtime architecture must continue to preserve the Phase 3 corrections:

- The One Ring proof has no fabricated repeating interval.
- journey progress is independent of `time.interval-duration`.
- journey role assignment does not require interval or movement-budget state.
- journey progress feeds progress-triggered events.
- transient event effects feed persistent effects directly.
- D&D 2024 Arctic remains `fast-if-appropriately-equipped`.
- broad fallback-source declarations are not used to hide false dependencies.
- runtime dispatch never branches on named-system identity.

Phase 7 adds the further invariant that current `participant.activity-state` belongs to the expedition party/session and active intervals snapshot that state rather than dereferencing mutable standing assignments.

## Deferred runtime work

Still deferred are generalized movement capability composition, environment execution, generalized consequences/effects, survival/resource execution, activity-driven foraging/camping effects, movement/activity capacity consumption, multi-stage journey execution, expanded encounter runtime/handoff behavior, arbitrary-bearing travel, multi-hex route unwinding, a general calendar/rest clock, real-time multiplayer synchronization, and battle-map behavior.

See `docs/dm-expedition-workbench.md` and `docs/phase-3-proof-matrix.md` for current application and proof-model details.