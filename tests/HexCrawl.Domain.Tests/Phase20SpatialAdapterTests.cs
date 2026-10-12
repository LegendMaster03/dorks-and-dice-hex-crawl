using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase20SpatialAdapterTests
{
    private static MaplessCellSessionStart Session(
        Guid? identity = null, PeriodicCellAddress? start = null)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:6,3>", 1d, "world-unit");
        var id = identity ?? Guid.NewGuid();
        var tiling = new PeriodicWorldTiling(
            id, realized.Topology!, realized.Realization!, new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(6, DistanceUnit.Miles));
        return MaplessCellSessionGeometry.Create(id, tiling, start);
    }

    [Fact]
    public void MaplessPeriodicGeometryStartsAtExactQualifiedCellCenter()
    {
        var id = Guid.NewGuid();
        var first = Session(id);
        var second = MaplessCellSessionGeometry.Create(id, first.World);

        Assert.Equal(first.World.Id, second.World.Id);
        Assert.Equal(first.Expedition.Traversal.CurrentCell, second.Expedition.Traversal.CurrentCell);
        Assert.Equal(first.Expedition.Traversal.Position, second.Expedition.Traversal.Position);
        Assert.Equal(first.World.Resolve(first.Expedition.Traversal.CurrentCell.Address).Center,
            first.Expedition.Traversal.Position);
        Assert.Equal(0, first.Expedition.DistanceTraveled!.Value.Value);
    }

    [Theory]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:3,6>")]
    public void MaplessStartsUseSuppliedPeriodicGeometryAndQualifiedAddresses(string symbol)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1d, "world-unit");
        Assert.Equal("realized", realized.Status);
        var id = Guid.NewGuid();
        var tiling = new PeriodicWorldTiling(
            id, realized.Topology!, realized.Realization!, new WorldPoint(4, -3),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(2, DistanceUnit.Miles));
        var startAddress = new PeriodicCellAddress(
            tiling.Topology.MotifCells[^1].Id, new LatticeDisplacement(2, -1));
        var start = MaplessCellSessionGeometry.Create(id, tiling, startAddress);
        Assert.Equal(new WorldCellId(id, startAddress), start.Expedition.Traversal.CurrentCell);
        Assert.Equal(tiling.Resolve(startAddress).Center, start.Expedition.Traversal.Position);
        Assert.Equal(tiling.Boundaries(startAddress).Count,
            tiling.Topology.MotifCells[^1].Boundary.Count);
        var context = new MaplessSpatialCrawlSessionContext("Periodic session", tiling);
        context.Validate();
    }

    [Fact]
    public void NeighborCourseUsesAuthoritativePeriodicCellCenters()
    {
        var session = Session();
        var world = session.World;
        var current = session.Expedition.Traversal.CurrentCell;
        var from = world.Resolve(current.Address);
        var neighbors = world.Neighbors(current.Address);
        Assert.Equal(6, neighbors.Count); // Six is a property of this test fixture, not a runtime direction contract.
        foreach (var neighbor in neighbors)
        {
            var heading = SpatialCourseSelection.ParallelToNeighborCenters(world, current, neighbor);
            Assert.Equal(world.Resolve(neighbor.Address).Center - from.Center, heading);
        }
    }

    [Fact]
    public void TurnChargeDoesNotMoveTheAuthoritativeCursor()
    {
        var session = Session();
        var world = session.World;
        var cursor = session.Expedition.Traversal with { TravelHeading = new WorldPoint(1, 0) };
        var before = cursor.Position;
        // The general realized motif is not necessarily equidistant to every
        // neighbor. A procedure-defined turning-cost reference is required
        // rather than silently using the first neighbor's distance.
        Assert.Throws<NotSupportedException>(() =>
            new SpatialTurnBudgetPolicy(1d / 6d)
                .EvaluateDeliberateTurn(world, cursor, new WorldPoint(0, 1)));
        Assert.Equal(before, cursor.Position);
    }
}
