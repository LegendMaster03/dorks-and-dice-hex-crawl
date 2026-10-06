# Phase 15 core UX and presentation architecture

## Scope and testing boundary

Phase 15 turns the Phase 0–14 generic expedition engine into one coherent DM-facing application. It is a product and presentation architecture phase, not a cosmetic cleanup and not a new rules engine.

The roadmap sequence is now:

- **Phase 14 — complete:** broad architecture, correctness, integration, security, performance, documentation, and encounter-handoff remediation.
- **Phase 15 — current:** core UX and presentation architecture: unified expedition workspace, Compact, Advanced, expert JSON editing, preset browsing, procedure-aware presentation, responsive interaction patterns, and reusable UI primitives.
- **After Phase 15 acceptance:** internal human testing begins.
- **Phase 15.1 — later:** Guided Hex Crawl experience for users who understand tabletop RPGs/D&D but do not already know hexcrawling. Guided should enrich/wrap Compact rather than fork the application.
- **Phase 15.5 — later:** internal-testing stabilization and pre-release hardening.

Battle Map ownership and cross-tool tactical-map integration are **outside the Hex Crawl roadmap**. Phase 15 does not design or implement a Battle Map tool and does not decide its future ownership architecture. Block Initiative remains authoritative for tactical combat; existing provider-neutral linked-scene references and encounter-handoff behavior remain valid integration surfaces without implying a future Battle Map design.

Before Phase 15 acceptance, development database resets remain acceptable and obsolete development representations should still be removed rather than preserved through compatibility scaffolding. After Phase 15 acceptance, tester data has a human cost: a straightforward architecture-preserving migration is preferred where practical, while resets remain possible when architecture warrants them.

## Presentation principle

> Organize the interface around the DM's tabletop workflow and information needs, not around backend modules or persistence boundaries.

The backend may own separate aggregates for party state, environment, resources, effects, journeys, runtime history, and world maps. The DM should not need to mentally assemble those aggregates to answer what is happening now or what to do next.

Phase 15 therefore uses domain summaries and focused workspaces over the existing authoritative mutation paths. Presentation may combine related information, but it must not become a second rules engine.

## Compact persona

The Phase 15 Compact baseline targets an experienced traditional-hexcrawl DM who has no reason to know Hex Crawl's internal software model. The design shorthand is **Old School Dave**.

Compact assumes the user already understands concepts such as travel watches/turns, navigation, encounters, terrain, supplies, getting lost, foraging, and party roles. It should not require knowledge of mechanic IDs, dependency keys, handler/version dispatch, persistence, or input/output contracts.

The acceptance question is whether such a DM can inspect a familiar procedure, determine how it behaves, change it, and run an expedition efficiently without learning the implementation model.

## Experience levels

Phase 15 establishes three presentations over the same canonical state:

```text
Compact
  "I know hexcrawls. Give me efficient tabletop controls."

Advanced
  "I understand Hex Crawl's procedure model. Give me detailed control."

JSON
  "I understand the canonical data. Give me the representation."
```

Phase 15.1 later adds Guided above Compact.

### Compact

Compact uses tabletop terminology, scannable status blocks, focused domain-area editors, and actionable diagnostics. It does not expose implementation keys for ordinary edits.

### Advanced

Advanced keeps the same product/workspace organization while progressively exposing exact generic mechanics, versions, execution support, automation level, raw parameters, input/output contracts, dependency state, provenance, and detailed diagnostics.

### JSON

JSON is an expert editor for the canonical `CampaignProcedure`, not a separate representation. Server parsing and domain validation remain mandatory. Invalid syntax or domain-invalid data never becomes authoritative; identity and optimistic-concurrency checks remain enforced; semantically unchanged edits do not create revision noise.

## One canonical procedure

Compact, Advanced, and JSON operate on one `CampaignProcedure`:

```text
CampaignProcedure
      |
      +-- Compact presentation
      +-- Advanced presentation
      +-- JSON presentation
```

Presets remain creation-time recipes. Origin metadata is provenance, not runtime authority. Removing or changing a preset can not alter an already-materialized procedure.

## Preset discovery and provenance

A preset browser should let a DM inspect a procedure before selecting it. Cards summarize only what the actual materialized procedure supports: broad style, travel structure, navigation emphasis, activities/roles, encounter style, resources/survival, journey-process use, approximate complexity, spatial/interval/journey orientation, and attribution/disclaimer where applicable.

Shared/alias recipes such as B/X and OSE must be described truthfully rather than given fabricated distinctions.

During editing, origin may help the human understand changes (for example, starting behavior versus campaign modification). It must not dispatch runtime behavior or force a live catalog lookup. If later presentation metadata needs to survive independently of the catalog, it should be materialized as campaign-owned semantic presentation metadata rather than preset-conditioned UI branching.

## Stat-block principle

Major expedition concepts should behave like well-designed tabletop stat blocks: related raw and derived information appears together so the DM does not have to perform avoidable mental joins.

Examples include:

- movement capability beside movement already consumed/remaining;
- navigator/role beside whether a navigation result is due;
- a counted supply beside useful depletion/remaining context when the authoritative data is sufficient;
- current journey stage beside progress and unresolved event/action state.

Derived summaries must not invent outcomes the procedure does not know. Unknown/manual/provider-dependent information remains visibly unresolved.

## Unified expedition workspace

Spatial expeditions use a map-centered workspace rather than separate map and tracker products. The primary surface keeps the map, current action, current state, and frequent domain context together.

The Compact runtime should make four questions cheap to answer:

1. What is happening now?
2. Is anything blocking progress?
3. What action can I take next?
4. What will that action affect?

The current-action surface is derived from authoritative runtime/journey state. Existing watch, navigation, party, environment, survival/resource, journey, and encounter controllers remain authoritative for mutation.

The map is contextual rather than mandatory input. Selecting a hex can expose relevant authored locations and current-position context without mutating durable expedition state. Travel changes still require explicit actions. Keyboard/non-map alternatives remain available.

### Current-cell adjacency presentation boundary

Routine spatial travel uses a current-cell adjacency abstraction rather than a hardcoded compass widget. Presentation consumes the current cell's display polygon, ordered edges, traversable adjacent cells, and selected edge. A topology-specific adapter may translate that selection into the runtime representation; for the current regular-hex runtime this remains the existing numeric direction value.

The floating navigator is positioned over the map, defaults to the upper-left, and places one semantic button on each traversable edge. Its visible arrow points outward from the cell geometry. Accessible names use screen-relative edge position and adjacent-cell identity. Hex orientation alone does not establish map north, so cardinal labels are not inferred by default.

Map and navigator input are bidirectional views of the same intended adjacent-cell selection. Neither input mutates position. The explicit travel action remains the durable boundary where movement amount, navigation, partial progress, encounters, terrain/routes, forced travel, consequences, interruption, and boundary crossing resolve through application/runtime authority.

The presentation contract permits a non-six-edge fixture so future topology work does not have to replace the interaction architecture. Phase 15 does **not** implement square, triangular, mixed, or other alternate runtime tilings.

## Nonspatial and journey adaptation

The workspace adapts to the stored procedure instead of forcing every expedition through spatial watch travel.

```text
Spatial expedition
  -> map/current-travel dominant

Journey process
  -> journey stage/progress/event dominant

Nonspatial custom procedure
  -> applicable procedure/process state dominant
```

A real focused interval may expose watch/time bookkeeping. A no-interval journey procedure must not fabricate a repeating watch, hex movement, coordinates, movement rate, or resource dependency merely to fit the spatial layout. The One Ring proof remains the key no-fabricated-interval case.

## Current state, history, and focused work

Current state and current action are primary. History remains available for watch/interval ledgers, journey/event history, provenance, audit detail, and spatial context where available, but it is not the main action surface.

The interaction vocabulary is:

```text
summary
   -> select
   -> inspect/expand
   -> focused work
   -> return to context
```

Inline disclosures hold frequently useful secondary detail. Lightweight contextual cards/drawers handle inspection and direct edits. Larger focused workspaces handle substantial tasks such as procedure-area customization, party management, or journey-event resolution. Modal interruption is reserved for genuinely interruptive decisions such as destructive confirmation, unsaved-change decisions, and stale/conflict resolution.

## Tracking-sheet research

Phase 15 uses tracking sheets and established at-table references as information-architecture evidence only. It does not reproduce their artwork, expressive prose, or exact visual layouts.

### Old-School Essentials / B/X

Necrotic Gnome's official OSE Dungeon Time Tracker emphasizes a visible sequence of turn check boxes, time-related rules, and referee notes. Its main lesson is that repeated procedural time should be immediately scannable rather than buried in configuration. The OSE Reference Booklet likewise foregrounds compact game-procedure reference for dungeon, wilderness, and waterborne adventuring.

Adopted structurally: Compact should keep current time/procedure cadence visible and make repeated procedure actions easy to scan, while rules/configuration detail remains secondary.

### The Alexandrian

The Alexandrian's public hexcrawl worksheet groups day/watch, current hex, progress, intended direction/veer, encounters, marching order, watch assignments, and base movement speed on one operational sheet. The accompanying watch checklist orders the repeating workflow from direction/mode through navigation, encounter, actual movement, hex progress, and boundary handling.

Adopted structurally: keep current route/progress/navigation information together; treat party/watch assignments and base movement as nearby operating context; preserve a chronological ledger without making it the primary action surface.

### Pathfinder 2e Hexploration

The official Hexploration rules organize play around a **daily activity budget** derived from the slowest member, with group activities such as Travel/Reconnoiter and individual activities competing for that budget. Terrain changes how many activities traversal/reconnaissance consume.

Adopted structurally: activity-oriented procedures should present the available budget and the activities consuming it together. They should not be forced into a watch-centric presentation merely because other presets use watches.

### The One Ring 2e

Free League's official Journey Log groups journey identity (year/season/origin/destination/days), the travelling company, each member's journey role and travel fatigue, mounts, a journey path, journal space, and discrete event target/event/result records. The route/progress and event history are visually central, while roles/fatigue remain immediately adjacent supporting state.

Adopted structurally: a journey-first procedure should foreground stage/path/progress/events and current roles, with fatigue/effects and mount context nearby. It must not fabricate repeating watch state.

### Forbidden Lands

The official product and quickstart emphasize map-first open-world travel and survival. Established player aids and the Foundry party-sheet workflow group travel activities with party assignments and convenient travel rolls, while common references keep hiking/terrain and survival information close to the journey workflow.

Adopted structurally: survival-heavy procedures need party activity assignment, current travel, and resource/survival status within low-cost reach of one another; they should not be isolated into unrelated subsystems during routine play.

### D&D 2024 / simpler procedures

The usability lesson for the simpler modern-D&D proof is restraint: a procedure with fewer active hexcrawl concepts should not inherit the visual complexity of Alexandrian Advanced or a survival-heavy procedure. The same semantic workspace should collapse naturally to only the concepts the materialized procedure actually uses.

### Synthetic mixed procedure and Custom

The deliberately mixed architecture proof is test-only rather than a production preset. Its purpose is to verify that presentation and runtime behavior follow materialized mechanics and semantic metadata rather than named-preset identity. A custom procedure with no origin must remain coherent.

## Accessibility

Phase 15 component architecture requires semantic controls, labels, keyboard access, visible focus, accessible disclosure, keyboard/touch-capable contextual workspaces, Escape/close behavior, focus restoration, no hover-only required information, no color-only status communication, sensible headings, live status for asynchronous actions where needed, and non-map alternatives for map actions.

Focused workspaces preserve the underlying main context and restore focus to the invoking element when closed.

## Responsive behavior

The same product model must remain usable at wide desktop, ordinary laptop, embedded Site widths, tablet-like widths, and narrow/mobile widths.

Wide layouts may place map/state side by side. Narrow layouts collapse to one column and focused drawers occupy the available viewport without producing inaccessible off-screen controls. Context rail/secondary cards may move below the primary surface. Required actions stay reachable and map interaction always has non-pointer alternatives.

## Loading, failure, and stale state

Major surfaces must distinguish initial/slow loading, mutation pending, provider delay/unavailability, Surveyor unavailability where applicable, unsupported mechanics, manual/adjudication-required state, empty state, validation errors, optimistic concurrency conflicts, deleted references, and retryable failure.

A mutation should not erase the entire useful workspace when the last authoritative state can safely remain visible. JSON syntax/domain errors remain local to the editor until a valid authoritative save succeeds.

## Performance and lifecycle

Phase 15 preserves Phase 14 performance/lifecycle constraints:

- do not reconstruct canvases for ordinary state updates;
- avoid duplicate aggregate/status fetches where a shared authoritative result is already available;
- use explicit runtime/state callbacks rather than DOM observation;
- do not use `MutationObserver` to synchronize application-owned DOM;
- dispose route-scoped listeners/workspaces;
- avoid hidden-panel expensive work when it provides no current value;
- keep procedure/domain calculation on authoritative application/server boundaries rather than recomputing rules in browser presentation code.

## Phase 15.1 extension points

Guided should later be able to add inline explanations, "Why?" affordances, recommendations, examples, consequence explanations, and next-action coaching around the same Compact components and canonical state. Phase 15 deliberately establishes reusable places for those additions without implementing the beginner-teaching layer now.

## Deferred work

Phase 15 intentionally does not implement:

- Phase 15.1 Guided onboarding/coaching;
- Phase 15.5 internal-testing stabilization;
- a Battle Map tool or Battle Map ownership/integration architecture;
- square/triangle Hex Crawl runtime topology;
- unrelated Rules Core, Character Sheet, or Block Initiative feature work.
