using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Spatial;
using Xunit;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16NomenclatureTests
{
    private const string Square = "<1:1,1,1:4,4>";
    private static PeriodicMetricRealization Realization(bool skew) => new(
        "mile", new(1, 0), skew ? new(.4, 1) : new(0, 1),
        new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
        {
            ["cell"] = [new(0, 0), new(1, 0), new(skew ? 1.4 : 1, 1), new(skew ? .4 : 0, 1)]
        }, []);

    [Fact]
    public void AQuadrilateralIsNotAutomaticallyASquare()
    {
        var catalog = new TilingNomenclatureCatalog([
            new(Square, "Square tiling", null, ["square grid"], "Repeated squares", "Test conformance",
                Geometry: new(4, EqualSideLengths: true, InteriorAngleDegrees: 90))
        ]);
        Assert.Equal("Square tiling", catalog.Describe(Square, Realization(false))!.Label);
        Assert.False(catalog.Describe(Square, Realization(true))!.IsNamed);
        Assert.False(catalog.Describe(Square)!.IsNamed);
        Assert.Equal("a repeating pattern of four-sided cells", catalog.Describe(Square, Realization(true))!.Label);
    }

    [Fact]
    public void RulesetSelectorVisibilityDoesNotAffectNomenclatureResolution()
    {
        var catalog = new TilingNomenclatureCatalog([
            new(Square, "Visible square", null, [], "Visible choice", "Test"),
            new(Square, "Hidden square", null, [], "Hidden choice", "Test",
                ShowInRulesetSelector: false)
        ]);
        Assert.Equal(2, catalog.ListEntries().Count);
        Assert.Single(catalog.ListEntries(rulesetSelectorOnly: true));
        Assert.Equal("Visible square", catalog.ListEntries(true)[0].PreferredName);
        Assert.True(catalog.ListEntries(true)[0].ShowInRulesetSelector);
        Assert.False(catalog.Describe(Square)!.IsNamed);
    }

    [Fact]
    public void MixedTriangleQuadrilateralStaysNamedButOutsideRulesetChoices()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            value => value.PreferredName == "Triangle and quadrilateral tiling");
        Assert.False(entry.ShowInRulesetSelector);
        Assert.DoesNotContain(CrawlTilingNames.Catalog.ListEntries(rulesetSelectorOnly: true),
            value => value.CanonicalDsSymbol == entry.CanonicalDsSymbol);
        Assert.Contains(CrawlTilingNames.Catalog.ListEntries(),
            value => value.CanonicalDsSymbol == entry.CanonicalDsSymbol);
        Assert.Equal(entry.PreferredName,
            CrawlTilingNames.Catalog.Describe(entry.CanonicalDsSymbol)!.Label);
    }

    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    public void RegularNamedTilesHaveVerifiedPreviewRealizations(string symbol)
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            choice => choice.CanonicalDsSymbol == symbol);
        var preview = CrawlTilingNames.RepresentativePreview(entry);
        Assert.NotNull(preview);
        var resolved = CrawlTilingNames.Catalog.Describe(symbol, preview);
        Assert.True(resolved?.IsNamed);
        Assert.Equal(entry.PreferredName, resolved!.Label);
    }

    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    public void NamedRegularTilingDefaultsAreExtractedFromVerifiedGeometry(string symbol)
    {
        var period = CrawlTilingNames.VerifiedDefaultPeriod(symbol);
        Assert.NotNull(period);
        var result = DelaneyDressHarmonicMetricRealization.Construct(
            symbol, 2, "world-unit", constraints:
            [
                new("period-u-length", "world-unit", 2 * period.Value.U, 2 * period.Value.U),
                new("period-v-length", "world-unit", 2 * period.Value.V, 2 * period.Value.V),
                new("period-angle-degrees", "degrees",
                    period.Value.AngleDegrees, period.Value.AngleDegrees)
            ]);
        Assert.Equal("realized", result.Status);
        Assert.Equal(CrawlTilingNames.Catalog.Describe(symbol, result.Realization)!.Label,
            CrawlTilingNames.Catalog.ListEntries().Single(x => x.CanonicalDsSymbol == symbol).PreferredName);
    }

    [Fact]
    public void EveryCatalogPreviewHasVerifiedGeometryIncludingMixedCells()
    {
        foreach (var entry in CrawlTilingNames.Catalog.ListEntries())
        {
            var preview = CrawlTilingNames.RepresentativePreview(entry);
            Assert.NotNull(preview);
            Assert.Equal(entry.PreferredName, CrawlTilingNames.Catalog.Describe(entry.CanonicalDsSymbol, preview)!.Label);
            Assert.True(Math.Abs(preview.TranslationU.X * preview.TranslationV.Y
                - preview.TranslationU.Y * preview.TranslationV.X) > 1e-8);
            Assert.All(preview.Polygons.Values, polygon =>
            {
                Assert.True(polygon.Count >= 3);
                Assert.All(polygon, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
            });
            if (entry.PreferredName == "Triangle and quadrilateral tiling")
                Assert.Equal(new[] { 3, 4 }, preview.Polygons.Values.Select(p => p.Count).Distinct().Order());
        }
    }

    [Fact]
    public void ConvexPentagonalType1UsesExistingPeriodicTopologyAndMetricValidation()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            item => item.PresetId == "convex-pentagon-type-1");
        Assert.Equal(new[] { "Pentagons", "Convex" }, entry.CategoryPath);
        var topology = Assert.IsType<PeriodicTopologyWitness>(entry.RepresentativeTopology);
        var metric = Assert.IsType<PeriodicMetricRealization>(entry.RepresentativeRealization);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), topology, metric, new WorldPoint(0, 0));
        world.Validate();

        Assert.Equal(2, topology.MotifCells.Count);
        var first = metric.Polygons["pentagon-a"];
        var second = metric.Polygons["pentagon-b"];
        Assert.Equal(5, first.Count);
        Assert.Equal(5, second.Count);
        // Both cells are the same convex pentagon under a 180° rotation.
        for (var i = 0; i < 5; i++)
        {
            Assert.InRange(Math.Abs(first[i].X + second[i].X), 0, 1e-9);
            Assert.InRange(Math.Abs(first[i].Y + second[i].Y), 0, 1e-9);
        }

        var address = new PeriodicCellAddress("pentagon-a", new(0, 0));
        Assert.Equal(5, world.Neighbors(address).Count);
        foreach (var boundary in world.Boundaries(address))
        {
            var reverse = world.Reciprocal(boundary);
            Assert.Equal(boundary.From, reverse.To);
            Assert.Equal(boundary.To, reverse.From);
        }

        Assert.Equal(entry.PreferredName,
            CrawlTilingNames.Catalog.Describe(entry.CanonicalDsSymbol, metric)!.Label);
    }

    [Fact]
    public void Type15TwelveCellMotifPreservesSourcePentagonAnglesAndGenericInterfaces()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            e => e.PresetId == "convex-pentagon-type-15");
        var topology = Assert.IsType<PeriodicTopologyWitness>(entry.RepresentativeTopology);
        var geometry = Assert.IsType<PeriodicMetricRealization>(entry.RepresentativeRealization);
        Assert.Equal(12, topology.MotifCells.Count);
        Assert.Equal(64, topology.MotifCells.Sum(cell => cell.Boundary.Count));
        Assert.Equal(4, topology.MotifCells.Count(cell => cell.Boundary.Count == 6));
        Assert.All(topology.MotifCells, cell =>
            Assert.Equal(5, cell.Boundary.Select(e => e.BoundarySideIndex).Distinct().Count()));

        var world = new PeriodicWorldTiling(Guid.NewGuid(), topology, geometry, new WorldPoint(0, 0));
        world.Validate();
        static double Length(TilingWorldPoint a, TilingWorldPoint b) =>
            Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        foreach (var poly in geometry.Polygons.Values)
        {
            var corners = Enumerable.Range(0, poly.Count).Where(i =>
            {
                var a = poly[(i + poly.Count - 1) % poly.Count];
                var b = poly[i];
                var d = poly[(i + 1) % poly.Count];
                return Math.Abs((b.X - a.X) * (d.Y - b.Y)
                    - (b.Y - a.Y) * (d.X - b.X)) > 1e-8;
            }).Select(i => poly[i]).ToArray();
            Assert.Equal(5, corners.Length);
            var angles = new double[5];
            var sides = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var a = corners[(i + 4) % 5];
                var b = corners[i];
                var d = corners[(i + 1) % 5];
                double ax = a.X - b.X, ay = a.Y - b.Y;
                double dx = d.X - b.X, dy = d.Y - b.Y;
                angles[i] = Math.Acos(Math.Clamp(
                    (ax * dx + ay * dy) / (Length(a,b) * Length(b,d)), -1, 1)) * 180 / Math.PI;
                sides[i] = Length(b, d);
            }
            var expectedAngles = new[] { 60.0, 90.0, 105.0, 135.0, 150.0 };
            var expectedSides = new[] { .5, .5, .5, Math.Cos(Math.PI / 12), 1.0 };
            Assert.All(angles.Order(), (angle) =>
                Assert.Contains(expectedAngles, expected => Math.Abs(expected - angle) < 1e-7));
            Assert.All(expectedAngles.Zip(angles.Order()), pair =>
                Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 1e-7));
            Assert.All(expectedSides.Zip(sides.Order()), pair =>
                Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 1e-7));
        }
        foreach (var cell in topology.MotifCells)
        {
            var boundaries = world.Boundaries(new(cell.Id, new(0, 0)));
            Assert.Equal(cell.Boundary.Count, boundaries.Count);
            foreach (var edge in boundaries)
                Assert.Equal(edge.From, world.Reciprocal(edge).To);
        }
        Assert.Equal(entry.PreferredName,
            CrawlTilingNames.Catalog.Describe(entry.CanonicalDsSymbol, geometry)!.Label);
    }

    [Fact]
    public void EquilateralCairoType2DistinguishesMetricRealizationsOfTheSameTopology()
    {
        var entries = CrawlTilingNames.Catalog.ListEntries();
        var type2 = Assert.Single(entries, e => e.PresetId == "convex-pentagon-type-2");
        var type4 = Assert.Single(entries, e => e.PresetId == "convex-pentagon-type-4");
        Assert.Equal(type4.CanonicalDsSymbol, type2.CanonicalDsSymbol);
        var topology = Assert.IsType<PeriodicTopologyWitness>(type2.RepresentativeTopology);
        var geometry = Assert.IsType<PeriodicMetricRealization>(type2.RepresentativeRealization);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), topology, geometry, new WorldPoint(0, 0));
        world.Validate();

        foreach (var polygon in geometry.Polygons.Values)
        {
            Assert.Equal(5, polygon.Count);
            var sides = Enumerable.Range(0, 5).Select(i =>
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % 5];
                return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            }).ToArray();
            Assert.True(sides.Max() - sides.Min() < 1e-9,
                "The Type 2 example must be an equilateral convex pentagon.");
            var angles = Enumerable.Range(0, 5).Select(i =>
            {
                var a = polygon[(i + 4) % 5];
                var b = polygon[i];
                var d = polygon[(i + 1) % 5];
                var ux = a.X - b.X; var uy = a.Y - b.Y;
                var vx = d.X - b.X; var vy = d.Y - b.Y;
                return Math.Acos(Math.Clamp((ux * vx + uy * vy)
                    / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy)), -1, 1))
                    * 180 / Math.PI;
            }).ToArray();
            Assert.Equal(2, angles.Count(angle => Math.Abs(angle - 90) < 1e-8));
            Assert.Contains(Enumerable.Range(0, 5), i =>
                Math.Abs(angles[i] - 90) < 1e-8 && Math.Abs(angles[(i + 2) % 5] - 90) < 1e-8);
        }

        Assert.Equal(type2.PreferredName,
            CrawlTilingNames.Catalog.Describe(type2.CanonicalDsSymbol, geometry)!.Label);
        Assert.Equal(type4.PreferredName,
            CrawlTilingNames.Catalog.Describe(type4.CanonicalDsSymbol, type4.RepresentativeRealization)!.Label);
        Assert.NotEqual(type2.PreferredName, type4.PreferredName);
    }

    [Fact]
    public void CairoType4IsCongruentConvexAndPlayableThroughGenericPeriodicGeometry()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            item => item.PresetId == "convex-pentagon-type-4");
        var topology = Assert.IsType<PeriodicTopologyWitness>(entry.RepresentativeTopology);
        var realization = Assert.IsType<PeriodicMetricRealization>(entry.RepresentativeRealization);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), topology, realization, new WorldPoint(0, 0));
        world.Validate();
        Assert.Equal(new[] { "Pentagons", "Convex" }, entry.CategoryPath);
        Assert.Equal(8, topology.MotifCells.Count);
        Assert.All(topology.MotifCells, cell => Assert.Equal(5, cell.Boundary.Count));

        var exemplar = realization.Polygons["H00"];
        static double Length(TilingWorldPoint left, TilingWorldPoint right) =>
            Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));
        static (double[] Sides, double[] Angles) Signature(IReadOnlyList<TilingWorldPoint> points)
        {
            var sides = new double[5];
            var angles = new double[5];
            for (var i = 0; i < 5; i++)
            {
                var previous = points[(i + 4) % 5];
                var current = points[i];
                var next = points[(i + 1) % 5];
                sides[i] = Length(current, next);
                var dx1 = previous.X - current.X;
                var dy1 = previous.Y - current.Y;
                var dx2 = next.X - current.X;
                var dy2 = next.Y - current.Y;
                var cos = (dx1 * dx2 + dy1 * dy2)
                    / (Length(current, previous) * Length(current, next));
                angles[i] = Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI;
            }
            return (sides, angles);
        }
        var reference = Signature(exemplar);
        foreach (var polygon in realization.Polygons.Values)
        {
            Assert.Equal(5, polygon.Count);
            var current = Signature(polygon);
            Assert.Equal(2, current.Angles.Count(value => Math.Abs(value - 90) < 1e-7));
            Assert.Equal(3, current.Angles.Count(value => Math.Abs(value - 120) < 1e-7));
            Assert.Equal(4, current.Sides.Count(value =>
                Math.Abs(value - 1 / Math.Sqrt(3)) < 1e-7));
            Assert.Single(current.Sides, value =>
                Math.Abs(value - (1 - 1 / Math.Sqrt(3))) < 1e-7);
            Assert.True(Enumerable.Range(0, 5).Any(offset =>
                Enumerable.Range(0, 5).All(i =>
                    Math.Abs(current.Sides[i] - reference.Sides[(i + offset) % 5]) < 1e-7
                    && Math.Abs(current.Angles[i] - reference.Angles[(i + offset) % 5]) < 1e-7))
                || Enumerable.Range(0, 5).Any(offset =>
                    Enumerable.Range(0, 5).All(i =>
                        Math.Abs(current.Sides[i] - reference.Sides[(offset - i + 4) % 5]) < 1e-7
                        && Math.Abs(current.Angles[i] - reference.Angles[(offset - i) % 5]) < 1e-7)),
                "Every pentagon in the motif must be a rotated or mirrored congruent copy.");
        }
        Assert.Equal(5, world.Neighbors(new("H00", new(0, 0))).Count);
        Assert.Equal(5, world.Boundaries(new("V10", new(0, 0))).Count);
    }

    [Fact]
    public void Type3PentagonsKeepFiveGeometricSidesAndSixAtomicInterfaces()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            item => item.PresetId == "convex-pentagon-type-3");
        var topology = Assert.IsType<PeriodicTopologyWitness>(entry.RepresentativeTopology);
        var realization = Assert.IsType<PeriodicMetricRealization>(entry.RepresentativeRealization);
        var world = new PeriodicWorldTiling(Guid.NewGuid(), topology, realization, new WorldPoint(0, 0));
        world.Validate();
        Assert.Equal(3, topology.MotifCells.Count);
        Assert.All(topology.MotifCells, motif =>
        {
            Assert.Equal(6, motif.Boundary.Count);
            Assert.Equal(5, motif.Boundary.Select(edge => edge.BoundarySideIndex).Distinct().Count());
            Assert.Equal(new[] { 0, 1, 2, 2, 3, 4 },
                motif.Boundary.Select(edge => edge.BoundarySideIndex));
        });
        // The long side equals the sum of its two half-edges; the
        // topological split is not an extra geometric corner.
        var points = realization.Polygons["A"];
        Assert.Equal(6, points.Count);
        double Dist(TilingWorldPoint a, TilingWorldPoint b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        Assert.InRange(Math.Abs(Dist(points[2], points[4]) -
            Dist(points[2], points[3]) - Dist(points[3], points[4])), 0, 1e-10);
        Assert.Equal(6, world.Boundaries(new("A", new(0, 0))).Count);
        Assert.Equal(entry.PreferredName,
            CrawlTilingNames.Catalog.Describe(entry.CanonicalDsSymbol, realization)!.Label);
    }

    [Fact]
    public void CategoryPathsAndCatalogIdentitiesDoNotRestrictGeometry()
    {
        var square = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            value => value.PreferredName == "Square tiling");
        Assert.Equal(new[] { "Squares" }, square.CategoryPath);
        Assert.Equal("regular-square", square.PresetId);
        Assert.Equal(Square, square.CanonicalDsSymbol);
        Assert.NotNull(CrawlTilingNames.RepresentativePreview(square));

        // A D-symbol can have more than one presentation entry or category.
        var catalog = new TilingNomenclatureCatalog([
            new(Square, "Square choice A", null, [], "First", "Test",
                CategoryPath: ["Quadrilaterals", "Squares"], PresetId: "square-a"),
            new(Square, "Square choice B", null, [], "Second", "Test",
                CategoryPath: ["Legacy", "Square grids"], PresetId: "square-b")
        ]);
        Assert.Equal(2, catalog.ListEntries().Count);
        Assert.All(catalog.ListEntries(), entry => Assert.Equal(Square, entry.CanonicalDsSymbol));
    }

    [Fact]
    public void CatalogRejectsMalformedCategoriesAndDuplicatePresetIds()
    {
        Assert.Throws<ArgumentException>(() => new TilingNomenclatureCatalog([
            new(Square, "Invalid", null, [], "Description", "Test",
                CategoryPath: ["Squares", " "])
        ]));
        Assert.Throws<ArgumentException>(() => new TilingNomenclatureCatalog([
            new(Square, "First", null, [], "Description", "Test",
                CategoryPath: ["Squares"], PresetId: "duplicate"),
            new(Square, "Second", null, [], "Description", "Test",
                CategoryPath: ["Quadrilaterals"], PresetId: "duplicate")
        ]));
    }

    [Fact]
    public void NotationIsSuppressedOutsideExplicitlyAuthorizedSurfaces()
    {
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.Normal, true, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, false, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, true));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, false));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.JsonEditor, true, true));
    }
}
