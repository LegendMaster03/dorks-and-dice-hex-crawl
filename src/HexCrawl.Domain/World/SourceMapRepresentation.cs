using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public enum SourceMapRole
{
    Gm,
    Player,
    Neutral,
    Other
}

public enum MapRegistrationTransformKind
{
    Affine,
    Projective
}

public sealed record MapRegistrationTransform(
    MapRegistrationTransformKind Kind,
    double M11,
    double M12,
    double M13,
    double M21,
    double M22,
    double M23,
    double M31,
    double M32)
{
    public static MapRegistrationTransform Affine(
        double m11,
        double m12,
        double m21,
        double m22,
        double offsetX,
        double offsetY) =>
        new(MapRegistrationTransformKind.Affine, m11, m12, offsetX, m21, m22, offsetY, 0, 0);

    public WorldPoint ToWorld(WorldPoint sourcePixel)
    {
        var denominator = (M31 * sourcePixel.X) + (M32 * sourcePixel.Y) + 1d;
        if (Math.Abs(denominator) < 1e-12)
        {
            throw new InvalidOperationException("The map registration transform is undefined at this source point.");
        }

        return new WorldPoint(
            ((M11 * sourcePixel.X) + (M12 * sourcePixel.Y) + M13) / denominator,
            ((M21 * sourcePixel.X) + (M22 * sourcePixel.Y) + M23) / denominator);
    }
}

public sealed record MapRegistrationControlPoint(WorldPoint SourcePixel, WorldPoint WorldPoint);

public sealed record SourceMapRepresentation(
    Guid Id,
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    int PixelWidth = 0,
    int PixelHeight = 0,
    string MediaType = "application/octet-stream",
    string? OriginalFileName = null,
    IReadOnlyList<SourceMapContentElement>? ImportedContent = null,
    SourceMapImportProvenance? ImportProvenance = null,
    SourceMapSourceArchive? SourceArchive = null);
