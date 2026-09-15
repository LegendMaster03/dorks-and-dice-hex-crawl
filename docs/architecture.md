# Hex Crawl architecture

## Boundaries

Hex Crawl maintains independent state axes rather than turning a rendered hex into the unit of all data:

1. **World/spatial truth** — `OverworldDefinition`, mathematical grid, semantic point/line/region features, locations, and source-map representations.
2. **Crawl/runtime state** — `ExpeditionState` and `WorldRuntimeState`; movement records physical distance and abstract traversal progress without mutating base geography.
3. **Player knowledge** — `PlayerKnowledgeState` records subject-specific knowledge. There is deliberately no `Hex.IsRevealed` flag.
4. **Presentation policy** — `MapPresentationPolicy` and the Canvas renderer decide how authoritative data is presented.
5. **Procedure configuration** — `CrawlProcedureProfile` defines crawl procedure behavior independently of world geometry. The Alexandrian advanced profile is one preset rather than a mandatory ruleset.

The persistence cycle preserves these boundaries. Database concerns do not enter the core spatial/runtime records.

## Project structure

The solution now has four production projects:

- `HexCrawl.Domain` owns spatial, world, knowledge, and deterministic runtime rules.
- `HexCrawl.Application` owns authenticated use cases, cross-aggregate validation, persistence ports, optimistic-version requirements, and procedure selection.
- `HexCrawl.Infrastructure` implements the application ports for SQLite persistence and Tool Host authentication redemption.
- `HexCrawl.Web` owns HTTP contracts, authentication middleware, route hosting, and the TypeScript application.

The application layer was introduced because persistent CRUD, authorization, expedition load/save, and validation across world and expedition aggregates now have real responsibilities that do not belong in either HTTP endpoints or domain types.

## Continuous overworld and semantic geometry

An `OverworldDefinition` is one continuous world coordinate space. The hex grid remains mathematical; creating an overworld does not pre-populate stored hex rows. Only authored semantic data and runtime state are persisted.

The grid supports pointy-top and flat-top orientation, axial `q,r` coordinates, configurable origin and rotation, world-space radius, and configurable physical center-to-center scale and units. Locations may occupy any `WorldPoint`; they do not need to be at hex centers.

Point, line, and region features are stored as real world-space geometry. Categories are strings rather than an exhaustive enum. This allows conventional terrain such as forest, swamp, mountain, grassland, and desert while also allowing campaign-specific terrain. The same extensible category model allows roads, trails, rivers, borders, and other routes without prematurely assigning mechanical effects.

`OverworldDefinition.FeaturesIntersecting()` remains the deterministic spatial-query boundary for feature/hex intersection. Future terrain/route resolution should extend tested domain query logic rather than reimplementing geometry in the browser.

## Persistence architecture

The initial persistent provider is **SQLite**, accessed through `Microsoft.Data.Sqlite` behind `IHexCrawlStore`.

SQLite fits the current Hex Crawl deployment because the tool is one service with deployment-owned storage, does not require a separate database server for development or CI, supports transactions and optimistic concurrency, and can be mounted as a durable file in the container deployment. It is not exposed to the domain model, so a later operational need can replace the provider without changing spatial/runtime types.

The default development connection string is `Data Source=hex-crawl.db`. Deployments are expected to override `ConnectionStrings:HexCrawl`; the Docker Compose development configuration uses `/data/hex-crawl.db` on a named volume. Credentials and storage locations remain deployment configuration and are not committed as secrets.

The schema is versioned through the non-destructive `schema_migrations` table. Startup applies missing forward migrations and never drops or recreates existing production data. Tests create real empty SQLite files and apply the migration from zero.

### World storage

`overworlds` stores ownership, stable world ID, name/index metadata, aggregate version/timestamps, and a serialized world snapshot. The snapshot contains the domain grid, semantic geometry, locations, and source-map metadata. A private infrastructure projection handles the polymorphic point/line/region feature representation so database serialization requirements do not leak into `SpatialFeature`.

The snapshot approach is intentional for this first aggregate: a world edit is an aggregate-level operation protected by one version, and no stored hex table is needed. It does not prevent later normalization if query volume warrants it.

### Expedition storage

`expeditions` stores an authoritative runtime snapshot containing traversal, intended/actual direction, lost/veer state, physical distance, elapsed time, completed/active watch state, player knowledge, pause/remainder state, and the full selected `CrawlProcedureProfile` configuration.

The full procedure configuration is persisted, not merely its key. An existing expedition therefore does not silently change if a built-in profile is modified in a later release.

Runtime history is stored separately in `expedition_events`, keyed by expedition ID and event sequence. The current expedition snapshot is authoritative; this is **not event sourcing**. Events are retained for auditability, session history, debugging, and future filtered player projections. Saving uses insert-if-absent semantics on `(expedition_id, sequence)` so reload/resume does not duplicate prior events.

Player knowledge remains a separate logical state object inside the expedition persistence envelope. One expedition currently owns one party-knowledge scope. Discovering a location does not reveal another location or feature in the same hex.

## Ownership and authentication

Persistent APIs require a stable user identity. Hosted requests use the current Dorks & Dice Tool Host ticket/introspection contract; the browser does not provide an authoritative user ID. The backend redeems the one-time ticket and derives `ClaimTypes.NameIdentifier` from the returned Tool Host context.

Every overworld has an `OwnerUserId`. Enumeration filters by that owner, direct loads require that owner, expedition creation requires access to the parent overworld, and expedition load/mutation is owner-scoped and rechecks the associated overworld. A guessed private ID therefore does not grant access.

This is intentionally an ownership model rather than full campaign sharing. A later access policy can expand the authorization decision without changing stable world/expedition identity.

Standalone development can enable `ToolHost:StandaloneIdentity:Enabled` with an explicit configured user ID. It is disabled by default and is not a production authentication substitute.

See `docs/tool-hosting.md` for the exact hosted boundary.

## Optimistic concurrency and mutation safety

Worlds and expeditions have monotonically increasing aggregate versions. Mutations carry `ExpectedVersion`; stale writes return HTTP 409 instead of silently overwriting a newer tab's work.

The first version takes conservative safety rules where persisted runtime references are possible:

- grid geometry can be edited before expeditions exist;
- consequential grid changes are blocked once an expedition exists so persisted spatial/runtime state is not silently reinterpreted;
- locations and spatial features can be edited while retaining their stable IDs;
- deleting a location or feature is blocked once an expedition exists because runtime history or knowledge may reference that ID.

These rules can later be refined with explicit archival/reference analysis, but they avoid data corruption now.

## API contracts

The Web layer exposes resource-oriented contracts instead of persistence entities:

- `GET/POST /api/overworlds`
- `GET/PUT /api/overworlds/{worldId}`
- location CRUD under `/api/overworlds/{worldId}/locations`
- feature CRUD under `/api/overworlds/{worldId}/features`
- source-map metadata CRUD under `/api/overworlds/{worldId}/source-maps`
- `GET /api/runtime/profiles`
- `GET/POST /api/overworlds/{worldId}/expeditions`
- `GET /api/expeditions/{expeditionId}`
- `POST /api/expeditions/{expeditionId}/advance`
- `POST /api/expeditions/{expeditionId}/discover`

HTTP contracts project domain values into stable DTOs; database rows are never exposed directly.

## Frontend and routing

The former single demonstrator has been split into world-list, world-editor, expedition, API, route, and map-surface modules. Application-owned DOM is driven by explicit route/state transitions. Canvas invalidation still goes through `RenderLifecycle`; `MutationObserver` is not used. `ResizeObserver` is limited to the external layout boundary needed for canvas sizing.

Tool-relative routes are:

- `/worlds`
- `/worlds/{worldId}`
- `/worlds/{worldId}/edit`
- `/worlds/{worldId}/expeditions/{expeditionId}`

Standalone mode serves the application shell for deep routes. Embedded mode derives these paths relative to the Tool Host base path, so the main Dorks & Dice site does not need knowledge of internal Hex Crawl routes. Hosted backend calls continue through the Tool Host upstream gateway.

The current editor is intentionally basic. It supports world/grid creation and editing, direct map placement for locations and point features, polyline authoring, simple polygon authoring/editing, free-form semantic categories, expedition creation/reopening, and the existing runtime controls. It stores the existing domain primitives instead of a frontend-specific map format.

## Source-map representations and future files

A world can persist multiple `SourceMapRepresentation` records for the same geography, including GM/player and grid/gridless variants or overlapping regional sources. They remain representations of one overworld rather than separate atlas pages.

Binary image upload remains deferred. Future map files should use deployment-owned blob/file storage while relational/application persistence stores metadata and a stable logical `AssetKey`. `AssetKey` is deliberately not an absolute machine path. A future asset service can resolve it to local durable storage, object storage, or another deployment-specific backend.

This supports multiple versions of the same geography and later pixel-to-world registration transforms without making an image authoritative world truth or changing the overworld model.

## Rendering and import-analysis boundary

Canvas 2D remains the current renderer. World-space geometry and viewport transforms stay outside domain storage, leaving a clean path to WebGL if profiling later justifies it.

Automatic image recognition is not part of this cycle. Future calibration/import should produce proposed grid/alignment data and semantic candidates. Image differencing, grid detection, icon recognition, road extraction, terrain segmentation, and optional local AI remain separate import concerns.

## Deferred work

The persistence/authoring slice intentionally does not add campaign sharing, real-time collaborative editing, Rules Core/Characters/Block Initiative integration, arbitrary-bearing runtime travel, multi-hex automatic backtracking, battle maps, binary map upload, image registration UI, or computer vision.

The main architectural question for the next map-import cycle is the concrete deployment asset-storage service behind logical `AssetKey` references. The current world/persistence model does not otherwise require redesign for imported source maps.
