# Crawl runtime engine

## Authority and state separation

`CrawlRuntimeEngine` remains the authoritative deterministic transition boundary. The Web client may collect choices, prefill resolved rolls, and render state, but it does not calculate authoritative movement, lost state, boundary crossings, encounters, or discoveries.

The runtime preserves the independent state axes established by the world foundation:

- `OverworldDefinition` is authoritative spatial/world truth.
- `ExpeditionState` is current crawl state.
- `PlayerKnowledgeState` is subject-specific party knowledge and now also carries known hexes plus the persisted presentation-policy snapshot.
- `CrawlProcedureProfile` is procedure configuration.
- presentation policy remains separate from crawl mechanics and world truth.

Persistence wraps those domain objects; it does not move persistence rules into the runtime engine.

## Abstract traversal

`ExpeditionState.Position` remains available for world rendering and future exact positioning. `WorldPositionPrecision` distinguishes exact positions from a `HexAnchor` created by abstract procedure movement.

`HexTraversalState` tracks the current hex, entry direction, last travel direction, accumulated abstract progress, and current exit requirement. It deliberately does not ray-cast a literal line through the rendered regular hex. Continuous-distance procedures express near/far/back requirements as factors of the grid's physical center distance.

The Alexandrian advanced baseline currently uses start `0.5`, near `0.5`, far `1.0`, back `0.5`, and a configurable direction-change cost. Those are procedure settings, not grid properties.

## Procedure profiles

The built-in profiles remain:

- `alexandrian-advanced` — watch-based navigation, veer, encounters, and abstract intra-hex progress;
- `simple-fixed-distance` — continuous fixed-distance travel without navigation or encounter checks;
- `simple-hex-step` — coarse hex-step movement.

A profile controls watch length, travel resolution, actual-distance resolution, encounter cadence, navigation, persistent veer, intra-hex tracking, direction-change cost, deliberate double-back support, and exit-progress factors.

Each expedition persists the **complete profile configuration snapshot** chosen when it starts. The profile key remains provenance, but it is not used to reconstruct an old expedition on reload. A DM can start from a built-in preset and customize the supported fields; `CrawlProcedureProfile.Validate()` remains the validity boundary for both presets and customized snapshots.

## Watch transition model

A transition is conceptually:

`world + profile + expedition + knowledge + travel plan + resolved inputs -> expedition + knowledge + events + optional pause`

New watches record intended direction, pace/mode metadata, navigation aid, and activities. The engine consumes explicit resolved travel/navigation/encounter inputs, derives actual direction from intended direction plus lost/veer state, and applies travel against abstract progress.

A boundary can pause a watch with remaining time and an `ActiveWatchState`. The DM can review changed terrain, routes, navigation assumptions, or encounter context and continue the same watch. Partially completed watches are persisted exactly and survive application/container restart.

The application workbench derives `CurrentDay` from `ElapsedTravelTime` in 24-hour bands. This is currently a travel-time day, not a general campaign calendar. Rest/calendar time should become a separate explicit state concept if added later.

## Direction changes and double-back

Direction changes are procedure actions, not literal geometric pivots. Profiles may charge abstract progress for them.

The deliberate double-back operation handles the current hex's known entry boundary. A full multi-hex route stack and automatic route unwinding remain deferred; persistence does not preclude adding traversal history later.

## Navigation, lost state, and veer

Navigation remains edition-neutral. `ResolvedNavigation` carries a resolved outcome and optional 60-degree veer steps; the engine has no dependency on a D&D skill name, proficiency system, DC formula, or dice expression.

`NavigationRuntimeState` persists lost state and veer. Boundary decisions explicitly record recognition/reorientation, and reorientation produces a transition/event rather than silently mutating state. Lost and veer state round-trip through persistence and continue deterministically after reload.

## Resolved-input boundary

The engine does not perform consequential random rolls. `ResolutionProvenance` records `ProcedureDefault`, `AutomaticRoll`, `ManualRoll`, `ExternalSystem`, or `DmOverride`.

The DM workbench carries independent provenance for travel, navigation, encounter, and boundary-decision inputs rather than applying one source label to an entire watch. It appends a compact `ResolutionProvenanceRecorded` history event after each application transition.

The same runtime therefore accepts UI helper rolls, physical dice, results from another tool, or explicit DM overrides without making Rules Core or another integration authoritative over spatial/runtime state.

## Encounters and discovery

Encounter cadence is procedure configuration; encounter content remains external. Timed encounter results can interrupt a watch and leave it resumable.

`PerWatch` and `Custom` cadence are passed directly to new-watch runtime transitions. `PerDay` is handled by `ExpeditionProcedureRequirements`: only the first watch in each derived 24-hour travel day is passed to the engine with encounter cadence enabled; later watches in that day use an ephemeral copy of the profile with encounter cadence `None`. The persisted procedure snapshot remains `PerDay`.

Crossing a hex boundary never reveals all content in that hex. Discovery targets a stable location or feature ID and updates only that subject in `PlayerKnowledgeState`. One location can therefore be discovered while another location or feature in the same hex remains hidden.

Presentation policy is applied after the runtime transition. `Exploration Map` can mark entered hexes known. `DM-Controlled` strips automatic runtime discovery mutations before the knowledge snapshot is persisted while retaining the corresponding mechanical runtime events. Manual discovery through the persistent API remains an explicit DM action.

## Runtime history and persistence

Important transitions append `CrawlRuntimeEvent` records covering watch lifecycle, navigation/lost/veer changes, direction changes, travel, hex exits/entries, encounters, discoveries, decision points, provenance, and DM overrides.

The storage design is intentionally **snapshot + retained history**, not full event sourcing:

- the persisted expedition snapshot is authoritative current state;
- player knowledge, known hexes, and presentation policy are part of the persisted expedition envelope;
- runtime events are retained separately in `expedition_events` for auditability, session history, debugging, and future filtered projections;
- `(expedition_id, sequence)` is unique, so saving/reloading does not duplicate history;
- reloading reconstructs `ExpeditionState.History` in sequence order before the next deterministic transition.

An expedition record also persists pause reason and remaining watch time, because those are application resume state returned by the engine in addition to the core expedition snapshot.

## Persistent DM workflow

`ExpeditionWorkbenchService` is the application orchestration layer over the engine. It starts expeditions from procedure/presentation presets, persists customized procedure snapshots, determines whether per-day encounter resolution is due, builds independent resolved-input provenance, calls `CrawlRuntimeEngine`, applies presentation knowledge projection, and saves with optimistic concurrency.

The DM application starts or reopens expeditions against persisted overworlds. The runtime view exposes current day/watch, current hex, entry relationship, intended/actual course, lost/veer state, distance/progress, elapsed/remaining watch time, pause reason, encounter state, subject-specific discovery, presentation/knowledge preview, procedure snapshots, and recent event history.

Every runtime mutation carries an optimistic `ExpectedVersion`. Two stale browser tabs therefore receive a conflict instead of one silently overwriting the other's newer expedition snapshot.

Grid geometry can not be changed after an expedition exists for the world. This prevents reload from reinterpreting persisted hex/spatial state against a different coordinate system.

The standalone container smoke now proves a partially completed watch survives a full container restart and resumes as watch 1 with its remaining two hours rather than becoming a new watch.

See `docs/dm-expedition-workbench.md` for the complete workbench ownership and presentation model.

## Alexandrian coverage

The implementation continues to cover the tested Alexandrian-inspired behaviors established in the runtime slice: watch-based travel, resolved variable travel distance, intended versus actual course, getting lost, persistent veer, near/far/back abstract progress, direction changes, deliberate single-hex double-back, multi-hex forward travel, encounter timing, keyed discovery, and explicit pause/resume decisions.

The Alexandrian profile remains optional. Terrain movement tables, encounter content, watch-action character rules, and edition-specific navigation checks remain outside the engine.

## Deferred runtime work

Still deferred are Rules Core/Characters integration, authoritative terrain/route mechanical interpretation, encounter-table content, arbitrary-bearing procedure travel, multi-hex route unwinding, a general campaign calendar/rest clock, real-time multiplayer synchronization, and battle-map behavior.
