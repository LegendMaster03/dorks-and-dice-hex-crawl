using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase2ReviewRegressionTests
{
    private const string Owner = "phase2-review";

    // Phase 20.2: the two V1 hex/watch entry points were replaced by one
    // geometric cell service. The fixed/variable distance invariants below
    // exercise the persisted V2 runtime instead of running retired V1 starts.
    [Theory]
    [InlineData("simple-fixed-distance", 2d, 2d)]
    [InlineData("alexandrian-advanced", 3d, 2d)]
    public async Task GeometricTravelHonorsPinnedFixedAndVariableDistances(
        string preset, double expected, double actual)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var service = new HexCrawlService(store);
        var start = new HexCoordinate(0, 0);
        var context = new AbstractHexCrawlSessionContext(
            "Geometric travel test", HexOrientation.PointyTop,
            new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));
        var created = await new ExpeditionStartService(
            store, service, new CampaignProcedureService(store))
            .StartStandaloneCellsAsync(
                Owner, "Distance resolution", new ProcedureStartSelection(PresetKey: preset),
                context, start);
        var geometry = (await service.ResolveCellGeometryAsync(created, Owner)).Tiling;
        var heading = PeriodicCourseFixture.ThroughInterface(geometry, Assert.IsType<CellExpeditionState>(created.Runtime).Traversal.CurrentCell);
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var plan = new CellWatchTravelPlan(
            heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None);
        var inputs = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(expected, DistanceUnit.Miles),
                new DistanceMeasure(actual, DistanceUnit.Miles),
                source),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
            ResolvedEncounter.None);

        var advanced = await service.AdvanceCellExpeditionAsync(
            created.Id, Owner, created.Version, plan, inputs);
        var spatial = Assert.IsType<CellExpeditionState>(advanced.Runtime);
        Assert.Equal(actual, spatial.DistanceTraveled!.Value.Value, 6);
        Assert.Equal(1, spatial.CompletedWatches);
    }

    [Fact]
    public async Task FixedGeometricDistanceRejectsContradictionWithoutPersisting()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var service = new HexCrawlService(store);
        var start = new HexCoordinate(0, 0);
        var context = new AbstractHexCrawlSessionContext(
            "Fixed distance", HexOrientation.PointyTop,
            new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));
        var created = await new ExpeditionStartService(
            store, service, new CampaignProcedureService(store))
            .StartStandaloneCellsAsync(
                Owner, "Fixed travel", new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                context, start);
        var geometry = (await service.ResolveCellGeometryAsync(created, Owner)).Tiling;
        var heading = PeriodicCourseFixture.ThroughInterface(geometry, Assert.IsType<CellExpeditionState>(created.Runtime).Traversal.CurrentCell);
        var plan = new CellWatchTravelPlan(
            heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None);
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdvanceCellExpeditionAsync(
                created.Id, Owner, created.Version, plan,
                new CellWatchAdvanceInputs(
                    ResolvedTravelAmount.Distance(
                        new DistanceMeasure(2, DistanceUnit.Miles),
                        new DistanceMeasure(3, DistanceUnit.Miles), source),
                    new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
                    ResolvedEncounter.None)));
        var saved = await store.GetExpeditionAsync(created.Id, Owner);
        Assert.Equal(created.Version, saved!.Version);
        Assert.Empty(saved.Runtime.History);
    }

    [Fact]
    public void NativeRuntimeRejectsUnsupportedFutureVersionWithKnownHandlerWithoutRewritingSnapshot()
    {
        var original = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var movement = original.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        var futureMovement = movement with
        {
            Mechanic = movement.Mechanic with { Version = 99 }
        };
        var future = original with
        {
            Modules = original.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                    ? futureMovement
                    : module)
                .ToArray()
        };
        future.Validate();
        var originalHandler = movement.Mechanic.ExecutionHandler;

        var exception = Assert.Throws<UnsupportedProcedureMechanicException>(() =>
            GenericProcedureRuntime.Bind(future));

        Assert.Equal(originalHandler, exception.ExecutionHandler);
        Assert.Equal(99, exception.MechanicVersion);
        Assert.Equal(99, future.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule).Mechanic.Version);
        Assert.Equal(originalHandler, future.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule).Mechanic.ExecutionHandler);
    }

    private static async Task<PostgresHexCrawlStore> StoreAsync(string connectionString)
    {
        var store = new PostgresHexCrawlStore(connectionString);
        await store.InitializeAsync();
        return store;
    }

    private static CrawlRuntimeContext Context() => new(Miles(12));

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);

    private static CreateOverworldCommand WorldCommand() => new(
        "Phase 2 review world",
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);
}
