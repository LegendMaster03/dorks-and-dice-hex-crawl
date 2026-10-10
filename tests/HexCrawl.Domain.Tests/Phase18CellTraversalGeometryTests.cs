using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase18CellTraversalGeometryTests
{
    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    [InlineData("<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")]
    public void DifferentTilingMotifsCrossTheSpecifiedAtomicBoundary(string symbol)
    {
        var world = CreateWorld(symbol);
        foreach (var motif in world.Topology.MotifCells)
        {
            var address = new PeriodicCellAddress(motif.Id, new LatticeDisplacement(-3, 7));
            var cell = world.Resolve(address);
            foreach (var boundary in world.Boundaries(address))
            {
                var midpoint = Midpoint(boundary.Start, boundary.End);
                var vector = midpoint - cell.Center;
                var traversal = new PeriodicCellTraversal
                {
                    CurrentCell = cell.Id,
                    Position = cell.Center,
                    TravelHeading = vector,
                    SelectedExitInterfaceIndex = boundary.InterfaceIndex
                };
                var crossing = PeriodicCellTraversalGeometry.NextCrossing(world, traversal);
                Assert.Equal(CellCrossingStatus.Crosses, crossing.Status);
                Assert.Equal(boundary.InterfaceIndex, crossing.Exit!.InterfaceIndex);
                Assert.Equal(boundary.To, crossing.Exit.To);
                Assert.Equal(world.Reciprocal(boundary), crossing.Entry);
                Assert.InRange(crossing.Point!.Value.DistanceTo(midpoint), 0, 1e-6);

                var moved = PeriodicCellTraversalGeometry.Advance(
                    world, traversal, crossing.DistanceWorldUnits!.Value);
                Assert.Equal(boundary.To, moved.Traversal.CurrentCell);
                Assert.Equal(cell.Id, moved.Traversal.EnteredFrom);
                Assert.Equal(boundary.ReciprocalInterfaceIndex, moved.Traversal.EntryInterfaceIndex);
                moved.Traversal.Validate(world);
            }
        }
    }

    [Fact]
    public void VertexCrossingWithoutExplicitInterfaceRequiresAdjudication()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var state = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = cell.Polygon[0] - cell.Center
        };
        var result = PeriodicCellTraversalGeometry.Advance(world, state, 10);
        Assert.Equal(CellCrossingStatus.RequiresAdjudication, result.Crossing.Status);
        Assert.Equal(0, result.ConsumedWorldUnits);
        Assert.Equal(state, result.Traversal);
    }

    [Fact]
    public void ExactBoundaryEntryDoesNotRecrossWithoutAnExplicitDoubleBack()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var edge = world.Boundaries(cell.Id.Address)[0];
        var state = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = Midpoint(edge.Start, edge.End) - cell.Center,
            SelectedExitInterfaceIndex = edge.InterfaceIndex
        };
        var advanced = PeriodicCellTraversalGeometry.Advance(world, state, 100);
        Assert.Equal(CellCrossingStatus.Crosses, advanced.Crossing.Status);

        var returnCourse = advanced.Traversal with
        {
            TravelHeading = cell.Center - advanced.Traversal.Position,
            SelectedExitInterfaceIndex = edge.ReciprocalInterfaceIndex
        };
        var back = PeriodicCellTraversalGeometry.Advance(world, returnCourse, 100);
        Assert.Equal(CellCrossingStatus.Crosses, back.Crossing.Status);
        Assert.Equal(0, back.ConsumedWorldUnits);
        Assert.Equal(cell.Id, back.Traversal.CurrentCell);
    }

    [Fact]
    public void WrongTilingIdentityAndInvalidEntryInterfaceAreRejected()
    {
        var world = CreateWorld("<1:1,1,1:6,3>");
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var wrongIdentity = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id with { TilingId = Guid.NewGuid() },
            Position = cell.Center,
            TravelHeading = new WorldPoint(1, 0)
        };
        Assert.Throws<InvalidOperationException>(() => wrongIdentity.Validate(world));

        var badEntry = wrongIdentity with
        {
            CurrentCell = cell.Id,
            EnteredFrom = cell.Id,
            EntryInterfaceIndex = 9999
        };
        Assert.Throws<InvalidOperationException>(() => badEntry.Validate(world));
    }

    [Fact]
    public void DistanceWithinCellUsesWorldCoordinatesWithoutInventingPhysicalUnits()
    {
        var world = CreateWorld("<1:1,1,1:4,4>");
        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var edge = world.Boundaries(cell.Id.Address)[0];
        var state = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = Midpoint(edge.Start, edge.End) - cell.Center
        };
        var crossing = PeriodicCellTraversalGeometry.NextCrossing(world, state);
        Assert.Equal(CellCrossingStatus.Crosses, crossing.Status);
        var part = crossing.DistanceWorldUnits!.Value / 3;
        var advanced = PeriodicCellTraversalGeometry.Advance(world, state, part);
        Assert.Equal(CellCrossingStatus.None, advanced.Crossing.Status);
        Assert.Equal(cell.Id, advanced.Traversal.CurrentCell);
        Assert.InRange(advanced.Traversal.Position.DistanceTo(cell.Center), part - 1e-8, part + 1e-8);
        Assert.Throws<NotSupportedException>(() => world.MeasurePhysicalDistance(
            state.Position, advanced.Traversal.Position));
    }

    [Theory]
    [InlineData(2e-6, 0d, 0d)]
    [InlineData(1d, 1e6, -1e6)]
    public void TraversalUsesMeasuredGeometryAtDifferentScales(
        double scale, double originX, double originY)
    {
        var generation = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", scale, "world-unit");
        Assert.Equal("realized", generation.Status);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), generation.Topology!,
            generation.Realization!, new WorldPoint(originX, originY));
        world.Validate();

        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(-2, 3)));
        var boundary = world.Boundaries(cell.Id.Address)[0];
        var direction = Midpoint(boundary.Start, boundary.End) - cell.Center;
        var state = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = direction,
            SelectedExitInterfaceIndex = boundary.InterfaceIndex
        };
        var next = PeriodicCellTraversalGeometry.NextCrossing(world, state);
        Assert.Equal(CellCrossingStatus.Crosses, next.Status);
        Assert.Equal(boundary.To, next.Exit!.To);
    }

    private static PeriodicWorldTiling CreateWorld(string symbol)
    {
        var generation = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", generation.Status);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), generation.Topology!,
            generation.Realization!, new WorldPoint(12, -7));
        world.Validate();
        return world;
    }

    private static WorldPoint Midpoint(WorldPoint a, WorldPoint b) =>
        new(a.X + (b.X - a.X) * 0.5, a.Y + (b.Y - a.Y) * 0.5);
}
