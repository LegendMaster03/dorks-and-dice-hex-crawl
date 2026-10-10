namespace HexCrawl.Domain.Spatial;

public static class FeatureIntersection
{
    public static bool IntersectsHex(HexGridDefinition grid, HexCoordinate coordinate, SpatialFeature feature)
    {
        var hex = HexGeometry.Corners(grid, coordinate);
        return feature switch
        {
            PointFeature point => PointInPolygon(point.Position, hex),
            LinearFeature line => PolylineIntersectsPolygon(line.Path, hex),
            RegionFeature region => PolygonsIntersect(region.Boundary, hex),
            _ => false
        };
    }


    /// <summary>The shortest Euclidean distance to a polygon (zero on or inside it).</summary>
    public static double DistanceToPolygon(WorldPoint point, IReadOnlyList<WorldPoint> polygon)
    {
        if (polygon.Count < 3)
            throw new ArgumentException("A polygon must have at least three vertices.", nameof(polygon));
        if (PointInPolygon(point, polygon)) return 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            double x = b.X - a.X, y = b.Y - a.Y;
            double squared = x * x + y * y;
            if (squared == 0) continue;
            double t = Math.Clamp(((point.X - a.X) * x + (point.Y - a.Y) * y) / squared, 0, 1);
            double dx = point.X - a.X - t * x, dy = point.Y - a.Y - t * y;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    public static bool IntersectsPolygon(IReadOnlyList<WorldPoint> polygon, SpatialFeature feature)
    {
        if (polygon.Count < 3)
            throw new ArgumentException("Cell polygon has fewer than three corners.", nameof(polygon));
        return feature switch
        {
            PointFeature point => PointInPolygon(point.Position, polygon),
            LinearFeature line => PolylineIntersectsPolygon(line.Path, polygon),
            RegionFeature region => PolygonsIntersect(region.Boundary, polygon),
            _ => false
        };
    }

    private static bool PolylineIntersectsPolygon(IReadOnlyList<WorldPoint> path, IReadOnlyList<WorldPoint> polygon)
    {
        if (path.Count == 0)
        {
            return false;
        }

        if (path.Any(point => PointInPolygon(point, polygon)))
        {
            return true;
        }

        for (var i = 1; i < path.Count; i++)
        {
            if (SegmentIntersectsPolygon(path[i - 1], path[i], polygon))
            {
                return true;
            }
        }

        return false;
    }

    public static bool PolygonsIntersect(IReadOnlyList<WorldPoint> left, IReadOnlyList<WorldPoint> right)
    {
        if (left.Count < 3 || right.Count < 3)
        {
            return false;
        }

        if (left.Any(point => PointInPolygon(point, right)) || right.Any(point => PointInPolygon(point, left)))
        {
            return true;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a1 = left[i];
            var a2 = left[(i + 1) % left.Count];
            for (var j = 0; j < right.Count; j++)
            {
                var b1 = right[j];
                var b2 = right[(j + 1) % right.Count];
                if (SegmentsIntersect(a1, a2, b1, b2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentIntersectsPolygon(WorldPoint start, WorldPoint end, IReadOnlyList<WorldPoint> polygon)
    {
        for (var i = 0; i < polygon.Count; i++)
        {
            if (SegmentsIntersect(start, end, polygon[i], polygon[(i + 1) % polygon.Count]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool PointInPolygon(WorldPoint point, IReadOnlyList<WorldPoint> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        for (var i = 0; i < polygon.Count; i++)
        {
            if (PointOnSegment(polygon[i], polygon[(i + 1) % polygon.Count], point))
            {
                return true;
            }
        }

        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var pi = polygon[i];
            var pj = polygon[j];
            var crosses = ((pi.Y > point.Y) != (pj.Y > point.Y))
                && point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool PointOnSegment(WorldPoint start, WorldPoint end, WorldPoint point)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length == 0) return false;
        // The cross product is an area, not a distance. Use a tolerance
        // in world-coordinate length units scaled by the actual edge length.
        // The previous fixed 1e-9 area tolerance made interior points of
        // valid microunit-scale cells appear to lie on every nearby edge.
        double tolerance = Math.Max(1e-13, length * 1e-10);
        double cross = dx * (point.Y - start.Y) - dy * (point.X - start.X);
        return Math.Abs(cross) <= tolerance * length
            && point.X >= Math.Min(start.X, end.X) - tolerance
            && point.X <= Math.Max(start.X, end.X) + tolerance
            && point.Y >= Math.Min(start.Y, end.Y) - tolerance
            && point.Y <= Math.Max(start.Y, end.Y) + tolerance;
    }

    private static bool SegmentsIntersect(WorldPoint a, WorldPoint b, WorldPoint c, WorldPoint d)
    {
        static double Cross(WorldPoint p1, WorldPoint p2, WorldPoint p3) =>
            ((p2.X - p1.X) * (p3.Y - p1.Y)) - ((p2.Y - p1.Y) * (p3.X - p1.X));

        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);

        if (((abC > 0 && abD < 0) || (abC < 0 && abD > 0))
            && ((cdA > 0 && cdB < 0) || (cdA < 0 && cdB > 0)))
        {
            return true;
        }

        // PointOnSegment already performs scale-aware collinearity testing;
        // a fixed absolute area threshold must not be applied here.
        return PointOnSegment(a, b, c)
            || PointOnSegment(a, b, d)
            || PointOnSegment(c, d, a)
            || PointOnSegment(c, d, b);
    }
}
