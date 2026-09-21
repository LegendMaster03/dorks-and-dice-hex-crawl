using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record ExpeditionWorldProjection(
    ExpeditionState State,
    PlayerKnowledgeState Knowledge);

/// <summary>
/// Composes map/world concerns around the map-independent crawl engine.
/// </summary>
public static class ExpeditionWorldComposition
{
    public static CrawlRuntimeContext RuntimeContext(OverworldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new CrawlRuntimeContext(world.Grid.NeighborCenterDistance);
    }

    public static ExpeditionWorldProjection Apply(
        OverworldDefinition world,
        WatchAdvanceResult runtime,
        PlayerKnowledgeState knowledge,
        bool applyAutomaticKnowledge)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(knowledge);

        var state = runtime.Expedition with
        {
            Position = HexGeometry.HexToWorld(world.Grid, runtime.Expedition.CurrentHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor
        };

        var triggered = runtime.Events.LastOrDefault(item =>
            item.Kind == CrawlRuntimeEventKind.EncounterTriggered
            && item.SubjectId.HasValue);
        if (triggered is null
            || state.ActiveWatch?.Encounter.Kind != EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            return new ExpeditionWorldProjection(state, knowledge);
        }

        var locationId = triggered.SubjectId!.Value;
        var location = world.Locations.SingleOrDefault(candidate => candidate.Id == locationId)
            ?? throw new InvalidOperationException("The resolved keyed location does not exist in this overworld.");
        if (HexGeometry.WorldToHex(world.Grid, location.Position) != state.CurrentHex)
        {
            throw new InvalidOperationException("A keyed-location encounter can only discover a location in the expedition's current hex.");
        }

        var nextSequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
        var encountered = new CrawlRuntimeEvent(
            nextSequence,
            triggered.WatchNumber,
            CrawlRuntimeEventKind.KeyedLocationEncountered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Encountered keyed location {location.Name}.",
            SubjectId: location.Id,
            SubjectType: KnowledgeSubjectType.Location);
        var discovered = new CrawlRuntimeEvent(
            nextSequence + 1,
            triggered.WatchNumber,
            CrawlRuntimeEventKind.LocationDiscovered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Discovered location {location.Name}; no other contents of the hex were revealed.",
            SubjectId: location.Id,
            SubjectType: KnowledgeSubjectType.Location);
        state = state with { History = [.. state.History, encountered, discovered] };

        if (applyAutomaticKnowledge)
        {
            knowledge = KnowledgeDiscovery.Discover(
                knowledge,
                location.Id,
                KnowledgeSubjectType.Location,
                "runtime:keyed-location");
        }

        return new ExpeditionWorldProjection(state, knowledge);
    }
}
