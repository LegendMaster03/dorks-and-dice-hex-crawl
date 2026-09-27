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
        var humblewood = await service.CreateOverworldAsync("alice", WorldCommand("Humblewood"));
        var magnostephis = await service.CreateOverworldAsync("alice", WorldCommand("Magnostephis"));
        var sessionReady = await service.CreateOverworldAsync("alice", WorldCommand("Magnostephis — Session Ready"));
        var qaWorld = await service.CreateOverworldAsync("alice", WorldCommand("QA Magnostephis PR17 2026-09-27"));
        var realSession = await service.StartExpeditionAsync(
            magnostephis.World.Id,
            "alice",
            new StartExpeditionCommand("Original Magnostephis expedition", "simple-fixed-distance", new HexCoordinate(0, 0)));
        var qaSession = await service.StartExpeditionAsync(
            qaWorld.World.Id,
            "alice",
            new StartExpeditionCommand("Disposable QA expedition", "simple-fixed-distance", new HexCoordinate(0, 0)));

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
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(
            world.World.Id,
            "alice",
            new StartExpeditionCommand("Alice session", "simple-fixed-distance", new HexCoordinate(0, 0)));

        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.DeleteExpeditionAsync(expedition.Id, "bob", expedition.Version));

        Assert.Equal(expedition.Id, (await service.GetExpeditionAsync(expedition.Id, "alice")).Id);
    }

    [Fact]
    public async Task DeleteRunningSheetRejectsStaleVersion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = database.Service;
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(
            world.World.Id,
            "alice",
            new StartExpeditionCommand("Versioned session", "simple-fixed-distance", new HexCoordinate(0, 0)));
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
        private readonly string _path;

        private TestDatabase(string path, SqliteHexCrawlStore store)
        {
            _path = path;
            Store = store;
            Service = new HexCrawlService(store);
        }

        public SqliteHexCrawlStore Store { get; }
        public HexCrawlService Service { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"hex-crawl-expedition-delete-{Guid.NewGuid():N}.db");
            var store = new SqliteHexCrawlStore($"Data Source={path}");
            await store.InitializeAsync();
            return new TestDatabase(path, store);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = _path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            return ValueTask.CompletedTask;
        }
    }
}
