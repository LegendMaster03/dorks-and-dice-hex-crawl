# Phase 20.3 — WorkChat signed-in dev/testing acceptance handoff

**Intent:** Review the deployed, owner-authorized Hex Crawl dev/testing environment after the Phase 15-first V2 cell UI cutover. This is **acceptance testing**, not implementation, deployment or merge permission. Use the project development plan and `docs/phase-20.3-ui-cutover-checkpoint.md` as the contract.

## Starting conditions and safety

1. Verify the owner says the **dev/testing deployment has completed**, the browser is authenticated, the backend/frontend deployed commits are the same intended build, and the authorized one-time database schema-10→11 migration has completed if applicable. Do **not** invoke a deployment, merge a branch, reset the database, or execute a migration yourself.
2. The owner/implementer must have separately secured the PostgreSQL DB and filesystem map assets and verified an isolated restore before applying the migration to the real dev DB. Tester expeditions from V1 are disposable by owner decision; existing worlds, maps/assets, saved procedures, accounts and V2/nonspatial histories must survive. If the schema migration reports an unknown or explicit-null discriminator, stop rather than bypass the preflight.
3. Use an identifiable, self-contained disposable test namespace (for example `Phase20.3-WorkChat-...`). Preserve existing tester data; delete **only** your own disposable records if the UI supports it and cleanup does not hide a defect. Never alter another person's saved rulesets or worlds.
4. Observe the Network/Console panels. For every failure, capture the page, action, exact HTTP method/path/status, request/response shape (redacting credentials), screenshot if helpful, persisted state before/after, and reproduction steps. Distinguish browser defect, API rejection, test-fixture issue, or expected fail-closed validation.
5. Prioritize ordinary existing **hex** presets and authored worlds. Do not add, expose or demand a square/triangle UI, new preset, or nonhex settings toggle. A held-out compatible generalized-world fixture may be used through already-available advanced/internal authoring only.

## Signed-in acceptance matrix

### A. Entry and procedure selection
- Verify existing signed-in home, world library, world editor, source maps and procedures still open. Test returning to the editor and reopening an expedition directly by URL after a hard reload.
- Create a disposable world-bound spatial expedition with the existing hex-step, fixed-distance and Alexandrian-style geometric presets, as supported by the saved test fixtures. Verify the normal `/cell-expeditions` route rather than a retired `/expeditions` V1 world start. Confirm pinned preset/revision and world ownership.
- Select an existing compatible saved ruleset revision; check the world/procedure compatibility message and that an incompatible or inconclusive revision does not start. If world changes after loading the form, verify that an invalid start is rejected and exposed rather than silently using stale topology.
- Start a new **mapless** spatial expedition via `/api/expeditions/cells` and independently a **nonspatial** journey via `/api/expeditions`. Check focused travel/navigation assistant entry routes and nonspatial interval/encounter helper routes. No screen should redirect to a deleted V1 spatial assistant.

### B. Unified Phase 15 experience and map geometry
- Verify the same map-first Phase 15 layout: header, status cards, Next action, Guided help toggle, at-table rail, GM Tools, travel-focused drawer and rule reference. Confirm the content is usable in narrow viewport and keyboard-only navigation, including focus after selecting atomic boundaries and after closing dialogs.
- In an existing hex-grid world with a saved source map, click within a cell and near seams. The server must resolve the **actual clicked world point**; ambiguous/empty points show a clear error. The selected polygon highlight must match the authoritative geometry, not an approximate hex drawn solely from q/r.
- Click a uniquely adjacent cell: it should set intended course and change Next action without moving the party. Click a distant cell or an adjacency with multiple atomic interfaces: it should inspect only. Confirm the map never teleports due merely to inspection.
- For a nonhex compatible advanced fixture, exercise actual atomic interfaces, reciprocal neighbors, exact polygons and scroll/zoom; do not require any new public nonhex UI.
- For mapless spatial travel, verify a meaningful current-cell polygon, centered viewport, correct current-cell status and ability to reload/resume without an authored world or imaginary q/r state.

### C. Movement, watches and helper provenance
- Run and resume cell-step and qualified physical-distance watches. Validate selected heading, pace, activity assignments, suggested versus DM-confirmed travel distances, time, current qualified cell and history; do not infer travel from a screen-space measurement.
- Exercise boundary crossing, deliberate double-back, continue across boundaries, changed conditions and partial remaining-watch time. Reload mid-watch and confirm the saved double-back/continue flags and current heading. An ambiguous exit must pause for a specific atomic exit decision; do not pick arbitrarily.
- Exercise automatic travel, navigation (including failure veer in **degrees**, not hex steps) and encounter helpers where the pinned preset configures them. Verify the server returns a generated-resolution id and version, the UI displays rolled results, and the exact generation can be consumed **once**. Editing helper DC/modifier/veer, movement inputs, selecting another boundary, or changing party inputs must invalidate it. A stale/cross-session grant must fail closed.
- In a second tab, mutate the same disposable expedition and then try an older version in the first. Confirm a clear conflict/reload path rather than duplicate or lost movement.

### D. Encounters, knowledge and GM actions
- Trigger or record an encounter and verify travel pauses. Test Block Initiative handoff using the saved runtime occurrence and return context, then separately mark the encounter resolved and resume. Handoff must not itself mark the encounter resolved.
- On a world-bound map, select a cell containing an authored point feature or location. Reveal it via GM player-knowledge controls; verify it appears in the saved knowledge state, map highlighting and after reload. Distant subject revelation is an explicit DM action, not inferred movement.
- Select a distinct authoritative cell and use GM teleport. Verify the confirmation, world cell identity, exact center/position and audit history; travel time must not increase. Teleport must be refused during an active watch, pending encounter or pending travel-budget charge. Try a stale world-cell geometry after a world tiling revision change and confirm a safe rejection.
- Verify party, activities, environment, survival, effects and journey editors can mutate and persist without breaking the V2 current-cell state.

### E. Restart, storage and cross-user boundaries
- Verify existing authoring maps, uploaded source-map images and assets after reload and application restart; repeat using a new disposable expedition. Check that source files and Postgres records are both intact.
- Where a separate test account is available, verify one owner cannot read/mutate another owner's expeditions, world-bound topology or helper grants. Do not use another person's real data for this.
- Check console and Network for unhandled errors, unexpected 404/500s, or calls to deleted routes such as `/api/expeditions/{id}/advance`, `/api/overworlds/{id}/expeditions` as POST, old hex-only assistant routes, or scalar `/resolution-helper` for cell sessions.

## Reporting protocol

Produce a concise PASS / FAIL / BLOCKED matrix for sections A–E, separating individually verified behaviors from assumptions. Include fixture names/IDs and specific HTTP calls where useful, exact repros for defects, and visual evidence. **Do not claim successful live acceptance unless these paths have actually been exercised.** Classify any release-blocking regression or data-loss concern distinctly. Leave Phase 20.3 unmerged pending owner review and targeted repairs.
