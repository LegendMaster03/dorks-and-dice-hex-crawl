using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public static class TravelDistanceResolver
{
    public static ResolvedTravelAmount Fixed(
        DistanceMeasure expectedDistance,
        ResolutionProvenance? provenance = null) =>
        ResolvedTravelAmount.Distance(
            expectedDistance,
            expectedDistance,
            provenance ?? ResolutionProvenance.ProcedureDefault);

    public static ResolvedTravelAmount AlexandrianVariable(
        DistanceMeasure expectedDistance,
        int firstD6,
        int secondD6,
        ResolutionProvenance provenance)
    {
        ValidateD6(firstD6, nameof(firstD6));
        ValidateD6(secondD6, nameof(secondD6));

        var percentage = (firstD6 + secondD6 + 3) * 0.1d;
        return ResolvedTravelAmount.Distance(
            expectedDistance,
            new DistanceMeasure(expectedDistance.Value * percentage, expectedDistance.Unit),
            provenance);
    }

    public static ResolvedTravelAmount Override(
        DistanceMeasure expectedDistance,
        DistanceMeasure actualDistance,
        string? note = null) =>
        ResolvedTravelAmount.Distance(
            expectedDistance,
            actualDistance,
            new ResolutionProvenance(ResolutionSource.DmOverride, note));

    private static void ValidateD6(int value, string parameterName)
    {
        if (value is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A d6 result must be between 1 and 6.");
        }
    }
}
