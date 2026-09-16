using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public static class AffineRegistrationSolver
{
    private const double Epsilon = 1e-10;

    public static MapRegistrationTransform Solve(IReadOnlyList<MapRegistrationControlPoint> controlPoints)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        if (controlPoints.Count != 3)
        {
            throw new ArgumentException("Affine registration requires exactly three control-point pairs.", nameof(controlPoints));
        }

        foreach (var point in controlPoints)
        {
            ValidateFinite(point.SourcePixel, "source");
            ValidateFinite(point.WorldPoint, "world");
        }

        EnsureDistinct(controlPoints.Select(point => point.SourcePixel).ToArray(), "source");
        EnsureDistinct(controlPoints.Select(point => point.WorldPoint).ToArray(), "world");

        var source = controlPoints.Select(point => point.SourcePixel).ToArray();
        var determinant = Determinant(source[0], source[1], source[2]);
        if (Math.Abs(determinant) <= Epsilon)
        {
            throw new ArgumentException("Source control points are collinear and do not define an affine basis.", nameof(controlPoints));
        }

        var x = SolveAxis(source, controlPoints.Select(point => point.WorldPoint.X).ToArray(), determinant);
        var y = SolveAxis(source, controlPoints.Select(point => point.WorldPoint.Y).ToArray(), determinant);
        var transform = MapRegistrationTransform.Affine(x.A, x.B, y.A, y.B, x.C, y.C);

        var linearDeterminant = (transform.M11 * transform.M22) - (transform.M12 * transform.M21);
        if (!double.IsFinite(linearDeterminant) || Math.Abs(linearDeterminant) <= Epsilon)
        {
            throw new ArgumentException("Control points produce a degenerate affine transform.", nameof(controlPoints));
        }

        foreach (var value in new[]
        {
            transform.M11, transform.M12, transform.M13,
            transform.M21, transform.M22, transform.M23
        })
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Control points produce a non-finite affine transform.", nameof(controlPoints));
            }
        }

        return transform;
    }

    public static IReadOnlyList<WorldPoint> Coverage(MapRegistrationTransform transform, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Source-map pixel dimensions must be positive.");
        }

        return
        [
            transform.ToWorld(new WorldPoint(0, 0)),
            transform.ToWorld(new WorldPoint(pixelWidth, 0)),
            transform.ToWorld(new WorldPoint(pixelWidth, pixelHeight)),
            transform.ToWorld(new WorldPoint(0, pixelHeight))
        ];
    }

    private static (double A, double B, double C) SolveAxis(
        IReadOnlyList<WorldPoint> source,
        IReadOnlyList<double> target,
        double determinant)
    {
        var a = Determinant(
            new WorldPoint(target[0], source[0].Y),
            new WorldPoint(target[1], source[1].Y),
            new WorldPoint(target[2], source[2].Y)) / determinant;

        var b = Determinant(
            new WorldPoint(source[0].X, target[0]),
            new WorldPoint(source[1].X, target[1]),
            new WorldPoint(source[2].X, target[2])) / determinant;

        var c = (
            source[0].X * ((source[1].Y * target[2]) - (target[1] * source[2].Y))
            - source[0].Y * ((source[1].X * target[2]) - (target[1] * source[2].X))
            + target[0] * ((source[1].X * source[2].Y) - (source[1].Y * source[2].X))) / determinant;

        return (a, b, c);
    }

    private static double Determinant(WorldPoint first, WorldPoint second, WorldPoint third) =>
        (first.X * (second.Y - third.Y))
        - (first.Y * (second.X - third.X))
        + ((second.X * third.Y) - (second.Y * third.X));

    private static void EnsureDistinct(IReadOnlyList<WorldPoint> points, string label)
    {
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                if (Math.Abs(points[i].X - points[j].X) <= Epsilon
                    && Math.Abs(points[i].Y - points[j].Y) <= Epsilon)
                {
                    throw new ArgumentException($"Duplicate {label} control points are not valid for registration.");
                }
            }
        }
    }

    private static void ValidateFinite(WorldPoint point, string label)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException($"The {label} control point must contain finite coordinates.");
        }
    }
}
