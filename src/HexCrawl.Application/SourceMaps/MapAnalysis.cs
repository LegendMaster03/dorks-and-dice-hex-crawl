namespace HexCrawl.Application;

public sealed record MapAnalysisOptions(
    double? MinimumSpacingPixels = null,
    double? MaximumSpacingPixels = null,
    int? MaximumEdgeSamples = null,
    double? MinimumConfidence = null);

public sealed record MapAnalysisPoint(double X, double Y);

public sealed record MapHexGridFit(
    string Orientation,
    double RotationDegrees,
    double CenterSpacingPixels,
    MapAnalysisPoint AnchorPixel,
    double Confidence,
    double ResidualPixels,
    double SupportCoverage,
    double OrientationSupport,
    double TranslationScore,
    double CompetingTranslationScore,
    double LinePeriodicityScore,
    double PhaseScore);

public sealed record MapAnalysisSource(int Width, int Height, string MediaType);
public sealed record MapAnalysisRaster(int Width, int Height, double Scale, bool SourceResolutionVerified);

public sealed record MapHexGridAnalysis(
    string ApiVersion,
    string Capability,
    string Status,
    string Reason,
    MapAnalysisSource Source,
    MapAnalysisRaster Analysis,
    MapHexGridFit? Fit,
    string? TilingDsSymbol = null);

public interface IMapAnalysisService
{
    Task<MapHexGridAnalysis> DetectHexGridAsync(
        Stream raster,
        string mediaType,
        MapAnalysisOptions options,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

public sealed class MapAnalysisUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class MapAnalysisTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);

public sealed class MapAnalysisAuthenticationException(string message)
    : Exception(message);

public sealed class MapAnalysisProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
