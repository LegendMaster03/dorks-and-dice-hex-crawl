namespace HexCrawl.Infrastructure.Wonderdraft;

public enum WonderdraftRasterRelationshipKind
{
    Exact,
    ProportionalScale,
    UnresolvedDimensionMismatch
}

public sealed record WonderdraftRasterRelationship(
    WonderdraftRasterRelationshipKind Kind,
    double? UniformScale,
    double ScaleX,
    double ScaleY,
    double WidthResidualPixels,
    double HeightResidualPixels,
    string Explanation)
{
    private const double ExportRoundingTolerancePixels = 0.75;

    public bool CanMapProjectCoordinates =>
        Kind is WonderdraftRasterRelationshipKind.Exact
            or WonderdraftRasterRelationshipKind.ProportionalScale;

    public static WonderdraftRasterRelationship Analyze(
        int projectWidth,
        int projectHeight,
        int rasterWidth,
        int rasterHeight)
    {
        if (projectWidth <= 0 || projectHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectWidth),
                "Wonderdraft project dimensions must be positive.");
        }
        if (rasterWidth <= 0 || rasterHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rasterWidth),
                "Raster dimensions must be positive.");
        }

        var scaleX = (double)rasterWidth / projectWidth;
        var scaleY = (double)rasterHeight / projectHeight;
        if (projectWidth == rasterWidth && projectHeight == rasterHeight)
        {
            return new WonderdraftRasterRelationship(
                WonderdraftRasterRelationshipKind.Exact,
                1,
                scaleX,
                scaleY,
                0,
                0,
                "The raster dimensions exactly match the Wonderdraft project canvas.");
        }

        // Least-squares uniform scale. Integer export dimensions may differ from the ideal
        // proportional size by a fraction of one pixel because of rounding.
        var denominator = ((double)projectWidth * projectWidth)
            + ((double)projectHeight * projectHeight);
        var uniformScale = (((double)rasterWidth * projectWidth)
            + ((double)rasterHeight * projectHeight)) / denominator;
        var widthResidual = rasterWidth - (projectWidth * uniformScale);
        var heightResidual = rasterHeight - (projectHeight * uniformScale);

        if (Math.Abs(widthResidual) <= ExportRoundingTolerancePixels
            && Math.Abs(heightResidual) <= ExportRoundingTolerancePixels)
        {
            return new WonderdraftRasterRelationship(
                WonderdraftRasterRelationshipKind.ProportionalScale,
                uniformScale,
                scaleX,
                scaleY,
                widthResidual,
                heightResidual,
                $"The raster is consistent with a proportional Wonderdraft export at {uniformScale:g6}×; residual dimension error is within export rounding tolerance.");
        }

        return new WonderdraftRasterRelationship(
            WonderdraftRasterRelationshipKind.UnresolvedDimensionMismatch,
            null,
            scaleX,
            scaleY,
            widthResidual,
            heightResidual,
            "The raster dimensions are not consistent with a proportional export of the Wonderdraft canvas. The dimensions alone can not distinguish cropping, padding, or nonuniform scaling, so project coordinates must not be stretched to fit automatically.");
    }
}
