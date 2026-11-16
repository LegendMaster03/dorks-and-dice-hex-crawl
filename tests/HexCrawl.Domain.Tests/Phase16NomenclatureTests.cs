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
