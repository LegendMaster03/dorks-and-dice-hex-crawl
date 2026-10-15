using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class PlayableGridRasterFitTests
{
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
