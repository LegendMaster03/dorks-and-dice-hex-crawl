using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Runtime;

public sealed record ManualDiscoveryResult(
    ExpeditionState Expedition,
    PlayerKnowledgeState Knowledge,
    CrawlRuntimeEvent Event);

public sealed record CellManualDiscoveryResult(
    CellExpeditionState Expedition,
    PlayerKnowledgeState Knowledge,
    CrawlRuntimeEvent Event);

public static class CrawlRuntimeActions
{
    public static ExpeditionState Reposition(
        ExpeditionState expedition,
        HexCoordinate destination,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(expedition);

        var sequence = expedition.History.Count == 0 ? 1 : expedition.History[^1].Sequence + 1;
        var watchNumber = expedition.ActiveWatch?.WatchNumber ?? expedition.CompletedWatches;
        var priorHex = expedition.CurrentHex;
        var activeWatch = expedition.ActiveWatch?.WatchNumber;
        var message = priorHex == destination
            ? $"DM reset the party position in hex {destination} without recording travel."
            : $"DM repositioned the party from hex {priorHex} to hex {destination} without recording travel.";
        if (activeWatch.HasValue)
        {
            message += $" Active watch {activeWatch.Value} was ended.";
        }
        if (!string.IsNullOrWhiteSpace(note))
        {
            message += $" {note.Trim()}";
        }

        var runtimeEvent = new CrawlRuntimeEvent(
            sequence,
            watchNumber,
            CrawlRuntimeEventKind.DmOverrideApplied,
            expedition.ElapsedTravelTime,
            destination,
            message);

        return expedition with
        {
            Traversal = HexTraversalState.StartingIn(destination, expedition.Traversal.Progress.Unit),
            IntendedDirection = null,
            ActualDirection = null,
            Navigation = new NavigationRuntimeState(false, 0),
            ActiveWatch = null,
            History = [.. expedition.History, runtimeEvent]
        };
    }

    public static ExpeditionState SetIntendedCourse(
        ExpeditionState expedition,
        HexDirection? intendedDirection)
    {
        ArgumentNullException.ThrowIfNull(expedition);

        if (expedition.PendingEncounter is not null)
        {
            throw new InvalidOperationException(
                "Resolve the pending encounter before changing the intended course.");
        }

        if (expedition.ActiveWatch?.PendingDecision is RuntimePauseReason.LostRecognitionRequired
            or RuntimePauseReason.BacktrackBoundaryReached)
        {
            throw new InvalidOperationException(
                "Resolve the pending boundary decision before changing the intended course.");
        }

        if (intendedDirection is null && expedition.ActiveWatch is not null)
        {
            throw new InvalidOperationException(
                "The active travel watch requires an intended course. Choose another adjacent course instead of clearing it.");
        }

        var activeWatch = expedition.ActiveWatch;
        if (activeWatch is not null && intendedDirection is { } activeDirection)
        {
            activeWatch = activeWatch with
            {
                Plan = activeWatch.Plan with { IntendedDirection = activeDirection }
            };
        }

        return expedition with
        {
            IntendedDirection = intendedDirection,
            ActiveWatch = activeWatch
        };
    }

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

        var (name, eventKind) = ResolveWorldDiscoverySubject(world, subjectId, subjectType);

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
    public static CellManualDiscoveryResult DiscoverCell(
        OverworldDefinition world,
        CellExpeditionState expedition,
        PlayerKnowledgeState knowledge,
        Guid subjectId,
        KnowledgeSubjectType subjectType,
        string source)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(knowledge);
        expedition.Validate(world.SpatialTiling);
        if (knowledge.OverworldId != world.Id)
            throw new InvalidOperationException("Discovery knowledge belongs to another world.");

        var (name, kind) = ResolveWorldDiscoverySubject(world, subjectId, subjectType);
        var updatedKnowledge = KnowledgeDiscovery.Discover(
            knowledge, subjectId, subjectType, source);
        var sequence = expedition.History.Count == 0 ? 1 : expedition.History[^1].Sequence + 1;
        var watch = expedition.ActiveWatch?.WatchNumber ?? Math.Max(1, expedition.CompletedWatches);
        var recorded = new CrawlRuntimeEvent(
            sequence, watch, kind, expedition.ElapsedTravelTime,
            null, $"Discovered {subjectType.ToString().ToLowerInvariant()} {name}; unrelated contents remain unchanged.",
            SubjectId: subjectId, SubjectType: subjectType)
        { Cell = expedition.Traversal.CurrentCell };
        return new CellManualDiscoveryResult(
            expedition with { History = [.. expedition.History, recorded] },
            updatedKnowledge, recorded);
    }

    private static (string Name, CrawlRuntimeEventKind Kind) ResolveWorldDiscoverySubject(
        OverworldDefinition world, Guid subjectId, KnowledgeSubjectType type)
    {
        // The user-directed discovery action is semantic rather than a
        // proximity assertion; this intentionally matches existing hex rules.
        return type switch
        {
            KnowledgeSubjectType.Location when world.Locations.FirstOrDefault(x => x.Id == subjectId) is { } location
                => (location.Name, CrawlRuntimeEventKind.LocationDiscovered),
            KnowledgeSubjectType.Feature when world.Features.FirstOrDefault(x => x.Id == subjectId) is { } feature
                => (feature.Name, CrawlRuntimeEventKind.FeatureDiscovered),
            KnowledgeSubjectType.Location or KnowledgeSubjectType.Feature
                => throw new InvalidOperationException(
                    $"The {type.ToString().ToLowerInvariant()} does not exist in this overworld."),
            _ => throw new InvalidOperationException(
                "Manual discovery currently supports named locations and features only.")
        };
    }

}
