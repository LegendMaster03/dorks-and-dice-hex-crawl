using Microsoft.Data.Sqlite;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class SqliteSchemaMigrationTests
{
    [Fact]
    public async Task VersionOneExpeditionBecomesExplicitWorldBoundSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hex-crawl-v1-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var worldId = Guid.NewGuid();
        var expeditionId = Guid.NewGuid();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    PRAGMA foreign_keys=ON;
                    CREATE TABLE schema_migrations (
                        version INTEGER PRIMARY KEY,
                        applied_at TEXT NOT NULL
                    );
                    CREATE TABLE overworlds (
                        id TEXT PRIMARY KEY,
                        owner_user_id TEXT NOT NULL,
                        name TEXT NOT NULL,
                        world_json TEXT NOT NULL,
                        version INTEGER NOT NULL,
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
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
                    INSERT INTO schema_migrations(version, applied_at) VALUES (1, '2026-01-01T00:00:00Z');
                    INSERT INTO overworlds(id, owner_user_id, name, world_json, version, created_at, updated_at)
                    VALUES ($world, 'alice', 'Legacy world', '{}', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                    INSERT INTO expeditions(
                        id, overworld_id, owner_user_id, name, state_json, knowledge_json,
                        procedure_json, pause_reason, remaining_watch_ticks, version, created_at, updated_at)
                    VALUES(
                        $expedition, $world, 'alice', 'Legacy crawl', '{}', '{}',
                        '{}', NULL, 0, 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                    """;
                command.Parameters.AddWithValue("$world", worldId.ToString("D"));
                command.Parameters.AddWithValue("$expedition", expeditionId.ToString("D"));
                await command.ExecuteNonQueryAsync();
            }

            await new SqliteSchemaMigrator(connectionString).MigrateAsync();

            await using var migrated = new SqliteConnection(connectionString);
            await migrated.OpenAsync();

            await using (var command = migrated.CreateCommand())
            {
                command.CommandText = "SELECT overworld_id, context_json FROM expeditions WHERE id = $id;";
                command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(worldId.ToString("D"), reader.GetString(0));
                var contextJson = reader.GetString(1);
                Assert.Contains("\"kind\":\"WorldBound\"", contextJson, StringComparison.Ordinal);
                Assert.Contains(worldId.ToString("D"), contextJson, StringComparison.OrdinalIgnoreCase);
            }

            await using (var command = migrated.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(expeditions);";
                await using var reader = await command.ExecuteReaderAsync();
                var overworldNullable = false;
                var contextRequired = false;
                var partyRequired = false;
                var generatedResolutionsRequired = false;
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(1);
                    var notNull = reader.GetInt32(3) == 1;
                    if (name == "overworld_id") overworldNullable = !notNull;
                    if (name == "context_json") contextRequired = notNull;
                    if (name == "party_json") partyRequired = notNull;
                    if (name == "generated_resolutions_json") generatedResolutionsRequired = notNull;
                }
                Assert.True(overworldNullable);
                Assert.True(contextRequired);
                Assert.True(partyRequired);
                Assert.True(generatedResolutionsRequired);
            }

            await using (var command = migrated.CreateCommand())
            {
                command.CommandText = "SELECT party_json FROM expeditions WHERE id = $id;";
                command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
                Assert.Equal("{}", Convert.ToString(await command.ExecuteScalarAsync()));
            }

            await using (var command = migrated.CreateCommand())
            {
                command.CommandText = "SELECT generated_resolutions_json FROM expeditions WHERE id = $id;";
                command.Parameters.AddWithValue("$id", expeditionId.ToString("D"));
                Assert.Equal("[]", Convert.ToString(await command.ExecuteScalarAsync()));
            }

            await using (var command = migrated.CreateCommand())
            {
                command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
                Assert.Equal(SqliteSchemaMigrator.CurrentVersion, Convert.ToInt32(await command.ExecuteScalarAsync()));
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
