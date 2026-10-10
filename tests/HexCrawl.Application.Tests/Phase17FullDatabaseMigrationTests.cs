using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase17FullDatabaseMigrationTests
{
    [Fact]
    public async Task SchemaNineMigratesEveryWorldAndPinnedProcedureAtomically()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), Orientation = HexOrientation.FlatTop,
            Origin = new WorldPoint(12, -5), RotationDegrees = 17,
            HexRadiusWorldUnits = 2,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var map = new SourceMapRepresentation(Guid.NewGuid(), "campaign", "Map",
            SourceMapRole.Gm, "asset-content-unchanged", false,
            MapRegistrationTransform.Affine(1.4, 0, 0, 1.4, 6, -2),
            [new WorldPoint(0, 0), new WorldPoint(1, 0), new WorldPoint(1, 1)]);
        var feature = new PointFeature(Guid.NewGuid(), "Well", "water", new WorldPoint(7, 5));
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Legacy", Grid = grid,
            SourceMaps = [map], Features = [feature]
        };
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 3, now, now));
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        var savedProcedure = new StoredCampaignProcedureRevision(
            procedure, "owner", Guid.NewGuid(), null, now);
        await store.CreateCampaignProcedureRevisionAsync(savedProcedure);
        var coordinate = new HexCoordinate(-40, 92);
        var history = new CrawlRuntimeEvent(1, 2, CrawlRuntimeEventKind.WatchStarted,
            TimeSpan.FromHours(2), coordinate, "Migration-event");
        var expedition = new StoredExpedition("Running",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Position = HexGeometry.HexToWorld(grid, coordinate),
                PositionPrecision = WorldPositionPrecision.HexAnchor,
                Traversal = HexTraversalState.StartingIn(coordinate, DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(3, DistanceUnit.Miles),
                History = [history]
            },
            new WorldBoundCrawlSessionContext(world.Id), null, procedure, null,
            TimeSpan.FromHours(1), "owner", 7, now, now);
        await store.CreateExpeditionAsync(expedition);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            // Reproduce a schema-nine snapshot without touching any real tester database.
            downgrade.CommandText = """
                UPDATE overworlds
                SET world_json = world_json - 'formatVersion' - 'tiling';
                UPDATE campaign_procedure_revisions
                SET procedure_json = jsonb_set(procedure_json, '{schemaVersion}', '"1.2"'::jsonb);
                UPDATE expeditions
                SET procedure_json = jsonb_set(procedure_json, '{schemaVersion}', '"1.2"'::jsonb);
                UPDATE hex_crawl_schema_migrations SET version = 9 WHERE version = 10;
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT
                  (SELECT MAX(version) FROM hex_crawl_schema_migrations),
                  (SELECT count(*) FROM overworlds
                   WHERE world_json->>'formatVersion' = '2'
                     AND world_json ? 'tiling'
                     AND world_json->'grid'->>'id' IS NOT NULL
                     AND version = 3),
                  (SELECT count(*) FROM campaign_procedure_revisions
                   WHERE procedure_json->>'schemaVersion' = '1.3' AND revision = 1),
                  (SELECT count(*) FROM expeditions
                   WHERE procedure_json->>'schemaVersion' = '1.3' AND version = 7),
                  (SELECT count(*) FROM expedition_events
                   WHERE sequence = 1 AND kind = 'WatchStarted');
                """;
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(10, reader.GetInt64(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.Equal(1, reader.GetInt64(2));
            Assert.Equal(1, reader.GetInt64(3));
            Assert.Equal(1, reader.GetInt64(4));
        }
        var loaded = await store.GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(loaded);
        Assert.Equal(grid, loaded!.World.Grid);
        Assert.Equal(world.Id, loaded.World.Id);
        Assert.Equal(grid.Id, loaded.World.SpatialTiling.Id);
        Assert.Equal("world-unit", loaded.World.SpatialTiling.Realization.Units);
        var zero = loaded.World.SpatialTiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(0, 0))).Center;
        var east = loaded.World.SpatialTiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(1, 0))).Center;
        var physical = loaded.World.SpatialTiling.MeasurePhysicalDistance(zero, east);
        Assert.Equal(grid.NeighborCenterDistance.Unit, physical.Unit);
        Assert.InRange(Math.Abs(physical.Value - grid.NeighborCenterDistance.Value), 0, 1e-9);
        var persistedMap = Assert.Single(loaded.World.SourceMaps);
        Assert.Equal(map.Id, persistedMap.Id);
        Assert.Equal(map.AssetKey, persistedMap.AssetKey);
        Assert.Equal(map.Alignment, persistedMap.Alignment);
        Assert.Equal(map.WorldCoverageBoundary, persistedMap.WorldCoverageBoundary);
        Assert.Equal(feature.Id, Assert.Single(loaded.World.Features).Id);
        Assert.Equal(3, loaded.Version);
        Assert.Equal(new WorldCellId(grid.Id, LegacyHexTilingCompatibility.ToAddress(coordinate)),
            loaded.World.SpatialTiling.Resolve(
                LegacyHexTilingCompatibility.ToAddress(coordinate)).Id);

        var loadedProcedure = await store.GetCampaignProcedureRevisionAsync(
            procedure.ProcedureId, procedure.Revision, "owner");
        Assert.NotNull(loadedProcedure);
        Assert.Equal("1.3", loadedProcedure!.Procedure.SchemaVersion);
        Assert.Equal(savedProcedure.CampaignId, loadedProcedure.CampaignId);
        Assert.Equal(procedure.TilingDsSymbol, loadedProcedure.Procedure.TilingDsSymbol);
        var loadedExpedition = await store.GetExpeditionAsync(expedition.Id, "owner");
        Assert.NotNull(loadedExpedition);
        Assert.Equal(coordinate, loadedExpedition!.State.CurrentHex);
        Assert.Equal(7, loadedExpedition.Version);
        Assert.Equal(history.Message, Assert.Single(loadedExpedition.State.History).Message);
        Assert.Equal(procedure.Revision, loadedExpedition.CampaignProcedure.Revision);
        Assert.Equal("1.3", loadedExpedition.CampaignProcedure.SchemaVersion);
        Assert.Null(await store.GetOverworldAsync(world.Id, "not-owner"));
    }

    [Fact]
    public async Task ConcurrentSchemaNineStartupsSerializeAndUpgradeExactlyOnce()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Concurrent migration", Grid = grid
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE overworlds SET world_json = world_json - 'formatVersion' - 'tiling';
                UPDATE hex_crawl_schema_migrations SET version = 9 WHERE version = 10;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync()));
        var loaded = await store.GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(loaded);
        Assert.Equal(grid.Id, loaded!.World.SpatialTiling.Id);
        Assert.Equal(grid, loaded.World.Grid);

        await using var checkConnection = new NpgsqlConnection(database.ConnectionString);
        await checkConnection.OpenAsync();
        await using var check = checkConnection.CreateCommand();
        check.CommandText = """
            SELECT count(*) FROM hex_crawl_schema_migrations WHERE version = 10;
            """;
        Assert.Equal(1L, Convert.ToInt64(await check.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SchemaNineRejectsSnapshotIdentityMismatchAtomically()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var now = DateTimeOffset.UtcNow;
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Mismatched legacy snapshot", Grid = grid
        };
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 3, now, now));
        var wrongId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                UPDATE overworlds SET world_json = jsonb_set(
                    world_json - 'formatVersion' - 'tiling', '{id}', to_jsonb(@wrongId::text))
                WHERE id = @id;
                UPDATE hex_crawl_schema_migrations SET version = 9 WHERE version = 10;
                """;
            downgrade.Parameters.AddWithValue("id", world.Id);
            downgrade.Parameters.AddWithValue("wrongId", wrongId.ToString());
            await downgrade.ExecuteNonQueryAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync());
        Assert.Contains(world.Id.ToString(), failure.Message, StringComparison.OrdinalIgnoreCase);
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT (SELECT MAX(version) FROM hex_crawl_schema_migrations),
                   world_json->>'id', world_json ? 'tiling', version
            FROM overworlds WHERE id = @id;
            """;
        verify.Parameters.AddWithValue("id", world.Id);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(9L, reader.GetInt64(0));
        Assert.Equal(wrongId.ToString(), reader.GetString(1));
        Assert.False(reader.GetBoolean(2));
        Assert.Equal(3L, reader.GetInt64(3));
    }

    [Fact]
    public async Task SchemaNineUnknownWorldFieldsRejectBeforeAnyConversion()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Unknown legacy field", Grid = grid
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 2, now, now));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                UPDATE overworlds SET world_json = jsonb_set(
                    world_json - 'formatVersion' - 'tiling', '{futureSemanticField}',
                    '"must-not-be-dropped"'::jsonb) WHERE id = @id;
                UPDATE hex_crawl_schema_migrations SET version = 9 WHERE version = 10;
                """;
            downgrade.Parameters.AddWithValue("id", world.Id);
            await downgrade.ExecuteNonQueryAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync());
        Assert.Contains("futureSemanticField", failure.InnerException?.Message);
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT (SELECT MAX(version) FROM hex_crawl_schema_migrations),
                   world_json->>'futureSemanticField', world_json ? 'tiling', version
            FROM overworlds WHERE id = @id;
            """;
        verify.Parameters.AddWithValue("id", world.Id);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(9L, reader.GetInt64(0));
        Assert.Equal("must-not-be-dropped", reader.GetString(1));
        Assert.False(reader.GetBoolean(2));
        Assert.Equal(2L, reader.GetInt64(3));
    }

    [Fact]
    public async Task FailedWorldConversionRollsBackAllWorldsAndProcedureUpgrades()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var healthy = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Healthy", Grid = grid
        };
        var broken = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Broken", Grid = grid with { Id = Guid.NewGuid() }
        };
        await store.CreateOverworldAsync(new StoredOverworld(healthy, "owner", 1, now, now));
        await store.CreateOverworldAsync(new StoredOverworld(broken, "owner", 1, now, now));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                UPDATE overworlds SET world_json = world_json - 'tiling' - 'formatVersion';
                UPDATE overworlds SET world_json = world_json - 'grid'
                WHERE id = @broken;
                UPDATE hex_crawl_schema_migrations SET version = 9 WHERE version = 10;
                """;
            downgrade.Parameters.AddWithValue("broken", broken.Id);
            await downgrade.ExecuteNonQueryAsync();
        }

        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync());
        Assert.Contains(broken.Id.ToString(), failure.Message, StringComparison.OrdinalIgnoreCase);
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT MAX(version) FROM hex_crawl_schema_migrations),
                   (SELECT count(*) FROM overworlds WHERE world_json ? 'tiling'),
                   (SELECT count(*) FROM overworlds);
            """;
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(9, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
        Assert.Equal(2, reader.GetInt64(2));
    }
}
