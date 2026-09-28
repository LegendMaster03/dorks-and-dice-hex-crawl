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

    [Fact]
    public async Task FixedContinuousDistanceThroughWorkbenchUsesOneEffectiveDistance()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var workbench = new ExpeditionWorkbenchService(store, core, new CrawlSessionContextResolver(core));
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await workbench.StartAsync(
            world.World.Id,
            Owner,
            new StartExpeditionWorkbenchCommand(
                "Fixed workbench",
                "simple-fixed-distance",
                "exploration-map",
                new HexCoordinate(0, 0)));

        var advanced = await workbench.AdvanceAsync(
            started.Id,
            Owner,
            new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = started.Version,
                IntendedDirection = 0,
                EffectiveDistance = 2,
                ContinueAcrossBoundaries = true,
                TravelResolutionSource = ResolutionSource.ProcedureDefault
            });

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
        Assert.Equal(2, state.DistanceTraveled.Value, 6);
        Assert.Equal(1, state.CompletedWatches);
    }

    [Fact]
    public async Task FixedContinuousDistanceThroughManualServiceAcceptsOneExistingDistanceField()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await core.StartExpeditionAsync(
            world.World.Id,
            Owner,
            new StartExpeditionCommand("Fixed manual", "simple-fixed-distance", new HexCoordinate(0, 0)));

        var advanced = await core.AdvanceExpeditionAsync(
            started.Id,
            Owner,
            new AdvanceExpeditionCommand
            {
                ExpectedVersion = started.Version,
                IntendedDirection = 0,
                ActualDistance = 2,
                ContinueAcrossBoundaries = true,
                ResolutionSource = ResolutionSource.ProcedureDefault
            });

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
        Assert.Equal(2, state.DistanceTraveled.Value, 6);
        Assert.Equal(1, state.CompletedWatches);
    }

    [Fact]
    public async Task VariableContinuousDistanceThroughWorkbenchUsesExpectedAndActualIndependently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var workbench = new ExpeditionWorkbenchService(store, core, new CrawlSessionContextResolver(core));
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await workbench.StartAsync(
            world.World.Id,
            Owner,
            new StartExpeditionWorkbenchCommand(
                "Variable workbench",
                "alexandrian-advanced",
                "exploration-map",
                new HexCoordinate(0, 0)));

        var advanced = await workbench.AdvanceAsync(
            started.Id,
            Owner,
            VariableWorkbenchAdvance(started.Version, 3, 2));

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
        Assert.Equal(2, state.DistanceTraveled.Value, 6);
        Assert.Equal(1, state.CompletedWatches);
    }

    [Fact]
    public async Task VariableContinuousDistanceThroughManualServiceUsesExpectedAndActualIndependently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await core.StartExpeditionAsync(
            world.World.Id,
            Owner,
            new StartExpeditionCommand("Variable manual", "alexandrian-advanced", new HexCoordinate(0, 0)));

        var advanced = await core.AdvanceExpeditionAsync(
            started.Id,
            Owner,
            new AdvanceExpeditionCommand
            {
                ExpectedVersion = started.Version,
                IntendedDirection = 0,
                ExpectedDistance = 3,
                ActualDistance = 2,
                NavigationOutcome = NavigationCheckOutcome.Succeeded,
                EncounterOutcome = EncounterOutcomeKind.None,
                ContinueAcrossBoundaries = true,
                ResolutionSource = ResolutionSource.ManualRoll
            });

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
        Assert.Equal(2, state.DistanceTraveled.Value, 6);
        Assert.Equal(1, state.CompletedWatches);
    }

    [Fact]
    public async Task WorkbenchFixedDistanceRejectsContradictorySuppliedValues()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var workbench = new ExpeditionWorkbenchService(store, core, new CrawlSessionContextResolver(core));
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await workbench.StartAsync(
            world.World.Id,
            Owner,
            new StartExpeditionWorkbenchCommand(
                "Fixed contradiction",
                "simple-fixed-distance",
                "exploration-map",
                new HexCoordinate(0, 0)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workbench.AdvanceAsync(
            started.Id,
            Owner,
            new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = started.Version,
                IntendedDirection = 0,
                ExpectedDistance = 2,
                ActualDistance = 3,
                ContinueAcrossBoundaries = true
            }));

        Assert.Contains("same effective travel distance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeRuntimeRejectsDifferingFixedExpectedAndActualDistance()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var malformed = ResolvedTravelAmount.Distance(
            Miles(3),
            Miles(2),
            ResolutionProvenance.ProcedureDefault);

        var exception = Assert.Throws<InvalidOperationException>(() => new CrawlRuntimeEngine().Advance(
            Context(),
            procedure,
            SpatialState(),
            Plan(),
            new WatchAdvanceInputs(malformed)));

        Assert.Contains("same effective distance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeRuntimeRejectsIncompleteVariableExpectedActualDistance()
    {
        var procedure = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        var malformed = new ResolvedTravelAmount
        {
            ExpectedDistance = Miles(3),
            Provenance = ResolutionProvenance.ProcedureDefault
        };

        var exception = Assert.Throws<InvalidOperationException>(() => new CrawlRuntimeEngine().Advance(
            Context(),
            procedure,
            SpatialState(),
            Plan(),
            new WatchAdvanceInputs(malformed)));

        Assert.Contains("requires both expected and actual distance", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeHexStepTravelStillUsesResolvedStepCountOnly()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-hex-step").MaterializeGeneric().Procedure;

        var result = new CrawlRuntimeEngine().Advance(
            Context(),
            procedure,
            SpatialState(),
            Plan(),
            new WatchAdvanceInputs(ResolvedTravelAmount.Steps(1, ResolutionProvenance.ProcedureDefault)));

        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Equal(new HexCoordinate(1, 0), result.Expedition.CurrentHex);
        Assert.Equal(12, result.Expedition.DistanceTraveled.Value, 6);
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

    private static AdvanceExpeditionWorkbenchCommand VariableWorkbenchAdvance(
        long version,
        double expected,
        double actual) => new()
    {
        ExpectedVersion = version,
        IntendedDirection = 0,
        ExpectedDistance = expected,
        ActualDistance = actual,
        NavigationOutcome = NavigationCheckOutcome.Succeeded,
        EncounterOutcome = EncounterOutcomeKind.None,
        ContinueAcrossBoundaries = true,
        ResolutionSource = ResolutionSource.ManualRoll
    };

    private static async Task<PostgresHexCrawlStore> StoreAsync(string connectionString)
    {
        var store = new PostgresHexCrawlStore(connectionString);
        await store.InitializeAsync();
        return store;
    }

    private static CrawlRuntimeContext Context() => new(Miles(12));

    private static ExpeditionState SpatialState() => new()
    {
        Id = Guid.NewGuid(),
        Position = new WorldPoint(0, 0),
        PositionPrecision = WorldPositionPrecision.HexAnchor,
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        Navigation = new NavigationRuntimeState(false, 0),
        DistanceTraveled = Miles(0)
    };

    private static WatchTravelPlan Plan() => new(
        new HexDirection(0),
        TravelModeSelection.Normal,
        NavigationAidSelection.None,
        false,
        true);

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
