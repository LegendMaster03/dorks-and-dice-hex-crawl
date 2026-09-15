using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Demo;

public sealed record DemoWorldResponse(
    Guid Id,
    string Name,
    DemoGridResponse Grid,
    IReadOnlyList<DemoFeatureResponse> Features,
    IReadOnlyList<DemoLocationResponse> Locations)
{
    public static DemoWorldResponse From(OverworldDefinition world) => new(
        world.Id,
        world.Name,
        DemoGridResponse.From(world.Grid),
        world.Features.Select(DemoFeatureResponse.From).ToArray(),
        world.Locations.Select(DemoLocationResponse.From).ToArray());
}

public sealed record DemoGridResponse(
    Guid Id,
    string Orientation,
    string CoordinateConvention,
    WorldPoint Origin,
    double RotationDegrees,
    double HexRadiusWorldUnits,
    DemoDistanceResponse NeighborCenterDistance)
{
    public static DemoGridResponse From(HexGridDefinition grid) => new(
        grid.Id,
        grid.Orientation.ToString(),
        grid.CoordinateConvention.ToString(),
        grid.Origin,
        grid.RotationDegrees,
        grid.HexRadiusWorldUnits,
        DemoDistanceResponse.From(grid.NeighborCenterDistance));
}

public sealed record DemoDistanceResponse(double Value, DemoDistanceUnitResponse Unit)
{
    public static DemoDistanceResponse From(DistanceMeasure distance) => new(
        distance.Value,
        new DemoDistanceUnitResponse(distance.Unit.Kind.ToString(), distance.Unit.Symbol, distance.Unit.MetersPerUnit));
}

public sealed record DemoDistanceUnitResponse(string Kind, string Symbol, double? MetersPerUnit);

public sealed record DemoFeatureResponse(
    Guid Id,
    string Name,
    string Category,
    string Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary)
{
    public static DemoFeatureResponse From(SpatialFeature feature) => feature switch
    {
        PointFeature point => new(point.Id, point.Name, point.Category, "Point", point.Position, null, null),
        LinearFeature line => new(line.Id, line.Name, line.Category, "Line", null, line.Path, null),
        RegionFeature region => new(region.Id, region.Name, region.Category, "Region", null, null, region.Boundary),
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };
}

public sealed record DemoLocationResponse(
    Guid Id,
    string Name,
    string Category,
    WorldPoint Position,
    string Discoverability)
{
    public static DemoLocationResponse From(Location location) => new(
        location.Id,
        location.Name,
        location.Category,
        location.Position,
        location.Discoverability.ToString());
}
