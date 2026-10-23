using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

/// <summary>
/// A read-only, fail-closed live map view for an expedition. Only knowledge
/// already granted by the expedition plus explicit GM map release is projected.
/// </summary>
public static class ExpeditionPlayerMapProjection
{
    public static (OverworldDefinition World, IReadOnlyList<Guid> FeatureLabels,
        IReadOnlyList<Guid> LocationLabels, bool ShowGrid,
        IReadOnlyList<WorldCellId> KnownCells) Create(
            OverworldDefinition world, PlayerKnowledgeState knowledge)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(knowledge);
        if (knowledge.OverworldId != world.Id)
            throw new ArgumentException("Expedition knowledge does not belong to this world.");
        var policy = knowledge.PresentationPolicy ?? MapPresentationPolicy.DmControlled();
        policy.Validate();
        var original = world.PlayerMapDisclosure;
        original.Validate(world);

        var knownFeatures = knowledge.Entries.Values
            .Where(e => e.SubjectType == KnowledgeSubjectType.Feature
                && world.Features.Any(f => f.Id == e.SubjectId))
            .Select(e => e.SubjectId).Distinct().ToArray();
        var knownLocations = knowledge.Entries.Values
            .Where(e => e.SubjectType == KnowledgeSubjectType.Location
                && world.Locations.Any(l => l.Id == e.SubjectId))
            .Select(e => e.SubjectId).Distinct().ToArray();

        var visibleFeatures = original.VisibleFeatureIds.Concat(knownFeatures).Distinct().ToArray();
        var visibleLocations = original.VisibleLocationIds.Concat(knownLocations).Distinct().ToArray();
        var knownCells = (knowledge.KnownCells ?? [])
            .Where(cell => cell.TilingId == world.SpatialTiling.Id
                && world.SpatialTiling.IsPlayable(cell.Address))
            .Distinct().ToArray();
        var showAllTerrain = original.ShowTerrain || policy.TerrainMode == TerrainPresentationMode.AlwaysVisible;
        var showKnownTerrain = policy.TerrainMode == TerrainPresentationMode.HiddenUntilKnown;

        // The static disclosure remains an explicit GM override. Automatic
        // discovery adds only identities that the saved expedition knows.
        var effective = original with
        {
            VisibleFeatureIds = visibleFeatures,
            VisibleLocationIds = visibleLocations,
            VisibleFeatureLabelIds = original.VisibleFeatureLabelIds.Concat(knownFeatures).Distinct().ToArray(),
            VisibleLocationLabelIds = original.VisibleLocationLabelIds.Concat(knownLocations).Distinct().ToArray(),
            ShowTerrain = showAllTerrain || showKnownTerrain
        };
        var result = PlayerMapProjection.Create(world with { PlayerMapDisclosure = effective });
        var projected = result.World;
        if (!showAllTerrain)
        {
            var allowed = knownCells.ToHashSet();
            var visibleAnnotations = showKnownTerrain
                ? projected.EnvironmentAnnotations.Where(a => a.Scope.Cell is { } id && allowed.Contains(id)).ToArray()
                : [];
            var visibleCategories = visibleAnnotations.SelectMany(a => a.Facts)
                .Select(f => f.Tag).Where(t => !string.IsNullOrWhiteSpace(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            projected = projected with
            {
                EnvironmentAnnotations = visibleAnnotations,
                MapAppearance = projected.MapAppearance with
                {
                    TerrainColors = projected.MapAppearance.TerrainColors
                        .Where(pair => visibleCategories.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                }
            };
        }
        return (projected, result.FeatureLabelIds, result.LocationLabelIds,
            original.ShowGrid && policy.PlayerGrid == GridVisibility.Visible, knownCells);
    }
}
