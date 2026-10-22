using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record FeatureContract(
    Guid Id,
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary)
{
    public static FeatureContract From(SpatialFeature feature) => feature switch
    {
        PointFeature point => new(point.Id, point.Name, point.Category, point.Kind, point.Position, null, null),
        LinearFeature line => new(line.Id, line.Name, line.Category, line.Kind, null, line.Path, null),
        RegionFeature region => new(region.Id, region.Name, region.Category, region.Kind, null, null, region.Boundary),
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };
}

public sealed record LocationContract(
    Guid Id,
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability)
{
    public static LocationContract From(Location location) => new(
        location.Id,
        location.Name,
        location.Category,
        location.Position,
        location.Discoverability);
}

public sealed record SourceMapContract(
    Guid Id,
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary)
{
    public static SourceMapContract From(SourceMapRepresentation sourceMap) => new(
        sourceMap.Id,
        sourceMap.GeographyKey,
        sourceMap.Name,
        sourceMap.Role,
        sourceMap.AssetKey,
        sourceMap.ContainsBakedGrid,
        sourceMap.Alignment,
        sourceMap.WorldCoverageBoundary);
}

public sealed record OverworldContract(
    Guid Id,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FeatureContract> Features,
    IReadOnlyList<LocationContract> Locations,
    IReadOnlyList<SourceMapContract> SourceMaps,
    PeriodicWorldTiling Tiling,
    MapAppearance MapAppearance,
    PlayerMapDisclosure PlayerMapDisclosure)
{
    public static OverworldContract From(StoredOverworld world) => new(
        world.World.Id,
        world.World.Name,
        world.Version,
        world.CreatedAt,
        world.UpdatedAt,
        world.World.Features.Select(FeatureContract.From).ToArray(),
        world.World.Locations.Select(LocationContract.From).ToArray(),
        world.World.SourceMaps.Select(SourceMapContract.From).ToArray(),
        world.World.SpatialTiling,
        world.World.MapAppearance,
        world.World.PlayerMapDisclosure);
}

public sealed record LocationMutationRequest(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion)
{
    public CreateLocationCommand ToCreateCommand() => new(Name, Category, Position, Discoverability, ExpectedVersion);
    public UpdateLocationCommand ToUpdateCommand() => new(Name, Category, Position, Discoverability, ExpectedVersion);
}

public sealed record FeatureMutationRequest(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion)
{
    public CreateFeatureCommand ToCreateCommand() => new(Name, Category, Kind, Position, Path, Boundary, ExpectedVersion);
    public UpdateFeatureCommand ToUpdateCommand() => new(Name, Category, Kind, Position, Path, Boundary, ExpectedVersion);
}

