using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase18NativeCellProcedureTests
{
    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    public void Version2CellStepContractMovesThroughAnySupportedTiling(string symbol)
    {
        var world = CreateWorld(symbol);
        var procedure = Version2Procedure(world, TravelResolutionMode.CellSteps);
        var runtime = GenericProcedureRuntime.Bind(procedure);
        Assert.Equal(2, runtime.Movement.MechanicVersion);
        Assert.Throws<InvalidOperationException>(() => _ = runtime.HexProgress);

        var cursor = Start(world);
        var result = new CrawlRuntimeEngine().ResolveCellTravel(
            world, procedure, cursor,
            ResolvedTravelAmount.CellTransitions(1, ResolutionProvenance.ProcedureDefault), true);
        var exit = Assert.Single(result.Transitions);
        Assert.Equal(cursor.CurrentCell, exit.From);
        Assert.Equal(exit.To, result.Traversal.CurrentCell);
        Assert.Equal(exit.EntryInterfaceIndex, result.Traversal.EntryInterfaceIndex);
    }

    [Fact]
    public void Version2PhysicalMovementHonorsActualDistanceAndCalibration()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var procedure = Version2Procedure(world, TravelResolutionMode.ContinuousDistance);
        var cursor = Start(world);
        var budget = new DistanceMeasure(0.1, DistanceUnit.Miles);
        var result = new CrawlRuntimeEngine().ResolveCellTravel(
            world, procedure, cursor,
            ResolvedTravelAmount.Distance(budget, budget, ResolutionProvenance.ProcedureDefault), true);
        Assert.InRange(result.ConsumedPhysicalDistance!.Value.Value, 0.1 - 1e-8, 0.1 + 1e-8);
        Assert.InRange(result.ConsumedWorldUnits, 0.05 - 1e-8, 0.05 + 1e-8);
    }

    [Fact]
    public void Version1HexPolicyIsNotReinterpretedAsGeneralizedMovement()
    {
        var world = CreateWorld("<1:1,1,1:6,3>");
        var procedure = TestProcedureProfiles.HexStep().Materialize();
        var runtime = GenericProcedureRuntime.Bind(procedure);
        Assert.Equal(1, runtime.Movement.MechanicVersion);
        var legacy = new CrawlRuntimeEngine();
        Assert.Throws<NotSupportedException>(() => legacy.ResolveCellTravel(
            world, procedure, Start(world),
            ResolvedTravelAmount.Steps(1, ResolutionProvenance.ProcedureDefault), true));
    }

    [Fact]
    public void PinnedTilingIdentityIsCheckedBeforeCrossing()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var wrong = CreateWorld("<1:1,1,1:3,6>");
        var procedure = Version2Procedure(wrong, TravelResolutionMode.CellSteps);
        Assert.Throws<InvalidOperationException>(() => new CrawlRuntimeEngine().ResolveCellTravel(
            world, procedure, Start(world),
            ResolvedTravelAmount.CellTransitions(1, ResolutionProvenance.ProcedureDefault), true));
    }

    [Fact]
    public void HexStepsAreNotAcceptedAsCellStepInput()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var procedure = Version2Procedure(world, TravelResolutionMode.CellSteps);
        Assert.Throws<InvalidOperationException>(() => new CrawlRuntimeEngine().ResolveCellTravel(
            world, procedure, Start(world),
            ResolvedTravelAmount.Steps(1, ResolutionProvenance.ProcedureDefault), true));
    }

    [Fact]
    public void GeneralizedContinuousDistanceDoesNotAcceptHexProgressPolicy()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var valid = Version2Procedure(world, TravelResolutionMode.ContinuousDistance);
        var legacyHexProgress = TestProcedureProfiles.FixedDistance().Materialize().Modules.Single(x =>
            x.Mechanic.ExecutionHandler == GenericProcedureExecutionHandlers.HexProgressPolicy);
        var invalid = valid with { Modules = [.. valid.Modules, legacyHexProgress] };
        Assert.Throws<InvalidOperationException>(() => GenericProcedureRuntime.Bind(invalid));
    }

    private static CampaignProcedure Version2Procedure(
        PeriodicWorldTiling world, TravelResolutionMode mode)
    {
        var original = TestProcedureProfiles.FixedDistance().Materialize();
        var modules = original.Modules
            .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = mode.ToString();
                parameters["tracksIntraHexProgress"] = "false";
                return m with { Mechanic = m.Mechanic with { Version = 2 }, Parameters = parameters };
            }).ToArray();
        var procedure = original with
        {
            TilingDsSymbol = world.Topology.QuotientDsSymbol,
            Modules = modules
        };
        procedure.Validate();
        return procedure;
    }

    private static PeriodicWorldTiling CreateWorld(string symbol)
    {
        var metric = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", metric.Status);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), metric.Topology!, metric.Realization!,
            new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(2, DistanceUnit.Miles));
        world.Validate();
        return world;
    }

    private static PeriodicCellTraversal Start(PeriodicWorldTiling world)
    {
        var address = new PeriodicCellAddress(world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0));
        var cell = world.Resolve(address);
        var boundary = world.Boundaries(address)[0];
        var midpoint = new WorldPoint(
            boundary.Start.X + (boundary.End.X - boundary.Start.X) / 2,
            boundary.Start.Y + (boundary.End.Y - boundary.Start.Y) / 2);
        return new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = midpoint - cell.Center,
            SelectedExitInterfaceIndex = boundary.InterfaceIndex
        };
    }
}
