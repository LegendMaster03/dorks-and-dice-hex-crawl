using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// A nonspatial cost against a procedure's movement budget. Amount is in the
/// procedure's travel-budget distance unit; it is not measured displacement
/// and must never be added to CellExpeditionState.DistanceTraveled.
/// </summary>
public sealed record NonGeometricTravelCharge(DistanceMeasure BudgetCost, string Reason);

/// <summary>
/// Represents a historical turning penalty as a configurable budget charge,
/// independent of the actual geometric cursor. This policy does not dispatch
/// based on the name or geometry of the tiling.
///
/// A fractional neighboring center-distance charge can be represented by
/// its configured factor for any regular, calibrated tiling. For an
/// inhomogeneous tiling, the procedure must supply an explicit cost reference
/// instead of silently selecting an arbitrary adjacent center distance.
/// </summary>
public sealed record SpatialTurnBudgetPolicy(double NeighborCenterDistanceFactor)
{
    public void Validate()
    {
        if (!double.IsFinite(NeighborCenterDistanceFactor) || NeighborCenterDistanceFactor < 0)
            throw new InvalidOperationException("Turning cost factor must be finite and nonnegative.");
    }

    /// <summary>
    /// Call only for an explicitly requested course change. A navigation
    /// failure or automatic angular veer is not a deliberate turning action.
    /// </summary>
    public NonGeometricTravelCharge? EvaluateDeliberateTurn(
        PeriodicWorldTiling world, PeriodicCellTraversal cursor, WorldPoint nextHeading)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cursor);
        Validate();
        cursor.Validate(world);
        ValidateHeading(nextHeading);
        if (cursor.TravelHeading is not { } previous || SameCourse(previous, nextHeading)
            || NeighborCenterDistanceFactor == 0)
            return null;

        if (world.PhysicalTurnCostReference is { } explicitReference)
        {
            // This reference is authored data, independent of cell side counts.
            var explicitCost = explicitReference.Value * NeighborCenterDistanceFactor;
            if (!double.IsFinite(explicitCost))
                throw new InvalidOperationException("Explicit turning cost exceeded the supported budget range.");
            return new NonGeometricTravelCharge(
                new DistanceMeasure(explicitCost, explicitReference.Unit),
                "procedure-direction-change");
        }

        var center = world.Resolve(cursor.CurrentCell.Address).Center;
        var neighbors = world.Neighbors(cursor.CurrentCell.Address);
        if (neighbors.Count == 0)
            throw new NotSupportedException("Turning-cost reference requires at least one adjacent cell.");

        var distances = neighbors
            .Select(id => center.DistanceTo(world.Resolve(id.Address).Center))
            .ToArray();
        var reference = distances[0];
        if (!double.IsFinite(reference) || reference <= 0
            || distances.Any(distance => !double.IsFinite(distance)
                || Math.Abs(distance - reference) > 1e-9 * Math.Max(1d, reference)))
            throw new NotSupportedException(
                "This tiling has unequal neighbor center distances; a procedure-defined explicit turning-cost reference is required.");

        // A procedure using a distance-based effort budget needs calibration.
        // No physical distance is synthesized for uncalibrated cell-step worlds.
        var calibration = world.PhysicalDistancePerWorldUnit
            ?? throw new NotSupportedException("Distance-based turning costs require physical calibration.");
        var cost = reference * calibration.Value * NeighborCenterDistanceFactor;
        if (!double.IsFinite(cost))
            throw new InvalidOperationException("Turning cost exceeded the supported budget range.");
        return new NonGeometricTravelCharge(
            new DistanceMeasure(cost, calibration.Unit), "procedure-direction-change");
    }

    private static bool SameCourse(WorldPoint a, WorldPoint b)
    {
        ValidateHeading(a);
        var aLength = Math.Sqrt(a.X * a.X + a.Y * a.Y);
        var bLength = Math.Sqrt(b.X * b.X + b.Y * b.Y);
        if (!double.IsFinite(aLength) || !double.IsFinite(bLength))
            throw new InvalidOperationException("Direction length exceeds finite range.");
        var cosine = (a.X / aLength) * (b.X / bLength)
            + (a.Y / aLength) * (b.Y / bLength);
        return cosine >= 1d - 1e-12;
    }

    private static void ValidateHeading(WorldPoint heading)
    {
        if (!double.IsFinite(heading.X) || !double.IsFinite(heading.Y)
            || heading.X == 0 && heading.Y == 0)
            throw new InvalidOperationException("Turning cost requires a finite nonzero heading.");
    }
}
