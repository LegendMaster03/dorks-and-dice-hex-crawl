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
        var first = await service.CreatePeriodicOverworldAsync("alice", "First", MaplessSessionFixture.Start().World);
        var second = await service.CreatePeriodicOverworldAsync("alice", "Second", MaplessSessionFixture.Start().World);
        var bob = await service.CreatePeriodicOverworldAsync("bob", "Bob", MaplessSessionFixture.Start().World);

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
        var world = await service.CreatePeriodicOverworldAsync("alice", "World", MaplessSessionFixture.Start().World);
        var updated = await service.RenameOverworldAsync(
            world.World.Id, "alice", "Renamed", world.Version);

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            service.DeleteOverworldAsync(world.World.Id, "alice", world.Version));

        Assert.Equal(updated.Version, (await service.GetOverworldAsync(world.World.Id, "alice")).Version);
    }

    [Fact]
    public async Task DeleteOverworldRejectsWorldWithSavedExpedition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreatePeriodicOverworldAsync("alice", "World", MaplessSessionFixture.Start().World);
        await database.StartCellAsync(world.World.Id, "Active");

        await Assert.ThrowsAsync<HexCrawlConflictException>(() =>
            service.DeleteOverworldAsync(world.World.Id, "alice", world.Version));

        Assert.Equal(world.World.Id, (await service.GetOverworldAsync(world.World.Id, "alice")).World.Id);
    }

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

        public async Task StartCellAsync(Guid worldId, string name)
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            var service = new HexCrawlService(store);
            await new ExpeditionStartService(store, service, new CampaignProcedureService(store))
                .StartWorldBoundCellsAsync(
                    worldId, "alice", name,
                    new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                    new PeriodicCellAddress("face-0", new LatticeDisplacement(0, 0)));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
