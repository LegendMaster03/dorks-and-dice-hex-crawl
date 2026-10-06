using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ExpeditionStartServiceTests
{
    [Fact]
    public async Task PresetStartPersistsCampaignOwnedProcedureBeforePinningSessionSnapshot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);

        var session = await starter.StartStandaloneAsync(
            "alice",
            "Preset session",
            new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
            new NonSpatialCrawlSessionContext("Procedure clock"));

        var saved = Assert.Single(await procedures.ListLatestAsync("alice"));
        Assert.Equal(saved.ProcedureId, session.CampaignProcedure.ProcedureId);
        Assert.Equal(saved.Revision, session.CampaignProcedure.Revision);
        Assert.Equal("simple-fixed-distance", saved.ProcedureOrigin?.PresetKey);
        Assert.Equal(saved.ProcedureOrigin, session.ProcedureOrigin);
    }

    [Fact]
    public async Task SavedHistoricalRevisionStartPinsExactlyTheSelectedRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);

        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        await composer.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [new CampaignProcedureOverride(
                "six-hour-start-proof",
                GenericProcedureCatalog.TimeIntervalModule,
                null,
                null,
                new Dictionary<string, string> { ["durationTicks"] = sixHours })]);

        var session = await starter.StartStandaloneAsync(
            "alice",
            "Pinned historical procedure",
            new ProcedureStartSelection(
                ProcedureId: created.ProcedureId,
                ProcedureRevision: created.Revision),
            new NonSpatialCrawlSessionContext("Procedure clock"));

        Assert.Equal(created.ProcedureId, session.CampaignProcedure.ProcedureId);
        Assert.Equal(1, session.CampaignProcedure.Revision);
        Assert.Equal(
            TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture),
            session.CampaignProcedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.TimeIntervalModule)
                .Parameters["durationTicks"]);
        Assert.Equal(2, (await procedures.GetLatestAsync("alice", created.ProcedureId)).Revision);
    }

    [Fact]
    public async Task SavedProcedureStartRequiresExplicitRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            starter.StartStandaloneAsync(
                "alice",
                "Ambiguous saved procedure",
                new ProcedureStartSelection(ProcedureId: created.ProcedureId),
                new NonSpatialCrawlSessionContext("Procedure clock")));
    }

    [Fact]
    public async Task SavedProcedureStartIsOwnerScoped()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var composer = new ProcedureComposerService(procedures);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);

        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            starter.StartStandaloneAsync(
                "bob",
                "Unauthorized saved procedure",
                new ProcedureStartSelection(
                    ProcedureId: created.ProcedureId,
                    ProcedureRevision: created.Revision),
                new NonSpatialCrawlSessionContext("Procedure clock")));

        Assert.Empty(await procedures.ListLatestAsync("bob"));
    }

    [Fact]
    public async Task InvalidStandaloneContextDoesNotMaterializePresetProcedure()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            starter.StartStandaloneAsync(
                "alice",
                "Invalid standalone context",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                new WorldBoundCrawlSessionContext(Guid.NewGuid())));

        Assert.Empty(await procedures.ListLatestAsync("alice"));
    }
}
