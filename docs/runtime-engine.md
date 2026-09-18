# Crawl runtime engine

## Authority and state separation

`CrawlRuntimeEngine` remains the authoritative deterministic full-watch transition boundary. It consumes `CrawlRuntimeContext` (the physical hex-center distance), `CrawlProcedureProfile`, `ExpeditionState`, a travel plan, and resolved inputs. It does not depend on `OverworldDefinition`, source maps, semantic locations/features, player knowledge, or rendered map types. The Web client may collect choices, prefill resolved rolls, and render state, but it does not calculate authoritative full-watch movement, lost state, boundary crossings, or encounter timing.

The persisted session model preserves these independent axes:

- `CrawlSessionContext` says whether the session is `WorldBound`, `AbstractHex`, or `NonSpatial`.
- `CrawlRuntimeContext` supplies only crawl-scale physical distance for spatial sessions.
- `ExpeditionState` is spatial crawl state for world-bound and abstract-hex sessions.
- `NonSpatialSessionState` is non-spatial procedure/time/history state. Its optional `NonSpatialActiveWatchState` contains only watch number, configured duration, elapsed time, and remaining time; it contains no fake map state.
- `CrawlProcedureProfile` is procedure configuration.
- `OverworldDefinition` remains authoritative spatial/world truth outside the runtime engine and is optional at the session level.
- `PlayerKnowledgeState` remains world-specific party knowledge outside the runtime engine and is absent for standalone contexts.
- presentation policy remains separate from crawl mechanics and world truth.

`ExpeditionWorldComposition` is the application boundary that projects a runtime hex back into world coordinates, validates keyed-location encounters against authored world truth, and applies subject-specific knowledge when presentation policy permits it.

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

Each expedition persists the **complete profile configuration snapshot** chosen when it starts. The profile key remains provenance, but it is not used to reconstruct an old expedition on reload. A DM can start from a built-in preset and customize the supported fields; `CrawlProcedureProfile.Validate()` remains the validity boundary for both presets and customized snapshots. A profile may also carry optional resolution-helper definitions (dice formulas, result bands, timing slots, and travel multipliers). Those definitions describe procedure behavior and persist with the snapshot; situational values such as expected distance, navigation DC/modifier, and failure veer are supplied by the DM when the helper is invoked.

New-expedition customization exposes `None`, `PerWatch`, and `PerDay` encounter cadence. The `Custom` enum member remains valid for backward compatibility with persisted profiles but is not presented as a new customization option because no typed custom-cadence parameters exist in this slice.

## Watch transition model

A transition is conceptually:

`crawl context + profile + expedition + travel plan + resolved inputs -> expedition + events + optional pause`

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

The engine does not perform consequential random rolls. `ResolutionProvenance` supports `ProcedureDefault`, `AutomaticRoll`, `ManualRoll`, `ExternalSystem`, or `DmOverride`.

`AutomaticRoll` is emitted only when the server-side procedure-resolution helper actually generates the corresponding resolved value. Ordinary manual selectors still do not expose `AutomaticRoll`; manual DM entry can record `ProcedureDefault`, `ManualRoll`, `ExternalSystem`, or `DmOverride`. `POST /api/expeditions/{id}/resolution-helper` reads the persisted procedure snapshot and current session state, combines them with explicit DM-confirmed situational inputs, and returns resolved travel/navigation/encounter drafts with roll traces and provenance. Consequential generation is an **audit-only session mutation**: every generated attempt is immediately appended as `ProcedureResolutionHelperGenerated` history and increments the aggregate version before another generation or application can occur. It does not advance time, movement, navigation, encounter state, knowledge, or any other mechanical runtime state. The existing `/advance` operation remains the only full-watch application boundary.

The built-in Alexandrian Advanced snapshot configures its variable-distance helper as `2d6+3` with a 10% factor per roll point, its navigation check helper as `1d20`, and its encounter helper as `1d8` with wandering/keyed-location edge results plus eight timing slots. Navigation DC/modifier and the current runtime's required non-zero failure veer are not inferred by the helper. A world-bound keyed-location result also remains subject to explicit location selection and the existing world-composition validation.

The DM workbench carries independent provenance for travel, navigation, encounter, and boundary-decision inputs rather than applying one source label to an entire watch. It appends a compact `ResolutionProvenanceRecorded` history event after each application transition.

The same runtime therefore remains able to accept future helper-generated results, physical dice, results from another tool, or explicit DM overrides without making Rules Core or another integration authoritative over spatial/runtime state.

## Encounters and discovery

Encounter cadence is procedure configuration; encounter content remains external. Timed encounter results can interrupt a watch and leave it resumable.

`PerWatch` is passed directly to each new-watch runtime transition. `PerDay` is handled by `ExpeditionProcedureRequirements`: only the first watch in each derived 24-hour travel day is passed to the engine with encounter cadence enabled; later watches in that day use an ephemeral copy of the profile with encounter cadence `None`. The persisted procedure snapshot remains `PerDay`.

Persisted legacy profiles whose cadence is `Custom` retain the historical deterministic behavior in `ExpeditionProcedureRequirements`: they request one resolved encounter result at each new watch, matching the prior per-watch handling. This compatibility behavior is intentionally separate from the new-expedition customization UI and does not imply that a custom scheduling model exists.

Crossing a hex boundary never reveals all content in that hex. The core runtime records a keyed-location encounter mechanically by stable subject ID but does not load or mutate world/knowledge state. `ExpeditionWorldComposition` validates that subject against the current authored world and can project only that subject into `PlayerKnowledgeState`; unrelated locations/features remain hidden.

Presentation policy is applied after the runtime transition. `Exploration Map` can mark entered hexes known. `DM-Controlled` retains mechanical runtime history without automatically applying subject discovery to persisted player knowledge. Manual discovery through the persistent API remains an explicit DM action.

## Runtime history and persistence

Important transitions append `CrawlRuntimeEvent` records covering watch lifecycle, navigation/lost/veer changes, direction changes, travel, hex exits/entries, encounters, discoveries, decision points, automatic helper-generation attempts, provenance, and DM overrides. Because each automatic attempt is retained before the value can be accepted or rerolled, discarded helper results remain visible in history rather than becoming hidden rerolls.

The storage design is intentionally **snapshot + retained history**, not full event sourcing:

- the persisted expedition snapshot is authoritative current state;
- player knowledge, known hexes, and presentation policy are part of the persisted expedition envelope;
- runtime events are retained separately in `expedition_events` for auditability, session history, debugging, and future filtered projections;
- `(expedition_id, sequence)` is unique, so saving/reloading does not duplicate history;
- reloading reconstructs `ExpeditionState.History` in sequence order before the next deterministic transition.

An expedition record also persists pause reason and remaining watch time, because those are application resume state returned by the engine in addition to the core expedition snapshot.

## Persistent DM workflow

`CrawlSessionService` creates true standalone `AbstractHex` and `NonSpatial` sessions without creating an Overworld. `CrawlSessionContextResolver` resolves world-bound scale from the actual Overworld, abstract-hex scale from the persisted context, and no spatial context for non-spatial sessions.

`ExpeditionWorkbenchService` is the full-watch application orchestration layer over the spatial engine. It starts expeditions from procedure/presentation presets, persists customized procedure snapshots, determines whether per-day encounter resolution is due, preserves legacy `Custom` cadence behavior, builds independent resolved-input provenance, calls `CrawlRuntimeEngine`, composes world/knowledge projection, applies presentation projection, and saves with optimistic concurrency.

`ExpeditionAssistantService` exposes independent manual bookkeeping mutations over the same persisted session. Spatial travel/watch, navigation, and encounter-cadence assistants each mutate only their owned state/history and do not silently resolve the other subsystems. Spatial focused mutations are blocked while a full-workbench `ActiveWatchState` exists so a partial full-watch transition can not be corrupted by an independent helper. Non-spatial sessions instead use the dedicated watch mutation, which advances only procedure time, persists a lightweight active watch for partial/resume behavior, records provenance/DM overrides, and automatically completes the watch when its configured duration is fully consumed.

The DM application can start or reopen sessions without any Overworld. Abstract-hex sessions expose the spatial runtime view without map composition. Non-spatial sessions expose applicable procedure/time/history state, including configured watch length, current watch, elapsed/remaining watch time, partial/resume state, completed watches, and total elapsed session time. World-bound sessions add subject-specific discovery and presentation/knowledge preview.

Every runtime mutation carries an optimistic `ExpectedVersion`. Two stale browser tabs therefore receive a conflict instead of one silently overwriting the other's newer expedition snapshot.

Grid geometry can not be changed after an expedition exists for the world. This prevents reload from reinterpreting persisted hex/spatial state against a different coordinate system.

The standalone container smoke now proves a partially completed watch survives a full container restart and resumes as watch 1 with its remaining two hours rather than becoming a new watch.

See `docs/dm-expedition-workbench.md` for the complete workbench ownership and presentation model.

## Alexandrian coverage

The implementation continues to cover the tested Alexandrian-inspired behaviors established in the runtime slice: watch-based travel, resolved variable travel distance, intended versus actual course, getting lost, persistent veer, near/far/back abstract progress, direction changes, deliberate single-hex double-back, multi-hex forward travel, encounter timing, keyed discovery, and explicit pause/resume decisions.

The Alexandrian profile remains optional. Terrain movement tables, encounter content, watch-action character rules, and edition-specific navigation checks remain outside the engine.

## Deferred runtime work

Still deferred are Rules Core/Characters integration, authoritative terrain/route mechanical interpretation, encounter-table content, typed custom encounter-cadence parameters or a scheduling DSL, arbitrary-bearing procedure travel, multi-hex route unwinding, a general campaign calendar/rest clock, real-time multiplayer synchronization, and battle-map behavior.