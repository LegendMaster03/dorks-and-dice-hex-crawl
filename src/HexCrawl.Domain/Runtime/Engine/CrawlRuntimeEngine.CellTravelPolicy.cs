using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Verifies resolved physical-distance input for native cell traversal.
/// This policy has no coordinate system or polygon-family dependency.
/// </summary>
public sealed partial class CrawlRuntimeEngine
{
    private static void ValidateCellDistanceTravelPolicy(
        ProcedureMovementRuntime movementPolicy,
        ResolvedTravelAmount travel)
    {
        if (movementPolicy.TravelResolution != TravelResolutionMode.ContinuousDistance)
            throw new InvalidOperationException(
                "Physical-distance resolution requires an executable continuous-distance movement policy.");
        if (travel.CellSteps is not null)
            throw new InvalidOperationException(
                "Continuous-distance travel accepts physical distances, not discrete boundary-transition counts.");
        switch (movementPolicy.ActualDistanceResolution)
        {
            case ActualDistanceResolutionMode.Fixed:
                if (travel.ExpectedDistance is null || travel.ActualDistance is null)
                    throw new InvalidOperationException(
                        "Fixed continuous-distance travel requires one effective distance represented consistently as both expected and actual distance.");
                if (!EquivalentCellDistance(travel.ExpectedDistance.Value, travel.ActualDistance.Value))
                    throw new InvalidOperationException(
                        "Fixed continuous-distance travel requires expected and actual distance to represent the same effective distance.");
                break;

            case ActualDistanceResolutionMode.VariableResolved:
                if (travel.ExpectedDistance is null || travel.ActualDistance is null)
                    throw new InvalidOperationException(
                        "Variable-resolved continuous-distance travel requires both expected and actual distance values.");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported actual-distance resolution mode '{movementPolicy.ActualDistanceResolution}'.");
        }
    }

    private static bool EquivalentCellDistance(DistanceMeasure expected, DistanceMeasure actual)
    {
        try
        {
            var converted = Convert(actual, expected.Unit);
            return Math.Abs(expected.Value - converted.Value) <= Epsilon;
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                "Fixed continuous-distance travel requires expected and actual distance to use compatible units.",
                error);
        }
    }
}
