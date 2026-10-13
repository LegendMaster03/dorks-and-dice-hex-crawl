using System.Text.Json;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase20OneTimeStartupCutoverTests
{
    [Fact]
    public async Task Schema10UpgradeConvertsAuthoredHexWorldWithoutLosingAssetsOrFacts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            HexRadiusWorldUnits = 2,
            NeighborCenterDistance = new DistanceMeasure(6, DistanceUnit.Miles)
        };
        var feature = new PointFeature(Guid.NewGuid(), "Ancient tower", "structure",
            new WorldPoint(14, -7));
        var fact = new EnvironmentFact
        {
            Id = Guid.NewGuid(), Dimension = "terrain",
            ValueKind = EnvironmentValueKind.Tag, Tag = "woodland",
            Provenance = "owner-authored"
        };
        var map = new SourceMapRepresentation(
            Guid.NewGuid(), "region-a", "Registered map",
            SourceMapRole.Gm, "owned/maps/asset.png", true,
            MapRegistrationTransform.Affine(2, 0, 0, 2, 12, -3),
            [new WorldPoint(0, 0), new WorldPoint(50, 0),
             new WorldPoint(50, 50), new WorldPoint(0, 50)],
            100, 100, "image/png", "original.png");
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Persisted authored world", Grid = grid,
            Features = [feature],
            SourceMaps = [map],
            EnvironmentAnnotations =
            [
                new EnvironmentAnnotation
                {
                    Id = Guid.NewGuid(),
                    Scope = new EnvironmentAnnotationScope
                    {
                        Kind = EnvironmentAnnotationScopeKind.Hex,
                        Hex = new HexCoordinate(2, -1)
                    },
                    Facts = [fact]
                }
            ]
        };
        var createdAt = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(
            world, "owner", 7, createdAt, createdAt));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        await using (var verify = connection.CreateCommand())
        {
            verify.CommandText = """
                SELECT world_json::text, owner_user_id, version, created_at
                FROM overworlds WHERE id = @id;
                """;
            verify.Parameters.AddWithValue("id", world.Id);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            using var json = JsonDocument.Parse(reader.GetString(0));
            var root = json.RootElement;
            Assert.False(root.TryGetProperty("grid", out _));
            Assert.Equal(3, root.GetProperty("formatVersion").GetInt32());
            Assert.NotEqual(JsonValueKind.Null, root.GetProperty("tiling").ValueKind);
            var scope = root.GetProperty("environmentAnnotations")[0].GetProperty("scope");
            Assert.Equal("Cell", scope.GetProperty("kind").GetString());
            Assert.False(scope.TryGetProperty("hex", out _));
            Assert.Equal(grid.Id, scope.GetProperty("cell").GetProperty("tilingId").GetGuid());
            Assert.Equal("owner", reader.GetString(1));
            Assert.Equal(7L, reader.GetInt64(2));
            Assert.Equal(createdAt.ToUnixTimeSeconds(),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(3)).ToUnixTimeSeconds());
        }

        var reloaded = await new PostgresHexCrawlStore(database.ConnectionString)
            .GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(reloaded);
        Assert.Equal(grid.Id, reloaded!.World.SpatialTiling.Id);
        Assert.Equal(map.AssetKey, Assert.Single(reloaded.World.SourceMaps).AssetKey);
        Assert.Equal(map.Alignment, Assert.Single(reloaded.World.SourceMaps).Alignment);
        Assert.Equal(feature.Id, Assert.Single(reloaded.World.Features).Id);
        var annotation = Assert.Single(reloaded.World.EnvironmentAnnotations);
        Assert.Equal(EnvironmentAnnotationScopeKind.Cell, annotation.Scope.Kind);
        Assert.Equal(new WorldCellId(grid.Id,
            new PeriodicCellAddress("hex", new LatticeDisplacement(2, -1))),
            annotation.Scope.Cell);
        Assert.Equal(fact.Id, Assert.Single(annotation.Facts).Id);
        Assert.Equal(fact.Provenance, annotation.Facts[0].Provenance);
    }

    [Fact]
    public async Task InvalidAuthoredWorldPreventsTheEntireStartupConversion()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        static OverworldDefinition World(string name) => new()
        {
            Id = Guid.NewGuid(), Name = name,
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(),
                HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(6, DistanceUnit.Miles)
            }
        };
        var valid = World("Valid authored world");
        var invalid = World("Unrecognized authored format");
        var timestamp = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(valid, "owner", 1, timestamp, timestamp));
        await store.CreateOverworldAsync(new StoredOverworld(invalid, "owner", 2, timestamp, timestamp));

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                UPDATE overworlds
                SET world_json = jsonb_set(world_json, '{formatVersion}', '99'::jsonb)
                WHERE id = @invalid;
                """;
            downgrade.Parameters.AddWithValue("invalid", invalid.Id);
            await downgrade.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync());
        Assert.Contains("Unknown authored-world snapshot format", exception.Message);
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                   (SELECT world_json ? 'grid' FROM overworlds WHERE id = @valid),
                   (SELECT world_json->>'formatVersion' FROM overworlds WHERE id = @invalid),
                   (SELECT count(*) FROM overworlds WHERE owner_user_id = 'owner');
            """;
        check.Parameters.AddWithValue("valid", valid.Id);
        check.Parameters.AddWithValue("invalid", invalid.Id);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal("99", reader.GetString(2));
        Assert.Equal(2L, reader.GetInt64(3));
    }

    [Fact]
    public async Task StartupUpgradeRemovesV1AndItsEventsButPreservesOtherKindsAndUnrelatedRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();

        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var nonspatial = Guid.NewGuid();
        var savedProcedure = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            // Simulate an already-deployed schema-10 database.
            seed.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                INSERT INTO campaign_procedure_revisions
                    (procedure_id, revision, owner_user_id, procedure_json, created_at)
                VALUES (@procedure, 1, 'owner', '{"source":"kept"}'::jsonb, now());
                INSERT INTO expeditions
                    (id, owner_user_id, name, context_json, state_json,
                     party_json, environment_json, effects_json, resources_json,
                     survival_json, journey_state_json, generated_resolutions_json,
                     procedure_json, remaining_watch_ticks, version,
                     created_at, updated_at)
                VALUES
                    (@v1, 'owner', 'old hex', '{}'::jsonb,
                     '{"kind":"Spatial","progress":{"value":0.5}}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 3, now(), now()),
                    (@v2, 'owner', 'cell', '{}'::jsonb,
                     '{"kind":"CellSpatial"}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 7, now(), now()),
                    (@nonspatial, 'owner', 'scene', '{}'::jsonb,
                     '{"kind":"NonSpatial"}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                     '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 2, now(), now());
                INSERT INTO expedition_events (expedition_id, sequence, kind, event_json)
                VALUES (@v1, 1, 'WatchStarted', '{}'::jsonb),
                       (@v2, 1, 'WatchStarted', '{}'::jsonb);
                """;
            seed.Parameters.AddWithValue("procedure", savedProcedure);
            seed.Parameters.AddWithValue("v1", v1);
            seed.Parameters.AddWithValue("v2", v2);
            seed.Parameters.AddWithValue("nonspatial", nonspatial);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(); // restart is idempotent
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                       (SELECT count(*) FROM expeditions WHERE id = @v1),
                       (SELECT count(*) FROM expeditions WHERE id = @v2 AND version = 7),
                       (SELECT count(*) FROM expeditions WHERE id = @nonspatial AND version = 2),
                       (SELECT count(*) FROM expedition_events WHERE expedition_id = @v1),
                       (SELECT count(*) FROM expedition_events WHERE expedition_id = @v2),
                       (SELECT count(*) FROM campaign_procedure_revisions WHERE procedure_id = @procedure);
                """;
            check.Parameters.AddWithValue("procedure", savedProcedure);
            check.Parameters.AddWithValue("v1", v1);
            check.Parameters.AddWithValue("v2", v2);
            check.Parameters.AddWithValue("nonspatial", nonspatial);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(11L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.Equal(0L, reader.GetInt64(4));
            Assert.Equal(1L, reader.GetInt64(5));
            Assert.Equal(1L, reader.GetInt64(6));
        }

        await using var forbidden = connection.CreateCommand();
        forbidden.CommandText = """
            INSERT INTO expeditions
                (id, owner_user_id, name, context_json, state_json,
                 party_json, environment_json, effects_json, resources_json,
                 survival_json, journey_state_json, generated_resolutions_json,
                 procedure_json, remaining_watch_ticks, version, created_at, updated_at)
            VALUES (gen_random_uuid(), 'owner', 'old', '{}'::jsonb,
                    '{"kind":"Spatial"}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 1, now(), now());
            """;
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
        Assert.Equal("23514", rejected.SqlState);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":null}")]
    [InlineData("{\"kind\":\"Spatial\"}")]
    [InlineData("[]")]
    public async Task V2OnlyConstraintRejectsMissingNullLegacyAndNonObjectKinds(string snapshot)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO expeditions
                (id, owner_user_id, name, context_json, state_json,
                 party_json, environment_json, effects_json, resources_json,
                 survival_json, journey_state_json, generated_resolutions_json,
                 procedure_json, remaining_watch_ticks, version, created_at, updated_at)
            VALUES (gen_random_uuid(), 'owner', 'invalid snapshot', '{}'::jsonb, @state,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                    0, 1, now(), now());
            """;
        command.Parameters.AddWithValue("state", NpgsqlTypes.NpgsqlDbType.Jsonb, snapshot);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("23514", error.SqlState);
    }

    [Theory]
    [InlineData("FutureKind")]
    [InlineData(null)]
    public async Task UnknownRuntimeKindRollsBackEntireUpgradeAndPreservesOldData(string? unknownKind)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var migrator = new PostgresSchemaMigrator(database.ConnectionString);
        await migrator.MigrateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                ALTER TABLE expeditions DROP CONSTRAINT ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 10 WHERE version = 11;
                INSERT INTO expeditions
                    (id, owner_user_id, name, context_json, state_json,
                     party_json, environment_json, effects_json, resources_json,
                     survival_json, journey_state_json, generated_resolutions_json,
                     procedure_json, remaining_watch_ticks, version, created_at, updated_at)
                SELECT gen_random_uuid(), 'owner', 'old', '{}'::jsonb,
                       jsonb_build_object('kind', value),
                       '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb,
                       '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, '{}'::jsonb, 0, 1, now(), now()
                FROM (VALUES ('Spatial'), (@unknownKind)) as kinds(value);
                """;
            seed.Parameters.Add("unknownKind", NpgsqlTypes.NpgsqlDbType.Text).Value =
                unknownKind is null ? DBNull.Value : unknownKind;
            await seed.ExecuteNonQueryAsync();
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync());
        Assert.Contains("No tester records were changed", failure.Message);
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                   (SELECT count(*) FROM expeditions),
                   (SELECT count(*) FROM expeditions WHERE state_json->>'kind' = 'Spatial');
            """;
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(10L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }
}
