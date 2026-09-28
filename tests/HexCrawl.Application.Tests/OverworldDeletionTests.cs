using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class OverworldDeletionTests
{
    [Fact]
    public async Task DeleteOverworldRemovesOnlyRequestedOwnedWorld()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var first = await service.CreateOverworldAsync("alice", WorldCommand("First"));
        var second = await service.CreateOverworldAsync("alice", WorldCommand("Second"));
        var bob = await service.CreateOverworldAsync("bob", WorldCommand("Bob"));

        var deleted = await service.DeleteOverworldAsync(first.World.Id, "alice", first.Version);

        Assert.Equal(first.World.Id, deleted.World.Id);
        var aliceWorlds = await service.ListOverworldsAsync("alice");
        Assert.Single(aliceWorlds);
        Assert.Equal(second.World.Id, aliceWorlds[0].Id);
        Assert.Equal(bob.World.Id, Assert.Single(await service.ListOverworldsAsync("bob")).Id);
        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.GetOverworldAsync(first.World.Id, "alice"));
    }

    [Fact]
    public async Task DeleteOverworldRejectsStaleVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var updated = await service.UpdateOverworldAsync(
            world.World.Id,
            "alice",
            new UpdateOverworldCommand("Renamed", world.World.Grid, world.Version));

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            service.DeleteOverworldAsync(world.World.Id, "alice", world.Version));

        Assert.Equal(updated.Version, (await service.GetOverworldAsync(world.World.Id, "alice")).Version);
    }

    [Fact]
    public async Task DeleteOverworldRejectsWorldWithSavedExpedition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        _ = await service.StartExpeditionAsync(
            world.World.Id,
            "alice",
            new StartExpeditionCommand("Active", "simple-fixed-distance", new HexCoordinate(0, 0)));

        await Assert.ThrowsAsync<HexCrawlConflictException>(() =>
            service.DeleteOverworldAsync(world.World.Id, "alice", world.Version));

        Assert.Equal(world.World.Id, (await service.GetOverworldAsync(world.World.Id, "alice")).World.Id);
    }

    private static CreateOverworldCommand WorldCommand(string name = "World") => new(
        name,
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        private string ConnectionString => _database.ConnectionString;

        private TestDatabase(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database);
        }

        public async Task<HexCrawlService> ServiceAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            return new HexCrawlService(store);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
