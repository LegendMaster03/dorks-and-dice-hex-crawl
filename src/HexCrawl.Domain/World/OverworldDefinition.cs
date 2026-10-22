using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public sealed record OverworldDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public PeriodicWorldTiling? Tiling { get; init; }

    public PeriodicWorldTiling SpatialTiling =>
        Tiling ?? throw new InvalidOperationException(
            "An overworld requires authoritative periodic geometry.");

    public void ValidateSpatialAuthority() => SpatialTiling.Validate();

    public IReadOnlyList<SpatialFeature> FeaturesIntersecting(PeriodicCellAddress address)
    {
        var polygon = SpatialTiling.Resolve(address).Polygon;
        return Features.Where(feature => FeatureIntersection.IntersectsPolygon(polygon, feature)).ToArray();
    }

    public IReadOnlyList<EnvironmentAnnotation> AnnotationsForCell(PeriodicCellAddress address)
    {
        var identity = new WorldCellId(SpatialTiling.Id, address);
        var features = FeaturesIntersecting(address).Select(f => f.Id).ToHashSet();
        return EnvironmentAnnotations.Where(a => a.Scope.Kind switch
        {
            EnvironmentAnnotationScopeKind.World => true,
            EnvironmentAnnotationScopeKind.Cell => a.Scope.Cell == identity,
            EnvironmentAnnotationScopeKind.SpatialFeature => a.Scope.FeatureId is { } id && features.Contains(id),
            _ => false
        }).ToArray();
    }
    public IReadOnlyList<SpatialFeature> Features { get; init; } = [];
    public IReadOnlyList<Location> Locations { get; init; } = [];
    public IReadOnlyList<SourceMapRepresentation> SourceMaps { get; init; } = [];
    public IReadOnlyList<EnvironmentAnnotation> EnvironmentAnnotations { get; init; } = [];
    public MapAppearance MapAppearance { get; init; } = new();

    public void ValidateEnvironmentAnnotations()
    {
        ValidateSpatialAuthority();
        var featureIds = Features.Select(value => value.Id).ToHashSet();
        var annotationIds = new HashSet<Guid>();
        var factIds = new HashSet<Guid>();
        foreach (var annotation in EnvironmentAnnotations)
        {
            annotation.Validate(featureIds);
            if (annotation.Scope.Kind == EnvironmentAnnotationScopeKind.Cell)
            {
                var cell = annotation.Scope.Cell!.Value;
                if (cell.TilingId != SpatialTiling.Id)
                    throw new InvalidOperationException("Environment annotation addresses another world tiling.");
                SpatialTiling.Resolve(cell.Address);
            }
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
