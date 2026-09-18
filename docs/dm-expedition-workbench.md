# DM expedition workbench

## Purpose

The expedition workbench is the persistent DM-facing layer over the existing deterministic crawl runtime. It does not replace `CrawlRuntimeEngine`, reinterpret semantic world truth, or make the browser authoritative for movement. Its job is to collect the resolved choices and inputs required by the configured procedure, call the runtime, persist the resulting snapshot/history, and present the state needed to continue play.

The persisted aggregate is a crawl session whose context is explicit rather than inferred from an Overworld:

`persisted procedure snapshot + session runtime/history + session context + resolved inputs -> transition -> persisted session`

`CrawlSessionContext` has three concrete forms:

- `WorldBound(overworldId)` — spatial runtime plus real authored world/map/knowledge composition.
- `AbstractHex(name, orientation, CrawlRuntimeContext)` — spatial runtime with its own persisted hex scale, but no Overworld record.
- `NonSpatial(name)` — procedure/session runtime with no hex coordinates, distance scale, world position, or Overworld.

The full map workbench exists only for `WorldBound`. The abstract-hex tracker runs the same deterministic spatial crawl engine without constructing or loading an Overworld. Non-spatial sessions use `NonSpatialSessionState` and only expose procedure tools that do not require invented spatial state.

The deterministic `CrawlRuntimeEngine` receives only `CrawlRuntimeContext` physical scale plus spatial procedure/runtime inputs. World-coordinate projection, keyed-location validation, and player-knowledge effects are application-level composition performed only for a real `WorldBound` session.

## Product composition

- **DM tools home** lists crawl sessions across all context kinds. It can start a `WorldBound` session from an existing Overworld, an `AbstractHex` session directly, or a `NonSpatial` session directly.
- **Abstract-hex tracker** runs watch/travel/navigation/encounter bookkeeping and history from persisted hex scale without creating or loading an Overworld and without constructing `MapSurface`.
- **Non-spatial tracker** presents procedure/time/history state without fabricating coordinates or distance state. Its Watch / time assistant can start a configured watch, record a partial segment, persist remaining time, resume after reload, and complete the same watch.
- **Full crawl workbench** is available only for `WorldBound` and composes the same spatial crawl state with authored world data, map rendering, discovery controls, and player-knowledge preview.
- **Travel / watch, Navigation, and Encounter cadence assistants** are independent manual bookkeeping surfaces over the same persisted expedition. Each has its own API mutation and updates only its owned state/history; it does not submit hidden inputs for the other assistants. They are disabled while a partial full-workbench watch is active, because that watch must resume atomically in the tracker.

## State ownership

Bookkeeping remains split across the established state axes.

### World truth

`OverworldDefinition` owns the continuous world coordinate space, mathematical hex grid, semantic features, locations, and source-map representations. Terrain, roads, rivers, locations, and similar authored facts remain world data. A procedure or presentation preset does not mutate them.

### Procedure configuration

`CrawlProcedureProfile` owns crawl-mechanics configuration. Each expedition stores the complete selected profile snapshot, including:

- watch length;
- continuous-distance versus hex-step travel;
- fixed versus externally resolved variable distance;
- encounter cadence;
- navigation checks and persistent veer;
- intra-hex progress;
- direction-change progress cost;
- deliberate double-back support;
- start/near/far/back exit factors and direction-change cost factor.

Built-in procedure keys are presets, not reload-time authorities. A customized expedition retains the selected preset key as provenance but persists the full customized snapshot. Later changes to a catalog preset therefore do not silently alter an ongoing expedition.

Domain `CrawlProcedureProfile.Validate()` is the validity boundary. The setup UI intentionally does not maintain an independent matrix of valid combinations.

### Session context and runtime state

Spatial contexts (`WorldBound` and `AbstractHex`) use `ExpeditionState` and `ActiveWatchState` for current travel state, including:

- current hex and world position;
- entry and last-travel directions;
- intended versus actual direction;
- lost state and veer;
- physical distance traveled;
- abstract intra-hex progress and current exit requirement;
- elapsed travel time;
- completed watches;
- active-watch total, elapsed, and remaining duration;
- selected pace, activities, navigation aid, encounter result, and pending decision.

Boundary interruptions remain the same watch. A reload therefore restores the active watch rather than approximating a new one.

`NonSpatial` instead uses `NonSpatialSessionState`, which owns total elapsed procedure time, completed watches, retained history, and an optional `NonSpatialActiveWatchState`. That lightweight active watch stores only watch number, configured total duration, elapsed duration, and derived remaining duration. It deliberately has no dummy `HexCoordinate`, `DistanceMeasure`, `WorldPoint`, direction, navigation state, or Overworld ID.

The DM-facing `CurrentDay` value is currently derived from elapsed session/travel time in 24-hour bands. This is intentionally travel-time semantics, not yet a general campaign calendar. A future rest/calendar system should introduce explicit world-time state rather than silently changing the meaning of `ElapsedTravelTime`.

### Player knowledge and presentation policy

`PlayerKnowledgeState` exists only for `WorldBound` sessions, because disclosure refers to actual authored world subjects. `AbstractHex` and `NonSpatial` sessions persist no synthetic knowledge snapshot.

For a world-bound session, `PlayerKnowledgeState` owns party-specific disclosure state. It now includes:

- subject-specific knowledge entries;
- known/explored hex coordinates;
- annotations;
- the expedition's complete `MapPresentationPolicy` snapshot.

The presentation snapshot is stored with player knowledge because it governs what this party knows and how that knowledge is presented; it is not world truth and it is not crawl mechanics.

`PresentationKnowledgeProjection` is the automatic disclosure boundary. The runtime may produce a mechanical keyed-location discovery event, but `DM-Controlled` presentation removes automatic runtime knowledge mutations before persistence. Mechanical history is retained, so suppressing player disclosure does not erase what happened in the crawl.

Manual subject discovery remains an explicit DM action through the existing discovery endpoint.

## Presentation presets

Four built-in policies are exposed by `/api/presentation/presets` and are selected when an expedition starts.

### Traditional Hidden Hexcrawl

- player grid hidden;
- terrain presentation manual;
- entering a hex does not automatically mark it known;
- no broad initial feature/location reveal;
- keyed or explicitly detected subjects can still become known individually.

### Exploration Map

- player grid visible;
- entering a hex marks that hex known;
- terrain can be presented as explored knowledge;
- configured ordinary roads and obvious settlement categories can begin known;
- hidden/conditional locations remain subject-specific and are not exposed by merely entering a hex.

### Open Regional Map

- player grid visible;
- regional terrain is presentation-visible;
- entered hexes are known;
- configured ordinary route/river/border/landmark categories and obvious settlement categories can begin known;
- hidden and conditional locations remain hidden until separately disclosed.

### DM-Controlled

- grid/terrain presentation is manual;
- no initial automatic knowledge changes;
- entering hexes does not automatically reveal them;
- runtime keyed-discovery mechanics are recorded in history but do not automatically modify persisted player knowledge.

## Guided watch workflow

The full expedition tracker asks only for inputs relevant to the persisted procedure and current runtime state. Focused assistants are separate from this atomic workflow. Spatial travel/navigation assistants remain blocked while a spatial full-workbench `ActiveWatchState` is in progress. A non-spatial active watch is different: it is itself the authoritative lightweight procedure watch and is resumed through the Watch / time assistant.

At a new watch it can request:

- direction;
- pace and optional activities;
- navigation aid/suppression choices;
- resolved travel amount in the profile's configured model;
- navigation outcome and veer only when navigation is required;
- encounter outcome only when the configured cadence says a check is due.

For a paused active watch, the workbench exposes the pending decision and remaining watch time. A conditions-review pause can resume with changed travel inputs without creating a new watch. Lost-recognition/reorientation decisions are supplied explicitly when required.

Fixed continuous-distance procedures accept one effective distance. Variable-distance procedures accept expected and actual resolved distance. Hex-step procedures accept a step count. The workbench does not interpret a semantic `forest`, `road`, or other category into a movement multiplier.

## Encounter cadence

New-expedition procedure customization currently offers only the cadence modes with implemented configuration semantics:

- `None` — no encounter check;
- `PerWatch` — one encounter check at each new watch;
- `PerDay` — one encounter check in each derived 24-hour travel-time day.

`EncounterCheckCadence.Custom` remains a valid domain value for backward compatibility with already persisted expedition profiles. It is not offered for newly customized expeditions because this slice has no typed custom-cadence parameters. A persisted legacy `Custom` profile retains its historical deterministic behavior: it requests a resolved encounter result at each new watch, equivalent to the old per-watch handling, until a real custom-cadence model is introduced.

`PerDay` is orchestrated without changing `CrawlRuntimeEngine`: only the first new watch in each 24-hour travel-time day is passed to the engine with encounter cadence enabled. Subsequent watches in that same derived day use an ephemeral runtime profile with encounter cadence `None`. The persisted procedure snapshot remains `PerDay`.

Encounter content, tables, monster selection, and edition-specific encounter mechanics remain external.

## Resolved-input provenance

The domain and application contracts support independent provenance for travel, navigation, encounter, and boundary decisions using:

- `ProcedureDefault`;
- `AutomaticRoll`;
- `ManualRoll`;
- `ExternalSystem`;
- `DmOverride`.

`AutomaticRoll` means that a trusted helper or integration actually generated the corresponding resolved value. The current workbench does not contain such a helper. Therefore ordinary manual DM entry offers only `ProcedureDefault`, `ManualRoll`, `ExternalSystem`, and `DmOverride`; it does not allow a manually typed value to be labeled `AutomaticRoll`.

A future helper may set `AutomaticRoll` programmatically when it genuinely produces a travel, navigation, encounter, or boundary result. This preserves the domain value without fabricating provenance in the current UI.

Optional notes can describe physical dice, an external system result, an override context, a table clock, or a future trusted helper result. Non-spatial watch bookkeeping records the provenance of each elapsed-time segment; `DmOverride` additionally produces `DmOverrideApplied` history. The workbench appends compact `ResolutionProvenanceRecorded` entries for auditability.

This keeps future integrations subordinate to the Hex Crawl runtime state. Rules Core or Characters may provide resolved values later, but they do not become the owner of expedition movement or spatial state.

## UI projections

The expedition page derives DM-facing status from authoritative persisted state. It exposes:

- current day/watch;
- watch elapsed and remaining time;
- current hex;
- entry/last-travel relationship;
- intended and actual course;
- lost/veer state;
- total travel distance;
- intra-hex progress only for profiles that use it;
- current pause/pending decision;
- encounter resolution state;
- recent runtime history;
- manual subject discovery controls;
- a player-knowledge preview using the persisted presentation policy.

GM source-map rasters remain DM evidence. The knowledge preview does not reinterpret a GM raster as player knowledge.

## Persistence and restart behavior

The existing SQLite `expeditions` table remains the compatibility envelope, but schema v2 persists required `context_json`, nullable `overworld_id`, nullable world-only `knowledge_json`, procedure state, discriminated runtime state, pause reason, and remaining watch time. Runtime history remains in `expedition_events`.

Schema-v1 rows migrate to `WorldBound` using their existing real Overworld ID. New `AbstractHex` and `NonSpatial` rows store `NULL` in `overworld_id`; no placeholder world is created. Non-spatial active-watch state is serialized inside the existing runtime snapshot, so adding partial/resume bookkeeping requires no schema-v3 migration. No separate expedition-clock table or presentation table was introduced.

Validation includes an end-to-end container smoke that:

1. creates a world and expedition;
2. advances far enough to cross a boundary and pause with two hours remaining in watch 1;
3. restarts the container against the same `/data` volume;
4. reloads the same active watch, procedure, presentation, and known-hex state;
5. resumes the watch;
6. verifies watch 1 completes at four elapsed travel hours.

## Explicitly deferred work

The workbench does not add:

- Rules Core or Characters coupling;
- edition-specific navigation skill/DC formulas;
- encounter-table or monster content;
- semantic terrain-to-mechanics interpretation;
- an automatic dice/resolution helper;
- typed custom encounter-cadence parameters or a scheduling DSL;
- machine vision, OCR, or raster analysis;
- arbitrary-bearing procedure travel;
- full multi-hex automatic route unwinding/backtracking;
- battle maps;
- real-time multiplayer synchronization;
- a general campaign calendar/rest clock.

Those systems can consume or extend the existing boundaries later without moving authoritative crawl state into the browser or an integration service.