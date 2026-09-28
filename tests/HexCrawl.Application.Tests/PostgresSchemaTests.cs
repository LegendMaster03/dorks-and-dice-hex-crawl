using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class PostgresSchemaTests
{
    [Fact]
    public async Task SchemaUsesNativePostgresTypesAndExpectedForeignKeys()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.InitializeAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var columns = new Dictionary<(string Table, string Column), string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT table_name, column_name, data_type
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name IN ('overworlds', 'expeditions', 'expedition_events');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
            }
        }

        Assert.Equal("uuid", columns[("overworlds", "id")]);
        Assert.Equal("jsonb", columns[("overworlds", "world_json")]);
        Assert.Equal("bigint", columns[("overworlds", "version")]);
        Assert.Equal("timestamp with time zone", columns[("overworlds", "created_at")]);
        Assert.Equal("uuid", columns[("expeditions", "overworld_id")]);
        Assert.Equal("jsonb", columns[("expeditions", "procedure_json")]);
        Assert.Equal("jsonb", columns[("expeditions", "procedure_origin_json")]);
        Assert.Equal("bigint", columns[("expedition_events", "sequence")]);
        Assert.Equal("jsonb", columns[("expedition_events", "event_json")]);
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
