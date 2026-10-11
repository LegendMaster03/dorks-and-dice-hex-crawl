using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Applies an intentional turn's nongeometric budget charge before movement.
/// Only the returned geometric travel amount is passed to the polygon executor;
/// the original externally resolved amount and provenance remain unchanged.
/// No cost is included in recorded physical distance or in cell-step counts.
/// </summary>
public sealed record SpatialTurnBudgetDebit(
    ResolvedTravelAmount GeometricTravel,
    DistanceMeasure? ChargedBudget,
    DistanceMeasure? OutstandingBudgetCharge);

public static class SpatialTurnBudgetAccounting
{
    public static SpatialTurnBudgetDebit Debit(
        PeriodicWorldTiling world,
        ProcedureSpatialTurnRuntime? policy,
        PeriodicCellTraversal cursor,
        WorldPoint? previousIntendedHeading,
        WorldPoint nextIntendedHeading,
        bool deliberateDoubleBack,
        ActualDistanceResolutionMode actualDistanceMode,
        ResolvedTravelAmount segmentTravel,
        DistanceMeasure? outstandingCharge = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(segmentTravel);
        var newCharge = policy is null || deliberateDoubleBack || previousIntendedHeading is null
            ? null
            : policy.CreateBudgetPolicy().EvaluateDeliberateTurn(
                world, cursor with { TravelHeading = previousIntendedHeading }, nextIntendedHeading);
        if (newCharge is null && outstandingCharge is null)
            return new SpatialTurnBudgetDebit(segmentTravel, null, null);

        if (segmentTravel.CellSteps is not null || segmentTravel.HexSteps is not null)
            throw new NotSupportedException(
                "A distance-denominated turning cost needs continuous-distance movement. A discrete-step conversion requires an explicit separate procedure rule.");
        if (segmentTravel.ActualDistance is not { } actual
            || segmentTravel.ExpectedDistance is not { } expected)
            throw new InvalidOperationException("Turning costs require an explicit resolved travel budget.");

        double required = 0;
        if (outstandingCharge is { } outstanding)
            required += outstanding.ConvertTo(actual.Unit).Value;
        if (newCharge is { } newDebit)
            required += newDebit.BudgetCost.ConvertTo(actual.Unit).Value;
        if (!double.IsFinite(required) || required < 0)
            throw new InvalidOperationException("Outstanding travel budget charge is invalid.");

        var paid = Math.Min(actual.Value, required);
        var debt = required - paid;
        var remaining = Math.Max(0d, actual.Value - paid);
        if (actualDistanceMode is not (ActualDistanceResolutionMode.Fixed
            or ActualDistanceResolutionMode.VariableResolved))
            throw new InvalidOperationException("Unsupported actual-distance resolution policy.");
        var fixedDistance = actualDistanceMode == ActualDistanceResolutionMode.Fixed;
        var geometricExpected = fixedDistance
            ? new DistanceMeasure(remaining, actual.Unit)
            : expected;
        return new SpatialTurnBudgetDebit(
            ResolvedTravelAmount.Distance(
                geometricExpected,
                new DistanceMeasure(remaining, actual.Unit),
                segmentTravel.Provenance),
            paid > 0 ? new DistanceMeasure(paid, actual.Unit) : null,
            debt > 0 ? new DistanceMeasure(debt, actual.Unit) : null);
    }
}
