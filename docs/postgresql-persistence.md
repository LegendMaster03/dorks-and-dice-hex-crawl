# PostgreSQL persistence

## Runtime architecture

Hex Crawl uses `IHexCrawlStore` as its application persistence boundary. Production implements that boundary with `PostgresHexCrawlStore`, Npgsql, and hand-written SQL. Entity Framework is not part of this persistence path.

The PostgreSQL schema preserves the aggregate-snapshot design:

- `overworlds` stores stable IDs, owner scope, name, aggregate version/timestamps, and the complete world snapshot;
- `expeditions` stores session context, runtime state, optional player knowledge, party state, generated procedure resolutions, the complete executable procedure snapshot, optional preset-origin metadata, pause state, remaining-watch state, and aggregate version/timestamps;
- `expedition_events` stores retained runtime history with the exact sequence, kind, optional subject, and complete payload.

PostgreSQL-native `uuid`, `bigint`, `timestamptz`, and `jsonb` types are used. Persisted JSON remains application-owned; snapshots are not normalized into a competing relational domain model. Generic execution-handler identity and mechanic version are persisted verbatim inside the `CampaignProcedure` JSON; current native handlers use behavior-oriented `procedure.*` identifiers.

## Schema lifecycle

Schema generation is tracked in `hex_crawl_schema_migrations`. `PostgresSchemaMigrator` applies application schema changes transactionally and uses a PostgreSQL transaction advisory lock so concurrent service starts can not race schema creation. A database newer than the running service is treated as an error; production data is never recreated or reset automatically.

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

CI runs application persistence tests, HTTP integration tests, and container restart smokes against PostgreSQL. The mapped smoke persists structured state and a filesystem map asset, restarts PostgreSQL and the application, and verifies both survive. A separate mapless smoke persists and reloads a `NonSpatial` crawl session.

The retired SQLite-to-PostgreSQL production cutover utility and its migration-only tests are intentionally not part of the ongoing repository surface after Phase 0 completion.
