using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
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
    public async Task HexWorldStartAcceptsCanonicalEquivalentSymbolButRejectsDifferentTiling()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), HexRadiusWorldUnits = 2,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Canonical hex world", Grid = grid
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));
        var baseProcedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        var equivalent = baseProcedure with
        {
            ProcedureId = Guid.NewGuid(), TilingDsSymbol = "<1: 1,1,1:6,3>"
        };
        equivalent.Validate();
        await store.CreateCampaignProcedureRevisionAsync(new StoredCampaignProcedureRevision(
            equivalent, "alice", null, null, now));
        var session = await starter.StartWorldBoundAsync(
            world.Id, "alice", "Equivalent symbol",
            new ProcedureStartSelection(ProcedureId: equivalent.ProcedureId,
                ProcedureRevision: equivalent.Revision),
            MapPresentationPolicyCatalog.All[1].Key, new HexCoordinate(0, 0));
        Assert.Equal(equivalent.TilingDsSymbol, session.CampaignProcedure.TilingDsSymbol);
        Assert.Equal(world.Id, ((WorldBoundCrawlSessionContext)session.Context).WorldId);

        var incompatible = baseProcedure with
        {
            ProcedureId = Guid.NewGuid(), TilingDsSymbol = "<1:1,1,1:4,4>"
        };
        incompatible.Validate();
        await store.CreateCampaignProcedureRevisionAsync(new StoredCampaignProcedureRevision(
            incompatible, "alice", null, null, now));
        await Assert.ThrowsAsync<NotSupportedException>(() => starter.StartWorldBoundAsync(
            world.Id, "alice", "Wrong topology",
            new ProcedureStartSelection(ProcedureId: incompatible.ProcedureId,
                ProcedureRevision: incompatible.Revision),
            MapPresentationPolicyCatalog.All[1].Key, new HexCoordinate(0, 0)));
    }

    [Fact]
    public async Task GeneralizedWorldStartPinsOwnedVersion2ProcedureWithoutInventingHexState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var generated = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), generated.Topology!,
            generated.Realization!, new WorldPoint(0, 0));
        tiling.Validate();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Version 2 start world", Tiling = tiling
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));

        var original = SyntheticProcedureFixtures.MixedProcedure();
        var modules = original.Modules
            .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                parameters["tracksIntraHexProgress"] = "false";
                return m with { Mechanic = m.Mechanic with { Version = 2 }, Parameters = parameters };
            }).ToArray();
        var version2 = original with
        {
            ProcedureId = Guid.NewGuid(),
            TilingDsSymbol = tiling.Topology.QuotientDsSymbol,
            Modules = modules
        };
        version2.Validate();
        await procedures.CreateAsync("alice", version2, null);

        var address = new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(2, -1));
        var selection = new ProcedureStartSelection(
            ProcedureId: version2.ProcedureId,
            ProcedureRevision: version2.Revision);
        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            starter.StartWorldBoundCellsAsync(
                world.Id, "bob", "Unauthorized cell expedition", selection, address));

        var session = await starter.StartWorldBoundCellsAsync(
            world.Id, "alice", "Authorized cell expedition", selection, address);
        var state = Assert.IsType<CellExpeditionState>(session.Runtime);
        state.Validate(tiling);
        Assert.Equal(tiling.Resolve(address).Id, state.Traversal.CurrentCell);
        Assert.Equal(tiling.Resolve(address).Center, state.Traversal.Position);
        Assert.Null(state.DistanceTraveled);
        Assert.Null(state.Traversal.TravelHeading);
        Assert.Equal(version2.ProcedureId, session.CampaignProcedure.ProcedureId);
        Assert.Equal(version2.Revision, session.CampaignProcedure.Revision);
        Assert.NotNull(session.Knowledge);

        var reopened = new PostgresHexCrawlStore(database.ConnectionString);
        await reopened.InitializeAsync();
        var reloaded = await reopened.GetExpeditionAsync(session.Id, "alice");
        Assert.NotNull(reloaded);
        var restored = Assert.IsType<CellExpeditionState>(reloaded!.Runtime);
        restored.Validate(tiling);
        Assert.Equal(state.Traversal, restored.Traversal);
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
