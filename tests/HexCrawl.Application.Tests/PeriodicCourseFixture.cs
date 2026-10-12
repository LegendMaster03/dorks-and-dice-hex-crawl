using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

/// <summary>
/// Builds a test course from authoritative periodic topology, never from
/// shape-specific axial directions. Interface indices here belong to each
/// test world's stored polygon data, not to a fixed set of movement commands.
/// </summary>
internal static class PeriodicCourseFixture
{
    public static WorldPoint ThroughInterface(
        PeriodicWorldTiling world,
        WorldCellId current,
        int interfaceIndex = 0)
    {
        var boundary = world.Boundaries(current.Address)[interfaceIndex];
        return SpatialCourseSelection.ParallelToNeighborCenters(world, current, boundary.To);
    }
    public static CellExpeditionState StartingState(PeriodicWorldTiling? world = null)
    {
        if (world is null)
        {
            var realized = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:4,4>", 1.5, "world-unit");
            if (realized.Status != "realized")
                throw new InvalidOperationException("Test periodic tiling realization failed.");
            world = new PeriodicWorldTiling(
                Guid.NewGuid(), realized.Topology!, realized.Realization!,
                new WorldPoint(0, 0),
                PhysicalDistancePerWorldUnit: new DistanceMeasure(12, DistanceUnit.Miles));
        }

        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        return new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = new PeriodicCellTraversal { CurrentCell = cell.Id, Position = cell.Center },
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
    }
}
