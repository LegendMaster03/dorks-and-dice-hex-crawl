# PostgreSQL persistence

## Runtime architecture

Hex Crawl uses `IHexCrawlStore` as its application persistence boundary. Production implements that boundary with `PostgresHexCrawlStore`, Npgsql, and hand-written SQL. Entity Framework is not part of this persistence path.

The PostgreSQL schema preserves the aggregate-snapshot design:

- `overworlds` stores stable IDs, owner scope, name, aggregate version/timestamps, and the complete world snapshot, including static world/hex/spatial-feature environment annotations;
- `expeditions` stores session context, runtime state, optional player knowledge, party state, current/transient environment facts, explicit DM environment overrides, expedition-owned effect/consequence state, generated procedure resolutions, the complete executable procedure snapshot, optional preset-origin metadata, pause state, remaining-watch state, and aggregate version/timestamps;
- `expedition_events` stores retained runtime history with the exact sequence, kind, optional subject, and complete payload.

PostgreSQL-native `uuid`, `bigint`, `timestamptz`, and `jsonb` types are used. Persisted JSON remains application-owned; snapshots are not normalized into a competing relational domain model. Generic execution-handler identity and mechanic version are persisted verbatim inside the `CampaignProcedure` JSON; current native handlers use behavior-oriented `procedure.*` identifiers.

## Environment persistence boundary

Phase 9 deliberately persists environment authority rather than derived environment output.

Static world environment truth is serialized as part of `OverworldDefinition` in `overworlds.world_json`. Annotations can target the world, a specific hex, or a spatial feature and retain their typed facts, notes, and provenance.

Expedition-owned environment state is serialized separately in `expeditions.environment_json`. It contains only:

- `CurrentFacts` — current/transient session conditions;
- `Overrides` — explicit DM overrides.

The following are derived and are **not** persisted as competing sources of truth:

- effective environment context;
- precedence winners/losers;
- conflict projections;
- environment-to-procedure evaluation output;
- Rules Core/provider responses;
- environment-derived movement composition results.

Those values are recomputed from the current world snapshot, current expedition position/context, expedition environment state, and pinned `CampaignProcedure`. Moving between hexes therefore changes applicable static world truth without copying annotations into expedition state.

## Effect/consequence persistence boundary

Phase 10 persists expedition-owned effect/consequence authority in `expeditions.effects_json`.

That snapshot retains active persistent effects, applied consequence history, unresolved/pending consequences, provenance, recovery semantics, and resolution provenance. Effect-derived runtime projections such as movement contributors are recomputed from the persisted effect state instead of being stored as a competing source of truth.

## Schema lifecycle

Schema generation is tracked in `hex_crawl_schema_migrations`. `PostgresSchemaMigrator` applies application schema changes transactionally and uses a PostgreSQL transaction advisory lock so concurrent service starts can not race schema creation.

The current pre-release schema version is **6**. Version 6 includes the required `environment_json jsonb NOT NULL` and `effects_json jsonb NOT NULL` expedition columns together with the Phase 9 environment and Phase 10 generalized effect/consequence persistence shapes.

Hex Crawl is still pre-release and deliberately does not maintain compatibility infrastructure for earlier development schemas. A database whose recorded schema version is not the current version is rejected with a reset/reinitialize instruction rather than silently reshaping obsolete development data. Production data is never recreated or reset automatically.

This schema migrator is part of the live PostgreSQL persistence layer and is distinct from the retired one-time SQLite cutover tooling.

## Production PostgreSQL model

Hex Crawl uses the existing shared PostgreSQL service and application network:

```text
PostgreSQL container: ix-dorks-and-dice-postgres-postgres-1
Application network: dorks-and-dice-backend
Application database: hex_crawl
Application login role: hex_crawl
Application database host name on the shared network: postgres
```

Hex Crawl owns schema initialization inside its database. It does not provision or manage the PostgreSQL server itself.

## Deployment configuration

Production requires the server-side environment file:

```text
/mnt/HDDs/www/dorks-and-dice-hex-crawl/.env
```

with:

```text
ConnectionStrings__HexCrawl=Host=postgres;Port=5432;Database=hex_crawl;Username=hex_crawl;Password=<production-password>
```

The file must remain outside source control and readable only by the deployment account.

Map binaries remain outside PostgreSQL:

```text
MapAssets__RootPath=/data/assets
```

The `hex-crawl-data` Docker volume remains required for filesystem-backed map assets.

Before replacing the live container, the deployment workflow starts the newly built image against the production configuration in an isolated preflight container. If the image can not start or the current PostgreSQL schema is incompatible, deployment stops and prints the preflight container logs without replacing the live service. Database resets remain explicit operator actions.

## Readiness

`/health` reports ASP.NET process health. `/ready` additionally verifies that PostgreSQL is reachable and at the schema version understood by the service. A ready response includes:

```json
{
  "status": "ready",
  "persistence": "postgresql-ready",
  "mapAssets": "filesystem"
}
```

If PostgreSQL becomes unavailable after startup, `/ready` returns HTTP 503 rather than reporting the process as ready.

## Validation

CI runs application persistence tests, HTTP integration tests, and container restart smokes against PostgreSQL. Phase 9 coverage verifies static environment annotations and expedition `environment_json` survive persistence/reload, and Phase 10 coverage verifies generalized effect/consequence state in `effects_json`, recovery semantics, unresolved consequence handling, and endpoint behavior survive the same persistence boundary.

The mapped smoke persists structured state and a filesystem map asset, restarts PostgreSQL and the application, and verifies both survive. A separate mapless smoke persists and reloads a `NonSpatial` crawl session, including the mapless state boundaries used by Phase 9 current-environment support.

The retired SQLite-to-PostgreSQL production cutover utility and its migration-only tests are intentionally not part of the ongoing repository surface after Phase 0 completion.
