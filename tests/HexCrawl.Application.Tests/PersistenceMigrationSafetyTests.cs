using HexCrawl.PersistenceMigration;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class PersistenceMigrationSafetyTests
{
    [Fact]
    public async Task VerifyOnlyDoesNotInitializeAnUnmigratedPostgresTarget()
    {
        var sqlitePath = await CreateEmptyVersionFiveSqliteAsync();
        try
        {
            await using var target = await PostgresTestDatabase.CreateAsync();
            var migrator = new SqliteToPostgresMigrator(
                $"Data Source={sqlitePath}",
                target.ConnectionString);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => migrator.VerifyAsync());
            Assert.Contains("not initialized", exception.Message, StringComparison.OrdinalIgnoreCase);

            await using var connection = new NpgsqlConnection(target.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('hex_crawl_schema_migrations') IS NULL;";
            Assert.True((bool)(await command.ExecuteScalarAsync() ?? false));
        }
        finally
        {
            File.Delete(sqlitePath);
        }
    }

    [Fact]
    public async Task MigrationForcesCallerSuppliedSqliteConnectionStringToReadOnly()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"hex-crawl-read-only-{Guid.NewGuid():N}.db");
        try
        {
            var migrator = new SqliteToPostgresMigrator(
                $"Data Source={sqlitePath};Mode=ReadWriteCreate",
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");

            await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAndVerifyAsync());
            Assert.False(File.Exists(sqlitePath));
        }
        finally
        {
            File.Delete(sqlitePath);
        }
    }

    private static async Task<string> CreateEmptyVersionFiveSqliteAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hex-crawl-verify-only-{Guid.NewGuid():N}.db");
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
            INSERT INTO schema_migrations(version, applied_at) VALUES (5, '2026-09-27T00:00:00+00:00');
            CREATE TABLE overworlds(
                id TEXT PRIMARY KEY,
                owner_user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                world_json TEXT NOT NULL,
                version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE expeditions(
                id TEXT PRIMARY KEY,
                overworld_id TEXT NULL,
                context_json TEXT NOT NULL,
                owner_user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                state_json TEXT NOT NULL,
                knowledge_json TEXT NULL,
                party_json TEXT NOT NULL DEFAULT '{}',
                generated_resolutions_json TEXT NOT NULL DEFAULT '[]',
                procedure_json TEXT NOT NULL,
                procedure_origin_json TEXT NULL,
                pause_reason TEXT NULL,
                remaining_watch_ticks INTEGER NOT NULL,
                version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE expedition_events(
                expedition_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                kind TEXT NOT NULL,
                subject_id TEXT NULL,
                subject_type TEXT NULL,
                event_json TEXT NOT NULL,
                PRIMARY KEY(expedition_id, sequence)
            );
            """;
        await command.ExecuteNonQueryAsync();
        return path;
    }
}
