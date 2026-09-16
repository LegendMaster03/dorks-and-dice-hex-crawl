# Hex Crawl architecture

## Boundaries

Hex Crawl maintains independent state axes rather than turning a rendered hex into the unit of all data:

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, and source-map representations.
2. **Crawl/runtime state** — `ExpeditionState` and `WorldRuntimeState`; movement records physical distance and abstract traversal progress without mutating base geography.
3. **Player knowledge** — `PlayerKnowledgeState` records subject-specific knowledge. There is deliberately no `Hex.IsRevealed` flag.
4. **Presentation policy** — `MapPresentationPolicy` and the Canvas renderer decide how authoritative data is presented.
5. **Procedure configuration** — `CrawlProcedureProfile` defines crawl procedure behavior independently of world geometry.

Database and binary-storage concerns do not enter the core spatial/runtime records.

## Project structure

- `HexCrawl.Domain` owns spatial, world, knowledge, source-map registration math, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, persistence/blob ports, optimistic concurrency, and procedure selection.
- `HexCrawl.Infrastructure` implements SQLite persistence, filesystem map assets, and Tool Host authentication redemption.
- `HexCrawl.Web` owns HTTP contracts, authentication middleware, route hosting, and the TypeScript application.

## Continuous overworld and semantic geometry

An `OverworldDefinition` is one continuous world coordinate space. The hex grid remains mathematical; creating an overworld does not pre-populate stored hex rows. Only authored semantic data, source representations, and runtime state are persisted.

The grid supports pointy-top and flat-top orientation, axial `q,r` coordinates, configurable origin and rotation, world-space radius, and configurable physical center-to-center scale and units. Locations may occupy any `WorldPoint`; they do not need to be at hex centers.

Point, line, and region features are stored as real world-space geometry. Categories are strings rather than an exhaustive enum, allowing terrain, roads, trails, rivers, borders, and campaign-specific semantics without coupling world truth to raster pixels.

`OverworldDefinition.FeaturesIntersecting()` remains the deterministic spatial-query boundary for feature/hex intersection.

## Persistence and asset architecture

SQLite remains behind `IHexCrawlStore`. Production uses `Data Source=/data/hex-crawl.db` on the existing `hex-crawl-data` volume. Forward schema migrations are tracked in `schema_migrations`; startup does not destructively recreate production data.

`overworlds` stores owner identity, stable ID, aggregate version/timestamps, and a serialized world snapshot containing grid, semantic geometry, locations, and source-map metadata. Binary raster data is not stored in SQLite.

Map assets are behind `IMapAssetStore`. The initial production implementation is filesystem-backed at `/data/assets` on the same durable volume. `SourceMapRepresentation` stores only a provider-relative opaque `AssetKey`, so replacing filesystem storage with object storage, NAS storage, or another provider does not change domain/world truth. Generated keys do not depend on user filenames, writes use temporary-file-plus-rename semantics, and asset paths are never exposed through HTTP contracts.

The production layout is conceptually:

```text
/data/hex-crawl.db
/data/assets/maps/...
```

See `docs/source-map-import.md` for upload compensation, limits, format validation, and deletion behavior.

### Expedition storage

`expeditions` stores authoritative runtime snapshots including traversal, navigation/lost state, distance/time, active watches, player knowledge, pause state, and the full selected procedure profile. Runtime history is stored separately in `expedition_events`; this is not event sourcing.

## Ownership and authentication

Persistent APIs require a stable identity. Hosted requests use the Dorks & Dice Tool Host ticket/introspection contract; standalone development can enable the explicit configured identity and it is disabled by default.

Every overworld has an `OwnerUserId`. Enumeration, direct loads, source-map mutation, source binary retrieval, expedition creation, and expedition mutation are scoped to that owner. There is deliberately no general asset-by-key HTTP endpoint.

Hosted frontend traffic, including streamed multipart map uploads and binary map downloads, uses `/tool-host/{slug}/api/upstream/...`; standalone mode uses local `/api/...` paths. The browser never needs direct backend-container access.

## Optimistic concurrency and mutation safety

Worlds and expeditions have monotonically increasing aggregate versions. Mutations carry `ExpectedVersion`; stale writes return HTTP 409 instead of silently overwriting a newer tab's work.

Grid changes remain conservatively blocked once expeditions exist. Locations and semantic features preserve stable IDs and retain the existing conservative runtime-reference deletion rules. Source-map representations are presentation/evidence, not semantic world objects, so their deletion is allowed independently of existing expeditions and does not delete semantic geography.

## API contracts

The Web layer exposes resource DTOs rather than persistence rows:

- `GET/POST /api/overworlds`
- `GET/PUT /api/overworlds/{worldId}`
- location CRUD under `/api/overworlds/{worldId}/locations`
- feature CRUD under `/api/overworlds/{worldId}/features`
- `GET/POST /api/overworlds/{worldId}/source-maps`
- `PUT/DELETE /api/overworlds/{worldId}/source-maps/{sourceMapId}`
- `PUT /api/overworlds/{worldId}/source-maps/{sourceMapId}/registration`
- `GET /api/overworlds/{worldId}/source-maps/{sourceMapId}/asset`
- `GET /api/runtime/profiles`
- expedition list/start/load/advance/discovery routes.

Only multipart source-map upload creates new asset keys. Clients can not bind an arbitrary provider key through an HTTP metadata contract.

## Frontend, routing, and raster rendering

Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation goes through `RenderLifecycle`; `MutationObserver` is not used. `ResizeObserver` remains limited to the external layout boundary required for Canvas sizing.

Tool-relative routes remain `/worlds`, `/worlds/{worldId}`, `/worlds/{worldId}/edit`, and `/worlds/{worldId}/expeditions/{expeditionId}`. Standalone deep routes receive the application shell; Embedded Module routes remain relative to the Tool Host base path.

The world editor supports semantic authoring plus raster source import. Source maps may be grouped by geography, classified GM/Player/Neutral/Other, marked as baked-grid/gridless, shown or hidden ephemerally, and registered/re-registered with three source/world control-point pairs.

`AffineRegistrationSolver` maps source pixels into world coordinates. The server derives the world coverage polygon from the four image corners. Existing projective-transform domain support remains for a later four-point/perspective UI.

Registered source images render beneath semantic regions and the mathematical grid. `RasterImageCache` loads ownership-scoped images asynchronously, reuses decoded images, requests explicit rerenders on readiness, and releases `ImageBitmap`/fallback URL resources during pruning or route cleanup. The same renderer is used by expedition runtime.

## Source-map import boundary

A `SourceMapRepresentation` is evidence/presentation for part of one continuous overworld, never an atlas page and never authoritative terrain. Alternate Bellowing Wilds representations can share one `GeographyKey`; Kylandria can use another key while overlapping/connecting in the same world coordinate system.

PNG, JPEG, and WebP are supported in the initial importer. File size, maximum dimension, and maximum pixel count are configurable. The default Hex Crawl file ceiling is 100 MiB, intentionally below the Tool Host upstream ceiling.

Automatic grid detection, image feature matching, overlapping-map registration, differencing, icon recognition, road/river extraction, terrain segmentation, OCR, and AI/ML interpretation remain separate future analysis systems. No placeholder analysis action is exposed.

## Deferred work

Still deferred are campaign sharing, real-time collaborative editing, final player-facing source-map selection/presentation policy, automatic map analysis, four-point projective registration UI, arbitrary-bearing runtime travel, multi-hex automatic backtracking, and battle maps.

The filesystem map provider is intentionally replaceable infrastructure. The source-map domain and continuous-overworld model do not require redesign when storage or later image-analysis implementations change.
