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

        var session = await starter.StartNonSpatialAsync(
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

        var session = await starter.StartNonSpatialAsync(
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
    public async Task CellWorldStartAcceptsCanonicalEquivalentSymbolButRejectsDifferentTiling()
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
        var native = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("simple-fixed-distance")).Procedure;
        var equivalent = native with
        {
            ProcedureId = Guid.NewGuid(), TilingDsSymbol = "<1: 1,1,1:6,3>"
        };
        equivalent.Validate();
        await procedures.CreateAsync("alice", equivalent, null);
        var address = LegacyHexTilingCompatibility.ToAddress(new HexCoordinate(0, 0));
        var session = await starter.StartWorldBoundCellsAsync(
            world.Id, "alice", "Equivalent symbol",
            new ProcedureStartSelection(ProcedureId: equivalent.ProcedureId,
                ProcedureRevision: equivalent.Revision), address, "dm-controlled");
        Assert.Equal(equivalent.TilingDsSymbol, session.CampaignProcedure.TilingDsSymbol);
        Assert.Equal(world.Id, ((WorldBoundCrawlSessionContext)session.Context).WorldId);
        Assert.IsType<CellExpeditionState>(session.Runtime);

        var incompatible = native with
        {
            ProcedureId = Guid.NewGuid(), TilingDsSymbol = "<1:1,1,1:4,4>"
        };
        incompatible.Validate();
        await procedures.CreateAsync("alice", incompatible, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            starter.StartWorldBoundCellsAsync(
                world.Id, "alice", "Wrong topology",
                new ProcedureStartSelection(ProcedureId: incompatible.ProcedureId,
                    ProcedureRevision: incompatible.Revision), address, "dm-controlled"));
    }

    [Fact]
    public async Task SavedNativeProcedureRevisionsStartThroughTheOnlyCellRuntime()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Pinned native world",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(), HexRadiusWorldUnits = 2,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));
        var catalogProcedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var sessionProcedure = CampaignProcedureMaterializer.MaterializeExecutableSpatial(
            CrawlProcedureCatalog.Resolve("simple-fixed-distance")).Procedure;
        await procedures.CreateAsync("alice", catalogProcedure, null);
        await procedures.CreateAsync("alice", sessionProcedure, null);
        var start = new HexCoordinate(0, 0);
        var address = LegacyHexTilingCompatibility.ToAddress(start);
        foreach (var procedure in new[] { catalogProcedure, sessionProcedure })
        {
            var pinned = new ProcedureStartSelection(
                ProcedureId: procedure.ProcedureId, ProcedureRevision: procedure.Revision);
            var inWorld = await starter.StartWorldBoundCellsAsync(
                world.Id, "alice", "World cells", pinned, address, "dm-controlled");
            var mapless = await starter.StartStandaloneCellsAsync(
                "alice", "Mapless cells", pinned, MaplessSessionFixture.Options("Mapless cell session"));
            Assert.IsType<CellExpeditionState>(inWorld.Runtime);
            Assert.IsType<CellExpeditionState>(mapless.Runtime);
        }
        Assert.Equal(4, (await store.ListExpeditionsAsync("alice")).Count);
    }

    [Theory]
    [InlineData("alexandrian-advanced")]
    [InlineData("simple-fixed-distance")]
    [InlineData("simple-hex-step")]
    public async Task ExplicitCellStartOnLegacyHexWorldPinsGeometricPresetWithoutV1State(string presetKey)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Geometric hex via legacy grid adapter",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(), HexRadiusWorldUnits = 2,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));
        Assert.True(world.HasLegacyHexGrid);
        Assert.Null(world.Tiling);

        var start = LegacyHexTilingCompatibility.ToAddress(new HexCoordinate(0, 0));
        var expedition = await starter.StartWorldBoundCellsAsync(
            world.Id, "alice", "V2 hex", new ProcedureStartSelection(PresetKey: presetKey),
            start, "dm-controlled");
        var cellState = Assert.IsType<CellExpeditionState>(expedition.Runtime);
        cellState.Validate(world.SpatialTiling);
        Assert.Equal(world.SpatialTiling.Resolve(start).Id, cellState.Traversal.CurrentCell);
        Assert.Equal(world.SpatialTiling.Resolve(start).Center, cellState.Traversal.Position);
        Assert.Equal(presetKey, expedition.ProcedureOrigin?.PresetKey);
        Assert.Null(expedition.ProcedureOrigin?.PresetRevision);
        var pinned = await procedures.GetAsync("alice", expedition.CampaignProcedure.ProcedureId,
            expedition.CampaignProcedure.Revision);
        Assert.Equal(expedition.CampaignProcedure.ProcedureId, pinned.Procedure.ProcedureId);
        Assert.Equal(expedition.CampaignProcedure.Revision, pinned.Procedure.Revision);
        Assert.Equal(expedition.CampaignProcedure.TilingDsSymbol, pinned.Procedure.TilingDsSymbol);
        Assert.Equal(expedition.ProcedureOrigin, pinned.ProcedureOrigin);

        var reopened = new PostgresHexCrawlStore(database.ConnectionString);
        await reopened.InitializeAsync();
        var restored = (await reopened.GetExpeditionAsync(expedition.Id, "alice"))!;
        Assert.IsType<CellExpeditionState>(restored.Runtime).Validate(world.SpatialTiling);
        Assert.Equal(expedition.CampaignProcedure.ProcedureId, restored.CampaignProcedure.ProcedureId);
        Assert.Equal(presetKey, restored.ProcedureOrigin?.PresetKey);
    }

    [Fact]
    public async Task GeneralizedWorldStartPinsOwnedProcedureWithoutInventingHexState()
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
            Id = Guid.NewGuid(), Name = "Cell start world", Tiling = tiling
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));

        var original = SyntheticProcedureFixtures.MixedProcedure();
        var modules = original.Modules

            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                return m with { Parameters = parameters };
            }).ToArray();
        var procedure = original with
        {
            ProcedureId = Guid.NewGuid(),
            TilingDsSymbol = tiling.Topology.QuotientDsSymbol,
            Modules = modules
        };
        procedure.Validate();
        await procedures.CreateAsync("alice", procedure, null);

        var address = new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(2, -1));
        var selection = new ProcedureStartSelection(
            ProcedureId: procedure.ProcedureId,
            ProcedureRevision: procedure.Revision);
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
        Assert.Equal(procedure.ProcedureId, session.CampaignProcedure.ProcedureId);
        Assert.Equal(procedure.Revision, session.CampaignProcedure.Revision);
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
            starter.StartNonSpatialAsync(
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
            starter.StartNonSpatialAsync(
                "bob",
                "Unauthorized saved procedure",
                new ProcedureStartSelection(
                    ProcedureId: created.ProcedureId,
                    ProcedureRevision: created.Revision),
                new NonSpatialCrawlSessionContext("Procedure clock")));

        Assert.Empty(await procedures.ListLatestAsync("bob"));
    }


}
