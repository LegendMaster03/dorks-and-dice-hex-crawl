# Phase 15 implementation notes

This document records the Phase 15 interaction architecture as implemented. The design intent and acceptance criteria remain authoritative in `docs/phase-15-design-architecture.md` and `docs/generic-procedure-development-plan.md`.

## Procedure authoring

Phase 15 uses one canonical `CampaignProcedure` across all authoring presentations.

- **Compact** is the normal procedure-authoring path. It composes the procedure through tabletop-facing structural areas and behavior/parameter choices rather than requiring module IDs, mechanic IDs, dependency keys, or canonical JSON.
- **Advanced** exposes exact generic module/mechanic contracts, parameters, versions, and diagnostics over the same composed procedure.
- **JSON** edits the server-produced canonical representation. Validation, procedure identity, optimistic concurrency, and revision creation remain application/server responsibilities.
- The preset browser is a creation-time starting-point browser. Once materialized, the campaign procedure does not depend on live preset identity for execution or presentation.
- Custom procedures may have no preset origin. Structural composition adds only explicitly selected generic procedure modules; omitted areas are absent rather than simulated with disabled mechanics.
- Compact focused-area editing remains open while the server recomposes the draft, so changing one behavior or parameter does not force repeated navigation back into the same area.
- Structured-save, canonical-save, and canonical-load failures remain visible after pending state clears. Canonical-load failure uses an explicit retry instead of an automatic retry loop.

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

- The workspace exposes an accessible six-direction control for adjacent travel intent.
- Selecting an adjacent map hex exposes a corresponding travel-intent action. Selecting a hex by itself does not mutate expedition position.
- Course and pace are remembered as reusable browser preferences for the expedition. Active runtime course/pace remain server-authoritative and are reconciled back into those preferences.
- Direction values remain the runtime's existing numeric 0–5 axial directions; presentation labels do not introduce a second direction model.
- Fixed continuous movement that the authoritative movement-composition projection resolves deterministically is consumed directly. `Resolved` and `ReferenceFallback` movement suggestions can supply the routine movement value without making the DM re-enter distance or provenance.
- Variable, step-based, unavailable, unsupported, adjudication-required, or otherwise unresolved movement is still requested explicitly rather than guessed in the browser.
- The DM can deliberately override derived movement. Editing the derived value records DM-override provenance instead of silently replacing procedure-default provenance.
- Navigation, partial progress, encounters, terrain/environment effects, forced travel, boundaries, consequences, and position changes continue through the existing runtime advance operation. The presentation does not directly assign the current hex.

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
- Existing provider boundaries remain optional and capability-oriented.

## Validation boundary

Automated validation covers frontend build/tests, Embedded Module smoke, .NET build and domain/application/integration tests, PostgreSQL persistence, render-lifecycle guards, container build, and restart smoke tests.

Phase 15 acceptance also requires rendered visual review across the viewport/state matrix listed in `docs/generic-procedure-development-plan.md`. That visual review is an acceptance activity in addition to exact-head CI and is not replaced by source-contract tests.