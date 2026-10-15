using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

/// <summary>
/// Source-pixel-to-world placement for a finite periodic quadrilateral grid,
/// allowing margins outside the declared rectangular grid window.
/// Neither an image's dimensions nor a ruleset name
/// proves that its artwork has the claimed cell count: the DM must preview
/// and explicitly accept this suggested registration.
/// </summary>
public sealed record RasterGridMargins(
    double LeftPercent = 0,
    double TopPercent = 0,
    double RightPercent = 0,
    double BottomPercent = 0)
{
    public (double Left, double Top, double Right, double Bottom) InnerRectangle(
        int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException("The raster must have valid dimensions.");
        var values = new[] { LeftPercent, TopPercent, RightPercent, BottomPercent };
        if (values.Any(value => !double.IsFinite(value) || value < 0 || value >= 100)
            || LeftPercent + RightPercent >= 100
            || TopPercent + BottomPercent >= 100)
            throw new InvalidOperationException(
                "Map margins must be finite non-negative percentages and leave a positive interior grid.");
        var left = pixelWidth * LeftPercent / 100;
        var top = pixelHeight * TopPercent / 100;
        var right = pixelWidth * (100 - RightPercent) / 100;
        var bottom = pixelHeight * (100 - BottomPercent) / 100;
        if (right - left <= 1e-6 || bottom - top <= 1e-6)
            throw new InvalidOperationException("Map margins leave too little space for a grid.");
        return (left, top, right, bottom);
    }
}

public static class PlayableGridRasterFit
{
    public static MapRegistrationTransform Calculate(
        PeriodicWorldTiling tiling, int pixelWidth, int pixelHeight,
        RasterGridMargins? margins = null)
    {
        ArgumentNullException.ThrowIfNull(tiling);
        var (left, top, right, bottom) =
            (margins ?? new RasterGridMargins()).InnerRectangle(pixelWidth, pixelHeight);
        if (tiling.PlayableBounds is not { } bounds)
            throw new InvalidOperationException(
                "Set finite world map bounds before fitting the entire raster to the playable grid.");
        bounds.Validate();
        if (tiling.Topology.MotifCells.Count != 1)
            throw new InvalidOperationException(
                "Full-image grid fit requires one quadrilateral cell per period. Use verified grid detection or manual registration for this tiling.");

        var cell = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(bounds.MinU, bounds.MinV)));
        var vertices = cell.Polygon;
        if (vertices.Count != 4)
            throw new InvalidOperationException(
                "Full-image grid fit requires quadrilateral cells. Use verified grid detection or manual registration instead.");

        var u = tiling.Realization.TranslationU;
        var v = tiling.Realization.TranslationV;
        var scale = Math.Max(1, Math.Max(
            Math.Sqrt(u.X * u.X + u.Y * u.Y),
            Math.Sqrt(v.X * v.X + v.Y * v.Y)));
        var tolerance = scale * 1e-7;
        static bool Near(WorldPoint point, WorldPoint target, double epsilon) =>
            Math.Abs(point.X - target.X) <= epsilon && Math.Abs(point.Y - target.Y) <= epsilon;

        WorldPoint? corner = null;
        foreach (var point in vertices)
        {
            var alongU = new WorldPoint(point.X + u.X, point.Y + u.Y);
            var alongV = new WorldPoint(point.X + v.X, point.Y + v.Y);
            var opposite = new WorldPoint(point.X + u.X + v.X, point.Y + u.Y + v.Y);
            if (vertices.Any(vertex => Near(vertex, alongU, tolerance))
                && vertices.Any(vertex => Near(vertex, alongV, tolerance))
                && vertices.Any(vertex => Near(vertex, opposite, tolerance)))
            {
                corner = point;
                break;
            }
        }
        if (corner is not { } origin)
            throw new InvalidOperationException(
                "The tile motif is not exactly the rectangle of its periodic translations. Use detected or manual registration.");

        // Use the same three-point affine registration solver as manual
        // source-map placement. Bounds only supply the authoritative matching
        // corners; they do not introduce another image-transform algorithm.
        var farU = new WorldPoint(
            origin.X + bounds.Width * u.X, origin.Y + bounds.Width * u.Y);
        var farV = new WorldPoint(
            origin.X + bounds.Height * v.X, origin.Y + bounds.Height * v.Y);
        var alignment = AffineRegistrationSolver.Solve(
        [
            new(new WorldPoint(left, top), origin),
            new(new WorldPoint(right, top), farU),
            new(new WorldPoint(left, bottom), farV)
        ]);
        var coverage = AffineRegistrationSolver.Coverage(alignment, pixelWidth, pixelHeight);
        if (coverage.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidOperationException("The fitted grid exceeds representable world coordinates.");
        return alignment;
    }
}
