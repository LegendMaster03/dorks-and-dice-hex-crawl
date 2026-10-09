# Phase 15 implementation notes

This document records Phase 15 interaction architecture as implemented. The historical design intent is captured by `docs/phase-15-design-architecture.md`; active future work is governed by `docs/tile-crawl-development-plan.md`.

## Procedure authoring

Phase 15 uses one canonical `CampaignProcedure` across all authoring presentations.

- **Compact** is the normal procedure-authoring path. It composes the procedure through tabletop-facing structural areas and behavior/parameter choices rather than requiring module IDs, mechanic IDs, dependency keys, or canonical JSON.
- **Advanced** exposes exact generic module/mechanic contracts, parameters, versions, and diagnostics over the same composed procedure.
- **JSON** edits the server-produced canonical representation. Validation, procedure identity, optimistic concurrency, and revision creation remain application/server responsibilities.
- The preset browser is a creation-time starting-point browser. Once materialized, the campaign procedure does not depend on live preset identity for execution or presentation.
- Custom procedures may have no preset origin. Structural composition adds only explicitly selected generic procedure modules; omitted areas are absent rather than simulated with disabled mechanics.
- Compact focused-area editing remains open while the server recomposes the draft, so changing one behavior or parameter does not force repeated navigation back into the same area.
- Structured-save, canonical-save, and canonical-load failures remain visible after pending state clears. Canonical-load failure uses an explicit retry instead of an automatic retry loop.
- Compact dependency repair stays tabletop-facing. When the application can prove one safe prerequisite closure, Compact names the required companion rules and offers a compound add action instead of requiring module-graph knowledge. Ambiguous dependency problems remain blocked without guessing.
- Semantic value formatting recognizes numeric literals before identifier humanization, so decimal parameters and mappings such as `rough=0.5;severe=0.25` remain numeric.
- Compact rule ordering is presentation organization only. The UI no longer claims the catalog display order is an authoritative execution sequence; actual triggers and dependencies remain authoritative.

## Final product shell and expedition entry

The Phase 15 candidate shell now teaches the current product model rather than the retired frontend architecture.

- **Expeditions** are the dominant table workflow. A saved expedition has one normal **Open expedition** action.
- **Procedures** and **Worlds / maps** are first-class management areas.
- Focused travel/time, navigation, and encounter tools remain available deliberately under **GM utilities** rather than appearing as alternate ways to run the same expedition.
- `/expeditions/{id}` is the canonical expedition route. The former world-scoped expedition route is only a redirect alias to the same workbench; there is no separate tracker route or tracker client-module ownership.
- The superseded tracker/running-sheet renderer and tracker-only layout CSS were removed rather than retained as pre-testing compatibility burden.

## Unified expedition workspace

Spatial and nonspatial expeditions use one procedure-driven expedition workspace rather than separate user-facing map and tracker modes.

The workspace presents:

- the next authoritative action or blocker;
- procedure-aware status summaries;
- map/current-position context when the expedition is spatial and world-bound;
- party/activity and movement context;
- navigation, survival/resource, journey, encounter, environment, and history surfaces only when applicable;
- focused drawers for substantial work rather than requiring round trips through unrelated tool navigation.

Nonspatial state is not coerced into spatial state. A real nonspatial interval can advance through the focused watch/time action, while a no-interval journey does not fabricate watch counts, coordinates, movement, or other spatial bookkeeping.

## Routine spatial travel

Routine travel expresses **intent** and always resolves through the existing procedure/runtime authority.

- The map carries a floating current-cell adjacency navigator in its upper-left control layer. Presentation consumes a generic current-cell polygon plus ordered traversable edges/adjacent cells; the current regular-hex adapter supplies six edges without making six-edge geometry part of the presentation contract.
- Edge controls use geometry-derived outward arrows and screen-relative/adjacent-cell descriptions. Compass terminology is not inferred merely from hex orientation; it should only be introduced if authoritative map-orientation metadata eventually establishes it.
- Map selection and navigator selection share the same semantic adjacent-cell state. Either input can select intended travel course and preview the neighboring cell without mutating expedition position.
- Travel remains two-step by default: selecting an edge/cell changes reusable intent, while the explicit current action performs authoritative procedure advancement.
- Reusable intended course is server-authoritative expedition runtime state. Selecting or clearing a course uses a dedicated optimistic-concurrency mutation that changes intent without advancing travel, movement, time, encounters, or history; when an active watch exists, its current travel plan is updated coherently. Browser persistence retains reusable pace only and can not override or resurrect course direction. On initial render and authoritative rebind, the runtime course is projected through current adjacency immediately so Current travel, navigator selection, map highlight, Next action, and focused workspaces agree without another click.
- Intended adjacent travel target and arbitrary inspected map cell are distinct presentation concepts. Inspecting an unrelated cell does not overwrite reusable travel intent.
- Travel-mode presentation follows the materialized movement contract: multiple finite `travelModeKeys` use bounded choices, while zero/one choices are fixed in the normal runtime UI (default `normal` when none is declared). Arbitrary free-text pace is not exposed unless a procedure contract explicitly supports it; preset identity is never used to infer choices.
- Focused movement distance inputs display the authoritative distance-unit symbol supplied by movement composition or spatial context rather than assuming miles.
- Direction values remain the runtime's existing numeric 0–5 representation underneath the regular-hex adapter; Compact interaction identifies the selected edge/adjacent cell instead of exposing axial steps as its primary model.
- Fixed continuous movement that the authoritative movement-composition projection resolves deterministically is consumed directly. `Resolved` and `ReferenceFallback` movement suggestions can supply the routine movement value without making the DM re-enter distance or provenance.
- Variable, step-based, unavailable, unsupported, adjudication-required, or otherwise unresolved movement is still requested explicitly rather than guessed in the browser.
- The DM can deliberately override derived movement. Editing the derived value records DM-override provenance instead of silently replacing procedure-default provenance.
- Navigation, partial progress, encounters, terrain/environment effects, forced travel, boundaries, consequences, and position changes continue through the existing runtime advance operation. The presentation does not directly assign the current hex.

## Direct DM repositioning

The DM also has an explicit **Teleport party** operation for setup corrections, teleportation, scene transitions, and other authoritative repositioning that is not ordinary overland travel.

- An adjacent selected cell means ordinary travel intent and does **not** present Teleport Party as a competing normal action. A deliberately inspected non-adjacent cell may expose **Teleport party here**, and the same explicit operation remains available from GM Tools with direct cell-coordinate entry.
- Repositioning is a distinct server mutation. It does not invoke normal travel advancement, add distance or elapsed travel time, run navigation or encounter checks, consume resources, advance journey progress, or fabricate travel provenance.
- The destination becomes the current cell and, for world-bound sessions, the map position is re-anchored to that cell. In-cell progress, entry/last-travel direction, intended/actual course, and lost/veer state are reset because they describe the previous local traversal context.
- If a full travel watch is active, the explicit reposition ends that watch and clears its pending pause/remaining-time state rather than silently carrying an obsolete travel segment to the new location.
- Aggregate elapsed travel, total distance already traveled, completed-watch count, party/resources/effects/journey state, and history remain intact. The reposition is recorded as a DM override in runtime history.
- Repositioning invalidates unconsumed generated procedure-resolution tokens. World-bound automatic presentation policy may mark the newly occupied hex known, but the operation does not automatically reveal unrelated locations or features.

## Journey-first and nonspatial operation

Journey-first procedures use a process/state-dominant primary workbench rather than a reduced spatial layout.

- The primary page projects the active journey/process, current stage, progress when defined, assigned roles, unresolved actions/events, relevant consequence state, and the actual next action.
- An unresolved journey event is surfaced as **Resolve journey event**; pending stage work uses the most specific generic action known by the runtime rather than falling back to a generic Journey drawer.
- The journey drawer remains the focused place to perform journey mutations. It is not required merely to discover what journey is active.
- Nonspatial procedures do not fabricate current cells, course, pace, hex progress, map controls, or a Movement status card when the materialized procedure has no travel capability.
- Generic interval bookkeeping remains available only when the materialized procedure actually defines a focused interval.

## Forced-travel presentation

The forced-travel focused workspace uses tabletop language by default while preserving the generalized consequence engine underneath.

- The primary view explains whether a forced-travel check is required, the travel amount and normal limit with authoritative units, the check model, failure consequence, affected target, and next operation.
- Exact mechanic/version/execution-handler information is behind **Advanced policy details**.
- Failure effect keys, effect-level deltas, and external-state contracts are behind **Advanced consequence details** and remain available for technical adjudication without dominating normal play.

## Automatic procedure resolution

Procedure-defined automatic helpers remain server-backed.

Automatic travel/navigation/encounter resolution is generated in place inside the focused travel drawer. The generated values, provenance, expedition version, and server resolution token remain associated with that drawer until the DM submits or edits them. Editing a generated component converts that component to DM-override provenance; a session-version change expires generated automatic provenance rather than replaying stale results.

The browser does not implement a parallel travel, navigation, or encounter rules engine.

## Map lifecycle

`MapSurface` persists across ordinary expedition-workspace rerenders.

The view reattaches the existing surface to the replacement map host and updates renderer state instead of disposing and reconstructing the canvas, viewport, renderer, accessibility help/status nodes, and map interaction state for ordinary UI changes. The map is disposed when the spatial/map context is actually removed or the route is disposed.

This preserves the Phase 14 lifecycle/performance constraint that routine state updates must not reconstruct the canvas.

## Focused mutation and failure behavior

Focused Party, Environment, Survival/Resources, Journey, map-discovery, and nonspatial-watch mutations share the unified expedition mutation boundary. Successful mutations refresh the authoritative expedition and auxiliary state before rerendering. Runtime updates that originate in Party, Environment, or Travel are applied through the shared runtime callback so runtime-change publication and travel-preference reconciliation remain consistent.

Event-driven focused mutations consume already-displayed failures rather than allowing discarded promises to become unhandled rejections. Spatial watch submission additionally keeps an accessible error inside the travel drawer, so validation, concurrency, or server rejection remains visible at the point of use.

Automatic procedure-helper generation is intentionally different: it does not invoke the outer rerendering mutation wrapper because doing so would destroy the generated values and server token before watch submission. Helper-generation failures remain local to the helper result surface.

## Runtime authority and generic architecture

Phase 15 presentation does not add named-system runtime dispatch.

- Presets remain removable creation-time recipes.
- Materialized `CampaignProcedure` state remains authoritative.
- Browser presentation consumes server/application projections for procedure, movement, environment, survival, journey, and consequence state rather than recreating those calculations solely for presentation.
- Directional actions express intended travel; they do not bypass runtime navigation, progress, encounter, terrain, forced-travel, boundary, or consequence mechanics.
- Current-action routing distinguishes encounter, navigation, boundary, forced-travel/resource consequence, journey, travel, and nonspatial-watch work. Navigation uses a focused navigation surface rather than routing through the whole travel/watch presentation; blocking encounter and survival/consequence state disables routine directional advancement.
- Existing provider boundaries remain optional and capability-oriented.

## Final Phase 15 acceptance baseline

This cleanup establishes the candidate build for comprehensive WorkChat testing, not a declaration of human-testing readiness.

The rendered acceptance fixture covers the 28 required Phase 15 surfaces/states: product shell desktop/narrow; procedure home, Compact, Advanced, and JSON; spatial no-course, selected-course, persisted-course, movement, navigation, lost/boundary, encounter, forced-travel, More options, non-adjacent inspection, and Teleport Party; three journey-first states; laptop, embedded, tablet, and narrow layouts; and representative light/dark spatial and nonspatial themes.

The next release step after managerial acceptance is comprehensive WorkChat testing and bounded remediation of its findings. Internal human testing remains later.

## Validation boundary

Automated validation covers frontend build/tests, Embedded Module smoke, .NET build and domain/application/integration tests, PostgreSQL persistence, render-lifecycle guards, container build, and restart smoke tests.

Phase 15 acceptance also required rendered visual review across the viewport/state matrix below. That visual review is distinct from exact-head CI and is not replaced by source-contract tests.

The preserved Phase 15 visual-review matrix covers wide desktop, ordinary laptop, embedded Site width, tablet-like width, and narrow/mobile width. At each relevant breakpoint review the default Compact expedition, expanded status/context, focused workspace, preset browser, Compact procedure overview, Advanced procedure editor, JSON editor, nonspatial journey, loading/error/conflict/empty states, long content, and scrolling/off-screen controls. This is historical UX evidence and a regression baseline for the Tile Crawl plan.

## Pre-human-test remediation

- A focused due encounter cadence/check records only encounter outcome/provenance through the existing Encounter Assistant path. It does not read hidden travel provenance or advance movement; active `EncounterTriggered` interruptions remain on the tactical encounter handoff/resume path.
- Focused resolution workspaces are required to submit only visible inputs or already-authoritative persisted state.
- On wide spatial layouts, Current travel is placed in the right-hand table rail beside the map. The top strip remains glanceable summary state; the rail adds actions and detail instead of repeating summary values. Empty environment/resource/journey cards are omitted, and party rail content is shown only when it provides setup or assignment detail.
