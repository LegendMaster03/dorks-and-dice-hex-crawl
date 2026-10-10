# WorkChat handoff — Phase 18 independent implementation review

## Mission

Independently audit the bounded Phase 18 generalized-cell **backend** implementation against existing hex and abstract/nonspatial behavior. The implementation team has produced a review candidate; do **not** assume it is correct, mergeable, or complete because the branch passes CI. Find and reproduce defects; distinguish release blockers from Phase 19 presentation or later imaging work. Apply narrowly scoped fixes only when explicitly instructed by the user, then retest and report the exact resulting commit.

## Exact review target

- Repository: `LegendMaster03/dorks-and-dice-hex-crawl`
- Branch: `feature/tile-crawl-phase-18`
- Compare against `main`. Record the **exact** branch SHA and independently verify the final GitHub Actions run for that SHA.
- Scope inventory: `docs/phase-18-traversal-integration.md`.
- No Surveyor changes. No migrations, merge, deploy, deleting expeditions, resetting databases, or modifying tester data.

## Critical investigations

1. **One authoritative engine:** examine `CrawlRuntimeEngine.AdvanceCore` and the cell watch adapter, shared watch-start/encounter-time/provenance/occurrence rules, and `PeriodicTraversalExecution`. Evaluate whether movement, pause/resume, encounters, travel time, geometry and watch completion remain semantically equivalent when applicable, without axial fabrication or duplicated independent mechanics.
2. **World geometry:** arbitrary admitted Euclidean periodic tilings, mixed-degree neighbors, reciprocal atomic interfaces rather than polygon side ordinals, center or edge positions, exact boundary, vertex ties, invalid/outward/tangential exits, mid-watch changed headings, double-back, lost veer/recognition, calibrated versus uncalibrated travel, movement at large translations/scales, and no phantom progress.
3. **State and API boundaries:** world-qualified cursor/entry/exit and event identities, optimistic concurrency, owner isolation, canonical version-2 saved procedure authoring and pinning, distinct cell start/advance/course/encounter routes, paused encounter resolution and retry semantics, server-issued provenance restrictions, and old hex/nonspatial JSON stability.
4. **Semantic composition:** initial player knowledge by selected presentation policy, known-cell updates, hidden and obvious location distinctions, manual and keyed location discovery, encounter scene snapshots, environment cell annotations and effective fact provenance, Phase 10/11 delays, resources and foraging/exposure policy interpretation, journey and forced-travel accounting. Identify any existing pinned gameplay mechanic that a cell expedition can now silently bypass. Never introduce unapproved rules formulas.
5. **Data protection:** validate existing schema-10 expeditions and maps without replacing them, persistence/restart of partially advanced watches, pending encounter occurrence, typed effects, discoveries, procedure revisions and environment scopes, concurrent updates and stale requests.
6. **Scope:** backend-only generalized world support belongs to Phase 18. The ordinary hex-centric browser workbench remains intentionally gated for nonhex sessions; shape-adaptive navigation/player rendering is Phase 19. Image recognition improvements and Surveyor changes are outside this review. Expedition deletion is also excluded.

## Evidence expected

Run frontend, domain, application/PostgreSQL, HTTP integration, container and persistence smoke tests on the exact branch head. Add focused tests for any uncovered risk. Give each blocking finding a reproducible sequence, affected files, expected/actual behavior, and proposed minimal correction. State which concerns were verified, which were not testable, and whether the **backend Phase 18 scope** is ready for a merge decision. Do not merge or deploy on your own. `main` is a **dev/testing** deployment; preserving the tester database matters even though this is not production.
