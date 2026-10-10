# Phase 19 — bounded world cell queries (integration foundation)

**Status:** Implemented server and client contracts; generalized Canvas integration, world authoring, and safe correction are not complete.

## Authority and ownership

The owner-scoped `GET /api/overworlds/{overworldId}/cells/viewport?minX=&minY=&maxX=&maxY=` endpoint resolves the saved world through `HexCrawlService.GetOverworldAsync`, then calls `SpatialTiling.Intersecting` with a 2,500-candidate bound. The four coordinates are world-space, not raster pixels. The return value contains authoritative `WorldCellGeometry` records (qualified cell ID, polygon, centroid). The caller must supply finite, ordered bounds; clients should select suitable zoom levels rather than ask for unbounded world enumeration.

`GET /api/overworlds/{overworldId}/cells/containing?x=&y=` resolves the saved world through the same authorization path, then calls `SpatialTiling.Containing`. It returns an explicit `NotFound`, `Unique`, or `Ambiguous` classification and qualified candidates. A map selection must not arbitrarily pick one candidate from an ambiguous shared edge or vertex.

These are read-only routes and do not alter world topology, registered map images, procedure revisions, or tester data. Existing `Grid`-based routes remain usable during Phase 19.

The client also exposes the existing versioned `GET /api/expeditions/{expeditionId}/cell-topology` contract and a `currentPeriodicCellAdjacency` adapter. The adapter preserves the backend's atomic `InterfaceIndex`, neighbor's `WorldCellId`, endpoints, and selection identity; it fits only presentation coordinates. It neither constructs adjacency from polygon sides nor executes traversal in TypeScript.

## Outstanding integration gates

The navigator adapter must still be connected to generalized expedition state, and viewport queries must be used by the Canvas renderer and map selection. Browser travel controls must call the Phase 18 cell endpoints with qualified intent rather than hex directions. The import/Surveyor qualification, world/ruleset compatibility preview and atomic correction workflows remain outstanding. Until those gates are implemented and tested, do not describe Phase 19 as complete or expose a generalized expedition browser workflow as supported.

No persistence schema change is made by these query contracts.
