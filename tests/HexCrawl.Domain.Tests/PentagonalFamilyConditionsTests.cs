using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Spatial;
using Xunit;

namespace HexCrawl.Domain.Tests;

/// <summary>
/// Independent classification check: tiling incidence, reciprocal boundaries,
/// and congruence are not sufficient to prove membership in a named family.
/// These checks use each family's angle and side-length equations directly.
/// The labels A-E and a-e may start at any vertex, and their direction may be
/// reversed. Some mathematical diagrams label a side before its vertex,
/// rather than after it; both conventions are checked explicitly.
/// </summary>
public sealed class PentagonalFamilyConditionsTests
{
    [Fact]
    public void CatalogIncludesOneRealizedRepresentativeOfEveryConvexPentagonFamily()
    {
        var entries = CrawlTilingNames.Catalog.ListEntries()
            .Where(entry => entry.PresetId?.StartsWith("convex-pentagon-type-", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(15, entries.Length);
        Assert.Equal(Enumerable.Range(1, 15).Select(type => $"convex-pentagon-type-{type}"),
            entries.Select(entry => entry.PresetId).OrderBy(id =>
                int.Parse(id!["convex-pentagon-type-".Length..])));
        Assert.All(entries, entry =>
        {
            Assert.NotNull(entry.RepresentativeTopology);
            Assert.NotNull(entry.RepresentativeRealization);
            Assert.Equal(new[] { "Pentagons", "Convex" }, entry.CategoryPath);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void RepresentativePrototileSatisfiesItsNamedFamilyConditions(int type)
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            item => item.PresetId == $"convex-pentagon-type-{type}");
        var witness = Assert.IsType<PeriodicMetricRealization>(entry.RepresentativeRealization);
        Assert.NotEmpty(witness.Polygons);

        // Inspect *every* motif cell, not only a preferred representative.
        foreach (var polygon in witness.Polygons.Values)
        {
            var corners = GeometricCorners(polygon);
            Assert.Equal(5, corners.Count);
            Assert.True(MatchesAnyLabeling(corners, type),
                $"The {type} representative has no cyclic labeling that satisfies its defining family equations.");
        }
    }

    private static IReadOnlyList<TilingWorldPoint> GeometricCorners(
        IReadOnlyList<TilingWorldPoint> polygon)
    {
        var result = new List<TilingWorldPoint>();
        for (int i = 0; i < polygon.Count; i++)
        {
            var before = polygon[(i + polygon.Count - 1) % polygon.Count];
            var current = polygon[i];
            var after = polygon[(i + 1) % polygon.Count];
            var ux = current.X - before.X;
            var uy = current.Y - before.Y;
            var vx = after.X - current.X;
            var vy = after.Y - current.Y;
            if (Math.Abs(ux * vy - uy * vx) > 1e-8) result.Add(current);
        }
        return result;
    }

    private static bool MatchesAnyLabeling(IReadOnlyList<TilingWorldPoint> vertices, int type)
    {
        for (int start = 0; start < 5; start++)
        for (int direction = -1; direction <= 1; direction += 2)
        {
            var points = Enumerable.Range(0, 5)
                .Select(i => vertices[(start + direction * i + 25) % 5]).ToArray();
            double[] angles = new double[5];
            double[] sides = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var previous = points[(i + 4) % 5];
                var point = points[i];
                var next = points[(i + 1) % 5];
                var leftX = previous.X - point.X;
                var leftY = previous.Y - point.Y;
                var rightX = next.X - point.X;
                var rightY = next.Y - point.Y;
                double before = Math.Sqrt(leftX * leftX + leftY * leftY);
                double after = Math.Sqrt(rightX * rightX + rightY * rightY);
                if (before <= 0 || after <= 0) return false;
                angles[i] = Math.Acos(Math.Clamp(
                    (leftX * rightX + leftY * rightY) / (before * after), -1, 1))
                    * 180 / Math.PI;
                sides[i] = after;
            }
            if (Satisfies(type, angles, sides)) return true;
            // Some source diagrams associate each side with its ending vertex.
            var incoming = Enumerable.Range(0, 5)
                .Select(i => sides[(i + 4) % 5]).ToArray();
            if (Satisfies(type, angles, incoming)) return true;
        }
        return false;
    }

    private static bool Satisfies(int type, double[] A, double[] a)
    {
        // Conventions: A[0] = angle A; a[0] = side a.
        // A-E and a-e are always cyclic; matching their chosen orientation is
        // performed by MatchesAnyLabeling, never by reordering independently.
        static bool Equal(double x, double y) => Math.Abs(x - y) < 0.004;
        return type switch
        {
            1 => Equal(A[3] + A[4], 180),
            2 => Equal(A[2] + A[4], 180) && Equal(a[0], a[3]),
            3 => Equal(A[0], 120) && Equal(A[2], 120) && Equal(A[3], 120)
                && Equal(a[0], a[1]) && Equal(a[3], a[2] + a[4]),
            4 => Equal(A[0], 90) && Equal(A[2], 90)
                && Equal(a[0], a[1]) && Equal(a[2], a[3]),
            // Type 5: A=120, C=60 in Sugimoto/Ogawa; older published
            // diagrams contain a conflicting Type 5 transcription.
            5 => Equal(A[0], 120) && Equal(A[2], 60)
                && Equal(a[0], a[1]) && Equal(a[2], a[3]),
            6 => Equal(A[2] + A[4], 180) && Equal(A[0], 2 * A[2])
                && Equal(a[0], a[1]) && Equal(a[0], a[4]) && Equal(a[2], a[3]),
            7 => Equal(2 * A[1] + A[2], 360) && Equal(2 * A[3] + A[0], 360)
                && Equal(a[0], a[1]) && Equal(a[0], a[2]) && Equal(a[0], a[3]),
            8 => Equal(2 * A[0] + A[1], 360) && Equal(2 * A[3] + A[2], 360)
                && Equal(a[0], a[1]) && Equal(a[0], a[2]) && Equal(a[0], a[3]),
            9 => Equal(2 * A[4] + A[1], 360) && Equal(2 * A[3] + A[2], 360)
                && Equal(a[0], a[1]) && Equal(a[0], a[2]) && Equal(a[0], a[3]),
            10 => Equal(A[4], 90) && Equal(A[0] + A[3], 180)
                && Equal(2 * A[1] - A[3], 180) && Equal(2 * A[2] + A[3], 360)
                && Equal(a[0], a[4]) && Equal(a[0], a[1] + a[3]),
            11 => Equal(A[0], 90) && Equal(A[2] + A[4], 180)
                && Equal(2 * A[1] + A[2], 360) && Equal(a[3], a[4])
                && Equal(a[3], 2 * a[0] + a[2]),
            12 => Equal(A[0], 90) && Equal(A[2] + A[4], 180)
                && Equal(2 * A[1] + A[2], 360) && Equal(2 * a[0], a[2] + a[4])
                && Equal(2 * a[0], a[3]),
            13 => Equal(A[0], 90) && Equal(A[2], 90)
                && Equal(2 * A[1], 360 - A[3]) && Equal(2 * A[4], 360 - A[3])
                && Equal(a[2], a[3]) && Equal(2 * a[2], a[4]),
            14 => Equal(A[3], 90) && Equal(2 * A[4] + A[0], 360)
                && Equal(A[0] + A[2], 180) && Equal(a[1], a[2])
                && Equal(a[1], 2 * a[0]) && Equal(a[1], 2 * a[3]),
            15 => Equal(A[0], 60) && Equal(A[1], 135) && Equal(A[2], 105)
                && Equal(A[3], 90) && Equal(A[4], 150) && Equal(a[0], 2 * a[1])
                && Equal(a[0], 2 * a[3]) && Equal(a[0], 2 * a[4]),
            _ => false
        };
    }
}
