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
    public async Task IncompatibleVersionTwoHexProceduresCannotCreateLegacySpatialSessions()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Legacy runtime version gate",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(),
                HexRadiusWorldUnits = 2,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));

        var legacy = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric().Procedure;
        var version2 = legacy with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = legacy.Modules
                .Where(module => module.Mechanic.ExecutionHandler !=
                    GenericProcedureExecutionHandlers.HexProgressPolicy)
                .Select(module =>
                {
                    if (module.Mechanic.ExecutionHandler !=
                        GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                        return module;
                    var parameters = module.Parameters.ToDictionary(x => x.Key, x => x.Value);
                    parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                    parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                    parameters["tracksIntraHexProgress"] = "false";
                    return module with
                    {
                        Mechanic = module.Mechanic with { Version = 2 },
                        Parameters = parameters
                    };
                }).ToArray()
        };
        version2.Validate();
        Assert.True(CampaignProcedureSchema.RequiresLegacyHexTiling(version2.TilingDsSymbol));
        await procedures.CreateAsync("alice", version2, null);
        var v2Selection = new ProcedureStartSelection(
            ProcedureId: version2.ProcedureId, ProcedureRevision: version2.Revision);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            starter.StartWorldBoundAsync(
                world.Id, "alice", "Rejected v2 world-bound", v2Selection,
                "exploration-map", new HexCoordinate(0, 0)));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            starter.StartStandaloneAsync(
                "alice", "Rejected v2 abstract hex", v2Selection,
                new AbstractHexCrawlSessionContext(
                    "Abstract hex", HexOrientation.PointyTop,
                    new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
                new HexCoordinate(0, 0)));
        Assert.Empty(await store.ListExpeditionsAsync("alice"));

        // The same saved v2 procedure is not invalid in general: only the
        // incompatible legacy runtime start must reject it.
        var nonSpatial = await starter.StartStandaloneAsync(
            "alice", "Nonspatial v2 remains independent", v2Selection,
            new NonSpatialCrawlSessionContext("Procedure clock"));
        Assert.IsType<NonSpatialSessionState>(nonSpatial.Runtime);
        var v1Selection = new ProcedureStartSelection(
            ProcedureId: legacy.ProcedureId, ProcedureRevision: legacy.Revision);
        await procedures.CreateAsync("alice", legacy, null);
        var worldBound = await starter.StartWorldBoundAsync(
            world.Id, "alice", "Legacy v1 still works", v1Selection,
            "exploration-map", new HexCoordinate(0, 0));
        var abstractHex = await starter.StartStandaloneAsync(
            "alice", "Legacy abstract v1 still works", v1Selection,
            new AbstractHexCrawlSessionContext(
                "Abstract hex", HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            new HexCoordinate(0, 0));
        Assert.IsType<ExpeditionState>(worldBound.Runtime);
        Assert.IsType<ExpeditionState>(abstractHex.Runtime);
        Assert.Equal(3, (await store.ListExpeditionsAsync("alice")).Count);
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
        Assert.Equal(2, GenericProcedureRuntime.Bind(expedition.CampaignProcedure).Movement.MechanicVersion);
        Assert.Equal(presetKey, expedition.ProcedureOrigin?.PresetKey);
        Assert.Null(expedition.ProcedureOrigin?.PresetRevision);
        Assert.DoesNotContain(expedition.CampaignProcedure.Modules,
            m => m.Mechanic.ExecutionHandler == GenericProcedureExecutionHandlers.HexProgressPolicy);
        var pinned = await procedures.GetAsync("alice", expedition.CampaignProcedure.ProcedureId,
            expedition.CampaignProcedure.Revision);
        Assert.Equal(expedition.CampaignProcedure.ProcedureId, pinned.Procedure.ProcedureId);
        Assert.Equal(expedition.CampaignProcedure.Revision, pinned.Procedure.Revision);
        Assert.Equal(expedition.CampaignProcedure.TilingDsSymbol, pinned.Procedure.TilingDsSymbol);
        Assert.Equal(2, GenericProcedureRuntime.Bind(pinned.Procedure).Movement.MechanicVersion);
        Assert.Equal(expedition.ProcedureOrigin, pinned.ProcedureOrigin);

        var reopened = new PostgresHexCrawlStore(database.ConnectionString);
        await reopened.InitializeAsync();
        var restored = (await reopened.GetExpeditionAsync(expedition.Id, "alice"))!;
        Assert.IsType<CellExpeditionState>(restored.Runtime).Validate(world.SpatialTiling);
        Assert.Equal(expedition.CampaignProcedure.ProcedureId, restored.CampaignProcedure.ProcedureId);
        Assert.Equal(presetKey, restored.ProcedureOrigin?.PresetKey);
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
