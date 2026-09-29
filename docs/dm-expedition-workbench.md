# DM expedition workbench

## Purpose

The expedition workbench is the persistent DM-facing layer over Hex Crawl's deterministic runtime. It collects procedure-relevant choices and resolved inputs, invokes the application/runtime boundary, persists the resulting snapshot/history, and presents the state needed to continue play.

The persisted aggregate is a crawl session with explicit context:

`CampaignProcedure + session runtime/history + session context + resolved inputs -> transition -> persisted session`

`CrawlSessionContext` has three concrete forms:

- `WorldBound(overworldId)` — spatial runtime plus authored world/map/knowledge composition.
- `AbstractHex(name, orientation, CrawlRuntimeContext)` — spatial runtime with persisted hex scale but no Overworld record.
- `NonSpatial(name)` — procedure/time/history state without hex coordinates, world position, or Overworld.

The full map workbench exists only for `WorldBound`. Abstract-hex and non-spatial sessions use the same persisted procedure model without fabricating world state.

## Procedure authority

Each crawl session stores exactly one authoritative `CampaignProcedure` snapshot. It contains the materialized generic modules, mechanic handler/version metadata, parameters, and campaign overrides used by that session.

Named presets are creation-time recipes only. `ProcedureOriginMetadata` may record the selected preset key/display name/revision as optional provenance, but reload, runtime binding, procedure helpers, automatic-resolution verification, and advancement never require a live preset or origin identity.

A later preset correction therefore affects new materializations only. Existing sessions remain pinned until explicitly revised.

## Pre-release API policy

Hex Crawl is pre-release. Current HTTP contracts are development surfaces, not compatibility commitments. When the procedure architecture changes, current API contracts should represent `CampaignProcedure` directly rather than adding adapters around a retired representation.

Current expedition detail/workbench responses therefore carry `CampaignProcedureContract`. It exposes procedure ID, revision, generic identity, materialized modules, handler/version/automation metadata, parameters, and an optional derived executable runtime projection.

Structural or currently non-executable generic procedures remain valid API data. They are not rejected merely because the deterministic runtime can not bind every selected mechanic. Runtime binding fails only when an execution operation actually requires unsupported behavior.

## Product composition

- **DM tools home** lists crawl sessions across all context kinds and links directly to Travel / Watch, Navigation, and Encounter Cadence assistants.
- **Abstract-hex tracker** runs spatial bookkeeping from persisted hex scale without constructing an Overworld.
- **Non-spatial tracker** presents procedure/time/history state without invented spatial fields.
- **Full crawl workbench** is world-bound only and composes crawl state with authored world data, map rendering, discovery controls, and knowledge preview.
- **Focused assistants** operate on the same persisted session and mutate only their owned state/history.

Top-level assistant routes remain setup-and-entry surfaces. They do not own a second procedure model and do not calculate authoritative runtime behavior in the browser.

## State ownership

### World truth

`OverworldDefinition` owns the continuous world coordinate space, mathematical grid, semantic features, locations, and source-map representations. Procedure presets do not mutate world truth.

### Procedure configuration

`CampaignProcedure` owns procedure configuration for the session. Executable policy is obtained through generic handler/version-aware binding.

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

Phase 3 also permits structural declarative modules that are persisted and presented even when their later execution engine has not been implemented.

### Session context and runtime state

Spatial contexts use `ExpeditionState` and `ActiveWatchState` for current travel state, including current hex, position, direction, navigation state, distance, abstract progress, elapsed time, completed watches, active-watch timing, and pending decisions.

`NonSpatialSessionState` owns elapsed procedure time, completed watches, history, and an optional lightweight active watch. It contains no dummy spatial values.

### Party running sheet

`CrawlPartySheet` remains optional expedition-owned reference data for members, marching order, watch rotation, standing orders, default navigator, and explicit movement references.

It is adjacent to runtime state rather than embedded in the active-watch object. Later participant-role/activity execution can build on this without making rich party integration mandatory for basic runtime use.

### Player knowledge and presentation

`PlayerKnowledgeState` exists only for world-bound sessions because disclosure refers to authored world subjects. Presentation policy controls automatic knowledge projection after mechanical runtime transitions.

Manual discovery remains an explicit DM action.

## Guided watch workflow

The full tracker asks only for inputs relevant to the executable runtime policy and current session state.

At a new spatial watch it can request:

- intended direction;
- pace and optional activities;
- navigation-aid/suppression choices;
- resolved travel amount in the selected movement model;
- navigation outcome and veer when required;
- encounter outcome when cadence requires a check.

For a paused watch, the workbench exposes the pending decision and remaining time. A conditions-review pause resumes the same watch rather than creating a new one.

Fixed continuous-distance mechanics accept one effective distance. Variable-distance mechanics require expected and actual resolved distance. Hex-step mechanics accept a non-negative step count.

The workbench does not infer terrain semantics into movement mechanics unless the active generic runtime actually implements that behavior.

## Resolved-input provenance

The application supports independent provenance for travel, navigation, encounter, and boundary decisions:

- procedure default;
- server-generated automatic resolution;
- manual roll/input;
- external system;
- DM override.

Server-generated helper results are persisted with a generated-resolution ID, audit sequence, watch number, exact resolved values, and aggregate version. Applying automatic provenance requires the matching persisted generated result.

External tools can supply resolved inputs without becoming authoritative owners of expedition state.

## UI projections

The expedition UI derives its display from authoritative persisted state. Depending on context and executable policy it may show:

- day/watch and elapsed/remaining time;
- current hex and course;
- lost/veer state;
- total distance and intra-hex progress;
- current pause/pending decision;
- encounter state;
- recent history and watch ledger;
- party register and movement references;
- the complete persisted generic procedure/module reference;
- executable helper formulas where available;
- generated helper results and provenance;
- discovery controls and knowledge preview for world-bound sessions.

Current frontend filenames may still use the word "profile" as presentation vocabulary, but the API data feeding those views is the generic `CampaignProcedureContract`; there is no second persisted procedure model behind the UI.

## Persistence and restart behavior

Production persistence is PostgreSQL through `PostgresHexCrawlStore`. `IHexCrawlStore` remains the application boundary.

The `expeditions` table stores:

- explicit session context;
- nullable world reference and world-only knowledge;
- party state;
- generated procedure resolutions;
- required `procedure_json` containing the authoritative `CampaignProcedure`;
- optional procedure-origin metadata;
- discriminated runtime state;
- pause/remaining-watch state;
- aggregate version and timestamps.

Ordered runtime history remains in `expedition_events`.

The current pre-release schema intentionally does not support older development procedure rows. A development database using an earlier procedure schema must be reset rather than upgraded through compatibility scaffolding.

Container/restart validation proves that mapped and mapless sessions reload their exact generic procedure and runtime state after PostgreSQL/application restart.

## Optimistic concurrency

Every runtime mutation carries an expected aggregate version. Concurrent stale callers receive a conflict rather than silently overwriting newer state.

This protection is independent of procedure representation.

## Phase 3 scope boundary

The workbench does not implement the full Procedure Composer, typed participant-activity execution, generalized movement capability composition, environment execution, generalized effects/consequences, survival/resource execution, journey-process execution, expanded encounter runtime, battle maps, or real-time multiplayer synchronization.

Phase 3 may expose the structural generic modules needed to prove those future capabilities. Their presence does not imply current runtime execution support.

See `docs/generic-procedure-architecture.md` and `docs/phase-3-proof-matrix.md` for the authoritative procedure and proof-model details.
