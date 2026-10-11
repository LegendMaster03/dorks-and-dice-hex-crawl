# Phase 19 deployed acceptance — corrective follow-up

**Original deployment:** `97156ba9c4384058578920b4307b2d64f525f486` (Phase 19 merged as PR #75).  
**Source of findings:** signed-in operational acceptance, October 10, 2026, with three reproduced blockers.  
**Corrective branch:** `fix/phase-19-deployed-acceptance`, based on the deployed main commit.  
**Status:** Code/regression corrections complete for independent WorkChat review. **Deployed browser acceptance is not yet repeated. Do not merge or deploy without explicit approval.**

## Corrections

### F1 — uncalibrated cell-step expedition opens without a physical distance

The shared party editor now uses an optional unit resolver. A generalized uncalibrated world with null distance total and no party movement references renders three **blank, optional** movement-reference inputs and a neutral `Choose a unit` selector. It does **not** assume miles, invent map calibration, or change the saved party. A calibrated session or one with saved party reference rates still offers its actual unit. Nonspatial editing retains its prior input behavior.

Implementation: `party-movement-rendering.ts` and `expedition-party-sheet.ts`. The actual renderer delegates to tested markup/unit-resolution helpers. UI regression uses the uncalibrated null-distance runtime and verifies blank values and no persisted movement. The complete signed-in rendered workbench still requires browser acceptance.

### F2 — generated cell travel has implicit fixed pace rather than a nonexistent choice

A generated cell-step ruleset may expose **zero finite travel-mode choices**. That represents the server's existing `TravelModeSelection.Normal` default (`normal`), not a reason to block travel. One configured choice is fixed to its declared key; multiple choices remain selectable. Active-watch pace is retained. The workbench only presents an editable selector when there are multiple authoritative options, shows fixed-pace guidance otherwise, and submits the precise selected/default key without introducing a new authoring requirement.

Implementation: `periodic-travel-pace.ts`, `periodic-expedition-workbench.ts`. Regression includes exact API routes: create periodic world, generate saved compatible cell-step procedure, start expedition, inspect empty `travelModeKeys`, advance one cell via a real authoritative interface with `normal` pace, reopen PostgreSQL and verify position/watch/version. Covers both uncalibrated square and physically calibrated triangle.

### F3 — navigator must stay inside current-position sidebar

The generalized expedition is scoped as `.hc-periodic-expedition`. The navigator is laid out in normal flow within `[data-cell-navigator]`, with a 100%-bounded width and desktop/narrow sizing. The absolute-position overlay used by the legacy hex map is unchanged. CSS regression verifies both scoped desktop and mobile overrides. Browser bounding-box acceptance remains required.

## Independent deployment/browser re-acceptance checklist

**Preserve tester data.** The original deployed reviewer listed 18 preexisting expeditions and two preexisting worlds before Phase 19 QA records were added. Five `PHASE 19 QA` worlds, two generated revisions and two expeditions were then created; do not delete or reset them. The old Bellowing Wildes `test` map's grid alignment was modified by the earlier test, so avoid changing it again unintentionally. Take and verify PostgreSQL and filesystem raster-asset backups before any deployed testing/restart.

1. **F1 signed-in reproduction:** open the existing Square traversal `/tools/hex-crawl/expeditions/471139e7-607b-41f8-b08c-a74a63413725` (world `1b9f1f8c-fe09-4a24-a716-79efcfe25dc5`); page must load without a null `unit` error. Inspect empty party movement controls and confirm no fabricated physical distance, rate or world scale. Add/save/reload a party member or a deliberately selected reference unit without losing route context.
2. **F2 signed-in reproduction:** open the existing Triangle traversal `/tools/hex-crawl/expeditions/6fafe289-f9b0-4302-89eb-f9e807ecdb25` (world `1acc94fe-6c9e-472f-8ce2-b84ddc86e0cb`). Verify its generated revision exposes a noneditable `normal` pace, the button becomes enabled after valid intent, and an actual cell step is persisted. Reload, verify adjacent cell/entry interface/watch state, and repeat on a fresh image-free square with no calibration. If QA records must not be advanced, create named *new* disposable testing records, not a reset or mutation of original expeditions.
3. **F3 desktop and mobile layout:** inspect the triangle's current-cell navigator. It must be contained within the current-position sidebar, never cover the Dorks & Dice header, and remain usable at desktop and 375px/tablet widths. Measure `getBoundingClientRect()` of the navigator, host and site header. Verify all actual atomic interfaces are keyboard/pointer selectable and map pan/zoom/navigation intent remain independent.
4. **Existing-data and Surveyor regression:** after a controlled approved restart, verify the Bellowing Wildes hex expedition, saved journey, source-map asset preview, saved alignment, and party/knowledge/asset persistence. Test the actual Surveyor raster only with permission to mutate existing alignment or on separate disposable QA data. Do not claim a successful matching v3 registration from a negative motif result.
5. **Broader unfinished Phase 19 matrix:** keyed discovery after a cell crossing, existing-gameplay panel edits, course/encounter pauses, session/known-world access control, multiple viewport sizes, Guided/Compact, light/dark, player-hidden visibility and independent map registration/correction concurrency.

## Validation boundary

Normal GitHub Actions CI covers frontend runtime-policy/markup tests, TypeScript, .NET domain/application, PostgreSQL HTTP integration, container and isolated database restart/asset smoke. The isolated smoke databases are **not** the live deployment database, and static CSS tests are **not** DOM positioning evidence. The signed-in deployment must be explicitly updated and retested before Phase 19 can be considered operationally accepted. No database reset, deployment, merge, or change to existing tester records is authorized by this document.
