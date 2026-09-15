using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class HexGeometryTests
{
    public static TheoryData<HexOrientation> Orientations => new()
    {
        HexOrientation.PointyTop,
        HexOrientation.FlatTop
    };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void ProjectionRoundTripsStableCoordinates(HexOrientation orientation)
    {
        var grid = Grid(orientation, rotation: 23);
        HexCoordinate[] coordinates = [new(0, 0), new(4, -3), new(-5, 2), new(7, 6)];

        foreach (var coordinate in coordinates)
        {
            Assert.Equal(coordinate, HexGeometry.WorldToHex(grid, HexGeometry.HexToWorld(grid, coordinate)));
        }
    }

    [Fact]
    public void NeighborAndDistanceUseAxialInvariants()
    {
        var origin = new HexCoordinate(0, 0);
        Assert.Equal(6, origin.Neighbors().Distinct().Count());
        Assert.All(origin.Neighbors(), neighbor => Assert.Equal(1, origin.DistanceTo(neighbor)));
        Assert.Equal(3, origin.DistanceTo(new HexCoordinate(3, -2)));
    }

    [Fact]
    public void DistanceMeasureConvertsKnownPhysicalUnits()
    {
        var miles = new DistanceMeasure(1, DistanceUnit.Miles);
        var kilometers = miles.ConvertTo(DistanceUnit.Kilometers);
        Assert.Equal(1.609344, kilometers.Value, 6);
    }

    private static HexGridDefinition Grid(HexOrientation orientation, double rotation = 0) => new()
    {
        Id = Guid.NewGuid(),
        Orientation = orientation,
        Origin = new WorldPoint(4.5, -8.25),
        RotationDegrees = rotation,
        HexRadiusWorldUnits = 1,
        NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
    };
}
