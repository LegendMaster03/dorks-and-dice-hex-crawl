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
    public IReadOnlyList<EnvironmentAnnotation> EnvironmentAnnotations { get; init; } = [];

    public IReadOnlyList<SpatialFeature> FeaturesIntersecting(HexCoordinate coordinate) =>
        Features.Where(feature => FeatureIntersection.IntersectsHex(Grid, coordinate, feature)).ToArray();

    public void ValidateEnvironmentAnnotations()
    {
        var featureIds = Features.Select(value => value.Id).ToHashSet();
        var annotationIds = new HashSet<Guid>();
        var factIds = new HashSet<Guid>();
        foreach (var annotation in EnvironmentAnnotations)
        {
            annotation.Validate(featureIds);
            if (!annotationIds.Add(annotation.Id))
            {
                throw new InvalidOperationException("Environment annotation ids must be unique within an overworld.");
            }
            foreach (var fact in annotation.Facts)
            {
                if (!factIds.Add(fact.Id))
                {
                    throw new InvalidOperationException("Static world environment fact ids must be unique within an overworld.");
                }
            }
        }
    }
}
