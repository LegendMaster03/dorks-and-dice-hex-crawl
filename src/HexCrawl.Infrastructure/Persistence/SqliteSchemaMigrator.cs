using Microsoft.Data.Sqlite;

namespace HexCrawl.Infrastructure.Persistence;

public sealed class SqliteSchemaMigrator(string connectionString)
{
    public const int CurrentVersion = 2;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createMigrations = connection.CreateCommand())
        {
            createMigrations.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL
                );
                """;
            await createMigrations.ExecuteNonQueryAsync(cancellationToken);
        }

        var current = await CurrentAsync(connection, cancellationToken);
        if (current < 1)
        {
            await ApplyVersion1Async(connection, cancellationToken);
            current = 1;
        }
        if (current < 2)
        {
            await ApplyVersion2Async(connection, cancellationToken);
        }
    }

    private static async Task<int> CurrentAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ApplyVersion1Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE overworlds (
                id TEXT PRIMARY KEY,
                owner_user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                world_json TEXT NOT NULL,
                version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX ix_overworlds_owner_updated
                ON overworlds(owner_user_id, updated_at DESC);

            CREATE TABLE expeditions (
                id TEXT PRIMARY KEY,
                overworld_id TEXT NOT NULL,
                owner_user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                state_json TEXT NOT NULL,
                knowledge_json TEXT NOT NULL,
                procedure_json TEXT NOT NULL,
                pause_reason TEXT NULL,
                remaining_watch_ticks INTEGER NOT NULL,
                version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(overworld_id) REFERENCES overworlds(id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_expeditions_owner_world_updated
                ON expeditions(owner_user_id, overworld_id, updated_at DESC);

            CREATE TABLE expedition_events (
                expedition_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                kind TEXT NOT NULL,
                subject_id TEXT NULL,
                subject_type TEXT NULL,
                event_json TEXT NOT NULL,
                PRIMARY KEY(expedition_id, sequence),
                FOREIGN KEY(expedition_id) REFERENCES expeditions(id) ON DELETE CASCADE
            );

            INSERT INTO schema_migrations(version, applied_at)
            VALUES (1, $appliedAt);
            """;
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ApplyVersion2Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var disable = connection.CreateCommand())
        {
            disable.CommandText = "PRAGMA foreign_keys=OFF;";
            await disable.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                ALTER TABLE expedition_events RENAME TO expedition_events_v1;
                ALTER TABLE expeditions RENAME TO expeditions_v1;

                CREATE TABLE expeditions (
                    id TEXT PRIMARY KEY,
                    overworld_id TEXT NULL,
                    context_json TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    state_json TEXT NOT NULL,
                    knowledge_json TEXT NULL,
                    procedure_json TEXT NOT NULL,
                    pause_reason TEXT NULL,
                    remaining_watch_ticks INTEGER NOT NULL,
                    version INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(overworld_id) REFERENCES overworlds(id) ON DELETE RESTRICT
                );

                INSERT INTO expeditions(
                    id, overworld_id, context_json, owner_user_id, name, state_json,
                    knowledge_json, procedure_json, pause_reason, remaining_watch_ticks,
                    version, created_at, updated_at)
                SELECT
                    id,
                    overworld_id,
                    '{"kind":"WorldBound","overworldId":"' || overworld_id || '"}',
                    owner_user_id,
                    name,
                    state_json,
                    knowledge_json,
                    procedure_json,
                    pause_reason,
                    remaining_watch_ticks,
                    version,
                    created_at,
                    updated_at
                FROM expeditions_v1;

                CREATE TABLE expedition_events (
                    expedition_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    kind TEXT NOT NULL,
                    subject_id TEXT NULL,
                    subject_type TEXT NULL,
                    event_json TEXT NOT NULL,
                    PRIMARY KEY(expedition_id, sequence),
                    FOREIGN KEY(expedition_id) REFERENCES expeditions(id) ON DELETE CASCADE
                );

                INSERT INTO expedition_events(
                    expedition_id, sequence, kind, subject_id, subject_type, event_json)
                SELECT expedition_id, sequence, kind, subject_id, subject_type, event_json
                FROM expedition_events_v1;

                DROP TABLE expedition_events_v1;
                DROP TABLE expeditions_v1;

                CREATE INDEX ix_expeditions_owner_world_updated
                    ON expeditions(owner_user_id, overworld_id, updated_at DESC);
                CREATE INDEX ix_expeditions_owner_updated
                    ON expeditions(owner_user_id, updated_at DESC);

                INSERT INTO schema_migrations(version, applied_at)
                VALUES (2, $appliedAt);
                """;
            command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await using var enable = connection.CreateCommand();
            enable.CommandText = "PRAGMA foreign_keys=ON;";
            await enable.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Schema migration 2 produced an invalid foreign-key reference.");
        }
    }
}
