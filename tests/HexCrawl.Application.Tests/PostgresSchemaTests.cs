using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class PostgresSchemaTests
{
    [Fact]
    public async Task SchemaUsesNativePostgresTypesAndSingleRequiredProcedureSnapshot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.InitializeAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var columns = new Dictionary<(string Table, string Column), (string Type, bool Nullable)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT table_name, column_name, data_type, is_nullable
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name IN ('overworlds', 'expeditions', 'expedition_events');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns[(reader.GetString(0), reader.GetString(1))] =
                    (reader.GetString(2), string.Equals(reader.GetString(3), "YES", StringComparison.Ordinal));
            }
        }

        Assert.Equal("uuid", columns[("overworlds", "id")].Type);
        Assert.Equal("jsonb", columns[("overworlds", "world_json")].Type);
        Assert.Equal("bigint", columns[("overworlds", "version")].Type);
        Assert.Equal("timestamp with time zone", columns[("overworlds", "created_at")].Type);
        Assert.Equal("uuid", columns[("expeditions", "overworld_id")].Type);
        Assert.Equal("jsonb", columns[("expeditions", "procedure_json")].Type);
        Assert.False(columns[("expeditions", "procedure_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "environment_json")].Type);
        Assert.False(columns[("expeditions", "environment_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "effects_json")].Type);
        Assert.False(columns[("expeditions", "effects_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "resources_json")].Type);
        Assert.False(columns[("expeditions", "resources_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "survival_json")].Type);
        Assert.False(columns[("expeditions", "survival_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "journey_state_json")].Type);
        Assert.False(columns[("expeditions", "journey_state_json")].Nullable);
        Assert.Equal("jsonb", columns[("expeditions", "procedure_origin_json")].Type);
        Assert.DoesNotContain(("expeditions", "campaign_procedure_json"), columns.Keys);
        Assert.Equal("bigint", columns[("expedition_events", "sequence")].Type);
        Assert.Equal("jsonb", columns[("expedition_events", "event_json")].Type);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(version) FROM hex_crawl_schema_migrations;";
        Assert.Equal(PostgresSchemaMigrator.CurrentVersion, Convert.ToInt32(await versionCommand.ExecuteScalarAsync()));
        Assert.Equal(10, PostgresSchemaMigrator.CurrentVersion);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public async Task RetiredOrFutureSchemaVersionsFailClosedWithoutChangingExistingRecords(int oldVersion)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        var procedureId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                UPDATE hex_crawl_schema_migrations SET version = @oldVersion WHERE version = 10;
                INSERT INTO campaign_procedure_revisions
                    (procedure_id, revision, owner_user_id, procedure_json, created_at)
                VALUES (@id, 1, 'preservation-test',
                    '{"schemaVersion":"1.2","tilingDsSymbol":"<1:1,1,1:6,3>"}'::jsonb, now());
                """;
            seed.Parameters.AddWithValue("oldVersion", oldVersion);
            seed.Parameters.AddWithValue("id", procedureId);
            await seed.ExecuteNonQueryAsync();
        }

        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());
        Assert.Contains("existing database has been preserved", failure.Message,
            StringComparison.OrdinalIgnoreCase);

        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                (SELECT count(*) FROM campaign_procedure_revisions
                 WHERE procedure_id = @id AND revision = 1
                 AND procedure_json->>'schemaVersion' = '1.2');
            """;
        verify.Parameters.AddWithValue("id", procedureId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((long)oldVersion, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    [Fact]
    public async Task UnsupportedSchemaVersionFailsWithoutDiscardingTesterRecords()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedureId = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                UPDATE hex_crawl_schema_migrations SET version = 7 WHERE version = 10;
                INSERT INTO campaign_procedure_revisions
                    (procedure_id, revision, owner_user_id, procedure_json, created_at)
                VALUES (@id, 1, 'preservation-test',
                    '{"schemaVersion":"1.2","tilingDsSymbol":"<1:1,1,1:6,3>"}'::jsonb, now());
                """;
            seed.Parameters.AddWithValue("id", procedureId);
            await seed.ExecuteNonQueryAsync();
        }

        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());
        Assert.Contains("existing database has been preserved", failure.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT MAX(version) FROM hex_crawl_schema_migrations),
                   (SELECT count(*) FROM campaign_procedure_revisions
                    WHERE procedure_id = @id AND revision = 1);
            """;
        check.Parameters.AddWithValue("id", procedureId);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(7, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
    }

    [Fact]
    public async Task LargeWorldSnapshotRoundTripsWithoutChangingAggregateVersion()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var worldId = Guid.NewGuid();
        var locations = Enumerable.Range(0, 1000)
            .Select(index => new Location(
                Guid.NewGuid(),
                $"Location {index:D4} {new string('x', 80)}",
                "migration-volume-test",
                new WorldPoint(index / 10d, -(index / 20d)),
                LocationDiscoverability.Hidden,
                []))
            .ToArray();
        var world = new OverworldDefinition
        {
            Id = worldId,
            Name = "Large representative PostgreSQL snapshot",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(),
                Orientation = HexOrientation.PointyTop,
                Origin = new WorldPoint(0, 0),
                RotationDegrees = 0,
                HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            },
            Locations = locations
        };
        var now = DateTimeOffset.UtcNow;
        _ = await store.CreateOverworldAsync(new StoredOverworld(world, "large-owner", 37, now, now));

        var loaded = await store.GetOverworldAsync(worldId, "large-owner");
        Assert.NotNull(loaded);
        Assert.Equal(37, loaded!.Version);
        Assert.Equal(1000, loaded.World.Locations.Count);
        var expectedLocation = locations[937];
        var actualLocation = loaded.World.Locations[937];
        Assert.Equal(expectedLocation.Id, actualLocation.Id);
        Assert.Equal(expectedLocation.Name, actualLocation.Name);
        Assert.Equal(expectedLocation.Category, actualLocation.Category);
        Assert.Equal(expectedLocation.Position, actualLocation.Position);
        Assert.Equal(expectedLocation.Discoverability, actualLocation.Discoverability);
        Assert.Equal(expectedLocation.DetailMaps.ToArray(), actualLocation.DetailMaps.ToArray());
    }

    [Fact]
    public async Task StoreHonorsPreCancelledOperations()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ListOverworldsAsync("cancelled", source.Token));
    }
}