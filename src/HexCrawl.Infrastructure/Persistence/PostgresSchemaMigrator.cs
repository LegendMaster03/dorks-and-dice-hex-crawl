using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Infrastructure.Persistence;

public sealed class PostgresSchemaMigrator(string connectionString)
{
    public const int CurrentVersion = 2;
    private const long MigrationLockKey = 0x484558435241574C;

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
            if (current > CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Hex Crawl PostgreSQL schema version {current} is newer than supported version {CurrentVersion}.");
            }

            if (current < 1)
            {
                await ApplyVersion1Async(connection, transaction, cancellationToken);
                current = 1;
            }
            if (current < 2)
            {
                await ApplyVersion2Async(connection, transaction, cancellationToken);
                current = 2;
            }

            if (current != CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Hex Crawl PostgreSQL schema initialization stopped at version {current}; expected {CurrentVersion}.");
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

    private static async Task ApplyVersion1Async(
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

            CREATE TABLE expeditions (
                id uuid PRIMARY KEY,
                overworld_id uuid NULL,
                context_json jsonb NOT NULL,
                owner_user_id text NOT NULL,
                name text NOT NULL,
                state_json jsonb NOT NULL,
                knowledge_json jsonb NULL,
                party_json jsonb NOT NULL,
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
            VALUES (1, @appliedAt);
            """;
        command.Parameters.AddWithValue("appliedAt", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyVersion2Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE expeditions
                ADD COLUMN campaign_procedure_json jsonb NULL;

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

            INSERT INTO hex_crawl_schema_migrations(version, applied_at)
            VALUES (2, @appliedAt);
            """;
        command.Parameters.AddWithValue("appliedAt", NpgsqlDbType.TimestampTz, DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
