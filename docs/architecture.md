# Hex Crawl architecture

## Boundaries

Hex Crawl maintains independent state axes rather than turning a rendered hex into the unit of all data:

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, and source-map representations.
2. **Crawl/session state** — every persisted crawl is a session with an explicit `CrawlSessionContext`: `WorldBound(overworldId)`, `AbstractHex(name, orientation, runtime scale)`, or `NonSpatial(name)`. Spatial sessions use `ExpeditionState`; non-spatial sessions use `NonSpatialSessionState`. The deterministic crawl engine needs physical hex scale and procedure/runtime state but no `OverworldDefinition` or renderer.
3. **Player knowledge** — `PlayerKnowledgeState` records subject-specific knowledge, known/explored hexes, annotations, and the party-specific presentation-policy snapshot. There is deliberately no `Hex.IsRevealed` flag.
4. **Presentation policy** — `MapPresentationPolicy` and presentation projections decide what knowledge changes may happen automatically and how authoritative data is presented.
5. **Procedure configuration** — `CrawlProcedureProfile` defines crawl procedure behavior independently of world geometry.

Database and binary-storage concerns do not enter the core spatial/runtime records.

## Project structure

- `HexCrawl.Domain` owns spatial, world, knowledge, presentation policy/projection, source-map registration math, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, persistence/blob ports, optimistic concurrency, procedure/presentation selection, world/knowledge composition around the map-independent runtime, the full expedition workbench, and focused assistant orchestration.
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

### Crawl-session storage

The existing `expeditions` table remains the persistence envelope for compatibility, but its aggregate is now a crawl session rather than an inherently world-bound expedition. Schema version 2 adds required `context_json`, makes `overworld_id` nullable, and makes `knowledge_json` nullable.

The three context forms are intentionally distinct:

- `WorldBound` stores the real Overworld ID. Its nullable `overworld_id` column is populated as an indexed foreign-key projection, and player knowledge/presentation may be persisted.
- `AbstractHex` stores its own context name, orientation, and `CrawlRuntimeContext` scale/unit. It has no Overworld row, no Overworld foreign key, and no player-knowledge snapshot.
- `NonSpatial` stores only non-spatial session context plus procedure/runtime/history state. It has no hex coordinates, distance scale, world position, Overworld, or player-knowledge snapshot.

Existing schema-v1 expedition rows migrate to explicit `WorldBound` contexts; no placeholder worlds or magic IDs are introduced. Runtime history remains in `expedition_events`; persistence is snapshot + retained history, not event sourcing.

Procedure and presentation catalogs are creation-time presets. Ongoing sessions reload their persisted snapshots rather than reconstructing behavior from current catalog definitions.

See `docs/dm-expedition-workbench.md` for the detailed bookkeeping ownership model, guided watch workflow, presentation presets, provenance, and restart behavior.

## Ownership and authentication

Persistent APIs require a stable identity. Hosted requests use the Dorks & Dice Tool Host ticket/introspection contract; standalone development can enable the explicit configured identity and it is disabled by default.

Every overworld has an `OwnerUserId`, and every crawl session has an owner independently of whether it has an Overworld. Enumeration, direct loads, source-map mutation, source binary retrieval, session creation, and session mutation are scoped to that owner. There is deliberately no general asset-by-key HTTP endpoint.

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
- `GET /api/presentation/presets`
- `GET /api/expeditions` for the authenticated user's crawl-session collection, independent of world navigation;
- `POST /api/expeditions` for true standalone `AbstractHex` or `NonSpatial` sessions;
- world-scoped expedition start/list for `WorldBound` sessions plus session load/advance/discovery routes;
- independent focused mutations at `/api/expeditions/{expeditionId}/assistants/travel`, `/watch`, `/navigation`, and `/encounters`;
- auditable procedure input generation at `/api/expeditions/{expeditionId}/resolution-helper`, which returns explicit resolved-value drafts and a server-generated resolution ID while atomically persisting the attempt, roll trace, values, status, history event, and new aggregate version; it does not apply the generated result or mutate mechanical crawl state. `/advance` accepts `AutomaticRoll` only when that ID resolves to an available generated attempt for the same session/current version/watch and the submitted component values match the persisted generated values.

World-bound creation accepts a procedure preset key, a presentation preset key, and an optional complete procedure snapshot. Standalone creation accepts either an `AbstractHex` context (name, orientation, physical center distance/unit, starting hex) or a `NonSpatial` context (name only). The procedure snapshot must retain the selected preset key as provenance and passes the same domain validation as built-in profiles.

Only multipart source-map upload creates new asset keys. Clients can not bind an arbitrary provider key through an HTTP metadata contract.

## Frontend, routing, and raster rendering

Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation goes through `RenderLifecycle`; `MutationObserver` is not used. `ResizeObserver` remains limited to the external layout boundary required for Canvas sizing.

`/` is the DM-tools home rather than a world route. Tool-relative routes separate product composition: `/worlds`, `/worlds/{worldId}`, and `/worlds/{worldId}/edit` own world/map authoring; `/worlds/{worldId}/expeditions/{expeditionId}` is the full crawl workbench that composes expedition state with a rendered map; `/expeditions/{expeditionId}` is the mapless tracker; and `/expeditions/{expeditionId}/travel`, `/navigation`, and `/encounters` remain session-attached focused assistants. First-class entry routes `/assistants/travel`, `/assistants/navigation`, and `/assistants/encounters` let a DM enter a focused workflow without visiting world or general-session setup first. Standalone deep routes receive the application shell; Embedded Module routes remain relative to the Tool Host base path.

The world editor supports semantic authoring plus raster source import and expedition creation. Expedition setup uses progressive disclosure: choose procedure and presentation presets first, then optionally customize the procedure snapshot, with progress factors under advanced controls.

Direct assistant entry uses the same persisted session model rather than a second temporary calculation model. Travel / Watch defaults to inline creation of a `NonSpatial` session and offers `AbstractHex` only when the DM explicitly chooses spatial travel. Encounter Cadence creates a minimal `NonSpatial` procedure session. Navigation lists existing spatial sessions or creates a minimal `AbstractHex` session inline. These entry screens list compatible saved sessions through session summaries and do not fetch or create an Overworld.

Session UI is composed around authoritative persisted session state rather than around the map. An `AbstractHex` tracker derives day/watch status, current hex, entry/course, lost/veer state, elapsed/remaining time, procedure-specific progress, pending decisions, provenance, and recent history without loading an Overworld or constructing `MapSurface`. A `NonSpatial` tracker exposes only non-spatial procedure/time/history state and applicable assistants. The full crawl workbench exists only for `WorldBound` sessions and alone loads the Overworld, map rendering, discovery controls, and player-knowledge presentation. Focused assistant routes render dedicated travel/watch, navigation, or encounter-cadence forms and call independent assistant mutations; travel/navigation require a spatial context while encounter cadence also supports non-spatial sessions.

Source maps may be grouped by geography, classified GM/Player/Neutral/Other, marked as baked-grid/gridless, shown or hidden ephemerally, and registered/re-registered with three source/world control-point pairs.

`AffineRegistrationSolver` maps source pixels into world coordinates. The server derives the world coverage polygon from the four image corners. Existing projective-transform domain support remains for a later four-point/perspective UI.

Registered source images render beneath semantic regions and the mathematical grid. `RasterImageCache` loads ownership-scoped images asynchronously, reuses decoded images, requests explicit rerenders on readiness, and releases `ImageBitmap`/fallback URL resources during pruning or route cleanup. The same renderer is used by expedition runtime. GM source-map rasters remain DM evidence and are not treated as player knowledge merely because a presentation policy is open.

## Source-map import boundary

A `SourceMapRepresentation` is evidence/presentation for part of one continuous overworld, never an atlas page and never authoritative terrain. Alternate Bellowing Wilds representations can share one `GeographyKey`; Kylandria can use another key while overlapping/connecting in the same world coordinate system.

PNG, JPEG, and WebP are supported in the initial importer. File size, maximum dimension, and maximum pixel count are configurable. The default Hex Crawl file ceiling is 100 MiB, intentionally below the Tool Host upstream ceiling.

Automatic grid detection, image feature matching, overlapping-map registration, differencing, icon recognition, road/river extraction, terrain segmentation, OCR, and AI/ML interpretation remain separate future analysis systems. No placeholder analysis action is exposed.

## Deferred work

Still deferred are campaign sharing, real-time collaborative editing, a dedicated player delivery/session surface for the persisted presentation state, automatic map analysis, four-point projective registration UI, arbitrary-bearing runtime travel, multi-hex automatic backtracking, a general campaign calendar/rest clock, and battle maps.

Optional procedure-resolution helpers now occupy the same resolved-input side of the boundary: they can use procedure-snapshot definitions and DM-confirmed situational values to generate explicit inputs. Generation owns only its audit record and optimistic-concurrency version increment; it does not apply or mutate mechanical crawl state. The deterministic runtime/application operation still applies those values. Rules Core/Characters integration remains optional future resolved-input plumbing; those systems do not become owners of Hex Crawl spatial/runtime state.

The filesystem map provider is intentionally replaceable infrastructure. The source-map domain, continuous-overworld model, procedure snapshots, and presentation snapshots do not require redesign when storage or later analysis/integration implementations change.
