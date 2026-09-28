# PostgreSQL persistence and SQLite cutover

## Runtime architecture

Hex Crawl uses `IHexCrawlStore` as its application persistence boundary. Production implements that boundary with `PostgresHexCrawlStore`, Npgsql, and hand-written SQL. Entity Framework is not part of this persistence path.

The PostgreSQL schema deliberately preserves the existing aggregate-snapshot design:

- `overworlds` stores stable IDs, owner scope, name, aggregate version/timestamps, and the complete world snapshot;
- `expeditions` stores session context, runtime state, optional player knowledge, party state, generated procedure resolutions, the complete executable procedure snapshot, optional preset-origin metadata, pause state, remaining-watch state, and aggregate version/timestamps;
- `expedition_events` stores retained runtime history with the exact sequence, kind, optional subject, and complete payload.

PostgreSQL-native `uuid`, `bigint`, `timestamptz`, and `jsonb` types are used. `jsonb` is used because persisted snapshots are interpreted as structured JSON rather than byte-for-byte text. The application serializer remains authoritative; no snapshot is normalized into new relational domain structures. Migration verification compares JSON semantically and then deserializes every migrated aggregate through the production store/application path.

Schema generation is tracked in `hex_crawl_schema_migrations`. Startup migrations run transactionally and use a PostgreSQL transaction advisory lock so concurrent service starts can not race schema creation. A database newer than the running service is treated as an error; production data is never recreated or reset automatically.

## Production PostgreSQL model

Hex Crawl does **not** provision a production PostgreSQL server. Production uses the existing shared PostgreSQL container and application network:

```text
PostgreSQL container: ix-dorks-and-dice-postgres-postgres-1
Application network: dorks-and-dice-backend
Application database: hex_crawl
Application login role: hex_crawl
Application database host name on the shared network: postgres
```

The application expects the database and login role to exist before deployment. Schema initialization inside the `hex_crawl` database is owned by Hex Crawl; PostgreSQL service/database-server provisioning is not.

No runtime SQLite fallback, startup import, or SQLite/PostgreSQL dual-write mode exists.

## Deployment configuration

Before Phase 0B, Hex Crawl did not use a persistent production `.env`; the SQLite connection string was hard-coded in `docker-compose.yml`.

Phase 0B intentionally adopts the already established Character Sheet deployment convention. The deployment workflow now requires:

```text
/mnt/HDDs/www/dorks-and-dice-hex-crawl/.env
```

This file does **not** already exist on the production server and must be created as an explicit cutover prerequisite. The deployment workflow checks for the file before removing the existing application container, and `docker-compose.yml` requires `ConnectionStrings__HexCrawl` from that file.

Production value:

```text
ConnectionStrings__HexCrawl=Host=postgres;Port=5432;Database=hex_crawl;Username=hex_crawl;Password=<retained-production-password>
```

Recommended file permissions:

```bash
chmod 600 /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env
```

Map binaries remain outside PostgreSQL:

```text
MapAssets__RootPath=/data/assets
```

The `hex-crawl-data` Docker volume remains required. It holds raster/source-map assets and retains the legacy `/data/hex-crawl.db` rollback artifact. Phase 0B does not remove or replace that volume.

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

The migration utility is intentionally separate from the normal application runtime. The web image does not need `Microsoft.Data.Sqlite` for normal production operation.

For local development, the utility can be run with the .NET SDK:

```bash
dotnet run --project tools/HexCrawl.PersistenceMigration -- \
  --sqlite /path/to/hex-crawl.db \
  --postgres 'Host=...;Database=...;Username=...;Password=...'
```

Production TrueNAS does **not** need the .NET SDK installed directly. Phase 0B includes a dedicated migration container image:

```bash
docker build \
  -t dorks-and-dice-hex-crawl-migration:cutover \
  -f tools/HexCrawl.PersistenceMigration/Dockerfile \
  .
```

The migration CLI accepts PostgreSQL through either `--postgres` or the normal `ConnectionStrings__HexCrawl` environment variable. The production commands below use the environment variable from the deployment `.env`, so the database password does not have to be placed in the migration command line.

The source SQLite connection is forced to `ReadOnly` by the migration implementation even if a caller supplies a writable SQLite connection string. The production container also mounts the backup read-only.

Schema generations 1 through 5 are supported. The reader inspects the actual `expeditions` columns rather than upgrading the SQLite database in place. Historical migration semantics are applied in memory:

- schema v1 world-bound rows receive their deterministic `WorldBound(overworldId)` context;
- schemas before v3 receive the historical empty party sheet;
- schemas before v4 receive an empty generated-resolution list;
- schemas before v5 import `NULL` procedure-origin metadata.

No preset origin is invented. IDs, aggregate versions, timestamps, event sequences, event subjects, event payloads, and available snapshot data are retained.

The importer requires an empty PostgreSQL target unless that target is already an exact migrated copy. A non-empty mismatched target is rejected rather than merged or overwritten. Initial data import is one PostgreSQL transaction across overworlds, expeditions, and events. A failed command exits nonzero.

`--verify-only` performs no schema initialization or data rewrite. The target must already be initialized by a successful migration; verification against an uninitialized target fails nonzero rather than modifying it.

## Verification performed by the utility

A migration is not accepted merely because inserts completed. The utility verifies:

- SQLite/PostgreSQL row counts for overworlds, expeditions, and events;
- every aggregate ID and aggregate version;
- owner scope, names, timestamps, pause/remaining-watch state, and overworld relationships;
- semantic equality of world, context, runtime, player-knowledge, party, generated-resolution, procedure, procedure-origin, and event JSON;
- event count, minimum/maximum sequence, exact order, duplicate-sequence absence, kinds, and optional subjects for each expedition;
- absence of orphan expedition → overworld and event → expedition relationships;
- actual production deserialization of every migrated world and crawl session through `PostgresHexCrawlStore` and `HexCrawlService`, including retained event history and all available session context kinds.

Any mismatch causes the command to fail nonzero.

## Exact TrueNAS production cutover

### Preconditions

Do not run the data migration from `feature/postgres-persistence`. Wait until Phase 0B has been merged to `main`, the merged `main` commit is green, and the production migration is pinned to that exact SHA.

The following production resources already exist and must be reused rather than recreated:

```text
Existing application container: dorks-and-dice-hex-crawl
Existing PostgreSQL container: ix-dorks-and-dice-postgres-postgres-1
Shared network: dorks-and-dice-backend
PostgreSQL login role: hex_crawl
PostgreSQL database: hex_crawl
```

The production SQLite backup prepared for this cutover is:

```text
/mnt/HDDs/backups/postgres-migrations/hex-crawl.20260927-220512.db
SHA-256: d1f30ef920f9f40507fec363a41cf07adc5410ac45fe4cb08884fbb54abc62cb
```

Verify that exact backup before migration:

```bash
printf '%s  %s\n' \
  'd1f30ef920f9f40507fec363a41cf07adc5410ac45fe4cb08884fbb54abc62cb' \
  '/mnt/HDDs/backups/postgres-migrations/hex-crawl.20260927-220512.db' \
  | sha256sum -c -
```

### 1. Stop Hex Crawl

Stop the existing application before the migration so SQLite can no longer change:

```bash
docker stop dorks-and-dice-hex-crawl
```

Do not delete `/data/hex-crawl.db`, the prepared backup, or `/data/assets`.

### 2. Create the Phase 0B deployment `.env`

Phase 0B intentionally introduces the same persistent `.env` pattern already used by Character Sheet. Create it now; do not assume it existed before this migration.

```bash
install -d -m 700 /mnt/HDDs/www/dorks-and-dice-hex-crawl
umask 077
cat > /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env <<'EOF'
ConnectionStrings__HexCrawl=Host=postgres;Port=5432;Database=hex_crawl;Username=hex_crawl;Password=REPLACE_WITH_RETAINED_PRODUCTION_PASSWORD
EOF
chmod 600 /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env
```

Replace only `REPLACE_WITH_RETAINED_PRODUCTION_PASSWORD` with the password already retained on the server. Do not commit this file.

### 3. Build the migration image from merged `main`

From the repository root of a checkout pinned to the green merged `main` SHA:

```bash
git fetch origin main
git checkout main
git pull --ff-only origin main
git rev-parse HEAD

docker build \
  -t dorks-and-dice-hex-crawl-migration:cutover \
  -f tools/HexCrawl.PersistenceMigration/Dockerfile \
  .
```

Confirm `git rev-parse HEAD` is the exact approved Phase 0B SHA before proceeding.

### 4. Import SQLite into the existing shared PostgreSQL instance

Run the migration container on the existing shared application network. The SQLite backup is mounted read-only. The PostgreSQL connection string is read from the production `.env` and resolves `Host=postgres` through `dorks-and-dice-backend`.

```bash
docker run --rm \
  --network dorks-and-dice-backend \
  --env-file /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env \
  --mount type=bind,src=/mnt/HDDs/backups/postgres-migrations/hex-crawl.20260927-220512.db,dst=/migration/hex-crawl.db,readonly \
  dorks-and-dice-hex-crawl-migration:cutover \
  --sqlite /migration/hex-crawl.db
```

This command initializes the Hex Crawl schema inside the already-created `hex_crawl` database, imports the structured data transactionally, verifies it, and exits nonzero on failure. It does not provision another PostgreSQL container, recreate the role/database, modify the SQLite source, or touch `/data/assets`.

### 5. Run an independent verification-only pass

Run verification separately after the import:

```bash
docker run --rm \
  --network dorks-and-dice-backend \
  --env-file /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env \
  --mount type=bind,src=/mnt/HDDs/backups/postgres-migrations/hex-crawl.20260927-220512.db,dst=/migration/hex-crawl.db,readonly \
  dorks-and-dice-hex-crawl-migration:cutover \
  --sqlite /migration/hex-crawl.db \
  --verify-only
```

`--verify-only` does not apply schema migrations and does not rewrite PostgreSQL data.

### 6. Deploy through the normal Compose workflow

After import and independent verification succeed, deploy the same merged `main` SHA through the normal `Deploy Hex Crawl` workflow. The workflow uses:

```bash
docker compose \
  --project-name dorks-and-dice-hex-crawl \
  --env-file /mnt/HDDs/www/dorks-and-dice-hex-crawl/.env \
  -f docker-compose.yml \
  up -d --force-recreate --remove-orphans
```

This is the Phase 0B production configuration path. The workflow does not provision PostgreSQL; it only recreates the Hex Crawl application against the existing shared service.

### 7. Verify production

Verify the application through the shared network:

```bash
docker run --rm --network dorks-and-dice-backend curlimages/curl:8.12.1 \
  -fsS http://dorks-and-dice-hex-crawl:8080/health

docker run --rm --network dorks-and-dice-backend curlimages/curl:8.12.1 \
  -fsS http://dorks-and-dice-hex-crawl:8080/ready

docker logs --tail 200 dorks-and-dice-hex-crawl
```

`/health` must report healthy. `/ready` must report `"status":"ready"`, `"persistence":"postgresql-ready"`, and `"mapAssets":"filesystem"`.

Then load representative existing overworlds and running sheets, confirm persisted event history and procedure state, and confirm existing source-map/raster assets still load from `/data/assets`.

## Rollback

Retain the original SQLite datastore and the timestamped backup intact until the PostgreSQL cutover is accepted.

If validation fails after deployment:

1. stop the PostgreSQL-backed Hex Crawl container before further writes;
2. restore the prior SQLite-capable application version/configuration;
3. point that prior version at the untouched SQLite datastore or backup;
4. reuse the same `hex-crawl-data` volume so `/data/assets` remains available.

The Phase 0B migration does not delete, replace, or modify the SQLite source or filesystem assets. Do not delete `hex-crawl.db`, the prepared backup, or the `hex-crawl-data` volume as part of this migration.
