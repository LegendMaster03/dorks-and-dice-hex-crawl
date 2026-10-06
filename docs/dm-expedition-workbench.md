# DM expedition workbench

## Purpose

The expedition workbench is the persistent DM-facing layer over Hex Crawl's deterministic runtime. It collects procedure-relevant choices and resolved inputs, invokes the application/runtime boundary, persists the resulting snapshot/history, and presents the state needed to continue play.

The persisted aggregate is a crawl session with explicit context:

`CampaignProcedure + session runtime/history + session context + party state + resolved inputs -> transition -> persisted session`

`CrawlSessionContext` has three concrete forms:

- `WorldBound(overworldId)` — spatial runtime plus authored world/map/knowledge composition.
- `AbstractHex(name, orientation, CrawlRuntimeContext)` — spatial runtime with persisted hex scale but no Overworld record.
- `NonSpatial(name)` — procedure/time/history state without hex coordinates, world position, or Overworld.

The full map workbench exists only for `WorldBound`. Abstract-hex and non-spatial sessions use the same persisted procedure model without fabricating world state.

## Procedure authority

Each crawl session stores exactly one authoritative `CampaignProcedure` snapshot. It contains the materialized generic modules, mechanic handler/version metadata, parameters, and campaign overrides used by that session.

Named presets are creation-time recipes only. `ProcedureOriginMetadata` may record the selected preset key/display name/revision as optional provenance, but reload, runtime binding, procedure helpers, participant-activity policy, movement-composition policy, automatic-resolution verification, and advancement never require a live preset or origin identity.

A later preset correction therefore affects new materializations only. Existing sessions remain pinned until explicitly revised.

## Pre-release API policy

Hex Crawl is pre-release. Current HTTP contracts are development surfaces, not compatibility commitments. When the procedure architecture changes, current API contracts should represent `CampaignProcedure` directly rather than adding adapters around a retired representation.

Current expedition detail/workbench responses therefore carry `CampaignProcedureContract`. It exposes procedure ID, revision, generic identity, materialized modules, handler/version/automation metadata, parameters, and an optional derived executable runtime projection.

Structural or currently non-executable generic procedures remain valid API data. They are not rejected merely because the deterministic runtime can not bind every selected mechanic. Focused projections may interpret a supported stored module independently when the operation does not require the complete procedure to bind. Runtime binding fails only when an execution operation actually requires unsupported behavior.

## Product composition

- **DM tools home** lists crawl sessions across all context kinds and links directly to Travel / Watch, Navigation, and Encounter Cadence assistants.
- **Abstract-hex tracker** runs spatial bookkeeping from persisted hex scale without constructing an Overworld.
- **Non-spatial tracker** presents procedure/time/history, party participant-assignment state, and non-spatial movement-budget/capability state without invented spatial fields.
- **Full crawl workbench** is world-bound only and composes crawl state with authored world data, map rendering, discovery controls, knowledge preview, and current movement composition.
- **Focused assistants** operate on the same persisted session and mutate only their owned state/history.

Top-level assistant routes remain setup-and-entry surfaces. They do not own a second procedure model and do not calculate authoritative runtime behavior in the browser.

## State ownership

### World truth

`OverworldDefinition` owns the continuous world coordinate space, mathematical grid, semantic features, locations, and source-map representations. Procedure presets do not mutate world truth.

### Procedure configuration

`CampaignProcedure` owns procedure configuration for the session. Executable policy is obtained through generic handler/version-aware binding. The selected stored `party.activities` module separately defines the exact participant-assignment policy for the session, including assignment scope, activity budget model, activity keys, role keys, and mechanic metadata. The selected stored `movement.budget` and `movement.terrain` modules define movement-composition policy when present.

The currently executable core can describe:

- watch/interval duration;
- continuous-distance versus hex-step travel;
- fixed versus variable resolved travel distance;
- encounter cadence;
- navigation/lost/veer behavior;
- intra-hex progress;
- direction-change progress cost;
- deliberate double-back support;
- exit-progress factors;
- optional deterministic travel/navigation/encounter helper configuration.

Structural declarative modules remain persisted and presented even when their later effect/execution engine has not been implemented. Typed activity state and Phase 8 movement composition do not falsely make unrelated declarative mechanics automatic.

### Session context and runtime state

Spatial contexts use `ExpeditionState` and `ActiveWatchState` for current travel state, including current hex, position, direction, navigation state, distance, abstract progress, elapsed time, completed watches, active-watch timing, pending decisions, and the immutable participant-assignment snapshot captured when the active watch started.

`NonSpatialSessionState` owns elapsed procedure time, completed watches, history, and an optional lightweight active watch. When a real interval exists, that active watch can also snapshot typed participant assignments. It contains no dummy spatial values.

Journey-role state does not require a watch. Structural procedures such as The One Ring can keep current role assignments and journey-progress movement state in the party/procedure layer with no fabricated `time.interval` or physical distance.

### Party running sheet

`CrawlPartySheet` remains optional expedition-owned state for members, marching order, watch rotation, standing orders, typed participant activity/role assignments, typed manual/stable movement contributors, and explicit movement references.

The party sheet is the authoritative current assignment and explicit movement-input state. It does not duplicate the procedure's activity catalog or movement-policy schema. Activity and role identities are generic string keys supplied by the exact pinned `CampaignProcedure`; movement contributors use generic kinds, operations, scopes, units, movement-unit keys, and provenance.

`CountsTowardPartyMovement` controls whether a participant can limit party movement. Mount/vehicle contributors can replace assigned rider/passenger movement units so those participants are not double-counted, while unassigned conveyances do not affect the party. `ExternalCharacterId` remains an optional lookup key rather than a copied Character Sheet record.

The former independently persisted default-navigator field is removed. If a stored procedure exposes a `navigator` role, that role is represented through the ordinary generalized assignment state. Removing a member must not leave dangling assignment or movement references; the UI may cascade draft references and the domain independently rejects invalid sheets.

When an interval begins, applicable current activity assignments are copied into the active interval. Later edits to standing party assignments do not mutate the already-active assignment snapshot. Historical generated/consumed movement resolutions retain their resolved values and provenance when later party movement state changes.

### Movement composition

`MovementCompositionPolicyResolver` derives policy only from the exact pinned `CampaignProcedure`. `MovementCapabilityComposer` combines current expedition-owned capabilities in deterministic stages and returns effective quantity, limiter, contributor breakdown, provenance, unresolved inputs/diagnostics, reference use, and an optional physical watch-distance suggestion.

Participant/mount/vehicle base capability is resolved first, followed by load/encumbrance, travel mode/pace, terrain/route or already-resolved environment consequences, persistent-effect consequences, and final DM override. Compatible physical units are converted only when explicit `DistanceUnit` metadata permits it.

`PartyMovementReference` remains a valid explicit authority and fallback. It is not double-counted when typed capability state already resolves movement. A DM override has final precedence and preserves the pre-override value for explanation.

Numeric terrain multipliers can compose directly. Other budget/cost/maximum-pace semantics remain distinct. Symbolic constraints such as D&D 2024 `fast-if-appropriately-equipped` are shown as unresolved/adjudicative rather than guessed into a number.

Rules Core and other optional providers can supply a missing resolved capability through the provider-neutral application boundary. They do not own movement policy or call the runtime. Explicit expected-distance input and locally resolved/manual composition take precedence; provider unavailability can fall back to an explicit party movement reference.

### Player knowledge and presentation

`PlayerKnowledgeState` exists only for world-bound sessions because disclosure refers to authored world subjects. Presentation policy controls automatic knowledge projection after mechanical runtime transitions.

Manual discovery remains an explicit DM action.

## Guided watch workflow

The full tracker asks only for inputs relevant to the executable runtime policy and current session state.

At a new spatial watch it can request:

- intended direction;
- pace/travel mode;
- navigation-aid/suppression choices;
- resolved travel amount in the selected movement model;
- navigation outcome and veer when required;
- encounter outcome when cadence requires a check.

Participant roles and activities are edited once through Party & travel order rather than through a second free-form watch field. Starting a new watch snapshots the current typed assignments into that watch. The active-watch display renders participant/activity/role relationships rather than an unowned comma-separated activity list.

For a paused watch, the workbench exposes the pending decision and remaining time. A conditions-review pause resumes the same watch rather than creating a new one.

Fixed continuous-distance mechanics accept one effective distance. Variable-distance mechanics require expected and actual resolved distance. Hex-step mechanics accept a non-negative step count.

When movement composition has enough information, the server supplies a suggested physical distance for the exact watch interval or active-watch remaining duration. The browser does not independently reconstruct party limiting, interval scaling, terrain semantics, provider precedence, or `PartyMovementReference` fallback. When composition is unresolved, manual resolved-distance entry remains available and no value is invented.

## Resolved-input provenance

The application supports independent provenance for travel, navigation, encounter, and boundary decisions:

- procedure default;
- server-generated automatic resolution;
- manual roll/input;
- external system;
- DM override.

Server-generated helper results are persisted with a generated-resolution ID, audit sequence, watch number, exact resolved values, and aggregate version. Applying automatic provenance requires the matching persisted generated result. Later party edits do not rewrite already-generated or consumed movement-resolution values/provenance.

External tools can supply resolved inputs without becoming authoritative owners of expedition state. Participant activity state and movement-composition state are native Hex Crawl expedition state and do not require Rules Core or Character Sheet.

## Phase 15 spatial interaction

World-bound spatial play uses one map-centered expedition workspace. A floating current-cell adjacency navigator is rendered from presentation geometry for the party's current cell and its traversable adjacent edges. The current production adapter is regular-hex and translates selected adjacent cells back to the runtime's existing direction representation; the presentation contract itself does not require exactly six edges and does not implement alternate topology support.

The map and navigator share one semantic selection. Selecting an adjacent map cell selects the corresponding intended edge, and selecting an edge previews the corresponding adjacent cell. Selection is intent only: it does not assign the current hex, world position, progress, elapsed time, navigation result, encounter state, or any other durable runtime value. The explicit Continue/Run action remains the authoritative mutation boundary.

Course and pace are reusable operating choices. The UI reconciles persisted active-watch course/pace into browser intent state and avoids asking for a deterministic movement value when the authoritative movement-composition projection already supplies it. Navigation failure preserves intended course separately from actual resolved course.

Current-action routing presents the active blocker directly. Encounter interruption, navigation resolution, lost-boundary decisions, forced-travel/resource consequences, journey resolution, ordinary travel, and nonspatial interval work do not all route through one generic travel form. The existing runtime/application controllers remain authoritative underneath those focused presentations.

The navigator uses screen-relative edge descriptions and adjacent-cell identity rather than assuming map north. Compass terminology must only be introduced if authoritative map-orientation metadata supports it.

### Direct DM repositioning

The workbench distinguishes ordinary travel from authoritative DM repositioning. **Move party** is intended for initial-position corrections, teleportation, scene transitions, or other cases where the party should simply be placed in another spatial cell.

This operation does not simulate movement. It preserves accumulated elapsed travel, total distance, completed watches, and non-positional expedition state while resetting the local traversal context: intra-cell progress, entry/last-travel direction, intended/actual direction, and lost/veer state. Any active full-workbench travel watch is ended and pending pause/remaining-time state is cleared because those values belong to the prior local travel segment.

For world-bound sessions the continuous map position is re-anchored to the destination cell. Presentation knowledge follows the configured automatic entered-hex policy, but unrelated locations/features are not automatically discovered. The reposition is persisted and audited as a DM override, and stale generated procedure-resolution tokens are discarded.

## UI projections

The expedition UI derives its display from authoritative persisted state. Depending on context and executable policy it may show:

- day/watch and elapsed/remaining time;
- current hex and course;
- lost/veer state;
- total distance and intra-hex progress;
- effective movement quantity and composition status;
- limiting movement participant/unit;
- applied and retained movement contributors;
- movement provenance, missing inputs, and adjudication diagnostics;
- current pause/pending decision;
- encounter state;
- current party participant activity/role assignments;
- active-interval participant assignment snapshot;
- participant assignment scope and activity-budget model as secondary policy context;
- recent history and watch ledger;
- party register and explicit movement references;
- the complete persisted generic procedure/module reference;
- executable helper formulas where available;
- generated helper results and provenance;
- discovery controls and knowledge preview for world-bound sessions.

Generic procedure keys may be humanized for display, but their stored identity is preserved. No UI organization depends on source-system branding.

Current frontend filenames may still use the word "profile" as presentation vocabulary, but the API data feeding those views is the generic `CampaignProcedureContract`; there is no second persisted procedure model behind the UI.

## Persistence and restart behavior

Production persistence is PostgreSQL through `PostgresHexCrawlStore`. `IHexCrawlStore` remains the application boundary.

The `expeditions` table stores:

- explicit session context;
- nullable world reference and world-only knowledge;
- party state, including current typed participant assignments and explicit/manual movement contributors;
- explicit `PartyMovementReference` when supplied;
- generated procedure resolutions and their provenance;
- required `procedure_json` containing the authoritative `CampaignProcedure`;
- optional procedure-origin metadata;
- discriminated runtime state, including typed active-interval assignment snapshots where applicable;
- pause/remaining-watch state;
- aggregate version and timestamps.

Ordered runtime history remains in `expedition_events`.

The current pre-release schema intentionally does not support retired development activity/navigator representations. Development data using those shapes may be reset rather than carried through compatibility aliases.

Container/restart validation proves that mapped and mapless sessions reload their exact generic procedure, party assignment/movement state, generated-resolution history, and applicable runtime snapshots after PostgreSQL/application restart.

## Optimistic concurrency

Every runtime and party mutation carries an expected aggregate version. Concurrent stale callers receive a conflict rather than silently overwriting newer state.

This protection is independent of procedure representation.

## Current scope boundary

Phase 8 completes generic movement capability composition over the Phase 7 typed party state. It does not execute downstream mechanics merely because a contributor category exists. Environment contributors are already-resolved movement consequences only; persistent-effect contributors are already-resolved movement consequences only; assigning forage still does not produce food; and forced-travel/resource mechanics remain structural.

Still deferred are Phase 9 generalized environment context/execution, Phase 10 generalized effects/consequence lifecycle, Phase 11 forced-travel/survival/resource execution, Phase 12 multi-stage journey execution, expanded encounter runtime, battle maps, and real-time multiplayer synchronization.

See `docs/generic-procedure-architecture.md` and `docs/phase-3-proof-matrix.md` for the procedure and proof-model details.

## Automatic raster analysis service

The source-map workspace requests automatic grid analysis through the Hex Crawl server. The browser no longer performs computer-vision preprocessing and does not know Surveyor's URL or credential. If Surveyor is unavailable or times out, the existing map and manual/Advanced registration workflow remain usable.
