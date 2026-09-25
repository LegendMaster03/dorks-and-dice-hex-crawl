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


    public static ResolvedTravelAmount Override(
        DistanceMeasure expectedDistance,
        DistanceMeasure actualDistance,
        string? note = null) =>
        ResolvedTravelAmount.Distance(
            expectedDistance,
            actualDistance,
            new ResolutionProvenance(ResolutionSource.DmOverride, note));

}
