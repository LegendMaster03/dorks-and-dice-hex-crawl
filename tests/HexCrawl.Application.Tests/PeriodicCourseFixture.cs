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
}
