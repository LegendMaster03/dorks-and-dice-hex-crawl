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
    public void NotationIsSuppressedOutsideExplicitlyAuthorizedSurfaces()
    {
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.Normal, true, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, false, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, true));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, false));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.JsonEditor, true, true));
    }
}
