using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public sealed record OverworldDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required HexGridDefinition Grid { get; init; }
    public IReadOnlyList<SpatialFeature> Features { get; init; } = [];
    public IReadOnlyList<Location> Locations { get; init; } = [];
    public IReadOnlyList<SourceMapRepresentation> SourceMaps { get; init; } = [];

    public IReadOnlyList<SpatialFeature> FeaturesIntersecting(HexCoordinate coordinate) =>
        Features.Where(feature => FeatureIntersection.IntersectsHex(Grid, coordinate, feature)).ToArray();
}
