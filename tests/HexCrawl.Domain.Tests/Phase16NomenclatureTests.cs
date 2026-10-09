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
    public void NotationIsSuppressedOutsideExplicitlyAuthorizedSurfaces()
    {
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.Normal, true, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, false, false));
        Assert.Null(TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, true));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.MissingSupportedNameError, true, false));
        Assert.Equal(Square, TilingNotationPresentationPolicy.Display(Square, TilingNotationSurface.JsonEditor, true, true));
    }
}
