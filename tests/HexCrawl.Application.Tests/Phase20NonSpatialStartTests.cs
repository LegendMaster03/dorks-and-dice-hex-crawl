using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase20NonSpatialStartTests
{
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
