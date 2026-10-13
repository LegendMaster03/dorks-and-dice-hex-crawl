using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record CreateLocationCommand(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion);

public sealed record UpdateLocationCommand(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion);

public sealed record CreateFeatureCommand(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion);

public sealed record UpdateFeatureCommand(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion);

public sealed record ImportedLocationDefinition(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability);

public sealed record ImportedFeatureDefinition(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary);

public sealed record ImportWorldObjectsCommand(
    IReadOnlyList<ImportedLocationDefinition> Locations,
    IReadOnlyList<ImportedFeatureDefinition> Features,
    long ExpectedVersion);

