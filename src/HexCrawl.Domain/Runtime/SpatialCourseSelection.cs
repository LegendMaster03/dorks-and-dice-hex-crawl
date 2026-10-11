using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Translates a selection into geometric heading intent. It never chooses a
/// crossing or claims that the requested neighbor was actually entered:
/// PeriodicCellTraversalGeometry remains the only boundary authority.
/// </summary>
public static class SpatialCourseSelection
{
    /// <summary>
    /// Select a course parallel to the centers of two actual adjacent cells.
    /// Starting from a noncentral position can lead to a different boundary;
    /// traversal must still resolve that boundary, including vertex ties.
    /// </summary>
    public static WorldPoint ParallelToNeighborCenters(
        PeriodicWorldTiling world, WorldCellId current, WorldCellId neighbor)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (current.TilingId != world.Id || neighbor.TilingId != world.Id)
            throw new InvalidOperationException("Both cell identities must belong to the authoritative tiling.");
        if (!world.Neighbors(current.Address).Contains(neighbor))
            throw new InvalidOperationException("A discrete neighbor selection requires a reciprocal atomic adjacency.");

        var heading = world.Resolve(neighbor.Address).Center - world.Resolve(current.Address).Center;
        RequireHeading(heading);
        return heading;
    }

    /// <summary>
    /// Six-way legacy input adapter for a known regular-hex address convention.
    /// It is intentionally outside the universal traversal engine; the actual
    /// exit is still resolved from the cursor and world geometry.
    /// </summary>
    public static WorldPoint FromLegacyHexDirection(
        PeriodicWorldTiling world, HexCoordinate current, HexDirection selectedDirection)
    {
        ArgumentNullException.ThrowIfNull(world);
        var from = new WorldCellId(world.Id, LegacyHexTilingCompatibility.ToAddress(current));
        var to = new WorldCellId(world.Id,
            LegacyHexTilingCompatibility.ToAddress(current.Neighbor(selectedDirection.Value)));
        return ParallelToNeighborCenters(world, from, to);
    }

    /// <summary>
    /// Aim at a particular real atomic interface from the exact cursor, for
    /// manual adjudication of topology-specific choices. The core still checks
    /// that the resulting ray can cross the selected interface.
    /// </summary>
    public static WorldPoint TowardAtomicInterface(
        PeriodicWorldTiling world, PeriodicCellTraversal cursor, int interfaceIndex)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cursor);
        cursor.Validate(world);
        var edges = world.Boundaries(cursor.CurrentCell.Address);
        if (interfaceIndex < 0 || interfaceIndex >= edges.Count)
            throw new ArgumentOutOfRangeException(nameof(interfaceIndex));
        var edge = edges[interfaceIndex];
        var midpoint = new WorldPoint(
            edge.Start.X + (edge.End.X - edge.Start.X) / 2d,
            edge.Start.Y + (edge.End.Y - edge.Start.Y) / 2d);
        var heading = midpoint - cursor.Position;
        RequireHeading(heading);
        return heading;
    }

    private static void RequireHeading(WorldPoint heading)
    {
        if (!double.IsFinite(heading.X) || !double.IsFinite(heading.Y)
            || (heading.X == 0d && heading.Y == 0d))
            throw new InvalidOperationException(
                "The requested course has no finite nonzero heading; explicit course adjudication is required.");
    }
}
