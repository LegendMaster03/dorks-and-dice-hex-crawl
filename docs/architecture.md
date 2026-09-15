# Hex Crawl architecture foundation

## Boundaries

Hex Crawl starts with four independent state axes:

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, and source-map representations.
2. **Crawl/runtime state** — `ExpeditionState` and `WorldRuntimeState`; movement stores physical distance, not a fraction-of-hex value. Generated features/locations and activation, depletion, or destruction state live here as runtime overlays rather than mutating base world definition.
3. **Player knowledge** — `PlayerKnowledgeState` records feature/location knowledge and player annotations. There is deliberately no `Hex.IsRevealed` flag.
4. **Presentation policy** — `MapPresentationPolicy` decides grid and terrain presentation independently of world truth and knowledge.

`CrawlProcedureProfile` is a separate procedure configuration axis. The Alexandrian advanced procedure is represented as one baseline preset, not as the definition of hexcrawling.

## Continuous overworld

An `OverworldDefinition` owns one continuous world coordinate space. Imported map images are modeled as `SourceMapRepresentation` records that cover geography within that space. An optional registration transform converts source-image pixels to world coordinates; the transform model supports both affine and projective/homography-style registration. Image boundaries never define world boundaries.

Source-map storage and image registration are deferred. The representation model exists now so later GM/player variants and neighboring/overlapping regional maps do not force an atlas-document redesign.

## Grid and scale

The grid is mathematical and independent of imagery. The first implementation supports:

- pointy-top and flat-top hexes;
- axial `q,r` coordinates with stable `HexId` identity;
- configurable origin and rotation;
- configurable world-space hex radius;
- configurable physical center-to-center distance and units;
- coordinate/world conversion, neighbors, distance, polygon corners, and feature/hex intersection.

The physical scale does not alter grid identity. Runtime movement records `DistanceMeasure` values; any fraction-of-hex display is derived.

## Semantic spatial features

Geography is not owned by individual hex records. Point, linear, and regional features live in world coordinates. `OverworldDefinition.FeaturesIntersecting()` derives feature membership for a hex through geometry queries. This supports roads, rivers, borders, forests, political regions, and other cross-hex structures without duplicating them into each hex.

Locations are separate semantic objects with discoverability metadata and a deliberately generic `LocationDetailMapReference` extension point. No battle-map behavior exists in this slice.

## Persistence

Persistence is intentionally deferred. The first slice proves domain invariants and rendering without choosing a database prematurely. Domain objects have stable IDs and do not depend on HTTP, EF Core, or storage-specific types, so persistence can be introduced behind application/infrastructure boundaries later.

When persistence arrives it must remain deployment-owned and separate from the main site's Identity storage. Campaign/user identity must enter through the Tool Host contract.

## Rendering decision

The first renderer uses Canvas 2D behind `CanvasMapRenderer`. SVG was rejected for the initial map surface because large visible hex counts, semantic overlays, and future large raster source maps would create an unnecessarily large retained DOM. WebGL was not selected yet because the first demonstrator does not need GPU-specific complexity and Canvas 2D can viewport-cull thousands of simple primitives efficiently enough to validate the spatial architecture.

The renderer receives world-space geometry and a viewport transform. This keeps renderer technology out of domain types and preserves a clean path to a WebGL implementation if profiling later shows Canvas is insufficient. Future image-analysis work is also intentionally separate from the renderer and can use WebAssembly, Web Workers, WebGL/WebGPU, or browser CV libraries without changing `OverworldDefinition`.

The TypeScript client contains a small projection/picking mirror of the deterministic C# grid math because pointer interaction and high-frequency rendering can not reasonably round-trip to the server. The duplication is restricted to pure hex projection/rounding and is covered by matching round-trip/distance invariants in both frontend and domain tests. Domain identity, feature intersection, runtime state, knowledge, and procedure semantics remain backend/domain concerns.

## Import-analysis boundary

No image recognition is implemented. Future map calibration should produce mathematical grid/alignment data and semantic candidates, not authoritative truth inferred from pixels. Deterministic browser-side techniques such as lattice detection, feature matching, affine/homography registration, image differencing, template matching, and segmentation are preferred; a general AI model is optional and must not become a runtime requirement.

## Procedure baseline

The initial procedure shape was checked against Justin Alexander's broader Alexandrian hexcrawl material, including wilderness travel, the watch checklist, and the later 5E advanced procedure. The architecture therefore has explicit places for configurable watch length, physical distance traveled, intended versus actual course, lost/veer state, encounter cadence, terrain/route semantics, intra-hex progress, and feature-level discovery.

Reference: https://thealexandrian.net/wordpress/17308/roleplaying-games/hexcrawl

Those concepts are capability requirements, not mandatory rules. `CrawlProcedureProfile.AlexandrianAdvancedBaseline()` is one preset over the general model. The grid itself does not assume a 12-mile scale, the encounter cadence can be per-watch/per-day/none/custom, navigation and veering can be disabled, and movement can use continuous physical distance or coarse hex steps. More detailed terrain-speed and encounter-table policy is intentionally deferred with the full crawl runtime.
