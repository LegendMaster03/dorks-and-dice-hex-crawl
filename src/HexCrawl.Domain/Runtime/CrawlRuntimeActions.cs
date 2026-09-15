using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Runtime;

public sealed record ManualDiscoveryResult(
    ExpeditionState Expedition,
    PlayerKnowledgeState Knowledge,
    CrawlRuntimeEvent Event);

public static class CrawlRuntimeActions
{
    public static ManualDiscoveryResult Discover(
        OverworldDefinition world,
        ExpeditionState expedition,
        PlayerKnowledgeState knowledge,
        Guid subjectId,
        KnowledgeSubjectType subjectType,
        string source)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(knowledge);

        var (exists, name, eventKind) = subjectType switch
        {
            KnowledgeSubjectType.Location => (
                world.Locations.Any(item => item.Id == subjectId),
                world.Locations.FirstOrDefault(item => item.Id == subjectId)?.Name,
                CrawlRuntimeEventKind.LocationDiscovered),
            KnowledgeSubjectType.Feature => (
                world.Features.Any(item => item.Id == subjectId),
                world.Features.FirstOrDefault(item => item.Id == subjectId)?.Name,
                CrawlRuntimeEventKind.FeatureDiscovered),
            _ => throw new InvalidOperationException("The demonstrator discovery action currently supports locations and features only.")
        };

        if (!exists)
        {
            throw new InvalidOperationException($"The {subjectType.ToString().ToLowerInvariant()} does not exist in this overworld.");
        }

        var updatedKnowledge = KnowledgeDiscovery.Discover(
            knowledge,
            subjectId,
            subjectType,
            source);
        var sequence = expedition.History.Count == 0 ? 1 : expedition.History[^1].Sequence + 1;
        var watchNumber = expedition.ActiveWatch?.WatchNumber ?? expedition.CompletedWatches;
        var runtimeEvent = new CrawlRuntimeEvent(
            sequence,
            watchNumber,
            eventKind,
            expedition.ElapsedTravelTime,
            expedition.CurrentHex,
            $"Discovered {subjectType.ToString().ToLowerInvariant()} {name}; unrelated contents remain unchanged.",
            SubjectId: subjectId,
            SubjectType: subjectType);
        var updatedExpedition = expedition with
        {
            History = expedition.History.Concat([runtimeEvent]).ToArray()
        };

        return new ManualDiscoveryResult(updatedExpedition, updatedKnowledge, runtimeEvent);
    }
}
