# PostgreSQL persistence

## Runtime architecture

Hex Crawl uses `IHexCrawlStore` as its application persistence boundary. Production implements that boundary with `PostgresHexCrawlStore`, Npgsql, and hand-written SQL. Entity Framework is not part of this persistence path.

The PostgreSQL schema preserves the aggregate-snapshot design:

- `overworlds` stores stable IDs, owner scope, name, aggregate version/timestamps, and the complete world snapshot, including static world/hex/spatial-feature environment annotations;
- `expeditions` stores session context, runtime state, optional player knowledge, party state, current/transient environment facts, explicit DM environment overrides, Phase 10 effects, Phase 11 resources/survival, Phase 12 journey/process state, generated procedure resolutions, the complete executable procedure snapshot, optional preset-origin metadata, pause state, remaining-watch state, and aggregate version/timestamps;
- `expedition_events` stores retained runtime history with the exact sequence, kind, optional subject, and complete payload.

PostgreSQL-native `uuid`, `bigint`, `timestamptz`, and `jsonb` types are used. Persisted JSON remains application-owned; snapshots are not normalized into a competing relational domain model. Generic execution-handler identity and mechanic version are persisted verbatim inside the `CampaignProcedure` JSON; current native handlers use behavior-oriented `procedure.*` identifiers.

## Expedition aggregate columns

Current-format expeditions persist the durable Phase 0–12 aggregate in explicit ownership columns:

- `state_json` — deterministic spatial or non-spatial crawl runtime state;
- `knowledge_json` — optional player-knowledge/presentation state;
- `party_json` — typed party members, movement contributors, and Phase 7 activity/role assignments;
- `environment_json` — Phase 9 current/transient environment facts and explicit DM overrides;
- `effects_json` — Phase 10 applied/pending consequences and persistent expedition effects;
- `resources_json` — Phase 11 generic expedition resources and audit history;
- `survival_json` — Phase 11 forced-travel, exposure, and camp state;
- `journey_state_json` — Phase 12 active/closed journey processes, journey events, resolutions, history, and idempotency identities;
- `generated_resolutions_json` — retained generated procedure-resolution state;
- `procedure_json` — the exact pinned `CampaignProcedure` authority;
- `procedure_origin_json` — optional informational creation provenance.

The columns separate subsystem authority while remaining one optimistic-concurrency expedition aggregate. A successful mutation writes the complete internally consistent aggregate at one new version.

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

## Effects, survival, and journey persistence boundaries

Phase 10 consequence state is authoritative in `effects_json`. Phase 11 resource inventory/audit state and survival state are authoritative in `resources_json` and `survival_json`; journey code does not duplicate those mutations.

Phase 12 stores journey/process authority in `journey_state_json`. It includes process definitions/execution snapshots, stage state, pending actions, closed-process history, journey event occurrences, resolution records, consumed resolution IDs, and observed runtime occurrence IDs. Stable identities make retries and completed-watch observation idempotent across persistence/restart.

A process stores its own execution snapshot when started. Current catalog recipes are therefore not consulted to reinterpret an in-progress process after restart. The expedition's pinned `procedure_json` remains the procedure authority for new focused operations.

Journey event environment facts are historical snapshots attached to occurrences/resolutions where requested; they do not replace `environment_json` as current environment authority.

## Schema lifecycle

Schema generation is tracked in `hex_crawl_schema_migrations`. `PostgresSchemaMigrator` applies application schema changes transactionally and uses a PostgreSQL transaction advisory lock so concurrent service starts can not race schema creation.

The current schema version is **10** (`PostgresSchemaMigrator.CurrentVersion`). Version 8 established the Phase 12 `journey_state_json jsonb NOT NULL` expedition column alongside the Phase 9–11 `environment_json`, `effects_json`, `resources_json`, and `survival_json` boundaries. Version 9 transactionally upgrades stored procedure revisions and expedition JSON from procedure schema 1/1.1 to 1.2 and replaces the obsolete tiling field. Version 10 transactionally converts every existing world to validated periodic tiling authority and upgrades all stored procedure revisions and pinned expedition procedure snapshots to schema 1.3 without changing IDs or mechanical authority.

**Post-Phase-15 operating policy:** existing tester databases and separately stored map assets must normally be migrated, not reset, between releases. Before deploying a schema change, test the upgrade from actual prior tester versions, establish recoverable PostgreSQL and map-asset backups, rehearse recovery as appropriate, verify idempotent/interrupted migration behavior, and confirm saved expeditions and maps still work afterward. Each phase must leave the deployed application usable.

The completed historical schema 8→9 and 9→10 upgrade implementations were retired after the full dev/testing database reached schema 10. The current migrator initializes new databases directly at schema 10, allows schema 10 databases to start unchanged, and rejects unsupported schema 8, 9, future, or unknown versions without deleting data. The advisory-lock and transaction-based upgrade framework remains for future migrations. An older backup must first be migrated using the archived Phase 17 implementation or a separately tested data-preserving upgrade; it cannot be opened directly by the current schema-10-only application. An exceptional destructive reset requires explicit project-owner authorization and is never the default path.

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

CI runs application persistence tests, HTTP integration tests, and container restart smokes against PostgreSQL. Coverage through Phase 12 verifies the durable environment/effect/resource/survival/journey aggregate round-trips through PostgreSQL and that focused runtime operations continue from the stored pinned procedure after reload.

The mapped smoke persists structured state and a filesystem map asset, restarts PostgreSQL and the application, and verifies both survive. A separate mapless smoke persists and reloads a `NonSpatial` crawl session, including the mapless state boundaries used by role-driven journey/process execution.

The retired SQLite-to-PostgreSQL production cutover utility and its migration-only tests are intentionally not part of the ongoing repository surface after Phase 0 completion.

Historical migration record: schema 9 upgraded procedure and expedition JSON from procedure schema 1/1.1 to 1.2 and removed the obsolete tiling notation. Schema 10 added verified topology/metric authority to each saved world and advanced procedure schema 1.2 to 1.3 in the completed cutover. See `docs/phase-17-world-authority-and-persistence.md` for the archived cutover details; the former Phase 17 deployment gate is no longer active.
