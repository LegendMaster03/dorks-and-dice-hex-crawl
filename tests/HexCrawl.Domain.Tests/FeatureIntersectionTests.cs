using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class FeatureIntersectionTests
{
    private readonly HexGridDefinition _grid = new()
    {
        Id = Guid.Parse("7e74d7bf-9f42-49cb-bdf4-32fe771af201"),
        Orientation = HexOrientation.PointyTop,
        Origin = new WorldPoint(0, 0),
        HexRadiusWorldUnits = 1,
        NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
    };

    [Fact]
    public void OverworldDerivesIntersectingFeaturesInsteadOfOwningThemOnHex()
    {
        var crossingRoad = new LinearFeature(Guid.NewGuid(), "Road", "road", [new(-2, 0), new(2, 0)]);
        var distantPoint = new PointFeature(Guid.NewGuid(), "Tower", "tower", new WorldPoint(9, 9));
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Grid = _grid,
            Features = [crossingRoad, distantPoint]
        };

        var features = world.FeaturesIntersecting(new HexCoordinate(0, 0));
        Assert.Contains(crossingRoad, features);
        Assert.DoesNotContain(distantPoint, features);
    }

    [Fact]
    public void PointOnHexBoundaryCountsAsIntersection()
    {
        var corner = HexGeometry.Corners(_grid, new HexCoordinate(0, 0))[0];
        var landmark = new PointFeature(Guid.NewGuid(), "Boundary marker", "landmark", corner);
        Assert.True(FeatureIntersection.IntersectsHex(_grid, new HexCoordinate(0, 0), landmark));
    }

    [Fact]
    public void RegionCanIntersectHexAcrossBoundary()
    {
        var region = new RegionFeature(Guid.NewGuid(), "Forest", "forest", [new(-2, -.25), new(2, -.25), new(2, .25), new(-2, .25)]);
        Assert.True(FeatureIntersection.IntersectsHex(_grid, new HexCoordinate(0, 0), region));
    }
}
