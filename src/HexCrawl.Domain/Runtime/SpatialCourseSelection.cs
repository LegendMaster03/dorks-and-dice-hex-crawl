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
        PeriodicWorldTiling world, WorldCellId current, WorldCellId neighbor,
        CellAdjacencyMode adjacency = CellAdjacencyMode.EdgeOnly)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (current.TilingId != world.Id || neighbor.TilingId != world.Id)
            throw new InvalidOperationException("Both cell identities must belong to the authoritative tiling.");
        if (!world.MovementNeighbors(current.Address, adjacency).Contains(neighbor))
            throw new InvalidOperationException("Selected cells are not adjacent under the configured geometric movement rule.");

        var heading = world.Resolve(neighbor.Address).Center - world.Resolve(current.Address).Center;
        RequireHeading(heading);
        return heading;
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
        if (!world.IsPlayable(edge.To.Address))
            throw new InvalidOperationException(
                "The selected interface leads outside the playable map bounds.");
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
