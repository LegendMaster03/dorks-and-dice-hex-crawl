# Phase 20.3 — UI cutover work checkpoint

**Repository and branch:** `LegendMaster03/dorks-and-dice-hex-crawl`, `feature/tile-crawl-phase-20.2-application-persistence`  
**Initial implementation code head:** `bd3e3d69463b088591a428512e412388a181a907`  
**Code verification:** [CI 38202965661](https://github.com/LegendMaster03/dorks-and-dice-hex-crawl/actions/runs/38202965661); check its actual completion before reporting results. The CI workflow's frontend, .NET, container and PostgreSQL checks are separate from signed-in browser acceptance.  
**Merge and deployment:** Neither is authorized. The Phase 20.2 one-time schema-10→11 migration has not been applied to the development deployment.

## Implemented first slice

- Home spatial world start now loads the selected world's accepted tiling. Existing hex-world q/r fields are converted to a world-space center and sent through `GET /api/overworlds/{id}/cells/containing`; only a unique cell in the matching tiling is accepted. The returned motif cell/translation, not q/r or invented hex-progress state, is sent to `POST /api/overworlds/{id}/cell-expeditions`.
- For generalized worlds the home setup exposes motif cell and lattice translation fields. Saved ruleset revisions are checked through the cell-procedure compatibility endpoint before start. The home selector retains the existing three native geometric hex presets in spatial contexts and permits the original broader preset list for nonspatial journeys. It introduces **no new tiling selector or nonhex presets**.
- The world editor's existing expedition form is replaced by the shared advanced `enhanceExpeditionSetup` form, with saved revisions, three native hex preset recipes, presentation policies, nonhex map-click start selection, compatibility assessment and compatible saved cell-travel ruleset creation.
- Abstract-mapless starts now call `POST /api/expeditions/cells`; nonspatial starts still use `POST /api/expeditions`. The V2 `currentCell` response enters the existing generalized workbench from the same expedition route, including without an authored world. Mapless UI does not offer authored-world location discoveries.
- CanvasMapRenderer now draws the server-authoritative current-cell polygon and party location on an existing hex grid **and** in a mapless session, rather than returning immediately for null world. Mapless viewport initialization and reset retain its actual cell center. The world-bound map still retains its source images.
- The existing generalized workbench uses `GET /cell-topology` and `POST /cells/advance`, `/cells/course`, `/cells/encounters/{occurrenceId}/resolve` for travel, boundary and encounter control, with current-world party/environment/journey/effects/survival subpanels.
- New frontend coverage in `test/phase20-ui-cutover.test.mjs` checks q/r-to-server-cell lookup integrity, rejection of ambiguous/foreign cells, active start-route elimination and mapless render-path guardrails. Obsolete V1 source-contract assertions were replaced where they contradicted the intentional cutover.

## Outstanding before owner merge review

1. **Render and manually exercise all supported workflows**, not only source-contract tests. Test current authored hex worlds; mapless starts and reloads; a structurally different compatible world; existing saved revisions; nonspatial journeys; owner isolation, stale versions, pause/resume, map selection, source-map imagery and keyboard/narrow-screen UI. Current GitHub CI skips the Phase 15 rendered visual matrix.
2. **Restore full Phase 15 Guided/Compact/Advanced/JSON experience on V2**, preserving comprehensible travel, ruleset help, automatic helper-resolution UX, navigation, encounters, partial-distance progress, discovery and player knowledge. The initial V2 workbench is a functional manual-DM entry surface, not proof of complete Phase 15 parity.
3. **Audit all remaining V1 client interactions**, including auxiliary assistant routes, old advance/reposition/course-intent calls and specialized actions; replace or remove them without reinstating retired backend endpoints. Do not rely solely on their absence from the primary home/editor start paths.
4. **Finish authoring and world-change coherency**, including revalidation of a setup form when a world is edited after loading it, qualified-cell picking/registration and mixed periodic fixture acceptance. Never silently rebase a saved procedure revision.
5. **Coordinate testing/deployment**, including a real PostgreSQL plus filesystem-map-asset backup and independently verified restore, schema-kind preflight, authorized one-time schema 10→11 development migration, and signed-in reload/restart tests. No merge or deployment until these acceptance results and owner authorization.

## Boundary conditions

The development plan's Phase 20 decisions control. Phase numbers are workstreams, not a reason to preserve V1 runtime code. Existing hex presets remain available through the general geometric engine. Other-tiling settings UI and new square/triangle presets remain deferred until the owner's system review.
