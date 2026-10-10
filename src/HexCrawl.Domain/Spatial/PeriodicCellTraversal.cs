namespace HexCrawl.Domain.Spatial;

/// <summary>
/// A stable, geometry-qualified traversal cursor. The position is expressed in
/// world coordinates; it is not a physical distance or a polygon-side index.
/// The entry interface belongs to CurrentCell, not to the cell just exited.
/// </summary>
public sealed record PeriodicCellTraversal
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required WorldCellId CurrentCell { get; init; }
    public required WorldPoint Position { get; init; }
    public WorldCellId? EnteredFrom { get; init; }
    public int? EntryInterfaceIndex { get; init; }
    public WorldPoint? TravelHeading { get; init; }
    public int? SelectedExitInterfaceIndex { get; init; }

    public void Validate(PeriodicWorldTiling world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (FormatVersion != CurrentFormatVersion)
            throw new NotSupportedException("Unsupported cell traversal snapshot format.");
        if (CurrentCell.TilingId != world.Id)
            throw new InvalidOperationException("Traversal references a different world tiling.");
        if (!double.IsFinite(Position.X) || !double.IsFinite(Position.Y))
            throw new InvalidOperationException("Traversal position must be finite.");
        if (!FeatureIntersection.PointInPolygon(Position, world.Resolve(CurrentCell.Address).Polygon))
            throw new InvalidOperationException("Traversal position is outside the authoritative current cell.");

        if (EnteredFrom.HasValue != EntryInterfaceIndex.HasValue)
            throw new InvalidOperationException("Entry cell and reciprocal interface must be recorded together.");
        if (EntryInterfaceIndex is int entry)
        {
            var boundaries = world.Boundaries(CurrentCell.Address);
            if (entry < 0 || entry >= boundaries.Count || boundaries[entry].To != EnteredFrom)
                throw new InvalidOperationException("Traversal entry interface does not lead to the recorded prior cell.");
            world.Reciprocal(boundaries[entry]);
        }

        if (SelectedExitInterfaceIndex is int selected
            && (selected < 0 || selected >= world.Boundaries(CurrentCell.Address).Count))
            throw new InvalidOperationException("Selected exit interface is not present on the current cell.");
        if (TravelHeading is { } heading
            && (!double.IsFinite(heading.X) || !double.IsFinite(heading.Y)
                || Math.Hypot(heading.X, heading.Y) <= 0))
            throw new InvalidOperationException("Travel heading must be finite and nonzero.");
    }
}

public enum CellCrossingStatus
{
    None,
    Crosses,
    RequiresAdjudication
}

public sealed record CellCrossingResolution(
    CellCrossingStatus Status,
    WorldCellBoundary? Exit,
    WorldCellBoundary? Entry,
    WorldPoint? Point,
    double? DistanceWorldUnits,
    IReadOnlyList<WorldCellBoundary> Candidates,
    string? Reason);

public sealed record CellTraversalAdvance(
    PeriodicCellTraversal Traversal,
    double ConsumedWorldUnits,
    CellCrossingResolution Crossing);

/// <summary>
/// Topology-independent straight-course geometry used by a spatial runtime.
/// It does not choose an unrequested destination at vertices, does not equate
/// polygon sides with atomic interfaces, and never converts world units into
/// physical distance without a world calibration.
/// </summary>
public static class PeriodicCellTraversalGeometry
{
    private const double RelativeTolerance = 1e-12;
    private const double MachineEpsilon = 2.2204460492503131e-16;

    private sealed record Hit(
        WorldCellBoundary Boundary, WorldPoint Point, double Distance,
        bool Endpoint);

    public static CellCrossingResolution NextCrossing(
        PeriodicWorldTiling world,
        PeriodicCellTraversal traversal)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(traversal);
        traversal.Validate(world);
        if (traversal.TravelHeading is not { } heading)
            throw new InvalidOperationException("A heading or a DM-resolved course is required to find a boundary.");

        var directionLength = Math.Hypot(heading.X, heading.Y);
        var direction = new WorldPoint(heading.X / directionLength, heading.Y / directionLength);
        var boundaries = world.Boundaries(traversal.CurrentCell.Address);
        var polygon = world.Resolve(traversal.CurrentCell.Address).Polygon;
        var cellScale = Math.Max(
            polygon.Max(p => p.X) - polygon.Min(p => p.X),
            polygon.Max(p => p.Y) - polygon.Min(p => p.Y));
        var worldScale = Math.Max(
            Math.Max(Math.Abs(traversal.Position.X), Math.Abs(traversal.Position.Y)), 1d);
        var positionTolerance = Math.Max(
            cellScale * RelativeTolerance,
            16 * MachineEpsilon * worldScale);
        var edgeTolerance = RelativeTolerance * 16;
        var hits = new List<Hit>();
        var collinear = new List<WorldCellBoundary>();

        foreach (var boundary in boundaries)
        {
            var edge = boundary.End - boundary.Start;
            var edgeLength = Math.Hypot(edge.X, edge.Y);
            if (!(edgeLength > 0) || !double.IsFinite(edgeLength))
                throw new InvalidOperationException("Authoritative boundary has no usable geometric length.");
            var offset = boundary.Start - traversal.Position;
            var denominator = Cross(direction, edge);
            var crossOffset = Cross(offset, direction);
            if (Math.Abs(denominator) <= edgeLength * edgeTolerance)
            {
                // Traveling on a side is not equivalent to crossing that side.
                if (Math.Abs(crossOffset) <= positionTolerance)
                    collinear.Add(boundary);
                continue;
            }

            var distance = Cross(offset, edge) / denominator;
            var fraction = crossOffset / denominator;
            if (fraction < -edgeTolerance || fraction > 1 + edgeTolerance
                || distance < -positionTolerance)
                continue;

            // Ignore the boundary the traveler is already standing on, unless
            // a particular interface was explicitly selected (e.g. double-back).
            if (distance <= positionTolerance && traversal.SelectedExitInterfaceIndex != boundary.InterfaceIndex)
                continue;

            var atEndpoint = fraction <= edgeTolerance || fraction >= 1 - edgeTolerance;
            var point = traversal.Position + direction * Math.Max(0d, distance);
            hits.Add(new Hit(boundary, point, Math.Max(0d, distance), atEndpoint));
        }

        static CellCrossingResolution ResolveAdjudication(
            IReadOnlyList<WorldCellBoundary> candidates, string reason) =>
            new(CellCrossingStatus.RequiresAdjudication, null, null, null, null, candidates, reason);

        if (collinear.Count > 0)
            return ResolveAdjudication(collinear,
                "The selected heading follows an atomic boundary; resolve the course explicitly.");

        if (hits.Count == 0)
            return new CellCrossingResolution(
                CellCrossingStatus.None, null, null, null, null, [], null);

        var closest = hits.Min(h => h.Distance);
        var nearest = hits.Where(h => Math.Abs(h.Distance - closest) <= positionTolerance)
            .ToArray();

        Hit chosen;
        if (traversal.SelectedExitInterfaceIndex is int requested)
        {
            var matching = nearest.Where(h => h.Boundary.InterfaceIndex == requested).ToArray();
            if (matching.Length != 1)
                return ResolveAdjudication(
                    nearest.Select(h => h.Boundary).ToArray(),
                    "The selected interface is not the first boundary crossed by this course.");
            chosen = matching[0];
        }
        else
        {
            if (nearest.Length != 1 || nearest[0].Endpoint)
                return ResolveAdjudication(
                    nearest.Select(h => h.Boundary).ToArray(),
                    "The course meets a shared vertex or more than one boundary interface.");
            chosen = nearest[0];
        }

        // Never rely on mere geometric proximity for the destination.
        var reciprocal = world.Reciprocal(chosen.Boundary);
        if (reciprocal.To != traversal.CurrentCell
            || reciprocal.From != chosen.Boundary.To)
            throw new InvalidOperationException("Crossing does not have a reciprocal authoritative interface.");

        return new CellCrossingResolution(
            CellCrossingStatus.Crosses, chosen.Boundary, reciprocal,
            chosen.Point, chosen.Distance, [chosen.Boundary], null);
    }

    /// <summary>
    /// Advance at most once across an authoritative interface. Callers handle
    /// watch budgets, encounter policy, and any remaining movement; the
    /// returned cursor can be persisted before continuing.
    /// </summary>
    public static CellTraversalAdvance Advance(
        PeriodicWorldTiling world,
        PeriodicCellTraversal traversal,
        double availableWorldUnits)
    {
        if (!double.IsFinite(availableWorldUnits) || availableWorldUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWorldUnits));
        var crossing = NextCrossing(world, traversal);
        if (crossing.Status == CellCrossingStatus.RequiresAdjudication)
            return new CellTraversalAdvance(traversal, 0, crossing);

        var heading = traversal.TravelHeading!.Value;
        var length = Math.Hypot(heading.X, heading.Y);
        var unit = new WorldPoint(heading.X / length, heading.Y / length);
        var needed = crossing.DistanceWorldUnits ?? double.PositiveInfinity;
        if (availableWorldUnits < needed)
        {
            var next = traversal.Position + unit * availableWorldUnits;
            var updated = traversal with { Position = next };
            updated.Validate(world);
            return new CellTraversalAdvance(
                updated, availableWorldUnits,
                new CellCrossingResolution(CellCrossingStatus.None, null, null, null, null, [], null));
        }

        if (crossing.Status != CellCrossingStatus.Crosses)
            throw new InvalidOperationException("A finite cell must have a boundary along any nonzero interior course.");

        var exit = crossing.Exit!;
        var entry = crossing.Entry!;
        var entered = traversal with
        {
            CurrentCell = exit.To,
            Position = crossing.Point!.Value,
            EnteredFrom = exit.From,
            EntryInterfaceIndex = entry.InterfaceIndex,
            SelectedExitInterfaceIndex = null
        };
        entered.Validate(world);
        return new CellTraversalAdvance(entered, needed, crossing);
    }

    private static double Cross(WorldPoint a, WorldPoint b) => a.X * b.Y - a.Y * b.X;
}
