using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase18TraversalExecutionTests
{
    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    public void CellStepsUseReciprocalInterfacesForEveryMotif(string symbol)
    {
        var world = CreateWorld(symbol, true);
        foreach (var motif in world.Topology.MotifCells)
        {
            var cell = world.Resolve(new PeriodicCellAddress(motif.Id, new LatticeDisplacement(2, -1)));
            foreach (var exit in world.Boundaries(cell.Id.Address))
            {
                var state = Towards(world, cell, exit);
                var result = PeriodicTraversalExecution.AdvanceCellSteps(world, state, 1, true);
                Assert.Equal(1, result.CompletedCellSteps);
                var transition = Assert.Single(result.Transitions);
                Assert.Equal(cell.Id, transition.From);
                Assert.Equal(exit.To, transition.To);
                Assert.Equal(exit.InterfaceIndex, transition.ExitInterfaceIndex);
                Assert.Equal(exit.ReciprocalInterfaceIndex, transition.EntryInterfaceIndex);
                Assert.True(result.ConsumedWorldUnits > 0);
                Assert.True(result.ConsumedPhysicalDistance!.Value.Value > 0);
                Assert.Equal(exit.To, result.Traversal.CurrentCell);
                Assert.Equal(exit.ReciprocalInterfaceIndex, result.Traversal.EntryInterfaceIndex);
                Assert.Equal(exit.From, result.Traversal.EnteredFrom);
            }
        }
    }

    [Fact]
    public void ContinuousDistancePreservesUnspentBudgetAcrossSaveAndReload()
    {
        var world = CreateWorld("<1:1,1,1:4,4>", true);
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var exit = world.Boundaries(cell.Id.Address)[0];
        var start = Towards(world, cell, exit);
        var firstCrossing = PeriodicCellTraversalGeometry.NextCrossing(world, start);
        Assert.Equal(CellCrossingStatus.Crosses, firstCrossing.Status);
        var half = firstCrossing.DistanceWorldUnits!.Value / 2;
        var budget = new DistanceMeasure(half * world.PhysicalDistancePerWorldUnit!.Value.Value,
            DistanceUnit.Miles);
        var first = PeriodicTraversalExecution.AdvanceDistance(world, start, budget, true);
        Assert.Empty(first.Transitions);
        Assert.Equal(cell.Id, first.Traversal.CurrentCell);
        var serialized = JsonSerializer.Serialize(first.Traversal);
        var reloaded = JsonSerializer.Deserialize<PeriodicCellTraversal>(serialized)!;
        reloaded.Validate(world);

        var second = PeriodicTraversalExecution.AdvanceDistance(world, reloaded, budget, true);
        Assert.Single(second.Transitions);
        Assert.Equal(exit.To, second.Traversal.CurrentCell);
        Assert.InRange(first.ConsumedWorldUnits + second.ConsumedWorldUnits,
            2 * half - 1e-8, 2 * half + 1e-8);
        Assert.Equal(1, first.Transitions.Count + second.Transitions.Count);
    }

    [Fact]
    public void MultiCellRouteNeverChargesUniformStepDistance()
    {
        var world = CreateWorld("<1:1,1,1:4,4>", true);
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var exit = world.Boundaries(cell.Id.Address)[0];
        var start = Towards(world, cell, exit);
        var result = PeriodicTraversalExecution.AdvanceCellSteps(world, start, 3, true);
        Assert.Equal(3, result.CompletedCellSteps);
        Assert.Equal(3, result.Transitions.Count);
        Assert.All(result.Transitions, transition =>
        {
            var reciprocal = world.Boundaries(transition.To.Address)[transition.EntryInterfaceIndex];
            Assert.Equal(transition.From, reciprocal.To);
            Assert.Equal(transition.ExitInterfaceIndex, reciprocal.ReciprocalInterfaceIndex);
        });
        Assert.Equal(result.ConsumedWorldUnits * world.PhysicalDistancePerWorldUnit!.Value.Value,
            result.ConsumedPhysicalDistance!.Value.Value, 8);
        Assert.True(result.Transitions[1].CumulativeWorldDistance >
            result.Transitions[0].CumulativeWorldDistance);
    }

    [Fact]
    public void ReviewGateStopsAtFirstBoundaryButCanResume()
    {
        var world = CreateWorld("<1:1,1,1:4,4>", true);
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var start = Towards(world, cell, world.Boundaries(cell.Id.Address)[0]);
        var initial = PeriodicTraversalExecution.AdvanceCellSteps(world, start, 3, false);
        Assert.True(initial.BoundaryReviewRequired);
        Assert.Equal(1, initial.CompletedCellSteps);
        Assert.Single(initial.Transitions);
        var continued = PeriodicTraversalExecution.AdvanceCellSteps(
            world, initial.Traversal, 2, true);
        Assert.Equal(2, continued.CompletedCellSteps);
        Assert.Equal(2, continued.Transitions.Count);
        Assert.Equal(3, initial.CompletedCellSteps + continued.CompletedCellSteps);
    }

    [Fact]
    public void NoCalibrationAllowsStepsButRefusesPhysicalDistance()
    {
        var world = CreateWorld("<1:1,1,1:4,4>", false);
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var state = Towards(world, cell, world.Boundaries(cell.Id.Address)[0]);
        var oneStep = PeriodicTraversalExecution.AdvanceCellSteps(world, state, 1, true);
        Assert.Single(oneStep.Transitions);
        Assert.Null(oneStep.ConsumedPhysicalDistance);
        Assert.Throws<NotSupportedException>(() =>
            PeriodicTraversalExecution.AdvanceDistance(
                world, state, new DistanceMeasure(1, DistanceUnit.Miles), true));
    }

    [Fact]
    public void AmbiguousVertexNeverSpendsBudgetOrFabricatesNeighbor()
    {
        var world = CreateWorld("<1:1,1,1:4,4>", true);
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var cursor = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = cell.Polygon[0] - cell.Center
        };
        var result = PeriodicTraversalExecution.AdvanceCellSteps(world, cursor, 5, true);
        Assert.True(result.RequiresAdjudication);
        Assert.Equal(0, result.ConsumedWorldUnits);
        Assert.Empty(result.Transitions);
        Assert.Equal(cursor, result.Traversal);
    }

    private static PeriodicCellTraversal Towards(
        PeriodicWorldTiling world, WorldCellGeometry cell, WorldCellBoundary exit)
    {
        var midpoint = new WorldPoint(
            exit.Start.X + (exit.End.X - exit.Start.X) / 2,
            exit.Start.Y + (exit.End.Y - exit.Start.Y) / 2);
        return new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = midpoint - cell.Center,
            SelectedExitInterfaceIndex = exit.InterfaceIndex
        };
    }

    private static PeriodicWorldTiling CreateWorld(string symbol, bool calibrated)
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var world = new PeriodicWorldTiling(
            Guid.NewGuid(), generated.Topology!, generated.Realization!,
            new WorldPoint(12, -7), PhysicalDistancePerWorldUnit: calibrated
                ? new DistanceMeasure(2, DistanceUnit.Miles)
                : null);
        world.Validate();
        return world;
    }
}
