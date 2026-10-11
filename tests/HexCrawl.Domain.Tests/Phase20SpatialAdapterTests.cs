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
    public void LegacySixDirectionInputIsTranslatedToGeometricCourse()
    {
        var session = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), Context(), new HexCoordinate(0, 0));
        var world = session.World;
        var current = new HexCoordinate(0, 0);
        for (var d = 0; d < 6; d++)
        {
            var heading = SpatialCourseSelection.FromLegacyHexDirection(world, current, new HexDirection(d));
            var from = world.Resolve(LegacyHexTilingCompatibility.ToAddress(current));
            var to = world.Resolve(LegacyHexTilingCompatibility.ToAddress(current.Neighbor(d)));
            Assert.Equal(to.Center - from.Center, heading);
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
