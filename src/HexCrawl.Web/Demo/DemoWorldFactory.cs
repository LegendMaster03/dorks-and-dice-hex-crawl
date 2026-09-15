using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Demo;

public static class DemoWorldFactory
{
    public static OverworldDefinition Create(HexOrientation orientation = HexOrientation.PointyTop, double scale = 12, DistanceUnit? unit = null)
    {
        var distanceUnit = unit ?? DistanceUnit.Miles;
        var grid = new HexGridDefinition
        {
            Id = Guid.Parse("7e74d7bf-9f42-49cb-bdf4-32fe771af201"),
            Orientation = orientation,
            Origin = new WorldPoint(0, 0),
            RotationDegrees = 0,
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(scale, distanceUnit)
        };

        SpatialFeature[] features =
        [
            new RegionFeature(
                Guid.Parse("455b16c2-08b9-4578-a5c5-b26142bbfb27"),
                "Northwood",
                "forest",
                [new(-4, -3), new(0, -5), new(4, -2), new(3, 2), new(-2, 3), new(-5, 0)]),
            new LinearFeature(
                Guid.Parse("40d16d48-137f-431b-9357-c945d70edfad"),
                "Old Trade Road",
                "road",
                [new(-8, 3), new(-3, 1), new(1, 0), new(5, -2), new(9, -1)]),
            new LinearFeature(
                Guid.Parse("70009179-4fa0-4e90-8ddd-23b4903cf982"),
                "Greywater",
                "river",
                [new(-6, -7), new(-4, -2), new(-1, 1), new(2, 4), new(7, 6)]),
            new PointFeature(
                Guid.Parse("0eb175a2-b30c-43bf-a379-f593de1841c2"),
                "Windscar Peak",
                "landmark",
                new WorldPoint(5.5, -5.5))
        ];

        Location[] locations =
        [
            new(
                Guid.Parse("5db28565-9379-4a14-9cd2-617af82120f2"),
                "Crossroads Keep",
                "settlement",
                new WorldPoint(1, 0),
                LocationDiscoverability.Obvious,
                []),
            new(
                Guid.Parse("0fbcc6dc-6934-4bd0-925d-bd2af493d57b"),
                "Hidden Tower",
                "tower",
                new WorldPoint(-2.1, -0.7),
                LocationDiscoverability.Hidden,
                [new LocationDetailMapReference(Guid.Parse("a7d395f4-2535-43b0-98db-668ee633fbf1"), "battle-map", "future:hidden-tower")])
        ];

        return new OverworldDefinition
        {
            Id = Guid.Parse("d95fa218-128d-42d7-ae9f-a2f02aaed74d"),
            Name = "Hex Crawl Geometry Demonstrator",
            Grid = grid,
            Features = features,
            Locations = locations,
            SourceMaps = []
        };
    }
}
