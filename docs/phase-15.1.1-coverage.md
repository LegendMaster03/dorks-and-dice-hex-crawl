# Phase 15.1.1 — language, disclosure, and guided task coverage

This records the developer audit and intended validation of presentation-only changes, not human-tester results. Names of persisted contexts, stored ruleset modules, API fields, and source/edition identities remain unchanged.

| Surface | Phase 15.1.1 change | Coverage and limit |
| --- | --- | --- |
| Home/expedition setup | Three optional, reversible steps: map/travel setup, exploration ruleset, details; direct form when help is hidden; grouped map scale | World-bound, abstract spatial, and nonspatial context choices retain their existing start APIs and input validation. No new onboarding state is persisted. |
| World list and editor | `Worlds / maps` and grouped map scale, with unit; internal alignment stays advanced | Existing grid calibration and geometry code are unchanged. |
| Preset chooser | Name, concise experience description, four consistent facts, action buttons; manual/non-executable caveat before selection | Active configured travel, time, navigation, journey, and automation mode are derived without identity switching. Full rules and source metadata remain in drawer. |
| Preset catalog | Short plain-language descriptions without phase history or schema jargon | B/X and Old-School Essentials keep distinct identities with shared configured behavior. |
| Custom rulesets | Ruleset-facing entry language and scale/progress help | Compact/Advanced/JSON, parameter contract, save/revision operations remain unchanged. Technical terms remain within advanced editors where exactness is needed. |
| Ruleset reference | Clear ruleset terminology and manual-execution limits | Module-specific exact parameters, dependencies, origins, and execution details remain accessible. |
| Focused travel/navigation/encounter utilities | Mapless/spatial/nonspatial explanations, ruleset labels, grouped map scale | Existing focused operation eligibility and mutations remain unchanged. |
| Active expedition | Next action uses clear travel-direction language; guided input/effect/last-event hints from saved state | Handles travel intent, unresolved movement, navigation, encounter pauses, forced travel and resources, journey stage/event, boundary decision, and time-only watches without new progression state. |

## Rules and mechanics checked

- **Time/interval:** Time-only rule selection does not claim a spatial travel requirement.
- **Movement:** Fixed/variable movement and partial-cell progress continue to rely on the saved ruleset and runtime; the UI does not invent a distance or a guaranteed completed cell.
- **Navigation:** Disabled navigation does not appear as active. Active checks and getting-lost behavior are distinct; selecting a direction does not automatically move the party.
- **Encounters:** Configured cadence of None is not presented as a required encounter check. Manual/contextual encounter schedules retain visible manual handling.
- **Survival/resources/effects:** Active configured areas and pending consequences remain visible; they are not claimed to resolve automatically.
- **Journeys:** Staged journey setup and pending actions are represented without fabricated geometry on a nonspatial journey.
- **Automation:** Automatic, DM-assisted, manual, and not-fully-executable behavior are distinguished; source identity is informational, not execution authority.
- **Generic tilings:** No new hex-only geometry assumptions, fixed-direction labels, or direction enums were introduced. The existing current-cell topology is retained.

## Verification plan and evidence boundaries

Frontend checks: client test suite, TypeScript typechecking, production build, and embedded smoke. Rendered review: existing `visual-review/capture-phase15.sh` across its desktop, embedded-container, narrow viewport, and light/dark states, including a guided setup back/forward, help-toggle, and retained-input scenario. Evaluate screenshots, not only overflow dimensions; capture them with the exact workflow/head, viewport sizes, and state names.

Source-contract tests validate readable wording and intact architecture but are not substitutes for interactions. Preset guidance tests cover configured behavior; visual fixtures are synthetic and must not be described as a real-catalog usability test. Human onboarding success remains unverified pending tester observation.

## Remaining dependencies

Restricted Hex Crawl deep links that return a generic not-found experience for users who are not yet authenticated or tester-authorized require changes in the Site host. Hex Crawl can not fix Site authentication at this layer.
