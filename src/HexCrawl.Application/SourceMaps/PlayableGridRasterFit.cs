using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

/// <summary>
/// Exact source-pixel-to-world placement for a full-bleed finite periodic
/// quadrilateral grid. Neither an image's dimensions nor a ruleset name
/// proves that its artwork has the claimed cell count: the DM must preview
/// and explicitly accept this suggested registration.
/// </summary>
public static class PlayableGridRasterFit
{
    public static MapRegistrationTransform Calculate(
        PeriodicWorldTiling tiling, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(tiling);
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new InvalidOperationException("A grid-fit requires a raster with valid pixel dimensions.");
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
            new(new WorldPoint(0, 0), origin),
            new(new WorldPoint(pixelWidth, 0), farU),
            new(new WorldPoint(0, pixelHeight), farV)
        ]);
        var coverage = AffineRegistrationSolver.Coverage(alignment, pixelWidth, pixelHeight);
        if (coverage.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new InvalidOperationException("The fitted grid exceeds representable world coordinates.");
        return alignment;
    }
}
