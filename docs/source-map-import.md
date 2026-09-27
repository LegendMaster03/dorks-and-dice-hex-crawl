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

`RasterImageInspector` checks file signatures and the relevant deterministic image header rather than trusting the browser MIME type or filename. It extracts source pixel width and height without a heavyweight server-side image-processing dependency. Those dimensions are persisted as source-map metadata because registration maps source pixel coordinates into overworld coordinates.

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
PUT    /api/overworlds/{worldId}/source-maps/{sourceMapId}/grid-alignment
GET    /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/alignment-context
GET    /api/overworlds/{worldId}/source-maps/{sourceMapId}/source-archive
DELETE /api/overworlds/{worldId}/source-maps/{sourceMapId}
```

POST is multipart and is the only HTTP path that mints a new map `AssetKey`. Legacy metadata-only HTTP creation/update paths that accepted arbitrary asset keys are not exposed.

Hosted clients use the normal Embedded Module upstream prefix, so the same requests become `/tool-host/{slug}/api/upstream/api/...`. Standalone clients use `/api/...` directly. Assets are never base64-wrapped in JSON and the browser never addresses the backend container hostname.

## Affine registration

A saved registration is a `MapRegistrationTransform` mapping source image pixels to overworld coordinates. Existing projective-transform domain support remains intact for future photographed/scanned/perspective-distorted sources; this UI creates affine transforms only.

Advanced registration collects exactly three source/world control-point pairs. For each pair the DM selects a landmark pixel in the source preview and then the corresponding point on the overworld Canvas. While registration mode is active, its explicit map-click interceptor consumes Canvas clicks before location/feature placement can see them.

`AffineRegistrationSolver` solves the six affine coefficients deterministically from the three pairs. It rejects duplicate points, collinear source points, degenerate output bases, and non-finite values. The browser runs the same deterministic math for preview; the server independently solves and validates the submitted control points before persistence, so client matrix coefficients are not trusted.

After a valid transform is solved, coverage is derived from these source corners:

```text
(0, 0)
(width, 0)
(width, height)
(0, height)
```

The four transformed points become `WorldCoverageBoundary`. The user does not author a second coverage polygon.

Manual affine registration is the fallback for rasters whose baked grid can not be detected reliably and for maps whose absolute placement must be tied to already-authored world landmarks.

## Raster hex-lattice detection and repair

Baked hex-grid alignment is a generic raster capability. It is not a Wonderdraft-specific feature.

The browser decodes the ownership-scoped raster, analyzes a bounded-resolution grayscale copy, and fits the global repeated lattice. The detector uses image gradients, three approximately 60-degree-separated line families, Hough-style projection profiles, periodic carrier-line fitting, harmonic checks, phase fitting, competing-fit separation, spatial coverage, and distant-region residual checks. Measurements are converted back to original raster pixels before they are used for registration.

A fit contains at least:

- flat-top or pointy-top orientation;
- raster rotation;
- center-to-center spacing in source pixels;
- lattice phase/anchor;
- confidence;
- distant residual error;
- support coverage and competing-fit evidence.

The fit is preview-only until the DM applies it. Preview renders both the proposed raster transform and the proposed Hex Crawl mathematical grid. Low-confidence, locally supported, ambiguous, or gridless results do not enable automatic Apply. `ContainsBakedGrid` is workflow metadata, not proof that a detected fit is valid.

For an unplaced raster with no separate physical-scale source, the detector aligns one raster lattice step to one geometric Hex Crawl neighbor step while preserving the world's existing `NeighborCenterDistance`. No miles or kilometers are invented from image geometry alone.

For an already placed raster whose saved transform is a uniform similarity transform, repair can keep that raster placement and derive the mathematical grid orientation, radius, and phase from the detected lattice. If the saved transform contains inappropriate shear or nonuniform scale, repair proposes a similarity transform that preserves the raster center and removes the distortion before fitting the grid.

Applying a repair is one optimistic-concurrency operation. It replaces the selected raster registration and world grid geometry together, preserves grid identity, recomputes the raster coverage polygon, and leaves locations, features, imported source records, source archive, provenance, and unrelated reference maps intact. The preview warns that those existing semantic objects and unrelated maps retain their current world coordinates when the grid geometry changes.

Existing expedition safety remains authoritative. If an overworld already has an expedition and the proposal would change grid geometry, the repair is rejected before any partial map mutation is saved.

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
3. inspect the project and establish whether project/raster dimensions prove an exact or proportional coordinate relationship;
4. preserve labels, symbols, paths, territories, source-grid metadata, scale metadata, and the opaque project archive without requiring semantic classification;
5. if the raster contains a baked hex grid, use the generic raster detector for orientation, spacing, rotation, and phase;
6. use trustworthy Wonderdraft physical scale independently when available;
7. preview any grid/raster and physical-distance changes before persistence;
8. review only optional semantic promotions and genuine exceptions.

The native mutation endpoint is:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/source
```

The request resubmits the Wonderdraft project and the expected overworld version. The server re-parses all source records and replaces the source-derived content attached to that source-map representation in one optimistic-concurrency save. A SHA-256 source fingerprint, source type, import time, and source-record count are retained as generic import provenance. Re-importing therefore replaces the retained source layer instead of appending another copy of every decorative record.

Source-derived content is intentionally separate from `Location` and `SpatialFeature`. A tree, mountain icon, map title, credit label, or unknown path remains cartographic source information unless the DM later promotes it to semantic world truth. This prevents large Wonderdraft projects from creating thousands of false POIs.

Lossless preservation and interpreted content are separate layers. The original uploaded project is stored byte-for-byte as an opaque source archive asset owned by the source-map representation. Its provider-relative storage key is not exposed through the detail contract. The archive survives restart, is replaced on a changed re-import, is reused for an identical re-import, and is deleted with the owning source map. This preserves unknown top-level fields, embedded paint data, boxes, windroses, future-version fields, and any other data that the current parser does not yet interpret.

The parsed operational layer stores generic source-map content kinds—label, symbol, line, and region—with a source record key, display name, descriptor, source geometry, scalar/nested source properties, and generic provenance. Wonderdraft-specific parsing and translation remain in the import boundary; universal world objects do not gain Wonderdraft-specific fields. Future Wonderdraft presentation adapters can re-read the opaque archive when richer format-specific rendering is needed instead of relying on a lossy reconstruction.

### Project-to-raster relationship

Structured-source coordinates and raster-grid geometry are deliberately independent questions.

For a Wonderdraft export, `WonderdraftRasterRelationship` compares the project canvas dimensions to the raster dimensions before any source coordinate is mapped. An exact match is accepted directly. A differently sized export is accepted only when one uniform scale explains both dimensions within a small integer-export rounding tolerance.

When that relationship is trustworthy, project positions are converted with the single verified project-to-raster scale:

```text
Wonderdraft project point
    -> uniform project-to-raster scale
raster pixel
    -> saved/proposed raster registration
world point
    -> viewport/canvas transform
screen point
```

The importer does not independently multiply X and Y by different factors to force a fit. If the dimensions are not consistent with a proportional export, the relationship is `UnresolvedDimensionMismatch`. Dimensions alone can not distinguish cropping, padding, or nonuniform stretching, so the import reports the mismatch and does not save misleading source-to-raster coordinates.

Browser viewport size, CSS image fitting, canvas backing dimensions, device-pixel ratio, browser zoom, pan, and viewport zoom are downstream presentation transforms and do not alter source registration.

If the selected raster already has a saved world registration, same-project Wonderdraft re-import preserves it. Re-import is a content refresh and is not represented as alignment repair. A bad saved registration must be replaced through the explicit raster-grid repair or advanced-registration workflow.

### Physical scale and initial placement

Wonderdraft scale-bar metadata is separate from Wonderdraft grid metadata and separate from raster lattice confidence.

A typed physical scale is used only when the stored scale metadata clearly identifies a supported unit label, distance per segment, segment count, and pixel length. The current physical conversion recognizes miles and kilometers.

For a baked-grid raster with a trustworthy project/raster relationship, the detected raster center spacing can be multiplied by Wonderdraft physical distance per raster pixel to derive physical distance per detected hex-center step. The raster remains authoritative for lattice geometry. If this derived distance materially changes the world's configured `NeighborCenterDistance`, the preview reports the old and proposed values and requires explicit confirmation before Apply.

If no trustworthy physical scale is available, geometric alignment still works. The world's existing physical neighbor distance remains authoritative.

For a gridless raster, physical scale can still establish world-units-per-raster-pixel. If the world has no semantic locations/features and no other placed source map, the importer may center that unanchored gridless raster on the existing grid origin as an initial placement convention. If existing world content already anchors translation or rotation, physical scale alone is not sufficient proof of placement and the importer leaves the raster unplaced for advanced registration.

### Wonderdraft grid metadata

The importer preserves Wonderdraft grid metadata generically instead of hardcoding undocumented numeric pattern meanings.

The current baked-grid workflow does not derive lattice orientation or phase from Wonderdraft `pattern`, offsets, or similar opaque fields. It detects those properties from raster evidence. `grid.size`, when present, is treated as an opaque candidate size and compared with detected center spacing after applying the verified project-to-raster scale. Agreement is useful fixture-specific corroboration; disagreement is reported. The metadata does not override raster geometry.

This distinction is intentional. The Humblewood fixture supplies direct evidence that its native `grid.size = 80` agrees with an approximately 80-pixel baked center spacing at 1:1 export scale. That evidence does not establish a universal semantic rule for every Wonderdraft version or pattern.

### Optional semantic promotion

After a placed source import, the existing candidate-preview endpoint remains available for semantic refinement:

```text
POST /api/overworlds/{worldId}/source-maps/{sourceMapId}/wonderdraft/candidates
```

The server maps trusted project geometry through the selected raster relationship and saved/derived world registration. Preview geometry remains server-authoritative.

The browser review provides:

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

- direct metadata-only reconstruction of Wonderdraft grid orientation/phase without raster evidence, if native field semantics are independently verified;
- automatic reconstruction of cropped or padded project-to-raster transforms when dimensions alone are insufficient;
- automatic reconciliation/deduplication of semantic objects that were promoted from an older revision of the same source;
- automatic association of nearby labels and settlement markers unless an explicit Wonderdraft relationship or sufficiently auditable inference is available;
- four-point/projective registration UI for perspective-distorted sources;
- automatic overlapping-map or unrelated-feature registration;
- GM/player image differencing;
- icon recognition beyond source metadata;
- road, trail, river, terrain, or other semantic interpretation not explicitly encoded by the source;
- OCR or AI/ML interpretation of raster-only information such as printed scale bars;
- final player-facing source-map presentation policy.

These are not required for lossless native source import or raster-driven hex-grid alignment. They can build on persisted source content, provenance, source/raster coordinate linkage, and server-authoritative world registration.
