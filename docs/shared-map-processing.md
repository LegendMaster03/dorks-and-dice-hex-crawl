# Shared map processing

Phase 13 moves reusable image analysis out of the Hex Crawl browser and into Dorks & Dice Surveyor. The architectural boundary is intentionally asymmetric: Surveyor observes image bytes and returns bounded analysis results; Hex Crawl remains authoritative for maps, grids, alignment, procedures, expeditions, and all persisted world state.

## Request path and authority

Automatic visible-grid analysis uses this path:

```text
Hex Crawl browser
    -> user clicks Detect / repair hex grid
    -> Hex Crawl ownership-scoped source-map API
    -> Hex Crawl server opens the authoritative map image
    -> Surveyor POST /v1/periodic-tiling/detect
       crNotation=6^3
    -> Hex Crawl validates/interprets the observation
    -> browser builds a transient Hex Crawl-owned alignment proposal
    -> Hex Crawl automatic-apply safety policy accepts or rejects the proposal
    -> if accepted, the same user action PUTs grid-alignment
```

There is no second user-facing Preview / Apply / Confirm sequence. The analysis request remains non-mutating, and the `grid-alignment` request remains the explicit persistence boundary in the architecture. The browser simply performs both parts of the trusted workflow behind the single Detect / repair action.

Surveyor's API is capability-based rather than hex-specific. Known-tiling detection is one operation; future tiling recognition and unrelated computer-vision operations belong on separate endpoints. Hex Crawl requests `6^3` directly by Cundy-Rollett notation. Surveyor derives the periodic-tiling classification from that notation and returns the normalized Cundy-Rollett (`crNotation`), GomJau-Hogg (`gjhNotation`), and `periodicTilingType` identities. Hex Crawl validates all three response fields before accepting the observation. A separate request-side tiling type, shape name, or side-count shorthand is not part of the public service contract.

The browser never calls Surveyor directly and does not receive the Surveyor service credential. Surveyor does not fetch URLs or resolve Hex Crawl asset keys. Hex Crawl loads the image through `IMapAssetStore` after normal world/source-map authorization and streams the encoded bytes to Surveyor.

Surveyor results are observations rather than commands. A successful `grid-analysis` request does not change the overworld version, mathematical grid, source-map alignment, or any expedition state. Hex Crawl owns the later decision to persist an accepted proposal under optimistic concurrency.

Hex Crawl also retains all consumer-specific interpretation: physical distance, Wonderdraft scale and grid cross-checks, alignment proposals, automatic-apply safety policy, map rendering, and final persistence. Surveyor contains no Hex Crawl domain or persistence model.

## Source-image coordinate contract

Surveyor returns detected spacing, anchor, and residual in source-image pixel coordinates. Internally it may analyze a bounded-resolution image, but it normalizes detector output back to the original source image before returning it. `analysis.sourceResolutionVerified` states whether the final phase was verified at source resolution.

Automatic persistence remains gated on source-resolution verification and the existing Hex Crawl canonical residual policy. A downscaled-only, inconclusive, gridless, or otherwise non-canonical fit does not silently become durable grid truth. In the one-step UI that condition is reported as a failed automatic alignment, while the map image remains visible and Manual placement remains available.

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
- a result that fails Hex Crawl's automatic-apply trust policy remains non-persistent.

The configured Hex Crawl timeout covers both receipt of Surveyor response headers and consumption/parsing of the response body. Switching source maps or starting a newer analysis invalidates earlier asynchronous work, including later Wonderdraft/physical-scale cross-checks, so stale results can not overwrite the current state or report false success.

Wonderdraft physical-scale lookup is an optional cross-check. Failure to read that optional context does not invalidate an otherwise trustworthy image-grid alignment; Hex Crawl simply preserves the existing physical distance when no usable independent scale is available.

Retrying analysis is safe because the observation request has no persistence side effect. Manual placement remains available when automatic analysis can not complete or can not be trusted strongly enough for one-step persistence.

## Scope

Phase 13 extracts generic image/grid computation only. It does not add terrain, road, river, icon, or semantic feature recognition; OCR; arbitrary URL fetching; AI/ML inference; battle-map ownership; automatic tiling recognition; or automatic neighboring-map alignment. Those concerns require separate capability and authority decisions rather than being inferred from periodic-grid analysis.
