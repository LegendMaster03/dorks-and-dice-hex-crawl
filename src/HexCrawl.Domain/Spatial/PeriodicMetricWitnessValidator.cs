using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Independent, bounded metric validation for an already-proved primitive
/// topological torus. The D-symbol alone does not prove this embedding.
/// This validator does not mutate a saved world, infer a tiling from an
/// image, or accept unsupported constraints by default.
/// </summary>
public static class PeriodicMetricWitnessValidator
{
    private const int MaximumCells = 24;
    private const int MaximumCornersPerCell = 64;

    private static TilingWorldPoint Add(TilingWorldPoint a, TilingWorldPoint b) =>
        new(a.X + b.X, a.Y + b.Y);
    private static TilingWorldPoint Sub(TilingWorldPoint a, TilingWorldPoint b) =>
        new(a.X - b.X, a.Y - b.Y);
    private static TilingWorldPoint Scale(TilingWorldPoint v, double x) =>
        new(v.X * x, v.Y * x);
    private static double Cross(TilingWorldPoint a, TilingWorldPoint b) =>
        a.X * b.Y - a.Y * b.X;
    private static double Dot(TilingWorldPoint a, TilingWorldPoint b) =>
        a.X * b.X + a.Y * b.Y;
    private static double Length(TilingWorldPoint v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
    private static double Distance(TilingWorldPoint a, TilingWorldPoint b) =>
        Length(Sub(a, b));
    private static bool Finite(TilingWorldPoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y)
        && Math.Abs(p.X) < 1e12 && Math.Abs(p.Y) < 1e12;
    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private static TilingWorldPoint Translation(PeriodicMetricRealization realization, LatticeDisplacement s) =>
        Add(Scale(realization.TranslationU, s.U), Scale(realization.TranslationV, s.V));

    private static double SignedArea(IReadOnlyList<TilingWorldPoint> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
            sum += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        return sum / 2;
    }

    private static bool OnSegment(TilingWorldPoint p, TilingWorldPoint a,
        TilingWorldPoint b, double tolerance)
    {
        var ab = Sub(b, a);
        var ap = Sub(p, a);
        return Math.Abs(Cross(ab, ap)) <= tolerance * Math.Max(1, Length(ab))
            && Dot(ap, ab) >= -tolerance
            && Dot(ap, ab) <= Dot(ab, ab) + tolerance;
    }

    private static bool ProperIntersection(TilingWorldPoint a, TilingWorldPoint b,
        TilingWorldPoint c, TilingWorldPoint d, double tolerance)
    {
        var ab = Sub(b, a); var cd = Sub(d, c);
        double x = Cross(ab, Sub(c, a)), y = Cross(ab, Sub(d, a));
        double z = Cross(cd, Sub(a, c)), w = Cross(cd, Sub(b, c));
        double threshold = tolerance * Math.Max(1, Length(ab) * Length(cd));
        return x > threshold && y < -threshold && z > threshold && w < -threshold
            || x < -threshold && y > threshold && z < -threshold && w > threshold
            || x > threshold && y < -threshold && z < -threshold && w > threshold
            || x < -threshold && y > threshold && z > threshold && w < -threshold;
    }

    private static bool SelfIntersection(IReadOnlyList<TilingWorldPoint> polygon, double tolerance)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            for (int j = i + 1; j < polygon.Count; j++)
            {
                if (j == i + 1 || i == 0 && j == polygon.Count - 1) continue;
                var c = polygon[j]; var d = polygon[(j + 1) % polygon.Count];
                if (ProperIntersection(a, b, c, d, tolerance)
                    || OnSegment(a, c, d, tolerance)
                    || OnSegment(b, c, d, tolerance)
                    || OnSegment(c, a, b, tolerance)
                    || OnSegment(d, a, b, tolerance))
                    return true;
            }
        }
        return false;
    }

    private static bool Inside(TilingWorldPoint p, IReadOnlyList<TilingWorldPoint> polygon,
        double tolerance)
    {
        bool winding = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if (OnSegment(p, a, b, tolerance)) return false;
            if ((a.Y > p.Y) != (b.Y > p.Y)
                && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                winding = !winding;
        }
        return winding;
    }

    private static bool InteriorsOverlap(IReadOnlyList<TilingWorldPoint> left,
        IReadOnlyList<TilingWorldPoint> right, double tolerance)
    {
        // Reject widely separated polygon pairs before edge comparisons.
        double aMinX = left.Min(p => p.X), aMaxX = left.Max(p => p.X);
        double aMinY = left.Min(p => p.Y), aMaxY = left.Max(p => p.Y);
        if (aMinX >= right.Max(p => p.X) - tolerance
            || aMaxX <= right.Min(p => p.X) + tolerance
            || aMinY >= right.Max(p => p.Y) - tolerance
            || aMaxY <= right.Min(p => p.Y) + tolerance)
            return false;
        for (int i = 0; i < left.Count; i++)
            for (int j = 0; j < right.Count; j++)
                if (ProperIntersection(left[i], left[(i + 1) % left.Count],
                    right[j], right[(j + 1) % right.Count], tolerance))
                    return true;

        // Both polygon families are CCW. Sampling just inside an edge catches
        // complete coincidence and containment without misclassifying shared
        // boundary segments as overlapping interiors.
        foreach (var (sample, other) in new[]
        {
            (sample: left, other: right),
            (sample: right, other: left)
        })
        {
            for (int i = 0; i < sample.Count; i++)
            {
                var a = sample[i]; var b = sample[(i + 1) % sample.Count];
                var edge = Sub(b, a);
                double magnitude = Length(edge);
                double inset = Math.Min(tolerance * 12, magnitude / 1000);
                var point = Add(Scale(Add(a, b), 0.5),
                    Scale(new TilingWorldPoint(-edge.Y, edge.X), inset / magnitude));
                if (Inside(point, other, tolerance)) return true;
            }
        }
        return false;
    }

    private static void ValidateConstraints(
        IReadOnlyList<TilingMetricConstraint> constraints,
        PeriodicMetricRealization realization,
        IReadOnlyDictionary<string, IReadOnlyList<TilingWorldPoint>> polygons,
        double tolerance)
    {
        foreach (var constraint in constraints)
        {
            Require(!string.IsNullOrWhiteSpace(constraint.Key)
                && !string.IsNullOrWhiteSpace(constraint.Unit)
                && double.IsFinite(constraint.Min) && double.IsFinite(constraint.Max)
                && constraint.Min <= constraint.Max, "Invalid metric constraint interval.");
            var values = constraint.Key switch
            {
                "period-u-length" => new[] { Length(realization.TranslationU) },
                "period-v-length" => new[] { Length(realization.TranslationV) },
                "period-angle-degrees" => new[] {
                    Math.Acos(Math.Clamp(Dot(realization.TranslationU, realization.TranslationV)
                      / (Length(realization.TranslationU) * Length(realization.TranslationV)),
                      -1, 1)) * 180 / Math.PI
                },
                "edge-length" => polygons.Values.SelectMany(p =>
                    Enumerable.Range(0, p.Count).Select(i =>
                        Distance(p[i], p[(i + 1) % p.Count]))).ToArray(),
                "tile-area" => polygons.Values.Select(p => Math.Abs(SignedArea(p))).ToArray(),
                _ when constraint.Key.StartsWith("cell-area:", StringComparison.Ordinal)
                    && polygons.TryGetValue(constraint.Key["cell-area:".Length..], out var target)
                    => new[] { Math.Abs(SignedArea(target)) },
                _ => throw new InvalidOperationException("Unsupported metric constraint key; cannot certify its satisfaction.")
            };
            Require(constraint.Unit == (constraint.Key == "period-angle-degrees"
                    ? "degrees" : realization.Units),
                "A metric constraint's unit does not match its measurement.");
            foreach (double value in values)
                Require(value >= constraint.Min - tolerance && value <= constraint.Max + tolerance,
                    "The supplied embedding violates a requested metric constraint.");
        }
    }

    /// <summary>
    /// Validate all polygonal cells, metric constraints, actual shared edges
    /// and a whole periodic fundamental domain. Returns true only if proved.
    /// Throws an explanatory InvalidOperationException otherwise.
    /// </summary>
    public static bool Validate(PeriodicTopologyWitness topology, PeriodicMetricRealization geometry)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(geometry);
        topology.ValidateAdjacency();
        Require(!string.IsNullOrWhiteSpace(geometry.Units), "Metric units are required.");
        Require(Finite(geometry.TranslationU) && Finite(geometry.TranslationV),
            "Metric translation basis must have finite bounded coordinates.");
        double det = Cross(geometry.TranslationU, geometry.TranslationV);
        double periodLength = Math.Max(Length(geometry.TranslationU), Length(geometry.TranslationV));
        double tolerance = Math.Max(1e-9, 1e-7 * periodLength);
        Require(det > tolerance * tolerance, "Translation basis must be nondegenerate and positively oriented.");
        Require(topology.MotifCells.Count is >= 1 and <= MaximumCells,
            "Metric validator supports at most 24 motif cells.");
        var byId = topology.MotifCells.ToDictionary(cell => cell.Id, StringComparer.Ordinal);
        var polygons = geometry.Polygons;
        Require(polygons is not null && polygons.Count == byId.Count
            && byId.Keys.All(polygons.ContainsKey),
            "Geometric polygon identities must exactly match the topology motif.");
        double areaSum = 0;
        foreach (var (id, cell) in byId)
        {
            var polygon = polygons[id];
            Require(polygon is not null && polygon.Count == cell.Boundary.Count
                && polygon.Count >= 3 && polygon.Count <= MaximumCornersPerCell,
                "Polygon corners must match the atomic topological boundary.");
            Require(polygon.All(Finite), "Polygon points must be finite and bounded.");
            for (int i = 0; i < polygon.Count; i++)
                Require(Distance(polygon[i], polygon[(i + 1) % polygon.Count]) > tolerance,
                    "A polygon contains a zero-length edge.");
            double area = SignedArea(polygon);
            Require(area > tolerance * tolerance, "Polygon must be simple, nondegenerate and counterclockwise.");
            Require(!SelfIntersection(polygon, tolerance), "Polygon has intersecting or repeated edges.");
            areaSum += area;
        }
        Require(Math.Abs(areaSum - det) <= Math.Max(1e-8, 1e-7 * Math.Abs(det)),
            "The polygon motif does not cover exactly one lattice fundamental-domain area.");

        foreach (var (id, cell) in byId)
        {
            var poly = polygons[id];
            for (int i = 0; i < cell.Boundary.Count; i++)
            {
                var edge = cell.Boundary[i];
                var other = polygons[edge.TargetMotifCellId];
                int peer = edge.ReciprocalInterfaceIndex;
                var offset = Translation(geometry, edge.TargetTranslation);
                Require(Distance(poly[i], Add(other[(peer + 1) % other.Count], offset)) <= tolerance
                    && Distance(poly[(i + 1) % poly.Count], Add(other[peer], offset)) <= tolerance,
                    "The embedding fails translated reciprocal boundary equality.");
            }
        }

        foreach (var (id, polygon) in polygons)
        {
            foreach (var (otherId, otherPolygon) in polygons)
            {
                for (int u = -2; u <= 2; u++)
                    for (int v = -2; v <= 2; v++)
                    {
                        if (id == otherId && u == 0 && v == 0) continue;
                        var delta = Translation(geometry, new LatticeDisplacement(u, v));
                        var translated = otherPolygon.Select(p => Add(p, delta)).ToArray();
                        Require(!InteriorsOverlap(polygon, translated, tolerance),
                            "Polygon interiors overlap in the periodic cover.");
                    }
            }
        }
        ValidateConstraints(geometry.Constraints, geometry, polygons, tolerance);
        return true;
    }
}
