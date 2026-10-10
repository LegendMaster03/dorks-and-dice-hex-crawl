using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public sealed record OverworldDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    // Grid is an explicit Phase 18 hex-runtime adapter, not generalized authority.
    // Existing callers receive an actionable rejection for a nonhex world rather
    // than a fabricated grid or a silent reinterpretation of coordinates.
    private HexGridDefinition? _legacyGrid;
    public HexGridDefinition Grid
    {
        get => _legacyGrid ?? throw new NotSupportedException(
            "This world has no hex grid. Generalized movement is gated until Phase 18.");
        init => _legacyGrid = value;
    }
    public bool HasLegacyHexGrid => _legacyGrid is not null;
    public PeriodicWorldTiling? Tiling { get; init; }

    public PeriodicWorldTiling SpatialTiling =>
        Tiling ?? LegacyHexTilingCompatibility.FromGrid(Grid);

    public void ValidateSpatialAuthority()
    {
        if (Tiling is null && _legacyGrid is null)
            throw new InvalidOperationException("An overworld requires spatial authority.");
        if (Tiling is not null)
        {
            Tiling.Validate();
            if (_legacyGrid is not null && !LegacyHexTilingCompatibility.Matches(Tiling, _legacyGrid))
                throw new InvalidOperationException(
                    "Legacy grid projection does not match authoritative periodic geometry.");
        }
        else _legacyGrid!.Validate();
    }

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
            EnvironmentAnnotationScopeKind.Hex when HasLegacyHexGrid =>
                a.Scope.Hex == LegacyHexTilingCompatibility.ToHex(address),
            EnvironmentAnnotationScopeKind.SpatialFeature => a.Scope.FeatureId is { } id && features.Contains(id),
            _ => false
        }).ToArray();
    }
    public IReadOnlyList<SpatialFeature> Features { get; init; } = [];
    public IReadOnlyList<Location> Locations { get; init; } = [];
    public IReadOnlyList<SourceMapRepresentation> SourceMaps { get; init; } = [];
    public IReadOnlyList<EnvironmentAnnotation> EnvironmentAnnotations { get; init; } = [];

    public IReadOnlyList<SpatialFeature> FeaturesIntersecting(HexCoordinate coordinate) =>
        Features.Where(feature => FeatureIntersection.IntersectsHex(Grid, coordinate, feature)).ToArray();

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
            if (annotation.Scope.Kind == EnvironmentAnnotationScopeKind.Hex && !HasLegacyHexGrid)
                throw new InvalidOperationException("Axial environment scope is not valid for a generalized world.");
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
