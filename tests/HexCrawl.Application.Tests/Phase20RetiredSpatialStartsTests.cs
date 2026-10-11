using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase20RetiredSpatialStartsTests
{
    [Fact]
    public async Task EveryLegacySpatialStartRejectsBeforeCreatingAnExpeditionOrProcedureRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var core = new HexCrawlService(store);
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, core, procedures);
        var workbench = new ExpeditionWorkbenchService(store, core, new CrawlSessionContextResolver(core));
        var context = new AbstractHexCrawlSessionContext(
            "Old abstract world", HexOrientation.PointyTop,
            new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));
        var start = new HexCoordinate(0, 0);
        var worldId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            starter.StartStandaloneAsync("owner", "old standalone",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                context, start));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            starter.StartWorldBoundAsync(worldId, "owner", "old world",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                "dm-controlled", start));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new CrawlSessionService(store).StartAsync("owner",
                new StartStandaloneCrawlSessionCommand(
                    "old abstract", "simple-fixed-distance", context, start)));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            core.StartExpeditionAsync(worldId, "owner",
                new StartExpeditionCommand("old core", "simple-fixed-distance", start)));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            workbench.StartAsync(worldId, "owner",
                new StartExpeditionWorkbenchCommand(
                    "old workbench", "simple-fixed-distance", "dm-controlled", start)));

        Assert.Empty(await store.ListExpeditionsAsync("owner"));
        Assert.Empty(await procedures.ListLatestAsync("owner"));
    }

    [Fact]
    public async Task NonspatialStartRemainsExecutable()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var core = new HexCrawlService(store);
        var starter = new ExpeditionStartService(store, core, new CampaignProcedureService(store));
        var created = await starter.StartStandaloneAsync(
            "owner", "scene", new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
            new NonSpatialCrawlSessionContext("Story mode"));
        Assert.IsType<NonSpatialSessionState>(created.Runtime);
        var reloaded = await new PostgresHexCrawlStore(database.ConnectionString)
            .GetExpeditionAsync(created.Id, "owner");
        Assert.IsType<NonSpatialSessionState>(reloaded!.Runtime);
    }
}
