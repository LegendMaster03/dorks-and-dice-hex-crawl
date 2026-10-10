# Phase 19 — world geometry and browser integration status

**Branch:** `feature/tile-crawl-phase-19`. **Status:** In progress; not an independent-review candidate yet.

## Authoritative integration completed

- Owner-scoped bounded polygon viewport and containment endpoints delegate to saved `PeriodicWorldTiling`; explicit ambiguity is preserved at shared boundaries. The client renders those polygons, never reconstructs an operational tiling in JavaScript. Qualified cells resolve via the backend.
- The shared `MapSurface` displays polygonal worlds without a source image, handles keyboard center selection, panning, zoom and reset to world origin. Asynchronous selection and viewport requests suppress stale results.
- The existing topology-neutral current-cell navigator receives Phase 18 atomic interface indexes, polygon geometry, and qualified neighbors. A cell expedition uses the normal expedition URL with Phase 18 advance/course/encounter endpoints and does not invent axial coordinates.
- Normal world setup offers square and triangular periodic geometry, optional calibration, and an explicit custom JSON route. The backend accepts any valid independently realizable Euclidean periodic symbol, subject to safe domain limits, not a closed shape allowlist.
- A new saved, editable cell-step campaign procedure can be generated from the existing `simple-fixed-distance` template, pinned to the accepted world topology, with origin/provenance retained; preset definitions and historical revisions are not rewritten. World name updates preserve topology and existing expeditions.
- A read-only owner-scoped procedure compatibility endpoint checks validated D-symbol equivalence via the world's translation cover, executable movement version, and required physical calibration. The generalized expedition start form keeps the Start button disabled until the selected pinned saved revision is confirmed usable, and offers one-click creation of a compatible saved ruleset.
- Polygonal world authoring supports qualified-cell terrain and environment facts and inspecting locations and spatial features against returned polygon geometry. Map drawing/placement still uses world coordinates, independent of any source image.
- Source map placement continues to use the existing three-point affine transform. Experimental Surveyor v3 observations are compared *read-only* with the saved world and optional selected pinned procedure. Uncertain status, unsupported patterns, metric gaps, registered source maps, saved expeditions and authored objects are disclosed rather than silently converted.
- Existing hex/Wonderdraft automatic grid analysis and the v2 client remain intact. Generalized patterns do not enter those hex-only controls.

## Authority boundaries and unresolved gates

The compatibility response currently sets `canCorrectInPlace=false` deliberately. A Surveyor observation is not an accepted geometric realization or a verified map-to-world phase transform. The current store provides versioned single-aggregate writes, but not a transaction spanning a new procedure revision, world topology, and source-map registration while rechecking all dependencies. Exposing the requested one-click conversion before establishing that transaction would risk broken cell identities and tester data.

Additional Phase 19 gates remain: independently registered Surveyor-driven metric/phase alignment; approved safe correction for unblocked defaults; a true mixed/rhombille visual and browser-workflow matrix; broader names/geometry-qualified catalog entries and guided unfamiliar-pattern parameter editing; complete generalized route/coverage/knowledge/overlay parity; mobile, keyboard, dark mode, embedded and signed-in tester verification; and explicit test deployment checks. Keep these open rather than marking Phase 19 complete.

## Test and release guidance

Existing CI exercises frontend compilation/unit tests, .NET domain/application/HTTP integration tests, container build and PostgreSQL workflows. Phase 19 adds image-free creation, owner isolation, pinned cell-step expedition start, polygon selection, qualified environment scopes and mismatch preview tests. Verify the **exact feature-branch head** passes before handing off. No production deployment, tester-data reset, merge or branch cutover is authorized by this document.
