using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase20OneTimeStartupCutoverTests
{
    [Fact]
    public async Task Schema11UpgradePreservesAuthoredWorldAssetsAndFacts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var gridId = Guid.NewGuid();
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
            Id = Guid.NewGuid(), Name = "Persisted authored world",
            Tiling = CurrentPeriodicFixture(),
            Features = [feature],
            SourceMaps = [map],
            EnvironmentAnnotations =
            [
                new EnvironmentAnnotation
                {
                    Id = Guid.NewGuid(),
                    Scope = new EnvironmentAnnotationScope
                    {
                        Kind = EnvironmentAnnotationScopeKind.World
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
                ALTER TABLE expeditions RENAME CONSTRAINT ck_expeditions_runtime_kind TO ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 11 WHERE version = 12;
                -- Recreate the persisted pre-cutover scope as raw archived JSON.
                -- The current domain intentionally has no axial environment scope.
                UPDATE overworlds SET world_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(world_json - 'tiling', '{grid}', @archivedGrid::jsonb),
                        '{formatVersion}', '1'::jsonb),
                    '{environmentAnnotations,0,scope}',
                    '{"kind":"Hex","hex":{"q":2,"r":-1}}'::jsonb)
                WHERE id = @legacyWorld;
                """;
            downgrade.Parameters.AddWithValue("legacyWorld", world.Id);
            downgrade.Parameters.AddWithValue("archivedGrid", NpgsqlTypes.NpgsqlDbType.Jsonb, ArchivedGridJson(gridId, 2, 6));
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
            Assert.Equal(gridId, scope.GetProperty("cell").GetProperty("tilingId").GetGuid());
            Assert.Equal("owner", reader.GetString(1));
            Assert.Equal(7L, reader.GetInt64(2));
            Assert.Equal(createdAt.ToUnixTimeSeconds(),
                new DateTimeOffset(reader.GetFieldValue<DateTime>(3)).ToUnixTimeSeconds());
        }

        var reloaded = await new PostgresHexCrawlStore(database.ConnectionString)
            .GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(reloaded);
        Assert.Equal(gridId, reloaded!.World.SpatialTiling.Id);
        Assert.Equal(map.AssetKey, Assert.Single(reloaded.World.SourceMaps).AssetKey);
        Assert.Equal(map.Alignment, Assert.Single(reloaded.World.SourceMaps).Alignment);
        Assert.Equal(feature.Id, Assert.Single(reloaded.World.Features).Id);
        var annotation = Assert.Single(reloaded.World.EnvironmentAnnotations);
        Assert.Equal(EnvironmentAnnotationScopeKind.Cell, annotation.Scope.Kind);
        Assert.Equal(new WorldCellId(gridId,
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

        static OverworldDefinition MakeWorld(string name) => new()
        {
            Id = Guid.NewGuid(), Name = name,
            Tiling = CurrentPeriodicFixture()
        };
        var valid = MakeWorld("Valid authored world");
        var invalid = MakeWorld("Unrecognized authored format");
        var validGridId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(valid, "owner", 1, timestamp, timestamp));
        await store.CreateOverworldAsync(new StoredOverworld(invalid, "owner", 2, timestamp, timestamp));

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                ALTER TABLE expeditions RENAME CONSTRAINT ck_expeditions_runtime_kind TO ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 11 WHERE version = 12;
                UPDATE overworlds SET world_json = jsonb_set(
                    jsonb_set(world_json - 'tiling', '{grid}', @validGrid::jsonb),
                    '{formatVersion}', '1'::jsonb)
                WHERE id = @valid;
                UPDATE overworlds
                SET world_json = jsonb_set(world_json, '{formatVersion}', '99'::jsonb)
                WHERE id = @invalid;
                """;
            downgrade.Parameters.AddWithValue("valid", valid.Id);
            downgrade.Parameters.AddWithValue("validGrid", NpgsqlTypes.NpgsqlDbType.Jsonb, ArchivedGridJson(validGridId, 1, 6));
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
        Assert.Equal(11L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal("99", reader.GetString(2));
        Assert.Equal(2L, reader.GetInt64(3));
    }

    [Theory]
    [InlineData("{\"q\":2}")]
    [InlineData("{\"r\":-1}")]
    [InlineData("{\"q\":2.5,\"r\":-1}")]
    public async Task IncompleteArchivedAxialScopeRollsBackWithoutChangingAuthoredWorld(string coordinates)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Authored scope must not move",
            Tiling = CurrentPeriodicFixture(),
            EnvironmentAnnotations =
            [
                new EnvironmentAnnotation
                {
                    Id = Guid.NewGuid(),
                    Scope = new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.World },
                    Facts =
                    [
                        new EnvironmentFact
                        {
                            Id = Guid.NewGuid(), Dimension = "terrain",
                            ValueKind = EnvironmentValueKind.Tag, Tag = "forest",
                            Provenance = "authored"
                        }
                    ]
                }
            ]
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var downgrade = connection.CreateCommand())
        {
            downgrade.CommandText = """
                ALTER TABLE expeditions RENAME CONSTRAINT ck_expeditions_runtime_kind TO ck_expeditions_runtime_v2_only;
                UPDATE hex_crawl_schema_migrations SET version = 11 WHERE version = 12;
                UPDATE overworlds SET world_json = jsonb_set(
                    jsonb_set(
                        jsonb_set(world_json - 'tiling', '{grid}', @grid::jsonb),
                        '{formatVersion}', '1'::jsonb),
                    '{environmentAnnotations,0,scope}', @scope::jsonb)
                WHERE id = @id;
                """;
            downgrade.Parameters.AddWithValue("id", world.Id);
            downgrade.Parameters.AddWithValue("grid", NpgsqlTypes.NpgsqlDbType.Jsonb,
                ArchivedGridJson(Guid.NewGuid(), 2, 6));
            downgrade.Parameters.AddWithValue("scope", NpgsqlTypes.NpgsqlDbType.Jsonb,
                "{\"kind\":\"Hex\",\"hex\":" + coordinates + "}");
            await downgrade.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PostgresSchemaMigrator(database.ConnectionString).MigrateAsync());
        await using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT (SELECT max(version) FROM hex_crawl_schema_migrations),
                   world_json->'environmentAnnotations'->0->'scope'->'hex',
                   world_json ? 'grid'
            FROM overworlds WHERE id = @id;
            """;
        check.Parameters.AddWithValue("id", world.Id);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(11L, reader.GetInt64(0));
        using var original = JsonDocument.Parse(reader.GetString(1));
        using var submitted = JsonDocument.Parse(coordinates);
        Assert.Equal(submitted.RootElement.GetRawText(), original.RootElement.GetRawText());
        Assert.True(reader.GetBoolean(2));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":null}")]
    [InlineData("{\"kind\":\"UnrecognizedKind\"}")]
    [InlineData("[]")]
    public async Task RuntimeConstraintRejectsMissingNullUnknownAndNonObjectKinds(string snapshot)
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

    private static PeriodicWorldTiling CurrentPeriodicFixture()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:6,3>", 1d, "world-unit");
        if (realized.Status != "realized")
            throw new InvalidOperationException("World fixture periodic geometry did not realize.");
        return new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!,
            new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(6, DistanceUnit.Miles));
    }

    private static string ArchivedGridJson(Guid id, double radius, double distance)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return JsonSerializer.Serialize(new
        {
            id,
            orientation = "PointyTop",
            coordinateConvention = "AxialQr",
            origin = new WorldPoint(0, 0),
            rotationDegrees = 0d,
            hexRadiusWorldUnits = radius,
            neighborCenterDistance = new DistanceMeasure(distance, DistanceUnit.Miles)
        }, options);
    }

}
