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

    private static bool PolygonsIntersect(IReadOnlyList<WorldPoint> left, IReadOnlyList<WorldPoint> right)
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

    private static bool PointInPolygon(WorldPoint point, IReadOnlyList<WorldPoint> polygon)
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
        const double epsilon = 1e-9;
        var cross = ((end.X - start.X) * (point.Y - start.Y)) - ((end.Y - start.Y) * (point.X - start.X));
        return Math.Abs(cross) < epsilon
            && point.X >= Math.Min(start.X, end.X) - epsilon
            && point.X <= Math.Max(start.X, end.X) + epsilon
            && point.Y >= Math.Min(start.Y, end.Y) - epsilon
            && point.Y <= Math.Max(start.Y, end.Y) + epsilon;
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

        const double epsilon = 1e-9;
        return (Math.Abs(abC) < epsilon && PointOnSegment(a, b, c))
            || (Math.Abs(abD) < epsilon && PointOnSegment(a, b, d))
            || (Math.Abs(cdA) < epsilon && PointOnSegment(c, d, a))
            || (Math.Abs(cdB) < epsilon && PointOnSegment(c, d, b));
    }
}
