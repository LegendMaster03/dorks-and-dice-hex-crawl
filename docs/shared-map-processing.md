# Shared map processing

Phase 13 moves reusable image analysis out of the Hex Crawl browser and into Dorks & Dice Surveyor. The architectural boundary is intentionally asymmetric: Surveyor observes image bytes and returns bounded analysis results; Hex Crawl remains authoritative for maps, grids, alignment, procedures, expeditions, and all persisted world state.

## Request path and authority

Automatic visible-grid analysis uses this path:

```text
Hex Crawl browser
    -> user clicks Detect / repair hex grid
    -> Hex Crawl ownership-scoped source-map API
    -> Hex Crawl server opens the authoritative map image
    -> Surveyor POST /v2/periodic-tiling/detect
       expectedDsSymbol=<1:1,1,1:6,3>
    -> Hex Crawl validates/interprets the observation
    -> browser builds a transient Hex Crawl-owned alignment proposal
    -> when Surveyor returned a usable fit, the same user action PUTs grid-alignment
```

There is no second user-facing Preview / Apply / Confirm sequence. The analysis request remains non-mutating, and the `grid-alignment` request remains the explicit persistence boundary in the architecture. The browser simply performs both parts behind the single Detect / repair action.

Surveyor's v2 API is capability-based rather than hex-specific.
Hex Crawl currently passes the optional expectedDsSymbol hint for the
hexagonal grid (`<1:1,1,1:6,3>`) to prioritize detection. Surveyor
independently evaluates the image and returns the observed canonical
Delaney-Dress symbol in `tiling.dsSymbol` when an identification is
supported; inconclusive and gridless responses have `tiling: null`.
The expected hint never dictates the observed identity. Hex Crawl retains
authority over its world grid and will not apply a detected tiling that
its current hexagonal geometry and movement implementation cannot support.


The browser never calls Surveyor directly and does not receive the Surveyor service credential. Surveyor does not fetch URLs or resolve Hex Crawl asset keys. Hex Crawl loads the image through `IMapAssetStore` after normal world/source-map authorization and streams the encoded bytes to Surveyor.

Surveyor results are observations rather than commands. A successful `grid-analysis` request does not change the overworld version, mathematical grid, source-map alignment, or any expedition state. Hex Crawl owns the later persistence request under optimistic concurrency.

Hex Crawl also retains all consumer-specific interpretation: physical distance, Wonderdraft scale and grid cross-checks, alignment proposals, map rendering, and final persistence. Surveyor contains no Hex Crawl domain or persistence model.

## Source-image coordinate contract

Surveyor returns detected spacing, anchor, and residual in source-image pixel coordinates. Internally it may analyze a bounded-resolution image, but it normalizes detector output back to the original source image before returning it. `analysis.sourceResolutionVerified` remains useful diagnostic metadata.

The one-click product workflow deliberately does not expose or require the user to adjudicate confidence, residual, or source-resolution verification. If Surveyor returns a usable non-gridless fit, Hex Crawl applies that fit. If Surveyor returns no fit or classifies the image as gridless, nothing is persisted and Manual placement remains available. This preserves the explicit one-button workflow while keeping no-result failures non-mutating.

## Map images and map sets

A reference map is one uploaded source image. A **map set** is a collection of alternate versions of the same underlying map/geographic extent, such as GM/player or versions with and without a printed grid. Neighboring regional maps belong in different map sets even though they share the same overworld coordinate space.

Unaligned map images are still rendered using a temporary centered placement so upload success never looks like disappearance. That temporary placement is presentation-only and is never persisted as authoritative alignment.

The current **Map view** selector is a presentation convenience over whole source images. It can coordinate GM/player versions across every map set while shared and auxiliary references retain independent visibility. Whole images are not persisted as the final layer model. Future image-comparison work may derive a common base plus true difference layers from the aligned source evidence.

Different geographic extents are manually placed into the shared overworld for now. Future Surveyor landmark, road, or other feature matching may automate that process, but Phase 13 does not infer neighboring-region placement.

## Service and resource boundaries

Surveyor is optional at Hex Crawl startup. A missing or unavailable Surveyor does not prevent Hex Crawl from serving worlds, source maps, expeditions, or Manual placement. The integration is configured with `Surveyor:BaseUrl`, `Surveyor:ServiceToken`, and `Surveyor:RequestTimeoutMilliseconds`; the equivalent environment variables use ASP.NET double-underscore notation.

Surveyor separately bounds encoded upload size, decoded pixels, analysis resolution, worker count, queued work, and analysis duration. CPU-heavy lattice detection runs in a bounded worker-thread pool. Cancellation and timeout replace the affected worker so abandoned analysis does not continue consuming CPU indefinitely.

The service-to-service analysis route requires a bearer credential. Surveyor health routes remain unauthenticated for internal monitoring. Image bytes and service tokens are not logged.

## Failure and cancellation behavior

Automatic analysis failure is explicit and non-mutating:

- unavailable or saturated Surveyor -> Hex Crawl `503`;
- rejected internal credential or invalid Surveyor protocol -> Hex Crawl `502`;
- Surveyor/Hex Crawl analysis timeout -> Hex Crawl `504`;
- browser cancellation aborts the in-flight request without applying a proposal;
- no-fit or gridless results remain non-persistent.

The configured Hex Crawl timeout covers both receipt of Surveyor response headers and consumption/parsing of the response body. Switching source maps or starting a newer analysis invalidates earlier asynchronous work, including later Wonderdraft/physical-scale cross-checks, so stale results can not overwrite the current state or report false success.

Wonderdraft physical-scale lookup is an optional cross-check. Failure to read that optional context does not invalidate an otherwise usable image-grid alignment; Hex Crawl simply preserves the existing physical distance when no usable independent scale is available.

Reference-map image loading retries bounded transient fetch/decode failures rather than leaving a map permanently blank after one failed request. Terminal failures are diagnosed in the browser console and retries are canceled when the cache entry is pruned or disposed.

Retrying analysis is safe because the observation request has no persistence side effect before the final version-checked alignment mutation. Manual placement remains available when automatic analysis can not produce a usable fit.

## Scope

Phase 13 extracts generic image/grid computation only. It does not add terrain, road, river, icon, or semantic feature recognition; OCR; arbitrary URL fetching; AI/ML inference; battle-map ownership; arbitrary-tiling recognition beyond the current three Regular families; or automatic neighboring-map alignment. Those concerns require separate capability and authority decisions rather than being inferred from periodic-grid analysis.
