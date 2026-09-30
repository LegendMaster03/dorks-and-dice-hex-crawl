# Crawl runtime engine

## Authority and state separation

`CrawlRuntimeEngine` is the authoritative deterministic full-watch transition boundary for behavior the current runtime can execute. It consumes a `CrawlRuntimeContext`, a materialized `CampaignProcedure`, crawl-session runtime state, a travel plan, and resolved inputs.

Runtime behavior is selected only from the pinned generic procedure snapshot through handler/version-aware binding. The engine does not depend on preset identity, publisher/system identity, the current preset catalog, an Overworld definition, source maps, player knowledge, or rendered map types.

The persisted session model keeps these axes independent:

- `CrawlSessionContext` identifies `WorldBound`, `AbstractHex`, or `NonSpatial` context.
- `CrawlRuntimeContext` supplies physical crawl scale for spatial sessions.
- `ExpeditionState` owns spatial crawl state.
- `NonSpatialSessionState` owns non-spatial procedure/time/history state.
- `CampaignProcedure` is the required persisted procedure snapshot.
- `OverworldDefinition` remains authoritative world/spatial truth outside the runtime engine.
- `PlayerKnowledgeState` remains world-specific party knowledge outside the runtime engine.
- presentation policy remains separate from procedure mechanics and world truth.

`ExpeditionWorldComposition` projects runtime state into authored world coordinates and subject-specific knowledge only when a real world-bound context exists.

## Procedure binding

The runtime path is:

`CampaignProcedure -> GenericProcedureRuntime.Bind -> CrawlRuntimeEngine`

`GenericProcedureRuntime.Bind` recognizes explicit `(ExecutionHandler, Version)` combinations embedded in the materialized snapshot. It does not resolve a current global mechanic or inspect origin preset identity.

The current native executable handlers are `procedure.time.fixed-interval`, `procedure.movement.resolution`, `procedure.movement.hex-progress`, `procedure.navigation.check-policy`, `procedure.encounter.cadence`, and `procedure.resolution-helpers`. Handler identity is behavior-oriented and remains paired with the persisted mechanic version for dispatch.

Unknown execution handlers and unsupported versions are preserved in persistence but fail clearly when runtime execution is attempted. Recognized `procedure.declarative-contract` mechanics remain intentionally non-executable and can not be marked `Automatic`.

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

Phase 3 also contains structural mechanics that prove later behavior families but are not yet runtime engines. Persistence and API representation do not treat those procedures as exceptional; execution fails only when a caller attempts to bind behavior the current runtime can not execute.

## Abstract traversal

`ExpeditionState.Position` remains available for world rendering and future exact positioning. `WorldPositionPrecision` distinguishes exact positions from a `HexAnchor` produced by abstract procedure movement.

`HexTraversalState` tracks current hex, entry direction, last travel direction, abstract progress, and current exit requirement. It does not ray-cast movement through the rendered regular hex.

Near/far/back requirements remain scale-independent procedure parameters. For example, a procedure may use start `0.5`, near `0.5`, far `1.0`, and back `0.5` factors without making any physical hex scale globally authoritative.

## Watch transition model

A spatial transition is conceptually:

`crawl context + campaign procedure + expedition + travel plan + resolved inputs -> expedition + events + optional pause`

New watches record intended direction, pace/mode metadata, navigation aid, and activities. The runtime consumes explicit resolved travel/navigation/encounter inputs, derives actual direction from intended direction plus lost/veer state, and applies travel against abstract progress.

A boundary can pause a watch with remaining time and an `ActiveWatchState`. The DM can review changed conditions and continue the same watch. Partially completed watches are persisted exactly and survive restart.

The application workbench derives `CurrentDay` from elapsed procedure/travel time in 24-hour bands. This is not yet a general campaign calendar.

## Direction changes and double-back

Direction changes are procedure actions rather than literal geometric pivots. Executable movement mechanics may charge abstract progress for them.

Deliberate double-back handles the current hex's known entry boundary. Full multi-hex route unwinding remains deferred.

## Navigation, lost state, and veer

Navigation remains edition-neutral. `ResolvedNavigation` carries a resolved outcome and optional directional veer steps. The engine has no dependency on an edition-specific skill name, proficiency system, DC formula, or dice expression.

`NavigationRuntimeState` persists lost state and veer. Recognition/reorientation decisions are explicit transitions and survive persistence/restart.

## Resolved-input boundary

The engine does not perform consequential random rolls. `ResolutionProvenance` supports procedure-default, automatic, manual, external-system, and DM-override sources.

Automatic results are produced by the server-side procedure-resolution helper and are persisted with a generated-resolution identifier, watch/version context, resolved values, and audit sequence before application. Manually typed values can not falsely claim automatic provenance.

Rules Core or other tools may provide resolved values, but they do not become authoritative owners of expedition runtime state.

## Encounters and discovery

Encounter cadence is executable procedure configuration; encounter content remains external.

`ExpeditionProcedureRequirements` centralizes whether an encounter resolution is due for the current persisted runtime state. The deterministic engine receives that resolved policy rather than reconstructing it from a named preset.

Crossing a hex boundary never reveals all content in that hex. Keyed-location encounter results use stable subject IDs, while `ExpeditionWorldComposition` validates and projects only the applicable authored subject into player knowledge.

Presentation policy is applied after the mechanical runtime transition.

## Runtime history and persistence

Important transitions append `CrawlRuntimeEvent` records for watch lifecycle, navigation/lost/veer changes, direction changes, travel, hex exits/entries, encounters, discoveries, decisions, provenance, and overrides.

Persistence remains snapshot plus retained history rather than event sourcing:

- the persisted expedition/session snapshot is authoritative current state;
- `CampaignProcedure`, party state, applicable knowledge, generated-resolution verification state, pause reason, and remaining watch time live in the aggregate envelope;
- ordered runtime history is retained in `expedition_events`;
- `(expedition_id, sequence)` is unique;
- restart reconstructs runtime history in sequence order before the next transition.

PostgreSQL stores the required `CampaignProcedure` in the single `expeditions.procedure_json` column. No parallel retired procedure representation is supported.

## Persistent DM workflow

`CrawlSessionService` creates standalone `AbstractHex` and `NonSpatial` sessions without creating an Overworld. `CrawlSessionContextResolver` resolves world-bound scale from the authored world, abstract-hex scale from the persisted context, and no spatial context for non-spatial sessions.

`ExpeditionWorkbenchService` orchestrates executable full-watch transitions. Focused assistants mutate only their owned bookkeeping state and share the same persisted aggregate.

Every mutation carries an optimistic `ExpectedVersion`. Stale clients receive a conflict rather than silently overwriting newer state.

Grid geometry remains protected once an expedition exists so persisted spatial state is not reinterpreted against a different coordinate system.

## Procedure origins and revisions

Creation-time presets may supply optional `ProcedureOriginMetadata`, but origin identity never drives execution. Removing or renaming a preset does not change a pinned expedition.

Procedure revisions are explicit. Existing expeditions keep the exact `CampaignProcedure` revision they already contain unless an explicit update operation changes it.

## Phase 3 proof constraints

The runtime architecture must continue to preserve the Phase 3 corrections:

- The One Ring proof has no fabricated repeating interval.
- journey progress is independent of `time.interval-duration`.
- journey role assignment does not require interval or movement-budget state.
- journey progress feeds progress-triggered events.
- transient event effects feed persistent effects directly.
- broad fallback-source declarations are not used to hide false dependencies.
- runtime dispatch never branches on named-system identity.

## Deferred runtime work

Still deferred are typed participant-activity execution, generalized movement capability composition, environment execution, generalized consequences/effects, survival/resource execution, multi-stage journey execution, expanded encounter runtime/handoff behavior, arbitrary-bearing travel, multi-hex route unwinding, a general calendar/rest clock, real-time multiplayer synchronization, and battle-map behavior.

See `docs/dm-expedition-workbench.md` and `docs/phase-3-proof-matrix.md` for current application and proof-model details.
