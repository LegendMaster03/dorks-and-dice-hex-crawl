using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ExpeditionDeletionTests
{
    [Fact]
    public async Task DeleteRunningSheetUnlocksOnlyItsQaWorldAndPreservesRealData()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = database.Service;
        var humblewood = await service.CreatePeriodicOverworldAsync("alice", "Humblewood", MaplessSessionFixture.Start().World);
        var magnostephis = await service.CreatePeriodicOverworldAsync("alice", "Magnostephis", MaplessSessionFixture.Start().World);
        var sessionReady = await service.CreatePeriodicOverworldAsync("alice", "Magnostephis — Session Ready", MaplessSessionFixture.Start().World);
        var qaWorld = await service.CreatePeriodicOverworldAsync("alice", "QA Magnostephis PR17 2026-09-27", MaplessSessionFixture.Start().World);
        var realSession = await StartCellAsync(database, magnostephis.World.Id, "Original Magnostephis expedition");
        var qaSession = await StartCellAsync(database, qaWorld.World.Id, "Disposable QA expedition");

        await Assert.ThrowsAsync<HexCrawlConflictException>(() =>
            service.DeleteOverworldAsync(qaWorld.World.Id, "alice", qaWorld.Version));

        var deleted = await service.DeleteExpeditionAsync(qaSession.Id, "alice", qaSession.Version);

        Assert.Equal(qaSession.Id, deleted.Id);
        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() => service.GetExpeditionAsync(qaSession.Id, "alice"));
        Assert.Equal(realSession.Id, (await service.GetExpeditionAsync(realSession.Id, "alice")).Id);
        Assert.Empty(await service.ListExpeditionsAsync(qaWorld.World.Id, "alice"));

        await service.DeleteOverworldAsync(qaWorld.World.Id, "alice", qaWorld.Version);
        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.GetOverworldAsync(qaWorld.World.Id, "alice"));

        var worlds = await service.ListOverworldsAsync("alice");
        Assert.Equal(3, worlds.Count);
        Assert.Contains(worlds, world => world.Id == humblewood.World.Id);
        Assert.Contains(worlds, world => world.Id == magnostephis.World.Id);
        Assert.Contains(worlds, world => world.Id == sessionReady.World.Id);
        Assert.DoesNotContain(worlds, world => world.Id == qaWorld.World.Id);

        var magnostephisSessions = await service.ListExpeditionsAsync(magnostephis.World.Id, "alice");
        Assert.Equal(realSession.Id, Assert.Single(magnostephisSessions).Id);
    }

    [Fact]
    public async Task DeleteRunningSheetRejectsOtherOwner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = database.Service;
        var world = await service.CreatePeriodicOverworldAsync("alice", "Owner isolation", MaplessSessionFixture.Start().World);
        var expedition = await StartCellAsync(database, world.World.Id, "Alice session");

        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.DeleteExpeditionAsync(expedition.Id, "bob", expedition.Version));

        Assert.Equal(expedition.Id, (await service.GetExpeditionAsync(expedition.Id, "alice")).Id);
    }

    [Fact]
    public async Task DeleteRunningSheetRejectsStaleVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = database.Service;
        var world = await service.CreatePeriodicOverworldAsync("alice", "Version conflict", MaplessSessionFixture.Start().World);
        var expedition = await StartCellAsync(database, world.World.Id, "Versioned session");
        var saved = await database.Store.SaveExpeditionAsync(
            expedition with { Name = "Updated version" },
            expedition.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        Assert.NotNull(saved.Value);

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            service.DeleteExpeditionAsync(expedition.Id, "alice", expedition.Version));

        var reloaded = await service.GetExpeditionAsync(expedition.Id, "alice");
        Assert.Equal(saved.Value!.Version, reloaded.Version);
        Assert.Equal("Updated version", reloaded.Name);
    }

    private static Task<StoredExpedition> StartCellAsync(
        TestDatabase database, Guid worldId, string name) =>
        new ExpeditionStartService(
            database.Store, database.Service, new CampaignProcedureService(database.Store))
        .StartWorldBoundCellsAsync(
            worldId, "alice", name,
            new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
            new PeriodicCellAddress(
                database.Service.GetOverworldAsync(worldId, "alice").GetAwaiter().GetResult()
                    .World.SpatialTiling.Topology.MotifCells[0].Id,
                new LatticeDisplacement(0, 0)));

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;

        private TestDatabase(PostgresTestDatabase database, PostgresHexCrawlStore store)
        {
            _database = database;
            Store = store;
            Service = new HexCrawlService(store);
        }

        public PostgresHexCrawlStore Store { get; }
        public HexCrawlService Service { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database, store);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
