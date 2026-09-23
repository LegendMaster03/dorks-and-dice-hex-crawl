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

The filesystem provider is an infrastructure choice, not a domain contract. A future object-store, NAS, or other blob provider can implement `IMapAssetStore` without changing semantic world truth. Raster assets and opaque import source archives are stored through this boundary and are never stored as SQLite blobs.

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
GET    /api/overworlds/{worldId}/source-maps/{sourceMapId}/source-archive
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

## Native Wonderdraft source import

Native `.wonderdraft_map` projects are structured import sources, not runtime dependencies and not automatically semantic world truth. The server continues to parse the Godot `GCPF` container, bounded FastLZ blocks, and Variant data under explicit decoded-size, collection-size, nesting, string, and geometry limits.

The normal Wonderdraft workflow is source-first:

1. upload or select the raster export that belongs to the Wonderdraft project;
2. choose the `.wonderdraft_map` project;
3. import the project as source-derived map content;
4. determine the deterministic project-to-raster scale from their pixel dimensions;
5. preserve labels, symbols, paths, territories, and source metadata without requiring semantic classification;
6. preserve source-grid and scale metadata;
7. place the raster automatically when the source physical scale and the Hex Crawl campaign scale provide enough information;
8. review only optional semantic promotions and genuine exceptions.

The native mutation endpoint is:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/source
```

The request resubmits the Wonderdraft project and the expected overworld version. The server re-parses all source records and replaces the source-derived content attached to that source-map representation in one optimistic-concurrency save. A SHA-256 source fingerprint, source type, import time, and source-record count are retained as generic import provenance. Re-importing therefore replaces the retained source layer instead of appending another copy of every decorative record.

Source-derived content is intentionally separate from `Location` and `SpatialFeature`. A tree, mountain icon, map title, credit label, or unknown path remains cartographic source information unless the DM later promotes it to semantic world truth. This prevents large Wonderdraft projects from creating thousands of false POIs.

Lossless preservation and interpreted content are separate layers. The original uploaded project is stored byte-for-byte as an opaque source archive asset owned by the source-map representation. Its provider-relative storage key is not exposed through the detail contract. The archive survives restart, is replaced on a changed re-import, is reused for an identical re-import, and is deleted with the owning source map. This preserves unknown top-level fields, embedded paint data, boxes, windroses, future-version fields, and any other data that the current parser does not yet interpret.

The parsed operational layer stores generic source-map content kinds—label, symbol, line, and region—with a source record key, display name, descriptor, source geometry, scalar/nested source properties, and generic provenance. Wonderdraft-specific parsing and translation remain in the import boundary; universal world objects do not gain Wonderdraft-specific fields. Future Wonderdraft presentation adapters can re-read the opaque archive when richer format-specific rendering is needed instead of relying on a lossy reconstruction.

### Project-to-raster registration

For an export produced from the same Wonderdraft project, project coordinates and raster pixels are not independently registered by clicking landmarks. The server computes:

```text
project pixel
    -> project/export scale
raster pixel
    -> source-map world registration
world point
    -> viewport/canvas transform
screen point
```

The project/export scale is derived independently on X and Y from the project canvas dimensions and raster pixel dimensions. Browser viewport size, CSS image fitting, canvas backing dimensions, device-pixel ratio, browser zoom, pan, and viewport zoom are downstream presentation transforms and do not alter source registration.

If the selected raster already has a saved world registration, native Wonderdraft import preserves it.

If the raster is unplaced and Wonderdraft provides a usable physical scale, the importer may establish initial world placement automatically. The current physical-scale path requires:

- a source unit with a known physical conversion, currently miles or kilometers;
- source scale-bar segment distance, segment count, and pixel length;
- a Hex Crawl grid whose neighboring-hex distance has a physical unit conversion;
- a proportional raster export rather than independent X/Y stretching.

The importer converts Wonderdraft physical distance per raster pixel into Hex Crawl world units per raster pixel. When the world has no semantic locations/features and no other placed source map, it may use the Hex Crawl grid origin as the initial coordinate-frame center. The raster and all imported Wonderdraft geometry remain locked together.

If existing semantic objects or another placed source map already anchor the world, physical scale alone is not treated as proof of translation or rotation. The importer preserves the recovered scale but reports source-only placement rather than guessing. The DM may then use advanced registration once for the whole raster. Individual Wonderdraft records do not require separate registration.

The three-point affine tool therefore remains useful for independently sourced, cropped, rotated, skewed, scanned, or otherwise unrelated representations. It is not the default same-project Wonderdraft workflow.

### Grid and physical-scale metadata

The importer no longer reduces the Wonderdraft grid to a boolean. Scalar grid metadata is preserved generically, and scale/ruler/measurement metadata is retained separately. A typed physical scale is derived only when the stored keys clearly identify the unit label, distance per segment, segment count, and pixel length. Unknown fields remain preserved source metadata instead of being guessed.

Wonderdraft's source-grid configuration and Hex Crawl's mathematical hex grid remain distinct. A visible or configured Wonderdraft grid is not assumed to define Hex Crawl hexes. Physical scale can relate source pixels to campaign distance even when the source map has no baked-in visible hex grid.

More format-specific grid interpretation—such as applying a verified native grid orientation or phase directly to the Hex Crawl grid—requires verified Wonderdraft field semantics. The importer does not invent those meanings from opaque field names.

### Optional semantic promotion

After a placed source import, the existing candidate-preview endpoint remains available for semantic refinement:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/candidates
```

The server maps project geometry through the selected raster dimensions and saved/derived world registration. Preview geometry remains server-authoritative.

The browser review is no longer a first-200-record list. It provides:

- source-kind filtering;
- metadata-aware text search across names, texture/type/style, and preserved properties;
- symbol grouping/filtering using explicit Wonderdraft `type` when available and texture-path grouping otherwise;
- 50-record paging;
- a default suggested-review view that suppresses obviously decorative symbol noise without promoting anything automatically;
- the raster and world map under the review geometry;
- highlighted point, line, and region candidates;
- list-to-map selection;
- map-to-list selection.

Every source record remains retained even when it is not shown in the suggested semantic view.

Explicit semantic promotion still uses:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/import
```

Only records that the DM deliberately promotes need a semantic target, name, category, and, for locations, discoverability. The server re-parses the uploaded project and recomputes world coordinates instead of trusting browser-submitted geometry. Selected semantic objects are validated before one optimistic-concurrency save.

Promoted semantic objects remain ordinary world objects and are not deleted when the source raster is deleted. Source provenance currently applies to the retained source layer; automatic reconciliation or deduplication of previously promoted semantic objects across later source revisions is not yet implemented.

Territory and other source geometry is retained as stored, including coordinates outside the nominal raster canvas. The source layer does not silently clamp geometry. Any later semantic promotion passes through the normal semantic geometry validation separately.

## Intentionally deferred analysis

The importer preserves source truth without pretending to understand cartographic meaning that Wonderdraft does not encode explicitly. The following remain separate future work:

- verified format-specific interpretation of Wonderdraft grid orientation, phase, and other native grid fields beyond the preserved generic metadata;
- automatic reconciliation/deduplication of semantic objects that were promoted from an older revision of the same source;
- automatic association of nearby labels and settlement markers unless an explicit Wonderdraft relationship or sufficiently auditable inference is available;
- four-point/projective registration UI for perspective-distorted sources;
- automatic overlapping-map or unrelated-feature registration;
- GM/player image differencing;
- icon recognition beyond source metadata;
- road, trail, river, terrain, or other semantic interpretation not explicitly encoded by the source;
- OCR or AI/ML interpretation of raster-only information;
- final player-facing source-map presentation policy.

These are not required for lossless native source import. They can build on persisted source content, provenance, source/raster coordinate linkage, and server-authoritative world registration.
