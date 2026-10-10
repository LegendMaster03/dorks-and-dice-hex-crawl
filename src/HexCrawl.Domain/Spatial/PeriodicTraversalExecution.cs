namespace HexCrawl.Domain.Spatial;

/// <summary>
/// A transition occurred at an authoritative atomic boundary, not by proximity
/// or arithmetic on a shape-specific direction index.
/// </summary>
public sealed record PeriodicTraversalTransition(
    WorldCellId From, WorldCellId To,
    int ExitInterfaceIndex, int EntryInterfaceIndex,
    WorldPoint CrossingPoint, double CumulativeWorldDistance);

public sealed record PeriodicTraversalExecutionResult(
    PeriodicCellTraversal Traversal,
    double ConsumedWorldUnits,
    DistanceMeasure? ConsumedPhysicalDistance,
    int CompletedCellSteps,
    IReadOnlyList<PeriodicTraversalTransition> Transitions,
    CellCrossingResolution? PendingAdjudication,
    bool BoundaryReviewRequired)
{
    public bool RequiresAdjudication => PendingAdjudication is not null;
}

/// <summary>
/// Shared deterministic spatial executor for a route segment. The caller owns
/// time, events, navigation, encounters and continuation policy. This class
/// owns only valid crossings and calibrated geometric distance. Both travel
/// policies use the same atomic-interface resolver.
/// </summary>
public static class PeriodicTraversalExecution
{
    private const int MaxTransitionsPerCall = 10000;

    /// <summary>
    /// Consume a resolved physical-distance budget along the current heading.
    /// A missing physical calibration is an explicit unmet requirement.
    /// </summary>
    public static PeriodicTraversalExecutionResult AdvanceDistance(
        PeriodicWorldTiling world,
        PeriodicCellTraversal start,
        DistanceMeasure distance,
        bool continueAcrossBoundaries)
    {
        ArgumentNullException.ThrowIfNull(world);
        var calibration = world.PhysicalDistancePerWorldUnit
            ?? throw new NotSupportedException(
                "This world has no physical-distance calibration; physical travel needs DM adjudication.");
        var converted = distance.ConvertTo(calibration.Unit);
        var worldUnits = converted.Value / calibration.Value;
        if (!double.IsFinite(worldUnits))
            throw new ArgumentOutOfRangeException(nameof(distance), "Travel budget exceeds finite world units.");
        return Execute(world, start, worldUnits, null, continueAcrossBoundaries);
    }

    /// <summary>
    /// Consume at most the requested number of atomic boundary transitions.
    /// One step is one successful transition, not an assumed fixed distance.
    /// Physical distance is reported only when calibration is available.
    /// </summary>
    public static PeriodicTraversalExecutionResult AdvanceCellSteps(
        PeriodicWorldTiling world,
        PeriodicCellTraversal start,
        int steps,
        bool continueAcrossBoundaries)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        return Execute(world, start, null, steps, continueAcrossBoundaries);
    }

    private static PeriodicTraversalExecutionResult Execute(
        PeriodicWorldTiling world,
        PeriodicCellTraversal start,
        double? worldDistanceBudget,
        int? stepBudget,
        bool continueAcrossBoundaries)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(start);
        start.Validate(world);
        if (start.TravelHeading is null)
            throw new InvalidOperationException("Cell traversal requires a resolved nonzero heading.");
        var remaining = worldDistanceBudget;
        var cursor = start;
        var consumed = 0d;
        var transitions = new List<PeriodicTraversalTransition>();
        CellCrossingResolution? pending = null;
        var review = false;
        int steps = 0;

        while ((remaining is > 0 || stepBudget is { } requested && steps < requested)
            && transitions.Count < MaxTransitionsPerCall)
        {
            var crossing = PeriodicCellTraversalGeometry.NextCrossing(world, cursor);
            if (crossing.Status == CellCrossingStatus.RequiresAdjudication)
            {
                pending = crossing;
                break;
            }
            if (crossing.Status != CellCrossingStatus.Crosses
                || crossing.DistanceWorldUnits is not { } needed
                || !double.IsFinite(needed))
                throw new InvalidOperationException("Unable to establish a finite authoritative cell crossing.");

            var available = remaining ?? needed;
            var step = PeriodicCellTraversalGeometry.Advance(world, cursor, available);
            cursor = step.Traversal;
            consumed += step.ConsumedWorldUnits;
            if (remaining.HasValue)
                remaining = Math.Max(0d, remaining.Value - step.ConsumedWorldUnits);

            if (step.Crossing.Status != CellCrossingStatus.Crosses)
                break;

            var exit = step.Crossing.Exit!;
            var entry = step.Crossing.Entry!;
            transitions.Add(new PeriodicTraversalTransition(
                exit.From, exit.To, exit.InterfaceIndex, entry.InterfaceIndex,
                step.Crossing.Point!.Value, consumed));
            steps++;

            if (!continueAcrossBoundaries
                && (remaining is > 0 || stepBudget is { } requestedSteps && steps < requestedSteps))
            {
                review = true;
                break;
            }
        }

        if (transitions.Count >= MaxTransitionsPerCall
            && (remaining is > 0 || stepBudget is { } requested && steps < requested))
            throw new InvalidOperationException(
                "Travel exceeded the bounded number of cell crossings; resolve a shorter segment.");

        var physical = world.PhysicalDistancePerWorldUnit is { } calibration
            ? new DistanceMeasure(consumed * calibration.Value, calibration.Unit)
            : (DistanceMeasure?)null;
        return new PeriodicTraversalExecutionResult(
            cursor, consumed, physical, steps, transitions, pending, review);
    }
}
