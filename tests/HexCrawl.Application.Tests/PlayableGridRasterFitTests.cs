using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class PlayableGridRasterFitTests
{
    [Theory]
    [InlineData(CrawlProcedureCatalog.WastelandWandererPresetKey, 5, -2)]
    [InlineData(CrawlProcedureCatalog.WastelandWarfarePresetKey, 9, -4)]
    public void CenteredWorldBoundsRetainTheWholeConfiguredSquarePreset(
        string presetKey, int count, int minimum)
    {
        var worldBounds = new PeriodicMapBounds(count, count, minimum, minimum);
        var procedure = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;
        var effective = PeriodicMapBounds.Intersection(worldBounds, procedure.PlayableBounds);
        Assert.Equal(worldBounds, effective);
        Assert.Equal(count * count, effective!.Value.Width * effective.Value.Height);
    }

    [Theory]
    [InlineData(5, -2)]
    [InlineData(9, -4)]
    public void SquareGridFitMapsFullRasterToEveryFiniteCell(int periods, int minimum)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "world-unit");
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!,
            new WorldPoint(0, 0))
        {
            PlayableBounds = new PeriodicMapBounds(periods, periods, minimum, minimum)
        };
        var map = PlayableGridRasterFit.Calculate(tiling, 1200, 1200);
        var first = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(minimum, minimum)));
        var corners = new[]
        {
            map.ToWorld(new WorldPoint(0, 0)),
            map.ToWorld(new WorldPoint(1200, 0)),
            map.ToWorld(new WorldPoint(0, 1200)),
            map.ToWorld(new WorldPoint(1200, 1200))
        };
        Assert.Contains(first.Polygon, point => Close(point, corners[0]));
        var u = tiling.Realization.TranslationU;
        var v = tiling.Realization.TranslationV;
        Assert.True(Close(corners[1],
            new WorldPoint(corners[0].X + periods * u.X, corners[0].Y + periods * u.Y)));
        Assert.True(Close(corners[2],
            new WorldPoint(corners[0].X + periods * v.X, corners[0].Y + periods * v.Y)));
        Assert.True(Close(corners[3],
            new WorldPoint(corners[1].X + periods * v.X, corners[1].Y + periods * v.Y)));
    }

    [Theory]
    [InlineData(5, 5, -2, -2, 1200, 900, 0)]
    [InlineData(9, 9, -4, -4, 1800, 1600, 19)]
    [InlineData(4, 7, 6, -8, 1250, 900, 23)]
    public void AllPrintedCellCornersAlignOnRotatedAndOffsetFiniteGrids(
        int width, int height, int minU, int minV,
        int imageWidth, int imageHeight, double rotation)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "world-unit", rotationDegrees: rotation);
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!,
            new WorldPoint(5, -3))
        {
            PlayableBounds = new PeriodicMapBounds(width, height, minU, minV)
        };
        var fit = PlayableGridRasterFit.Calculate(tiling, imageWidth, imageHeight);
        for (var u = 0; u < width; u++)
        for (var v = 0; v < height; v++)
        {
            var cell = tiling.Resolve(new PeriodicCellAddress(
                tiling.Topology.MotifCells[0].Id,
                new LatticeDisplacement(minU + u, minV + v)));
            var rasterCorner = fit.ToWorld(new WorldPoint(
                (double)u * imageWidth / width, (double)v * imageHeight / height));
            Assert.Contains(cell.Polygon, point => Close(point, rasterCorner));
        }
        Assert.True(double.IsFinite(fit.M11) && double.IsFinite(fit.M22));
    }

    [Fact]
    public void NonQuadrilateralGridDoesNotOfferFullBleedImageFit()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:6,3>", 1, "world-unit");
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!,
            new WorldPoint(0, 0))
        {
            PlayableBounds = new PeriodicMapBounds(5, 5, -2, -2)
        };
        Assert.Throws<InvalidOperationException>(() =>
            PlayableGridRasterFit.Calculate(tiling, 1024, 1024));
    }

    [Fact]
    public void UnboundedMapIsNotFittedToAnArbitraryImageSize()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "world-unit");
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!, new WorldPoint(0, 0));
        Assert.Throws<InvalidOperationException>(() =>
            PlayableGridRasterFit.Calculate(tiling, 1000, 1000));
    }

    private static bool Close(WorldPoint left, WorldPoint right) =>
        Math.Abs(left.X - right.X) < 1e-7 && Math.Abs(left.Y - right.Y) < 1e-7;
}
