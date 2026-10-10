using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    /// <summary>
    /// Resolve one topology-independent movement budget from the pinned
    /// materialized procedure. This is the spatial execution boundary used by
    /// the future generalized watch adapter; it does not create a separate
    /// game engine or change existing hex watch/event semantics.
    /// </summary>
    public PeriodicTraversalExecutionResult ResolveCellTravel(
        PeriodicWorldTiling world,
        CampaignProcedure procedure,
        PeriodicCellTraversal traversal,
        ResolvedTravelAmount travel,
        bool continueAcrossBoundaries)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(travel);

        var policy = GenericProcedureRuntime.Bind(procedure).Movement;
        if (policy.MechanicVersion != 2)
            throw new NotSupportedException(
                "Legacy hex movement semantics are not applicable to generalized world geometry. An explicit version 2 movement procedure is required.");

        var expected = DelaneyDressTopology.Inspect(procedure.TilingDsSymbol, 2048);
        var actual = DelaneyDressTopology.Inspect(world.Topology.QuotientDsSymbol, 2048);
        if (expected.Status != DelaneyDressStatus.Euclidean
            || actual.Status != DelaneyDressStatus.Euclidean
            || !string.Equals(expected.Symbol?.Canonical, actual.Symbol?.Canonical, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The pinned procedure's tiling requirement does not match this world's accepted topology.");

        traversal.Validate(world);
        switch (policy.TravelResolution)
        {
            case TravelResolutionMode.ContinuousDistance:
                ValidateNativeTravelAmountPolicy(policy, travel);
                return PeriodicTraversalExecution.AdvanceDistance(
                    world, traversal, travel.ActualDistance!.Value, continueAcrossBoundaries);

            case TravelResolutionMode.CellSteps:
                if (travel.CellSteps is not { } steps || steps < 0
                    || travel.HexSteps is not null || travel.ExpectedDistance is not null
                    || travel.ActualDistance is not null)
                    throw new InvalidOperationException(
                        "Version 2 cell-step travel requires one resolved, non-negative boundary-transition count.");
                return PeriodicTraversalExecution.AdvanceCellSteps(
                    world, traversal, steps, continueAcrossBoundaries);

            default:
                throw new InvalidOperationException(
                    "The pinned generalized procedure has no executable cell movement policy.");
        }
    }
}
