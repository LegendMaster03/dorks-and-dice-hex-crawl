# Crawl runtime engine

## Scope

This slice turns the existing spatial/world foundation into an executable crawl procedure without collapsing world geometry, crawl procedure state, runtime mutation, player knowledge, and presentation into one model.

The authoritative transition boundary is the domain `CrawlRuntimeEngine`. The Web client may collect choices, prefill resolved rolls, and render state, but it does not calculate authoritative movement, lost state, boundary crossings, encounters, or discoveries.

The runtime remains persistence-agnostic. The demonstrator stores a single process-local session only so the state machine can be exercised before campaign persistence is designed.

## State separation

The runtime preserves the foundation's independent state axes:

- `OverworldDefinition` remains static world/spatial truth.
- `ExpeditionState` contains expedition runtime state.
- `WorldRuntimeState` remains the overlay for mutable/generated world state.
- `PlayerKnowledgeState` contains subject-specific knowledge and annotations.
- `MapPresentationPolicy` remains presentation policy rather than world truth.
- `CrawlProcedureProfile` configures procedure behavior independently of world geometry.

`ExpeditionState.Position` is retained for map rendering and future exact positioning. Crawl traversal does not ray-cast that point through the drawn hex polygon.

`WorldPositionPrecision` records whether the position is exact or merely a `HexAnchor`. After an abstract procedure crossing, the demonstrator can anchor the marker at the new hex center without claiming that the center is the expedition's exact rules position.

## Abstract traversal state

`HexTraversalState` is the procedure-facing representation of sub-hex travel. It tracks:

- current hex;
- entry direction, when the expedition crossed a known face into the hex;
- last travel direction;
- accumulated abstract progress;
- the current abstract exit requirement.

This is deliberately different from literal regular-hex geometry. The Alexandrian procedure describes progress in abstract miles and uses different requirements for near, far, and back exits. A literal chord through the rendered hex would produce different distances depending on the exact geometric entry point and bearing, which is not the tabletop procedure being modeled.

For continuous-distance profiles, exit requirements are configured as factors of the grid's physical neighbor-center distance. The current Alexandrian baseline uses:

- start inside a hex: `0.5`;
- near exit: `0.5`;
- far exit: `1.0`;
- deliberate return to the entry boundary: `0.5`;
- direction-change progress cost: `1/6` when enabled.

Those are procedure settings, not properties of `HexGridDefinition`. The same runtime therefore works on non-12-mile grids and with kilometers or other physical units.

## Procedure profiles

`CrawlProcedureProfile` is intentionally a small configuration surface rather than a general rules scripting language. It currently controls:

- watch length;
- continuous physical distance versus coarse hex-step resolution;
- fixed versus externally resolved variable travel distance;
- encounter-check cadence;
- whether navigation checks are used;
- whether veer persists while lost;
- whether intra-hex progress is tracked;
- whether direction changes consume progress;
- whether deliberate double-back handling is enabled;
- scale-relative start/near/far/back progress requirements.

The built-in profiles are:

- `alexandrian-advanced` — the current Alexandrian-inspired baseline;
- `simple-fixed-distance` — continuous fixed-distance travel without navigation or encounter checks;
- `simple-hex-step` — coarse hex-step movement without intra-hex progress.

The Alexandrian profile is a preset over the general model. It is not treated as the definition of hexcrawling.

## Watch transition model

A watch advance is a deterministic state transition over explicit state and explicit resolved inputs. Conceptually:

`world + profile + expedition + knowledge + travel plan + resolved inputs -> expedition + knowledge + events + optional pause`

A new watch records the chosen intended direction, pace/mode metadata, navigation aid, and activities. If the profile requires navigation, the engine consumes an explicit navigation result. Actual travel direction is then derived from intended direction plus current lost/veer state.

Movement is applied against traversal progress. A segment may remain inside the current hex, cross one boundary, or cross several boundaries when the supplied movement is sufficient and the caller allows continued traversal.

A boundary can intentionally pause the watch with remaining time still recorded. This is important because terrain, route, navigation aid, movement rate, encounter context, or other assumptions may change after entering the next hex. The DM can review conditions and continue the same watch instead of the engine automatically carrying stale assumptions forward.

A watch completes only after its remaining travel time is resolved. `CompletedWatches` and `ActiveWatchState` make partially resolved watches explicit instead of inferring them from wall-clock arithmetic.

## Direction changes and double-backs

Direction changes are procedure actions, not geometric pivots of an exact world-space ray. Profiles may assign an abstract progress cost to changing direction.

A deliberate double-back is distinct from an ordinary direction change. When enabled, the expedition may retrace toward the face through which it entered the current hex. Reaching that boundary pauses the procedure instead of silently continuing through arbitrary prior route history.

The current model does not yet maintain a full multi-hex route stack. Therefore a deliberate double-back handles the current hex's known entry boundary, but automatic route unwinding across an arbitrary sequence of previously crossed hexes is deferred.

## Navigation, lost state, and veer

Navigation resolution is edition-agnostic. The domain engine does not know about a particular skill name, proficiency system, DC formula, or dice expression.

`ResolvedNavigation` supplies an already resolved outcome and, on failure, a veer measured in 60-degree hex-direction steps. `NavigationRuntimeState` stores whether the expedition is lost and the persistent veer offset. A profile can disable navigation entirely or disable persistent veer.

When a lost expedition reaches a decision point, the runtime can pause for recognition/reorientation. A `BoundaryNavigationDecision` records whether the lost state was recognized and whether the expedition reorients. Reorientation resets the veer through an explicit state transition and event.

Navigation aids are also explicit inputs. The current demonstrator includes a route-style aid that can suppress the navigation check and reset veer at a boundary. More detailed terrain/route policies remain outside the core engine for now.

## Randomness and resolved-input boundary

The domain runtime never calls a random-number generator for consequential resolution.

Resolved values carry `ResolutionProvenance`:

- `ProcedureDefault`;
- `AutomaticRoll`;
- `ManualRoll`;
- `ExternalSystem`;
- `DmOverride`.

This allows the same transition to consume values generated by the local UI, entered from physical dice, supplied by another Dorks & Dice tool, or overridden by the DM.

The Alexandrian variable-distance helper implements the procedure's `2d6 + 3` percentage result as a pure calculation over already resolved dice. It does not roll those dice itself. The DM-facing demonstrator can roll and prefill the resolved value, but the authoritative engine receives only the resolved distance and its provenance.

The same separation applies to navigation and encounters. The current UI can roll a d20 navigation helper, but the engine consumes the resolved success/failure and veer rather than embedding a specific D&D edition's navigation check.

## Encounters and discovery

Encounter cadence is configured by the procedure profile. Encounter content remains external to the engine.

A resolved encounter can currently be:

- none;
- wandering encounter;
- keyed-location discovery;
- manual/custom.

Timed encounter results can interrupt a watch before all movement is spent, leaving the watch resumable.

Crossing into a hex does not automatically discover all contents of that hex. Keyed locations and semantic features remain separate subjects. Discovery updates `PlayerKnowledgeState` through `KnowledgeDiscovery` for the specific location or feature that was actually discovered.

This preserves the existing architecture rule that knowledge is not `Hex.IsRevealed` and prepares for later separate DM and player map projections.

## Runtime events

Important transitions append `CrawlRuntimeEvent` records. Current event kinds cover:

- watch start/completion;
- navigation resolution;
- becoming lost;
- veer changes/resets and reorientation;
- direction changes;
- travel resolution and distance traveled;
- hex exits/entries;
- encounter checks and triggered encounters;
- keyed-location encounters;
- location/feature discovery;
- navigation/condition decision points;
- DM overrides.

The history is intentionally event-like for auditability and future persistence, but the current runtime is not an event-sourced storage design. Persistence remains deferred.

## DM-facing demonstrator

The Web demonstrator exposes the runtime through the same backend in standalone and hosted operation. It provides:

- procedure-profile selection;
- map orientation, scale, and unit controls;
- intended direction, pace, independent watch activities, and navigation-aid inputs;
- fixed/variable distance and hex-step inputs as appropriate to the profile;
- automatic distance and navigation roll helpers that only prefill explicit resolved values;
- manual/external/override provenance selection;
- deliberate double-back and multi-boundary continuation controls;
- encounter outcome and timing inputs;
- lost-recognition/reorientation controls when the engine pauses for them;
- current hex, intended/actual course, lost/veer state, progress, watch remainder, and pause reason;
- an expedition marker on the canvas;
- subject-specific manual discovery controls;
- recent runtime event history.

Changing the map's grid scale/orientation does not silently mutate an active expedition. The demonstrator requires an explicit runtime reset to start the expedition against the changed grid definition.

The demonstrator POST endpoints mutate only process-local demonstration state. They are not campaign persistence and do not establish an authentication or authorization model.

## Alexandrian coverage

The implementation was checked against the supplied Alexandrian references:

- https://thealexandrian.net/wordpress/17308/roleplaying-games/hexcrawl
- https://thealexandrian.net/wordpress/46020/roleplaying-games/5e-hexcrawl
- https://thealexandrian.net/wordpress/46198/roleplaying-games/5e-hexcrawl-part-5-encounters
- https://thealexandrian.net/wordpress/46226/roleplaying-games/5e-hexcrawl-part-7-watch-actions
- https://thealexandrian.net/wordpress/46262/roleplaying-games/5e-hexcrawl-part-8-example-of-play
- https://thealexandrian.net/wordpress/50363/roleplaying-games/hexcrawl-addendum-near-far

Covered in the executable slice are watch-based travel, resolved variable travel distance, intended versus actual course, getting lost, persistent veer, near/far/back abstract progress, direction changes, deliberate double-back, multi-hex travel, encounter timing, keyed discovery, and explicit pause/resume decisions.

The references also contain richer terrain movement tables, encounter-table construction/content, detailed watch-action mechanics, navigation modifiers, and system-specific character rules. Those remain manual or external because this cycle is establishing a reusable runtime engine rather than hard-coding every Alexandrian table or one D&D edition's character mechanics.

## Deferred work

The following remain intentionally outside this development cycle:

- database or campaign persistence;
- account/campaign authorization and Tool Host ticket introspection;
- Rules Core integration;
- authoritative character statistics and watch-action resolution;
- full terrain-speed policy and travel-rate tables;
- encounter table storage/selection/content generation;
- exact arbitrary-bearing sub-hex rules movement;
- a multi-hex backtracking/route-history stack;
- separate player-map visibility/fog presentation;
- map/world editing;
- source-map image recognition or calibration assistance;
- battle-map behavior.

## Questions for central planning

The implementation exposes several decisions that should be made before later systems depend on them:

1. Whether `HexDirection` should remain the authoritative six-direction procedure abstraction or be supplemented by an arbitrary-bearing procedure layer distinct from world-space geometry.
2. Whether deliberate multi-hex backtracking should become a formal route/traversal history structure or remain a higher-level expedition/planning concern.
3. Which layer should determine terrain/route travel rates and provide revised assumptions after a boundary pause once world editing and campaign persistence exist.
4. Whether Rules Core should eventually provide resolved navigation/watch-action checks to Hex Crawl, or whether Hex Crawl should continue receiving edition-neutral resolved outcomes from a separate orchestration layer.
5. How runtime events should be persisted later: as an audit/history stream alongside snapshot state, as true event sourcing, or only as transient UI history.
