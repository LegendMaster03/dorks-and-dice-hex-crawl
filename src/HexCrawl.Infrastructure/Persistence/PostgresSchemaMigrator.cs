using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Domain.Spatial;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

public sealed class PostgresSchemaMigrator(string connectionString)
{
    public const int CurrentVersion = 10;
    private const int ProcedureTilingVersion = 9;
    private const long MigrationLockKey = 0x484558435241574C;
    private static readonly HashSet<string> SupportedWorldSnapshotFields = new(StringComparer.Ordinal)
    {
        "id", "name", "grid", "features", "locations", "sourceMaps",
        "environmentAnnotations", "formatVersion", "tiling"
    };

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var bootstrap = connection.CreateCommand())
        {
            bootstrap.CommandText = """
                CREATE TABLE IF NOT EXISTS hex_crawl_schema_migrations (
                    version bigint PRIMARY KEY,
                    applied_at timestamptz NOT NULL
                );
                """;
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var advisoryLock = connection.CreateCommand())
            {
                advisoryLock.Transaction = transaction;
                advisoryLock.CommandText = "SELECT pg_advisory_xact_lock(@lockKey);";
                advisoryLock.Parameters.AddWithValue("lockKey", NpgsqlDbType.Bigint, MigrationLockKey);
                await advisoryLock.ExecuteNonQueryAsync(cancellationToken);
            }

            var current = await CurrentAsync(connection, transaction, cancellationToken);
            if (current == 0)
            {
                await ApplyCurrentSchemaAsync(connection, transaction, cancellationToken);
                current = CurrentVersion;
            }
            if (current == 8)
            {
                await UpgradeProcedureTilingIdentityAsync(connection, transaction, cancellationToken);
                current = ProcedureTilingVersion;
            }
            if (current == ProcedureTilingVersion)
            {
                await UpgradeGeneralizedWorldAuthorityAsync(connection, transaction, cancellationToken);
                current = CurrentVersion;
            }
            if (current != CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Hex Crawl PostgreSQL schema version {current} has no verified non-destructive upgrade path to schema version {CurrentVersion}. The existing database has been preserved. Restore a compatible application version or provide and test a data-preserving migration before retrying.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<int> CurrentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM hex_crawl_schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Rewrites previously validated v1 / v1.1 hex-procedure snapshots once, including
    /// historical revisions and active expeditions, without modifying revision numbers.
    /// The old serialized property is removed, not retained as a parallel notation.
    /// </summary>
    private static async Task UpgradeProcedureTilingIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM campaign_procedure_revisions
                    WHERE procedure_json ? 'tilingGjhNotation'
                      AND procedure_json->>'tilingGjhNotation' <> '6/m30/r(h1)'
                ) OR EXISTS (
                    SELECT 1 FROM expeditions
                    WHERE procedure_json ? 'tilingGjhNotation'
                      AND procedure_json->>'tilingGjhNotation' <> '6/m30/r(h1)'
                ) THEN
                    RAISE EXCEPTION 'Unexpected legacy procedure tiling; migration cannot silently change topology';
                END IF;
            END $$;

            UPDATE campaign_procedure_revisions
            SET procedure_json = jsonb_set(
                jsonb_set(
                    procedure_json - 'tilingGjhNotation',
                    '{schemaVersion}', to_jsonb('1.2'::text), true),
                '{tilingDsSymbol}', to_jsonb('<1:1,1,1:6,3>'::text), true)
            WHERE COALESCE(procedure_json->>'schemaVersion', '1') IN ('1', '1.1')
               OR procedure_json ? 'tilingGjhNotation';

            UPDATE expeditions
            SET procedure_json = jsonb_set(
                jsonb_set(
                    procedure_json - 'tilingGjhNotation',
                    '{schemaVersion}', to_jsonb('1.2'::text), true),
                '{tilingDsSymbol}', to_jsonb('<1:1,1,1:6,3>'::text), true)
            WHERE COALESCE(procedure_json->>'schemaVersion', '1') IN ('1', '1.1')
               OR procedure_json ? 'tilingGjhNotation';

            INSERT INTO hex_crawl_schema_migrations(version, applied_at)
            VALUES (@version, @appliedAt);
            """;
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, ProcedureTilingVersion);
        command.Parameters.AddWithValue("appliedAt", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Transactionally migrates every world in the schema, preserving the
    /// stored grid and all unknown semantic fields verbatim in PostgreSQL
    /// JSONB while adding the validated periodic authority. Fails the whole
    /// transaction if any existing world cannot be safely converted.
    /// </summary>
    private static async Task UpgradeGeneralizedWorldAuthorityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));

        var pending = new List<(Guid Id, PeriodicWorldTiling Tiling)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id, world_json::text FROM overworlds ORDER BY id FOR UPDATE;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                try
                {
                    using var document = JsonDocument.Parse(reader.GetString(1));
                    var root = document.RootElement;
                    // The format-2 reader rejects unknown top-level fields. Do not
                    // report a successful upgrade that strands a formerly readable
                    // record. Keep the original schema intact for a targeted upgrade.
                    foreach (var property in root.EnumerateObject())
                        if (!SupportedWorldSnapshotFields.Contains(property.Name))
                            throw new InvalidDataException(
                                $"Unsupported world snapshot field '{property.Name}' requires explicit migration.");
                    if (!root.TryGetProperty("id", out var worldId)
                        || worldId.ValueKind != JsonValueKind.String
                        || !worldId.TryGetGuid(out var snapshotId)
                        || snapshotId != id)
                        throw new InvalidDataException("World snapshot identity disagrees with its database row.");
                    var version = root.TryGetProperty("formatVersion", out var field)
                        ? field.GetInt32() : 1;
                    if (version == 2)
                    {
                        if (!root.TryGetProperty("tiling", out var tilingField)
                            || tilingField.ValueKind != JsonValueKind.Object)
                            throw new InvalidDataException("Format 2 has no tiling authority.");
                        var existing = tilingField.Deserialize<PeriodicWorldTiling>(options)
                            ?? throw new InvalidDataException("Empty authoritative tiling.");
                        existing.Validate();
                        if (root.TryGetProperty("grid", out var projected)
                            && projected.ValueKind == JsonValueKind.Object)
                        {
                            var grid = projected.Deserialize<HexGridDefinition>(options)
                                ?? throw new InvalidDataException("Empty hex compatibility projection.");
                            if (!LegacyHexTilingCompatibility.Matches(existing, grid))
                                throw new InvalidDataException("Stored grid disagrees with authoritative geometry.");
                        }
                        continue;
                    }
                    if (version != 1 || root.TryGetProperty("tiling", out _)
                        || !root.TryGetProperty("grid", out var legacyGrid)
                        || legacyGrid.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Unsupported legacy world JSON format.");
                    var gridValue = legacyGrid.Deserialize<HexGridDefinition>(options)
                        ?? throw new InvalidDataException("Missing legacy grid.");
                    var tiling = LegacyHexTilingCompatibility.Create(gridValue);
                    tiling.Validate();
                    pending.Add((id, tiling));
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or
                    InvalidOperationException or ArgumentException or OverflowException or NotSupportedException)
                {
                    throw new InvalidDataException(
                        $"World {id} cannot be upgraded safely. The database transaction will roll back; inspect and repair this record before retrying.", ex);
                }
            }
        }

        foreach (var (id, tiling) in pending)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE overworlds
                SET world_json = jsonb_set(
                    jsonb_set(world_json, '{tiling}', @tiling::jsonb, true),
                    '{formatVersion}', '2'::jsonb, true)
                WHERE id = @id;
                """;
            update.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            update.Parameters.AddWithValue("tiling", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(tiling, options));
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException($"World {id} disappeared during locked migration.");
        }

        await using (var validateProcedures = connection.CreateCommand())
        {
            validateProcedures.Transaction = transaction;
            validateProcedures.CommandText = """
                SELECT (
                    SELECT count(*) FROM campaign_procedure_revisions
                    WHERE COALESCE(procedure_json->>'schemaVersion', '') NOT IN ('1.2', '1.3')
                       OR (procedure_json->>'schemaVersion' = '1.2'
                           AND procedure_json->>'tilingDsSymbol' IS DISTINCT FROM '<1:1,1,1:6,3>')
                ) + (
                    SELECT count(*) FROM expeditions
                    WHERE COALESCE(procedure_json->>'schemaVersion', '') NOT IN ('1.2', '1.3')
                       OR (procedure_json->>'schemaVersion' = '1.2'
                           AND procedure_json->>'tilingDsSymbol' IS DISTINCT FROM '<1:1,1,1:6,3>')
                );
                """;
            long invalid = Convert.ToInt64(
                await validateProcedures.ExecuteScalarAsync(cancellationToken));
            if (invalid != 0)
                throw new InvalidDataException(
                    "Pinned procedures include unrecognized schemas or incompatible tiling data. Nothing was migrated.");
        }

        await using var procedure = connection.CreateCommand();
        procedure.Transaction = transaction;
        procedure.CommandText = """
            UPDATE campaign_procedure_revisions
            SET procedure_json = jsonb_set(procedure_json,
                '{schemaVersion}', to_jsonb('1.3'::text), true)
            WHERE procedure_json->>'schemaVersion' = '1.2';

            UPDATE expeditions
            SET procedure_json = jsonb_set(procedure_json,
                '{schemaVersion}', to_jsonb('1.3'::text), true)
            WHERE procedure_json->>'schemaVersion' = '1.2';

            INSERT INTO hex_crawl_schema_migrations(version, applied_at)
            VALUES (@version, @appliedAt);
            """;
        procedure.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, CurrentVersion);
        procedure.Parameters.AddWithValue("appliedAt", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await procedure.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyCurrentSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE overworlds (
                id uuid PRIMARY KEY,
                owner_user_id text NOT NULL,
                name text NOT NULL,
                world_json jsonb NOT NULL,
                version bigint NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL
            );
            CREATE INDEX ix_overworlds_owner_updated
                ON overworlds(owner_user_id, updated_at DESC, name);

            CREATE TABLE campaign_procedure_revisions (
                procedure_id uuid NOT NULL,
                revision integer NOT NULL CHECK (revision > 0),
                owner_user_id text NOT NULL,
                campaign_id uuid NULL,
                procedure_json jsonb NOT NULL,
                origin_json jsonb NULL,
                created_at timestamptz NOT NULL,
                PRIMARY KEY(procedure_id, revision)
            );
            CREATE INDEX ix_campaign_procedure_revisions_owner_campaign
                ON campaign_procedure_revisions(owner_user_id, campaign_id, procedure_id, revision DESC);

            CREATE TABLE expeditions (
                id uuid PRIMARY KEY,
                overworld_id uuid NULL,
                context_json jsonb NOT NULL,
                owner_user_id text NOT NULL,
                name text NOT NULL,
                state_json jsonb NOT NULL,
                knowledge_json jsonb NULL,
                party_json jsonb NOT NULL,
                environment_json jsonb NOT NULL,
                effects_json jsonb NOT NULL,
                resources_json jsonb NOT NULL,
                survival_json jsonb NOT NULL,
                journey_state_json jsonb NOT NULL,
                generated_resolutions_json jsonb NOT NULL,
                procedure_json jsonb NOT NULL,
                procedure_origin_json jsonb NULL,
                pause_reason text NULL,
                remaining_watch_ticks bigint NOT NULL,
                version bigint NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                CONSTRAINT fk_expeditions_overworld
                    FOREIGN KEY(overworld_id) REFERENCES overworlds(id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_expeditions_owner_world_updated
                ON expeditions(owner_user_id, overworld_id, updated_at DESC, name);
            CREATE INDEX ix_expeditions_owner_updated
                ON expeditions(owner_user_id, updated_at DESC, name);

            CREATE TABLE expedition_events (
                expedition_id uuid NOT NULL,
                sequence bigint NOT NULL,
                kind text NOT NULL,
                subject_id uuid NULL,
                subject_type text NULL,
                event_json jsonb NOT NULL,
                PRIMARY KEY(expedition_id, sequence),
                CONSTRAINT fk_expedition_events_expedition
                    FOREIGN KEY(expedition_id) REFERENCES expeditions(id) ON DELETE CASCADE
            );

            INSERT INTO hex_crawl_schema_migrations(version, applied_at)
            VALUES (@version, @appliedAt);
            """;
        command.Parameters.AddWithValue("version", NpgsqlDbType.Bigint, CurrentVersion);
        command.Parameters.AddWithValue("appliedAt", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}