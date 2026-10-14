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
    public int? EntryVertexIndex { get; init; }
    public WorldPoint? TravelHeading { get; init; }
    public int? SelectedExitInterfaceIndex { get; init; }
    public WorldCellId? SelectedExitVertexNeighbor { get; init; }

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

        if (EnteredFrom.HasValue != (EntryInterfaceIndex.HasValue ^ EntryVertexIndex.HasValue))
            throw new InvalidOperationException("Entry must identify exactly one reciprocal edge or point-only vertex contact.");
        if (EntryInterfaceIndex is int entry)
        {
            var boundaries = world.Boundaries(CurrentCell.Address);
            if (entry < 0 || entry >= boundaries.Count || boundaries[entry].To != EnteredFrom)
                throw new InvalidOperationException("Traversal entry interface does not lead to the recorded prior cell.");
            world.Reciprocal(boundaries[entry]);
        }
        if (EntryVertexIndex is int entryVertex
            && !world.VertexOnlyContacts(CurrentCell.Address).Any(contact =>
                contact.FromVertexIndex == entryVertex && contact.To == EnteredFrom))
            throw new InvalidOperationException("Recorded entry vertex has no reciprocal point-only contact.");

        if (SelectedExitInterfaceIndex.HasValue && SelectedExitVertexNeighbor.HasValue)
            throw new InvalidOperationException("A course can select an edge or a vertex neighbor, not both.");
        if (SelectedExitVertexNeighbor is { } selectedVertex
            && !world.VertexOnlyContacts(CurrentCell.Address).Any(contact => contact.To == selectedVertex))
            throw new InvalidOperationException("Selected vertex destination does not touch the current cell at a point.");

        if (SelectedExitInterfaceIndex is int selected
            && (selected < 0 || selected >= world.Boundaries(CurrentCell.Address).Count))
            throw new InvalidOperationException("Selected exit interface is not present on the current cell.");
        if (TravelHeading is { } heading
            && (!double.IsFinite(heading.X) || !double.IsFinite(heading.Y)
                || (heading.X == 0 && heading.Y == 0)))
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
    string? Reason)
{
    /// <summary>Set only for an actual point-only transition; edge interfaces remain null.</summary>
    public WorldCellVertexContact? VertexContact { get; init; }
}

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
        bool Endpoint, int? EndpointVertexIndex);

    public static CellCrossingResolution NextCrossing(
        PeriodicWorldTiling world,
        PeriodicCellTraversal traversal,
        CellAdjacencyMode adjacency = CellAdjacencyMode.EdgeOnly)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!Enum.IsDefined(adjacency))
            throw new ArgumentOutOfRangeException(nameof(adjacency));
        ArgumentNullException.ThrowIfNull(traversal);
        traversal.Validate(world);
        if (traversal.TravelHeading is not { } heading)
            throw new InvalidOperationException("A heading or a DM-resolved course is required to find a boundary.");

        var directionLength = Length(heading);
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
        var immediateOutward = new List<WorldCellBoundary>();

        foreach (var boundary in boundaries)
        {
            var edge = boundary.End - boundary.Start;
            var edgeLength = Length(edge);
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
            {
                // The current position may be exactly on the previous entry
                // boundary. Do not skip an outward crossing and then choose a
                // more distant, unrelated exit on the same heading.
                if (Cross(edge, direction) < -edgeLength * edgeTolerance)
                    immediateOutward.Add(boundary);
                continue;
            }

            var atEndpoint = fraction <= edgeTolerance || fraction >= 1 - edgeTolerance;
            var point = traversal.Position + direction * Math.Max(0d, distance);
            int? vertex = fraction <= edgeTolerance ? boundary.InterfaceIndex
                : fraction >= 1 - edgeTolerance ? (boundary.InterfaceIndex + 1) % boundaries.Count
                : null;
            hits.Add(new Hit(boundary, point, Math.Max(0d, distance), atEndpoint, vertex));
        }

        // A known first intersection permits safe movement through the clear ray prefix.
        static CellCrossingResolution ResolveAdjudication(
            IReadOnlyList<WorldCellBoundary> candidates, string reason,
            double? distanceToFirstIntersection = null) =>
            new(CellCrossingStatus.RequiresAdjudication, null, null, null,
                distanceToFirstIntersection, candidates, reason);

        // A saved cursor can be exactly on the vertex at the beginning of
        // a resumed watch. Treat this identically to approaching that vertex
        // from inside the cell; do not silently consume another edge.
        if (adjacency == CellAdjacencyMode.EdgeAndVertex
            && traversal.SelectedExitInterfaceIndex is null)
        {
            var currentVertices = Enumerable.Range(0, polygon.Count)
                .Where(index => traversal.Position.DistanceTo(polygon[index]) <= positionTolerance)
                .ToArray();
            if (currentVertices.Length == 1
                && ResolveVertexContact(world, traversal, currentVertices[0],
                    traversal.Position, 0d, direction, positionTolerance, cellScale)
                    is { } immediateContact)
                return immediateContact;
        }

        if (collinear.Count > 0)
            return ResolveAdjudication(collinear,
                "The selected heading follows an atomic boundary; resolve the course explicitly.");
        if (immediateOutward.Count > 0)
            return ResolveAdjudication(immediateOutward,
                "Travel begins by leaving the cell at an unselected boundary; resolve the exit interface.");

        if (hits.Count == 0)
            return ResolveAdjudication(boundaries,
                "No trustworthy boundary crossing could be determined for this course.");

        var closest = hits.Min(h => h.Distance);
        var nearest = hits.Where(h => Math.Abs(h.Distance - closest) <= positionTolerance)
            .ToArray();

        if (adjacency == CellAdjacencyMode.EdgeAndVertex
            && traversal.SelectedExitInterfaceIndex is null)
        {
            var vertices = nearest.Where(hit => hit.EndpointVertexIndex is not null)
                .Select(hit => hit.EndpointVertexIndex!.Value).Distinct().ToArray();
            if (vertices.Length == 1
                && ResolveVertexContact(world, traversal, vertices[0],
                    nearest[0].Point, closest, direction, positionTolerance, cellScale)
                    is { } vertexContact)
                return vertexContact;
        }
        if (traversal.SelectedExitVertexNeighbor is not null)
            return ResolveAdjudication(nearest.Select(hit => hit.Boundary).ToArray(),
                "The requested point-only destination is not entered by this course.", closest);

        Hit chosen;
        if (traversal.SelectedExitInterfaceIndex is int requested)
        {
            var matching = nearest.Where(h => h.Boundary.InterfaceIndex == requested).ToArray();
            if (matching.Length != 1)
                return ResolveAdjudication(
                    nearest.Select(h => h.Boundary).ToArray(),
                    "The selected interface is not the first boundary crossed by this course.", closest);
            chosen = matching[0];
        }
        else
        {
            if (nearest.Length != 1 || nearest[0].Endpoint)
                return ResolveAdjudication(
                    nearest.Select(h => h.Boundary).ToArray(),
                    "The course meets a shared vertex or more than one boundary interface.", closest);
            chosen = nearest[0];
        }

        // An explicitly selected interface is not permission to move backward
        // through it while the heading actually points into the current cell.
        // Resolved metric polygons have positive (counterclockwise) winding.
        var selectedEdge = chosen.Boundary.End - chosen.Boundary.Start;
        if (Cross(selectedEdge, direction) >= -Length(selectedEdge) * edgeTolerance)
            return ResolveAdjudication([chosen.Boundary],
                "The course does not leave the current cell through this interface.", closest);

        // Never rely on mere geometric proximity for the destination.
        var reciprocal = world.Reciprocal(chosen.Boundary);
        if (reciprocal.To != traversal.CurrentCell
            || reciprocal.From != chosen.Boundary.To)
            throw new InvalidOperationException("Crossing does not have a reciprocal authoritative interface.");

        if (chosen.Endpoint)
        {
            // A selected interface at a vertex is not sufficient by itself:
            // the course must actually enter the selected adjacent polygon.
            // Otherwise a point contact could become a phantom transition.
            var target = world.Resolve(chosen.Boundary.To.Address);
            var targetSpan = Math.Max(
                target.Polygon.Max(p => p.X) - target.Polygon.Min(p => p.X),
                target.Polygon.Max(p => p.Y) - target.Polygon.Min(p => p.Y));
            var sampleDistance = Math.Min(cellScale, targetSpan) * 1e-4;
            var sample = chosen.Point + direction * sampleDistance;
            bool strictlyInside = sampleDistance > positionTolerance * 2
                && FeatureIntersection.PointInPolygon(sample, target.Polygon);
            if (strictlyInside)
            {
                // General point membership intentionally includes the boundary
                // for cell lookup. At a T-junction it is not sufficient proof
                // that a course enters this particular neighboring interior.
                foreach (var (start, end) in target.Polygon.Select((p, i) =>
                    (p, target.Polygon[(i + 1) % target.Polygon.Count])))
                {
                    var segment = end - start;
                    double squared = segment.X * segment.X + segment.Y * segment.Y;
                    if (squared <= 0) { strictlyInside = false; break; }
                    var delta = sample - start;
                    double fraction = Math.Clamp(
                        (delta.X * segment.X + delta.Y * segment.Y) / squared, 0, 1);
                    var projectedPoint = start + segment * fraction;
                    if (sample.DistanceTo(projectedPoint) <= positionTolerance * 2)
                    {
                        strictlyInside = false;
                        break;
                    }
                }
            }
            if (!strictlyInside)
                return ResolveAdjudication([chosen.Boundary],
                    "The chosen vertex interface is not confirmed by the outgoing interior geometry.", closest);
        }

        return new CellCrossingResolution(
            CellCrossingStatus.Crosses, chosen.Boundary, reciprocal,
            chosen.Point, chosen.Distance, [chosen.Boundary], null);
    }

    private static CellCrossingResolution? ResolveVertexContact(
        PeriodicWorldTiling world, PeriodicCellTraversal traversal,
        int vertexIndex, WorldPoint intersection, double distance,
        WorldPoint direction, double tolerance, double sourceSpan)
    {
        var touching = world.VertexOnlyContacts(traversal.CurrentCell.Address)
            .Where(contact => contact.FromVertexIndex == vertexIndex
                && (traversal.SelectedExitVertexNeighbor is null
                    || traversal.SelectedExitVertexNeighbor == contact.To))
            .ToArray();
        var admitted = touching.Where(contact =>
        {
            var polygon = world.Resolve(contact.To.Address).Polygon;
            var span = Math.Max(polygon.Max(point => point.X) - polygon.Min(point => point.X),
                polygon.Max(point => point.Y) - polygon.Min(point => point.Y));
            var sampleDistance = Math.Min(sourceSpan, span) * 1e-4;
            if (!(sampleDistance > 4 * tolerance))
                return false;
            var sample = intersection + direction * sampleDistance;
            if (!FeatureIntersection.PointInPolygon(sample, polygon))
                return false;
            // Point-in-polygon includes the perimeter for lookup. A ray
            // following a side must never count as entering its interior.
            foreach (var (start, end) in polygon.Select((point, index) =>
                (point, polygon[(index + 1) % polygon.Count])))
            {
                var segment = end - start;
                var squared = segment.X * segment.X + segment.Y * segment.Y;
                if (!(squared > 0)) return false;
                var fraction = Math.Clamp(
                    ((sample.X - start.X) * segment.X + (sample.Y - start.Y) * segment.Y)
                    / squared, 0d, 1d);
                var nearest = start + segment * fraction;
                if (sample.DistanceTo(nearest) <= 2 * tolerance)
                    return false;
            }
            return true;
        }).ToArray();

        if (admitted.Length != 1) return null;
        return new CellCrossingResolution(
            CellCrossingStatus.Crosses, null, null, intersection, distance, [], null)
        { VertexContact = admitted[0] };
    }

    /// <summary>
    /// Advance at most once across an authoritative interface. Callers handle
    /// watch budgets, encounter policy, and any remaining movement; the
    /// returned cursor can be persisted before continuing.
    /// </summary>
    public static CellTraversalAdvance Advance(
        PeriodicWorldTiling world,
        PeriodicCellTraversal traversal,
        double availableWorldUnits,
        CellAdjacencyMode adjacency = CellAdjacencyMode.EdgeOnly)
    {
        if (!double.IsFinite(availableWorldUnits) || availableWorldUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWorldUnits));
        var crossing = NextCrossing(world, traversal, adjacency);
        var heading = traversal.TravelHeading!.Value;
        var length = Length(heading);
        var unit = new WorldPoint(heading.X / length, heading.Y / length);
        if (crossing.Status == CellCrossingStatus.RequiresAdjudication)
        {
            // Do not select any neighbor at an ambiguous vertex. The ray
            // prefix before the known intersection is still traversable.
            if (crossing.DistanceWorldUnits is not { } intersection || intersection <= 0)
                return new CellTraversalAdvance(traversal, 0, crossing);
            var progressed = Math.Min(availableWorldUnits, intersection);
            var positioned = traversal with
            {
                Position = traversal.Position + unit * progressed
            };
            positioned.Validate(world);
            if (availableWorldUnits < intersection)
                return new CellTraversalAdvance(positioned, progressed,
                    new CellCrossingResolution(CellCrossingStatus.None, null, null,
                        null, null, [], null));
            return new CellTraversalAdvance(positioned, progressed, crossing);
        }

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

        if (crossing.VertexContact is { } vertex)
        {
            var arrived = traversal with
            {
                CurrentCell = vertex.To,
                Position = crossing.Point!.Value,
                EnteredFrom = vertex.From,
                EntryInterfaceIndex = null,
                EntryVertexIndex = vertex.ToVertexIndex,
                SelectedExitInterfaceIndex = null,
                SelectedExitVertexNeighbor = null
            };
            arrived.Validate(world);
            return new CellTraversalAdvance(arrived, needed, crossing);
        }

        var exit = crossing.Exit!;
        var entry = crossing.Entry!;
        var entered = traversal with
        {
            CurrentCell = exit.To,
            Position = crossing.Point!.Value,
            EnteredFrom = exit.From,
            EntryInterfaceIndex = entry.InterfaceIndex,
            EntryVertexIndex = null,
            SelectedExitInterfaceIndex = null,
            SelectedExitVertexNeighbor = null
        };
        entered.Validate(world);
        return new CellTraversalAdvance(entered, needed, crossing);
    }

    private static double Cross(WorldPoint a, WorldPoint b) => a.X * b.Y - a.Y * b.X;

    private static double Length(WorldPoint point)
    {
        var max = Math.Max(Math.Abs(point.X), Math.Abs(point.Y));
        if (max == 0) return 0;
        var x = point.X / max;
        var y = point.Y / max;
        return max * Math.Sqrt(x * x + y * y);
    }
}
