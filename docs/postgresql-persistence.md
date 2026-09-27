# PostgreSQL persistence and SQLite cutover

## Runtime architecture

Hex Crawl uses `IHexCrawlStore` as its application persistence boundary. Production implements that boundary with `PostgresHexCrawlStore`, Npgsql, and hand-written SQL. Entity Framework is not part of this persistence path.

The PostgreSQL schema deliberately preserves the existing aggregate-snapshot design:

- `overworlds` stores stable IDs, owner scope, name, aggregate version/timestamps, and the complete world snapshot;
- `expeditions` stores session context, runtime state, optional player knowledge, party state, generated procedure resolutions, the complete executable procedure snapshot, optional preset-origin metadata, pause state, remaining-watch state, and aggregate version/timestamps;
- `expedition_events` stores retained runtime history with the exact sequence, kind, optional subject, and complete payload.

PostgreSQL-native `uuid`, `bigint`, `timestamptz`, and `jsonb` types are used. `jsonb` is used because persisted snapshots are interpreted as structured JSON rather than byte-for-byte text. The application serializer remains authoritative; no snapshot is normalized into new relational domain structures. Migration verification compares JSON semantically and then deserializes every migrated aggregate through the production store/application path.

Schema generation is tracked in `hex_crawl_schema_migrations`. Startup migrations run transactionally and use a PostgreSQL transaction advisory lock so concurrent service starts can not race schema creation. A database newer than the running service is treated as an error; production data is never recreated or reset automatically.

## Configuration

The application requires:

```text
ConnectionStrings__HexCrawl=Host=...;Port=5432;Database=...;Username=...;Password=...
```

There is no production SQLite fallback. `.env.example` contains development-only placeholder values.

Map binaries remain outside PostgreSQL:

```text
MapAssets__RootPath=/data/assets
```

The `hex-crawl-data` Docker volume remains required. It holds raster/source-map assets and should also retain the old `/data/hex-crawl.db` as a rollback artifact after cutover.

## Readiness

`/health` reports ASP.NET process health. `/ready` also verifies that PostgreSQL is reachable and at the schema version understood by the service. A ready response includes:

```json
{
  "status": "ready",
  "persistence": "postgresql-ready",
  "mapAssets": "filesystem"
}
```

If PostgreSQL becomes unavailable after startup, `/ready` returns HTTP 503 rather than reporting the process as ready.

## One-time SQLite migration utility

The migration utility is intentionally separate from runtime Infrastructure so `Microsoft.Data.Sqlite` is not a production runtime dependency:

```bash
dotnet run --project tools/HexCrawl.PersistenceMigration -- \
  --sqlite /path/to/hex-crawl.db \
  --postgres 'Host=...;Database=...;Username=...;Password=...'
```

Verification can be repeated without writing data:

```bash
dotnet run --project tools/HexCrawl.PersistenceMigration -- \
  --sqlite /path/to/hex-crawl.db \
  --postgres 'Host=...;Database=...;Username=...;Password=...' \
  --verify-only
```

The source SQLite database is opened read-only when a path is supplied. Schema generations 1 through 5 are supported. The reader inspects the actual `expeditions` columns rather than requiring the SQLite database to be upgraded in place. Historical migration semantics are applied in memory:

- schema v1 world-bound rows receive their deterministic `WorldBound(overworldId)` context;
- schemas before v3 receive the historical empty party sheet;
- schemas before v4 receive an empty generated-resolution list;
- schemas before v5 import `NULL` procedure-origin metadata.

No preset origin is invented. IDs, aggregate versions, timestamps, event sequences, event subjects, event payloads, and available snapshot data are retained.

The importer requires an empty PostgreSQL target unless that target is already an exact migrated copy. A non-empty mismatched target is rejected rather than merged or overwritten. Initial import is one PostgreSQL transaction across overworlds, expeditions, and events.

## Verification performed by the utility

A migration is not accepted merely because inserts completed. The utility verifies:

- SQLite/PostgreSQL row counts for overworlds, expeditions, and events;
- every aggregate ID and aggregate version;
- owner scope, names, timestamps, pause/remaining-watch state, and overworld relationships;
- semantic equality of world, context, runtime, player-knowledge, party, generated-resolution, procedure, procedure-origin, and event JSON;
- event count, minimum/maximum sequence, exact order, duplicate-sequence absence, kinds, and optional subjects for each expedition;
- absence of orphan expedition → overworld and event → expedition relationships;
- actual production deserialization of every migrated world and crawl session through `PostgresHexCrawlStore` and `HexCrawlService`, including retained event history and all available session context kinds.

Any mismatch causes the command to fail nonzero. `--verify-only` can be run again after deployment or before deciding to discard any rollback artifact.

## Production cutover

The cutover is intentionally operator-controlled because the production service and shared PostgreSQL credentials are external to the repository.

1. Stop Hex Crawl or otherwise block all writes.
2. Copy `/data/hex-crawl.db` to a separate timestamped backup. Do not edit the backup.
3. Confirm the `hex-crawl-data` volume and `/data/assets` remain intact.
4. Provision the PostgreSQL database/user and grant only the required database privileges.
5. Run the migration utility against the SQLite backup and the target PostgreSQL connection string.
6. Require a successful migration verification report.
7. Run the utility again with `--verify-only` if an independent verification pass is desired.
8. Put the PostgreSQL connection string in the server-side Hex Crawl `.env` file used by deployment.
9. Deploy/start Hex Crawl.
10. Require `/health` and `/ready`, including `"persistence":"postgresql-ready"`.
11. Load representative world-bound, abstract-hex, and non-spatial sessions that exist in production. Confirm map assets still resolve from `/data/assets` and perform a safe deterministic application read/transition where appropriate.
12. Retain both the original SQLite backup and the existing Docker volume until the PostgreSQL deployment has been accepted and rollback is no longer required.

## Rollback

If cutover validation fails, stop the PostgreSQL-backed service before any further writes. Restore the prior application version/configuration, point it at the untouched SQLite database/backup, and restart against the same `hex-crawl-data` volume. PostgreSQL migration does not delete or rewrite the SQLite source or filesystem map assets, so this rollback remains available until operators intentionally retire the old database.

Do not delete `hex-crawl.db` or the `hex-crawl-data` volume as part of the migration.
