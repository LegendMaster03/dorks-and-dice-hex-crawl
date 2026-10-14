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
        // A valid ruleset can name a smaller symmetry quotient than the
        // world's realized translation cover. Creation accepts equivalence
        // proven by that cover; execution must apply the same requirement.
        var comparison = DelaneyDressTopology.Compare(
            procedure.TilingDsSymbol,
            world.Topology.QuotientDsSymbol,
            world.Topology.TranslationDsSymbol);
        if (comparison.Status is not (
            TilingComparisonStatus.ExactIdentity
            or TilingComparisonStatus.ChamberIsomorphic
            or TilingComparisonStatus.ProvenEquivalent))
            throw new InvalidOperationException(
                "The pinned procedure's tiling requirement does not match this world's accepted topology: "
                + comparison.Reason);

        traversal.Validate(world);
        switch (policy.TravelResolution)
        {
            case TravelResolutionMode.ContinuousDistance:
                ValidateCellDistanceTravelPolicy(policy, travel);
                return PeriodicTraversalExecution.AdvanceDistance(
                    world, traversal, travel.ActualDistance!.Value, continueAcrossBoundaries,
                    policy.Adjacency);

            case TravelResolutionMode.CellSteps:
                if (travel.CellSteps is not { } steps || steps < 0
                    || travel.ExpectedDistance is not null
                    || travel.ActualDistance is not null)
                    throw new InvalidOperationException(
                        "Cell-step travel requires one resolved, non-negative boundary-transition count.");
                return PeriodicTraversalExecution.AdvanceCellSteps(
                    world, traversal, steps, continueAcrossBoundaries, policy.Adjacency);

            default:
                throw new InvalidOperationException(
                    "The pinned generalized procedure has no executable cell movement policy.");
        }
    }
}
