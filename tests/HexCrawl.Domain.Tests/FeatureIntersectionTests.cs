using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class FeatureIntersectionTests
{
    private static readonly WorldPoint[] Polygon =
    [
        new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)
    ];

    [Fact]
    public void OverworldDerivesIntersectingFeaturesFromItsPeriodicCells()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "unit");
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), realized.Topology!,
            realized.Realization!, new WorldPoint(0, 0));
        var address = new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0));
        var center = tiling.Resolve(address).Center;
        var crossingRoad = new LinearFeature(Guid.NewGuid(), "Road", "road",
            [new(center.X - 2, center.Y), new(center.X + 2, center.Y)]);
        var distantPoint = new PointFeature(Guid.NewGuid(), "Tower", "tower",
            new WorldPoint(center.X + 100, center.Y + 100));
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Tiling = tiling,
            Features = [crossingRoad, distantPoint]
        };

        var features = world.FeaturesIntersecting(address);
        Assert.Contains(crossingRoad, features);
        Assert.DoesNotContain(distantPoint, features);
    }

    [Fact]
    public void PointOnPolygonBoundaryCountsAsIntersection()
    {
        var landmark = new PointFeature(Guid.NewGuid(), "Boundary marker", "landmark", Polygon[0]);
        Assert.True(FeatureIntersection.IntersectsPolygon(Polygon, landmark));
    }

    [Fact]
    public void RegionCanIntersectCellAcrossBoundary()
    {
        var region = new RegionFeature(Guid.NewGuid(), "Forest", "forest",
            [new(-2, -.25), new(2, -.25), new(2, .25), new(-2, .25)]);
        Assert.True(FeatureIntersection.IntersectsPolygon(Polygon, region));
    }
}
