using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Presentation;

public static class PresentationKnowledgeProjection
{
    public static PlayerKnowledgeState Initialize(
        OverworldDefinition world,
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge,
        HexCoordinate startHex)
    {
        policy.Validate();
        var result = knowledge;
        if (policy.AutomationMode == PresentationAutomationMode.DmControlled)
        {
            return result;
        }

        if (policy.MarkEnteredHexKnown)
        {
            result = KnowledgeDiscovery.KnowHex(result, startHex);
        }

        return InitializeVisibleSubjects(world, policy, result);
    }

    /// <summary>
    /// Initialize a generalized player's knowledge at the actual qualified
    /// starting cell. Reuse the same initial visible feature/location policy
    /// as legacy hex starts without projecting any axial coordinate.
    /// </summary>
    public static PlayerKnowledgeState InitializeCells(
        OverworldDefinition world,
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge,
        WorldCellId startCell)
    {
        policy.Validate();
        if (startCell.TilingId != world.SpatialTiling.Id)
            throw new InvalidOperationException("Starting cell belongs to a different tiling.");
        world.SpatialTiling.Resolve(startCell.Address);

        if (policy.AutomationMode == PresentationAutomationMode.DmControlled)
            return knowledge;

        var result = policy.MarkEnteredHexKnown
            ? KnowledgeDiscovery.KnowCell(knowledge, startCell)
            : knowledge;
        return InitializeVisibleSubjects(world, policy, result);
    }

    private static PlayerKnowledgeState InitializeVisibleSubjects(
        OverworldDefinition world,
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge)
    {
        var result = knowledge;
        foreach (var feature in world.Features.Where(feature => policy.IsInitiallyKnownFeatureCategory(feature.Category)))
            result = KnowledgeDiscovery.Discover(
                result, feature.Id, KnowledgeSubjectType.Feature,
                $"presentation:{policy.Key}:initial");

        foreach (var location in world.Locations.Where(location =>
                     location.Discoverability == LocationDiscoverability.Obvious
                     && policy.IsInitiallyKnownLocationCategory(location.Category)))
            result = KnowledgeDiscovery.Discover(
                result, location.Id, KnowledgeSubjectType.Location,
                $"presentation:{policy.Key}:initial");
        return result;
    }

    /// <summary>
    /// Preserve the existing presentation policy's discovery-on-entry semantics
    /// without fabricating axial coordinates. A generalized world records only
    /// actual qualified cells; DM-controlled presentation stays manual.
    /// </summary>
    public static PlayerKnowledgeState ApplyEnteredCells(
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge,
        IEnumerable<WorldCellId> enteredCells)
    {
        policy.Validate();
        if (policy.AutomationMode == PresentationAutomationMode.DmControlled
            || !policy.MarkEnteredHexKnown)
            return knowledge;

        var result = knowledge;
        foreach (var cell in enteredCells.Distinct())
            result = KnowledgeDiscovery.KnowCell(result, cell);
        return result;
    }

    public static PlayerKnowledgeState ApplyEnteredHexes(
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge,
        IEnumerable<HexCoordinate> enteredHexes)
    {
        policy.Validate();
        if (policy.AutomationMode == PresentationAutomationMode.DmControlled || !policy.MarkEnteredHexKnown)
        {
            return knowledge;
        }

        var result = knowledge;
        foreach (var hex in enteredHexes.Distinct())
        {
            result = KnowledgeDiscovery.KnowHex(result, hex);
        }
        return result;
    }
}
