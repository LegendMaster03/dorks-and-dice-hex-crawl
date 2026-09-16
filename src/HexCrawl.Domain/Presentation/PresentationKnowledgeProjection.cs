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

        foreach (var feature in world.Features.Where(feature => policy.IsInitiallyKnownFeatureCategory(feature.Category)))
        {
            result = KnowledgeDiscovery.Discover(
                result,
                feature.Id,
                KnowledgeSubjectType.Feature,
                $"presentation:{policy.Key}:initial");
        }

        foreach (var location in world.Locations.Where(location =>
                     location.Discoverability == LocationDiscoverability.Obvious
                     && policy.IsInitiallyKnownLocationCategory(location.Category)))
        {
            result = KnowledgeDiscovery.Discover(
                result,
                location.Id,
                KnowledgeSubjectType.Location,
                $"presentation:{policy.Key}:initial");
        }

        return result;
    }

    public static PlayerKnowledgeState ApplyEnteredHexes(
        MapPresentationPolicy policy,
        PlayerKnowledgeState knowledge,
        IEnumerable<HexCoordinate> enteredHexes)
    {
        policy.Validate();
        if (policy.AutomationMode == PresentationAutomationMode.DmControlled)
        {
            return RemoveAutomaticRuntimeDiscoveries(knowledge);
        }
        if (!policy.MarkEnteredHexKnown)
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

    private static PlayerKnowledgeState RemoveAutomaticRuntimeDiscoveries(PlayerKnowledgeState knowledge)
    {
        var retained = knowledge.Entries
            .Where(pair => pair.Value.Source is null
                || !pair.Value.Source.StartsWith("runtime:", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return retained.Count == knowledge.Entries.Count
            ? knowledge
            : knowledge with { Entries = retained };
    }
}
