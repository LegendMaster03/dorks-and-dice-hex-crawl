# Source-map import and registration

## Source maps are representations, not world truth

A `SourceMapRepresentation` is raster evidence/presentation for part of one continuous `OverworldDefinition`. It does not become terrain, a separate atlas page, or a replacement for semantic locations, regions, roads, rivers, and other authored world objects. Deleting a source map therefore deletes only the representation and its uniquely uploaded binary asset.

Multiple representations may share one human-readable `GeographyKey`. For example, GM/grid, GM/gridless, Player/grid, and Player/gridless Bellowing Wilds images can remain four independent records in one `Bellowing Wilds` geography group. Kylandria can use a different key while occupying the same continuous overworld coordinate space.

Source roles are `Gm`, `Player`, `Neutral`, and `Other`. Role is descriptive metadata in this cycle. Runtime is DM-facing and does not automatically select a Player-role representation over a GM-role representation.

## Asset storage boundary

The domain and persisted source-map record know only an opaque provider-relative `AssetKey`. They do not contain absolute paths, Docker volume names, or host paths.

`IMapAssetStore` is the application/infrastructure boundary for streamed write, streamed read, metadata lookup, and deletion. The initial provider is `FilesystemMapAssetStore`. It generates keys shaped like `maps/{generated-id}` and never derives the storage path from a browser filename. Keys are validated before path resolution and can not traverse outside the configured asset root.

Production config sets `MapAssets:RootPath=/data/assets`. The existing `hex-crawl-data` volume is mounted at `/data`, so the production layout is:

```text
/data/hex-crawl.db
/data/assets/maps/...
/data/assets/.tmp/...
```

The filesystem provider is an infrastructure choice, not a domain contract. A future object-store, NAS, or other blob provider can implement `IMapAssetStore` without changing `SourceMapRepresentation` or semantic world truth. Binary maps are never stored as SQLite blobs.

Writes use a temporary file in the asset root, flush successfully, and then rename into the final generated key. A failed write never creates a final asset. Startup creates missing asset directories but performs no destructive cleanup.

## Upload consistency

Binary storage and SQLite metadata are not treated as a distributed transaction.

The upload sequence is:

1. authorize/load the containing overworld and verify its optimistic version;
2. validate multipart metadata and raster header/dimensions;
3. stream the binary into a new generated asset;
4. persist a new source-map representation with that `AssetKey`;
5. if metadata persistence fails, best-effort delete the newly written asset and log cleanup failure if compensation itself fails.

Deletion runs in the reverse authoritative order: source-map metadata is removed using optimistic concurrency and then its uniquely owned asset is deleted. If binary cleanup fails, the server logs the orphan and returns an explicit cleanup error rather than claiming complete success. Semantic geography is never deleted as part of this operation.

## Supported raster input and safety limits

This slice accepts PNG, JPEG, and WebP. SVG and PDF are intentionally unsupported.

`RasterImageInspector` checks file signatures and the relevant deterministic image header rather than trusting the browser MIME type or filename. It extracts source pixel width and height without a heavyweight image-processing dependency. Those dimensions are persisted as source-map metadata because registration maps source pixel coordinates into overworld coordinates.

Safety limits are deployment configurable:

- `MapImport:MaxFileBytes` — default 100 MiB;
- `MapImport:MaxPixelCount` — default 100,000,000 pixels;
- `MapImport:MaxDimension` — default 32,768 pixels per dimension.

The Hex Crawl file limit intentionally remains below the Dorks & Dice Tool Host upstream transport ceiling. ASP.NET may spool multipart bodies to temporary storage, but the map provider reads and writes the file as streams and does not materialize the full raster as a managed byte array.

## Authorization and binary routes

There is no general `GET /api/assets/{assetKey}` endpoint.

Binary access is scoped through the parent world and source-map identity:

```text
GET /api/overworlds/{worldId}/source-maps/{sourceMapId}/asset
```

The server first loads the world using the existing owner identity and then resolves the stored representation and its `AssetKey`. Guessing an `AssetKey` or another user's world/map ID does not bypass owner authorization.

The source-map API also provides:

```text
GET    /api/overworlds/{worldId}/source-maps
POST   /api/overworlds/{worldId}/source-maps
PUT    /api/overworlds/{worldId}/source-maps/{sourceMapId}
PUT    /api/overworlds/{worldId}/source-maps/{sourceMapId}/registration
DELETE /api/overworlds/{worldId}/source-maps/{sourceMapId}
```

POST is multipart and is the only HTTP path that mints a new map `AssetKey`. Legacy metadata-only HTTP creation/update paths that accepted arbitrary asset keys are not exposed.

Hosted clients use the normal Embedded Module upstream prefix, so the same requests become `/tool-host/{slug}/api/upstream/api/...`. Standalone clients use `/api/...` directly. Assets are never base64-wrapped in JSON and the browser never addresses the backend container hostname.

## Affine registration

A saved registration is a `MapRegistrationTransform` mapping source image pixels to overworld coordinates. Existing projective-transform domain support remains intact for future photographed/scanned/perspective-distorted sources; this UI creates affine transforms only.

Registration mode collects exactly three source/world control-point pairs. For each pair the DM selects a landmark pixel in the source preview and then the corresponding point on the overworld Canvas. While registration mode is active, its explicit map-click interceptor consumes Canvas clicks before location/feature placement can see them.

`AffineRegistrationSolver` solves the six affine coefficients deterministically from the three pairs. It rejects duplicate points, collinear source points, degenerate output bases, and non-finite values. The browser runs the same deterministic math for preview; the server independently solves and validates the submitted control points before persistence, so client matrix coefficients are not trusted.

After a valid transform is solved, coverage is derived from these source corners:

```text
(0, 0)
(width, 0)
(width, height)
(0, height)
```

The four transformed points become `WorldCoverageBoundary`. The user does not author a second coverage polygon.

## Canvas raster layer and cache

Registered affine source rasters render as base geography. Canvas layer order is:

1. registered source rasters;
2. semantic region overlays;
3. mathematical hex grid;
4. semantic line/point features;
5. locations;
6. selection/highlight;
7. expedition marker.

Semantic objects remain separate and interactive above the raster.

`RasterImageCache` asynchronously fetches an ownership-scoped asset once, decodes it to an `ImageBitmap` where available, asks the explicit render lifecycle for another frame when ready, and reuses the decoded image on subsequent renders. Removed maps are pruned. Route cleanup closes disposable `ImageBitmap` objects and revokes fallback object URLs. No application-owned `MutationObserver` is used.

The world editor keeps show/hide state only in the renderer. Visibility toggles do not mutate or version world truth. The expedition runtime uses the same Canvas renderer, so registered source maps are available as base geography there as well.

## Wonderdraft project review and selective semantic import

Native `.wonderdraft_map` projects are import sources, not runtime dependencies or world truth. The server reads the Godot `GCPF` container, bounded FastLZ blocks, and binary Variant structure directly. Decoded size, collection size, nesting depth, and candidate geometry have explicit limits; embedded image byte arrays are skipped for semantic review rather than promoted as map truth.

Inspection is owner-scoped and non-mutating:

```text
POST /api/overworlds/{worldId}/source-maps/wonderdraft/inspect
```

Candidate review is also non-mutating and requires a registered source raster:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/candidates
```

Wonderdraft canvas coordinates are first scaled into the selected raster's pixel dimensions, then transformed through that raster's saved registration. The server, not the browser, derives overworld geometry. Labels and symbols expose point candidates; paths expose line candidates; territories expose region candidates. Source type, texture/path descriptor, and unsupported-record problems are retained for review. No terrain, settlement, road, river, or other semantic category is inferred automatically.

Selective promotion is a separate mutation:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/import
```

The multipart request resubmits the Wonderdraft project, the expected overworld version, and an explicit JSON selection list. Every selected record requires a semantic target, name, and category. Point records may become a Location or Point feature; paths may become Line features; territories may become Region features. Location discoverability is explicit. The server re-parses the project and recomputes registered world coordinates instead of trusting preview geometry from the client.

All selected objects are validated before one optimistic-concurrency save. A failed candidate, stale version, invalid region, incompatible target, duplicate candidate key, or malformed project leaves the overworld unchanged. Successful promotion increments the world version once and creates ordinary semantic objects with stable IDs. The Wonderdraft project itself is not persisted, and later deletion of the source raster does not delete promoted semantic objects.

Re-import deduplication/provenance is intentionally not implicit in this first slice. Re-running an import can create additional semantic objects, so the review UI defaults every candidate to Skip and requires deliberate selection.

## Intentionally deferred analysis

This slice does not perform or pretend to perform automatic map interpretation. The following remain future import-analysis work:

- automatic hex-grid/Hough detection;
- automatic overlapping-map or feature registration;
- four-point/projective registration UI;
- GM/player image differencing;
- icon, tower, or star recognition;
- repeated-icon matching;
- road, trail, or river tracing;
- terrain segmentation;
- OCR;
- AI/ML map interpretation;
- Wonderdraft re-import provenance/deduplication;
- paged review for projects with more than 200 browser-visible candidates;
- final player-facing source-map presentation policy.

Those systems can now build on persisted, authorized source rasters with known dimensions, geography grouping, and real pixel-to-world registration rather than on placeholders.
