# Shared map processing

Phase 13 moves reusable raster analysis out of the Hex Crawl browser and into Dorks & Dice Surveyor. The architectural boundary is intentionally asymmetric: Surveyor observes image bytes and returns bounded analysis results; Hex Crawl remains authoritative for maps, grids, registrations, procedures, expeditions, and all persisted world state.

## Request path and authority

Automatic baked-grid analysis uses this path:

```text
Hex Crawl browser
    -> Hex Crawl ownership-scoped source-map API
    -> Hex Crawl server opens the authoritative raster asset
    -> Surveyor POST /v1/hex-grid/detect
    -> Hex Crawl validates/interprets the observation
    -> browser previews a Hex Crawl-owned proposal
    -> explicit Apply mutates Hex Crawl world state
```

The browser never calls Surveyor directly and does not receive the Surveyor service credential. Surveyor does not fetch URLs or resolve Hex Crawl asset keys. Hex Crawl loads the raster through `IMapAssetStore` after normal world/source-map authorization and streams the encoded bytes to Surveyor.

Surveyor results are observations rather than commands. A successful `grid-analysis` request does not change the overworld version, mathematical grid, source-map registration, or any expedition state. The existing `grid-alignment` mutation remains the explicit persistence boundary and continues to use Hex Crawl optimistic concurrency.

Hex Crawl also retains all consumer-specific interpretation: physical distance, Wonderdraft scale and grid cross-checks, registration proposals, preview rendering, warnings, confirmation policy, and final Apply behavior. Surveyor contains no Hex Crawl domain or persistence model.

## Source-image coordinate contract

Surveyor returns detected spacing, anchor, and residual in source-image pixel coordinates. Internally it may analyze a bounded-resolution image, but it normalizes detector output back to the original source raster before returning it. `analysis.sourceResolutionVerified` states whether the final phase was verified at source resolution.

Automatic Apply remains gated on source-resolution verification and the existing Hex Crawl confidence/residual policy. A downscaled-only fit can be previewed as inconclusive evidence but can not silently become durable grid truth.

## Service and resource boundaries

Surveyor is optional at Hex Crawl startup. A missing or unavailable Surveyor does not prevent Hex Crawl from serving worlds, source maps, expeditions, or manual registration. The integration is configured with `Surveyor:BaseUrl`, `Surveyor:ServiceToken`, and `Surveyor:RequestTimeoutMilliseconds`; the equivalent environment variables use ASP.NET double-underscore notation.

Surveyor separately bounds encoded upload size, decoded pixels, analysis resolution, worker count, queued work, and analysis duration. CPU-heavy lattice detection runs in a bounded worker-thread pool. Cancellation and timeout replace the affected worker so abandoned analysis does not continue consuming CPU indefinitely.

The service-to-service analysis route requires a bearer credential. Surveyor health routes remain unauthenticated for internal monitoring. Raster bytes and service tokens are not logged.

## Failure behavior

Automatic analysis failure is explicit and non-mutating:

- unavailable or saturated Surveyor -> Hex Crawl `503`;
- rejected internal credential or invalid Surveyor protocol -> Hex Crawl `502`;
- Surveyor/Hex Crawl analysis timeout -> Hex Crawl `504`;
- browser cancellation aborts the in-flight request without applying a proposal.

Retrying analysis is safe because observation has no persistence side effect. Manual advanced registration remains available when automatic analysis can not complete.

## Scope

Phase 13 extracts generic image/grid computation only. It does not add terrain, road, river, icon, or semantic feature recognition; OCR; arbitrary URL fetching; AI/ML inference; battle-map ownership; or automatic mutation of Hex Crawl domain truth. Those concerns require separate design and authority decisions rather than being inferred from raster analysis.
