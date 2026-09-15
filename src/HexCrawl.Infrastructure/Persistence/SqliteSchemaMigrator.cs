using Microsoft.Data.Sqlite;

namespace HexCrawl.Infrastructure.Persistence;

public sealed class SqliteSchemaMigrator(string connectionString)
{
    public const int CurrentVersion = 1;

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

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var current = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        if (current >= CurrentVersion)
        {
            return;
        }

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
}
