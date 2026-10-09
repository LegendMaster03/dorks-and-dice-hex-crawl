namespace HexCrawl.Application;

/// <summary>
/// Experimental raster observation, never an accepted world tiling, stored
/// map registration, or proof that measured polygons form a metric tiling.
/// </summary>
public sealed record PeriodicMotifInvestigation(
    string Status,
    string Reason,
    bool Authoritative,
    string Maturity,
    string? SurveyorApiVersion,
    PeriodicMotifCandidate? Candidate,
    PeriodicMotifEvidence? Evidence,
    MapAnalysisSource? Source,
    MapAnalysisRaster? Analysis);

public sealed record PeriodicMotifCandidate(
    string DsSymbol,
    IReadOnlyList<MapAnalysisPoint> TranslationBasisSourcePixels,
    IReadOnlyList<ObservedMotifCell> MotifCells);

public sealed record ObservedMotifCell(
    string ProvisionalId,
    IReadOnlyList<MapAnalysisPoint> PolygonSourcePixels,
    IReadOnlyList<ObservedMotifBoundary> Boundaries);

public sealed record ObservedMotifBoundary(
    int SideIndex,
    string TargetProvisionalId,
    int TargetSideIndex,
    long TranslationU,
    long TranslationV,
    int SupportingObservations);

public sealed record PeriodicMotifEvidence(
    int MatchedHypotheses,
    int CheckedHypotheses,
    int RejectedHypotheses,
    int MinimumEdgeObservations,
    double OriginalRasterEdgeSupport,
    double MaximumRigidVertexResidualSourcePixels,
    double? TranslationRefinementResidualSourcePixels);

/// <summary>Opt-in investigation: existing hex analysis remains on v2.</summary>
public interface IPeriodicMotifInvestigationService
{
    Task<PeriodicMotifInvestigation> InvestigateAsync(
        Stream raster,
        string mediaType,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
