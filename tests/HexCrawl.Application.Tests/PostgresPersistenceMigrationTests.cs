using System.Globalization;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;
using HexCrawl.PersistenceMigration;
using Microsoft.Data.Sqlite;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Application.Tests;

public sealed class PostgresPersistenceMigrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task SqliteSchemaGenerationsOneThroughFiveMigrateAndVerify(int schemaVersion)
    {
        await using var source = await PostgresTestDatabase.CreateAsync();
        var seeded = await SeedRepresentativeDataAsync(source.ConnectionString);
        var sqlitePath = await ExportSqliteFixtureAsync(source.ConnectionString, schemaVersion);
        try
        {
            await using var target = await PostgresTestDatabase.CreateAsync();
            var migrator = new SqliteToPostgresMigrator(
                $"Data Source={sqlitePath};Mode=ReadOnly",
                target.ConnectionString);

            var report = await migrator.MigrateAndVerifyAsync();
            Assert.Equal(schemaVersion, report.SourceSchemaVersion);
            Assert.Equal(1, report.OverworldCount);
            Assert.Equal(schemaVersion == 1 ? 1 : 3, report.ExpeditionCount);
            Assert.True(report.EventCount > 0);
            Assert.Contains("WorldBound", report.ContextKinds);
            if (schemaVersion >= 2)
            {
                Assert.Contains("AbstractHex", report.ContextKinds);
                Assert.Contains("NonSpatial", report.ContextKinds);
            }

            var store = new PostgresHexCrawlStore(target.ConnectionString);
            await store.InitializeAsync();
            var worldBound = Assert.NotNull(await store.GetExpeditionAsync(seeded.WorldBoundId, Owner));
            Assert.Equal(seeded.WorldBoundVersion, worldBound.Version);
            Assert.NotEmpty(worldBound.Runtime.History);

            if (schemaVersion >= 4)
            {
                var generated = Assert.Single(worldBound.GeneratedProcedureResolutions);
                Assert.Equal(seeded.GeneratedResolutionId, generated.Id);
                Assert.Equal(GeneratedProcedureResolutionStatus.Consumed, generated.Status);
                Assert.Equal(ResolutionSource.AutomaticRoll, generated.Travel!.Provenance.Source);
                Assert.Equal("migration-fixture", generated.Travel.Provenance.Note);
            }
            else
            {
                Assert.Empty(worldBound.GeneratedProcedureResolutions);
            }

            if (schemaVersion >= 5)
            {
                Assert.Equal("simple-fixed-distance", worldBound.ProcedureOrigin?.PresetKey);
            }
            else
            {
                Assert.Null(worldBound.ProcedureOrigin);
            }

            if (schemaVersion >= 2)
            {
                var abstractHex = Assert.NotNull(await store.GetExpeditionAsync(seeded.AbstractHexId, Owner));
                var nonSpatial = Assert.NotNull(await store.GetExpeditionAsync(seeded.NonSpatialId, Owner));
                Assert.IsType<AbstractHexCrawlSessionContext>(abstractHex.Context);
                Assert.IsType<NonSpatialCrawlSessionContext>(nonSpatial.Context);
                Assert.Equal(schemaVersion >= 5 ? "simple-fixed-distance" : null, abstractHex.ProcedureOrigin?.PresetKey);
                Assert.Equal(schemaVersion >= 5 ? "simple-fixed-distance" : null, nonSpatial.ProcedureOrigin?.PresetKey);
            }

            // Re-running the controlled migration is safe only when the target is an exact match.
            var repeated = await migrator.MigrateAndVerifyAsync();
            Assert.Equal(report, repeated);
        }
        finally
        {
            File.Delete(sqlitePath);
        }
    }

    [Fact]
    public async Task MigrationVerificationRejectsDeliberatelyCorruptedTarget()
    {
        await using var source = await PostgresTestDatabase.CreateAsync();
        _ = await SeedRepresentativeDataAsync(source.ConnectionString);
        var sqlitePath = await ExportSqliteFixtureAsync(source.ConnectionString, 5);
        try
        {
            await using var target = await PostgresTestDatabase.CreateAsync();
            var migrator = new SqliteToPostgresMigrator(
                $"Data Source={sqlitePath};Mode=ReadOnly",
                target.ConnectionString);
            _ = await migrator.MigrateAndVerifyAsync();

            await using (var connection = new NpgsqlConnection(target.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE expeditions SET version = version + 1 WHERE name = 'Migration world-bound';";
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => migrator.VerifyAsync());
            Assert.Contains("Version differs", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(sqlitePath);
        }
    }

    private const string Owner = "migration-owner";

    private static async Task<SeededIds> SeedRepresentativeDataAsync(string connectionString)
    {
        var store = new PostgresHexCrawlStore(connectionString);
        await store.InitializeAsync();
        var core = new HexCrawlService(store);
        var world = await core.CreateOverworldAsync(Owner, new CreateOverworldCommand(
            "Migration world",
            HexOrientation.PointyTop,
            new WorldPoint(100.25, -42.5),
            7.5,
            2.5,
            12,
            DistanceUnit.Miles));
        world = await core.CreateLocationAsync(world.World.Id, Owner, new CreateLocationCommand(
            "Migration ruin",
            "ruin",
            new WorldPoint(101.5, -40.25),
            HexCrawl.Domain.World.LocationDiscoverability.Hidden,
            world.Version));

        var worldBound = await core.StartExpeditionAsync(
            world.World.Id,
            Owner,
            new StartExpeditionCommand("Migration world-bound", "simple-fixed-distance", new HexCoordinate(0, 0)));
        worldBound = await core.AdvanceExpeditionAsync(
            worldBound.Id,
            Owner,
            new AdvanceExpeditionCommand
            {
                ExpectedVersion = worldBound.Version,
                IntendedDirection = 0,
                ExpectedDistance = 12,
                ActualDistance = 12,
                ResolutionSource = ResolutionSource.ManualRoll,
                ContinueAcrossBoundaries = false
            });

        var generatedId = Guid.NewGuid();
        var generated = new GeneratedProcedureResolution(
            generatedId,
            worldBound.Id,
            worldBound.Version,
            worldBound.Version,
            worldBound.Runtime.History.Max(item => item.Sequence) + 1,
            worldBound.Runtime.CompletedWatches + 1,
            [new ProcedureResolutionRoll("migration", "1d20+2", [17], 2, 19)],
            new ProcedureResolvedTravel(
                12,
                11,
                new ResolutionProvenance(ResolutionSource.AutomaticRoll, "migration-fixture")),
            null,
            null,
            GeneratedProcedureResolutionStatus.Consumed,
            worldBound.Version + 1,
            worldBound.Runtime.History.Max(item => item.Sequence) + 2);
        var save = await store.SaveExpeditionAsync(
            worldBound with { GeneratedProcedureResolutions = [generated] },
            worldBound.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);
        worldBound = Assert.NotNull(save.Value);

        var sessions = new CrawlSessionService(store);
        var abstractHex = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Migration abstract",
                "simple-fixed-distance",
                new AbstractHexCrawlSessionContext(
                    "Migration abstract",
                    HexOrientation.FlatTop,
                    new CrawlRuntimeContext(new DistanceMeasure(6, DistanceUnit.Miles))),
                new HexCoordinate(3, -2)));
        var nonSpatial = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Migration non-spatial",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Migration non-spatial")));

        return new SeededIds(
            world.World.Id,
            worldBound.Id,
            worldBound.Version,
            abstractHex.Id,
            nonSpatial.Id,
            generatedId);
    }

    private static async Task<string> ExportSqliteFixtureAsync(string postgresConnectionString, int schemaVersion)
    {
        var worlds = new List<WorldRow>();
        var expeditions = new List<ExpeditionRow>();
        var events = new List<EventRow>();

        await using (var connection = new NpgsqlConnection(postgresConnectionString))
        {
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT id, owner_user_id, name, world_json::text, version, created_at, updated_at
                    FROM overworlds ORDER BY id;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    worlds.Add(new WorldRow(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                        ReadTimestamp(reader, 5), ReadTimestamp(reader, 6)));
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT id, overworld_id, context_json::text, owner_user_id, name, state_json::text,
                           knowledge_json::text, party_json::text, generated_resolutions_json::text,
                           procedure_json::text, procedure_origin_json::text, pause_reason,
                           remaining_watch_ticks, version, created_at, updated_at
                    FROM expeditions ORDER BY id;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (schemaVersion == 1 && reader.IsDBNull(1)) continue;
                    expeditions.Add(new ExpeditionRow(
                        reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4), reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(8),
                        reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetInt64(12), reader.GetInt64(13),
                        ReadTimestamp(reader, 14), ReadTimestamp(reader, 15)));
                }
            }

            var included = expeditions.Select(item => item.Id).ToHashSet();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT expedition_id, sequence, kind, subject_id, subject_type, event_json::text
                    FROM expedition_events ORDER BY expedition_id, sequence;
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var expeditionId = reader.GetGuid(0);
                    if (!included.Contains(expeditionId)) continue;
                    events.Add(new EventRow(
                        expeditionId, reader.GetInt64(1), reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetGuid(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
                }
            }
        }

        var path = Path.Combine(Path.GetTempPath(), $"hex-crawl-migration-{Guid.NewGuid():N}.db");
        await using var sqlite = new SqliteConnection($"Data Source={path}");
        await sqlite.OpenAsync();
        await CreateSqliteSchemaAsync(sqlite, schemaVersion);

        foreach (var world in worlds)
        {
            await using var command = sqlite.CreateCommand();
            command.CommandText = """
                INSERT INTO overworlds(id, owner_user_id, name, world_json, version, created_at, updated_at)
                VALUES($id, $owner, $name, $world, $version, $created, $updated);
                """;
            command.Parameters.AddWithValue("$id", world.Id.ToString("D"));
            command.Parameters.AddWithValue("$owner", world.OwnerUserId);
            command.Parameters.AddWithValue("$name", world.Name);
            command.Parameters.AddWithValue("$world", world.WorldJson);
            command.Parameters.AddWithValue("$version", world.Version);
            command.Parameters.AddWithValue("$created", world.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$updated", world.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();
        }

        foreach (var expedition in expeditions)
        {
            var columns = new List<string>
            {
                "id", "overworld_id", "owner_user_id", "name", "state_json", "knowledge_json",
                "procedure_json", "pause_reason", "remaining_watch_ticks", "version", "created_at", "updated_at"
            };
            var parameters = new List<string>
            {
                "$id", "$world", "$owner", "$name", "$state", "$knowledge",
                "$procedure", "$pause", "$remaining", "$version", "$created", "$updated"
            };
            if (schemaVersion >= 2) { columns.Add("context_json"); parameters.Add("$context"); }
            if (schemaVersion >= 3) { columns.Add("party_json"); parameters.Add("$party"); }
            if (schemaVersion >= 4) { columns.Add("generated_resolutions_json"); parameters.Add("$generated"); }
            if (schemaVersion >= 5) { columns.Add("procedure_origin_json"); parameters.Add("$origin"); }

            await using var command = sqlite.CreateCommand();
            command.CommandText = $"INSERT INTO expeditions({string.Join(',', columns)}) VALUES({string.Join(',', parameters)});";
            AddSqlite(command, "$id", expedition.Id.ToString("D"));
            AddSqlite(command, "$world", expedition.OverworldId?.ToString("D"));
            AddSqlite(command, "$owner", expedition.OwnerUserId);
            AddSqlite(command, "$name", expedition.Name);
            AddSqlite(command, "$state", expedition.StateJson);
            AddSqlite(command, "$knowledge", expedition.KnowledgeJson);
            AddSqlite(command, "$procedure", expedition.ProcedureJson);
            AddSqlite(command, "$pause", expedition.PauseReason);
            AddSqlite(command, "$remaining", expedition.RemainingWatchTicks);
            AddSqlite(command, "$version", expedition.Version);
            AddSqlite(command, "$created", expedition.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            AddSqlite(command, "$updated", expedition.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            if (schemaVersion >= 2) AddSqlite(command, "$context", expedition.ContextJson);
            if (schemaVersion >= 3) AddSqlite(command, "$party", expedition.PartyJson);
            if (schemaVersion >= 4) AddSqlite(command, "$generated", expedition.GeneratedResolutionsJson);
            if (schemaVersion >= 5) AddSqlite(command, "$origin", expedition.ProcedureOriginJson);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var runtimeEvent in events)
        {
            await using var command = sqlite.CreateCommand();
            command.CommandText = """
                INSERT INTO expedition_events(expedition_id, sequence, kind, subject_id, subject_type, event_json)
                VALUES($expedition, $sequence, $kind, $subjectId, $subjectType, $event);
                """;
            AddSqlite(command, "$expedition", runtimeEvent.ExpeditionId.ToString("D"));
            AddSqlite(command, "$sequence", runtimeEvent.Sequence);
            AddSqlite(command, "$kind", runtimeEvent.Kind);
            AddSqlite(command, "$subjectId", runtimeEvent.SubjectId?.ToString("D"));
            AddSqlite(command, "$subjectType", runtimeEvent.SubjectType);
            AddSqlite(command, "$event", runtimeEvent.EventJson);
            await command.ExecuteNonQueryAsync();
        }

        return path;
    }

    private static async Task CreateSqliteSchemaAsync(SqliteConnection connection, int schemaVersion)
    {
        var contextColumn = schemaVersion >= 2 ? ", context_json TEXT NOT NULL" : string.Empty;
        var partyColumn = schemaVersion >= 3 ? ", party_json TEXT NOT NULL DEFAULT '{}'" : string.Empty;
        var generatedColumn = schemaVersion >= 4 ? ", generated_resolutions_json TEXT NOT NULL DEFAULT '[]'" : string.Empty;
        var originColumn = schemaVersion >= 5 ? ", procedure_origin_json TEXT NULL" : string.Empty;
        var overworldNullability = schemaVersion == 1 ? "NOT NULL" : "NULL";
        var knowledgeNullability = schemaVersion == 1 ? "NOT NULL" : "NULL";

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
            INSERT INTO schema_migrations(version, applied_at) VALUES ({schemaVersion}, '2026-09-27T00:00:00+00:00');
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
                overworld_id TEXT {overworldNullability},
                owner_user_id TEXT NOT NULL,
                name TEXT NOT NULL,
                state_json TEXT NOT NULL,
                knowledge_json TEXT {knowledgeNullability},
                procedure_json TEXT NOT NULL,
                pause_reason TEXT NULL,
                remaining_watch_ticks INTEGER NOT NULL,
                version INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
                {contextColumn}{partyColumn}{generatedColumn}{originColumn}
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
    }

    private static void AddSqlite(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static DateTimeOffset ReadTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(reader.GetFieldValue<DateTime>(ordinal));

    private sealed record SeededIds(
        Guid WorldId,
        Guid WorldBoundId,
        long WorldBoundVersion,
        Guid AbstractHexId,
        Guid NonSpatialId,
        Guid GeneratedResolutionId);

    private sealed record WorldRow(
        Guid Id, string OwnerUserId, string Name, string WorldJson, long Version,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record ExpeditionRow(
        Guid Id, Guid? OverworldId, string ContextJson, string OwnerUserId, string Name,
        string StateJson, string? KnowledgeJson, string PartyJson, string GeneratedResolutionsJson,
        string ProcedureJson, string? ProcedureOriginJson, string? PauseReason,
        long RemainingWatchTicks, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record EventRow(
        Guid ExpeditionId, long Sequence, string Kind, Guid? SubjectId, string? SubjectType, string EventJson);
}
