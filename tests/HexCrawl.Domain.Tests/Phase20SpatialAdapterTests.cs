using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase20SpatialAdapterTests
{
    private static AbstractHexCrawlSessionContext Context() =>
        new("Unmapped hex voyage", HexOrientation.PointyTop,
            new CrawlRuntimeContext(new DistanceMeasure(6, DistanceUnit.Miles)));

    [Fact]
    public void MaplessHexCreationIsDeterministicAndStartsAtExactCellCenter()
    {
        var id = Guid.NewGuid();
        var context = Context();
        var start = new HexCoordinate(3, -2);
        var first = AbstractCellSessionGeometry.CreateNewHexSession(id, context, start);
        var second = AbstractCellSessionGeometry.CreateNewHexSession(id, context, start);

        Assert.Equal(first.World.Id, second.World.Id);
        Assert.Equal(first.Expedition.Traversal.CurrentCell, second.Expedition.Traversal.CurrentCell);
        Assert.Equal(first.Expedition.Traversal.Position, second.Expedition.Traversal.Position);
        Assert.Equal(first.World.Resolve(first.Expedition.Traversal.CurrentCell.Address).Center,
            first.Expedition.Traversal.Position);
        Assert.Equal(0, first.Expedition.DistanceTraveled!.Value.Value);
    }

    [Fact]
    public void NeighborCourseUsesAuthoritativePeriodicCellCenters()
    {
        var session = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), Context(), new HexCoordinate(0, 0));
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
        var session = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), Context(), new HexCoordinate(0, 0));
        var world = session.World;
        var cursor = session.Expedition.Traversal with { TravelHeading = new WorldPoint(1, 0) };
        var before = cursor.Position;
        var debit = new SpatialTurnBudgetPolicy(1d / 6d)
            .EvaluateDeliberateTurn(world, cursor, new WorldPoint(0, 1));
        Assert.NotNull(debit);
        Assert.InRange(debit!.BudgetCost.Value, 0.9999999, 1.0000001);
        Assert.Equal(before, cursor.Position);
    }
}
