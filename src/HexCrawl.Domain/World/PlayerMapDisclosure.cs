namespace HexCrawl.Domain.World;

/// <summary>
/// Explicit GM release policy for a static map export. Nothing is shared by
/// default. This is independent of expedition knowledge and discoverability.
/// </summary>
public sealed record PlayerMapDisclosure
{
    public IReadOnlyList<Guid> VisibleFeatureIds { get; init; } = [];
    public IReadOnlyList<Guid> VisibleLocationIds { get; init; } = [];
    public IReadOnlyList<Guid> VisibleFeatureLabelIds { get; init; } = [];
    public IReadOnlyList<Guid> VisibleLocationLabelIds { get; init; } = [];
    public IReadOnlyList<Guid> VisibleSourceMapIds { get; init; } = [];
    public bool ShowTerrain { get; init; }
    public bool ShowGrid { get; init; } = true;

    public void Validate(OverworldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ValidateIds(VisibleFeatureIds, world.Features.Select(x => x.Id), "features");
        ValidateIds(VisibleLocationIds, world.Locations.Select(x => x.Id), "locations");
        ValidateIds(VisibleFeatureLabelIds, VisibleFeatureIds, "visible feature labels");
        ValidateIds(VisibleLocationLabelIds, VisibleLocationIds, "visible location labels");
        ValidateIds(VisibleSourceMapIds,
            world.SourceMaps.Where(x => x.Role == SourceMapRole.Player).Select(x => x.Id),
            "player-labeled source maps");
    }

    private static void ValidateIds(IReadOnlyList<Guid> selected, IEnumerable<Guid> allowed, string label)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Count > 20000 || selected.Contains(Guid.Empty)
            || selected.Distinct().Count() != selected.Count
            || selected.Except(allowed).Any())
            throw new ArgumentException($"Invalid {label} selection for player map export.");
    }
}

/// <summary>
/// A data-filtering boundary, not a CSS/canvas visibility toggle. The returned
/// world has no unreleased object names, geometry, annotations, or source assets.
/// </summary>
public static class PlayerMapProjection
{
    public static (OverworldDefinition World,
        IReadOnlyList<Guid> FeatureLabelIds, IReadOnlyList<Guid> LocationLabelIds) Create(
            OverworldDefinition source)
    {
        var release = source.PlayerMapDisclosure;
        release.Validate(source);
        // Geometry can be released without revealing a subject's name or category.
        // Redact them server-side rather than relying on drawLabel=false.
        var features = source.Features.Where(f => release.VisibleFeatureIds.Contains(f.Id))
            .Select(f => release.VisibleFeatureLabelIds.Contains(f.Id)
                ? f : f with { Name = "Unlabeled", Category = "unlabeled" }).ToArray();
        var locations = source.Locations.Where(l => release.VisibleLocationIds.Contains(l.Id))
            .Select(l => release.VisibleLocationLabelIds.Contains(l.Id)
                ? l : l with { Name = "Unlabeled", Category = "unlabeled" }).ToArray();
        var maps = source.SourceMaps.Where(m =>
            m.Role == SourceMapRole.Player && release.VisibleSourceMapIds.Contains(m.Id)).ToArray();
        // Only terrain tags are needed for color rendering. Notes/provenance and
        // other DM environment rules must never enter the projected result.
        EnvironmentAnnotation[] terrain = release.ShowTerrain
            ? source.EnvironmentAnnotations.Where(a => a.Scope.Kind == EnvironmentAnnotationScopeKind.Cell)
                .Select(a => a with { Facts = a.Facts
                    .Where(f => f.ValueKind == EnvironmentValueKind.Tag
                        && f.Dimension.Equals(EnvironmentDimensions.Terrain, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f with { Note = null, Provenance = null }).ToArray() })
                .Where(a => a.Facts.Count > 0).ToArray()
            : [];
        var visibleTerrains = terrain.SelectMany(annotation => annotation.Facts)
            .Select(fact => fact.Tag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var featureCategories = features.Select(f => f.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labeledCategories = features.Where(f => release.VisibleFeatureLabelIds.Contains(f.Id))
            .Select(f => f.Category).Concat(
                locations.Where(l => release.VisibleLocationLabelIds.Contains(l.Id))
                    .Select(l => l.Category)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appearance = source.MapAppearance;
        var filtered = new MapAppearance
        {
            TerrainColors = release.ShowTerrain
                ? appearance.TerrainColors.Where(x => visibleTerrains.Contains(x.Key))
                    .ToDictionary(x => x.Key, x => x.Value)
                : new Dictionary<string, string>(),
            FeatureColors = appearance.FeatureColors
                .Where(x => featureCategories.Contains(x.Key))
                .ToDictionary(x => x.Key, x => x.Value),
            LabelColors = appearance.LabelColors
                .Where(x => labeledCategories.Contains(x.Key))
                .ToDictionary(x => x.Key, x => x.Value)
        };
        return (source with
        {
            Features = features,
            Locations = locations,
            SourceMaps = maps,
            EnvironmentAnnotations = terrain,
            MapAppearance = filtered,
            PlayerMapDisclosure = new()
        }, release.VisibleFeatureLabelIds.ToArray(), release.VisibleLocationLabelIds.ToArray());
    }
}
