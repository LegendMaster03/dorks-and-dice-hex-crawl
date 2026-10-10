using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record CellWorldProjection(
    CellExpeditionState State,
    PlayerKnowledgeState Knowledge);

/// <summary>
/// Cell-specific *spatial* adapter around shared semantic knowledge and
/// encounter rules. All location membership is checked against the resolved
/// authoritative polygon, never an approximate axial hex or motif index.
/// </summary>
public static class ExpeditionCellWorldComposition
{
    public static CellWorldProjection Apply(
        OverworldDefinition world,
        CellWatchAdvanceResult runtime,
        PlayerKnowledgeState knowledge,
        bool applyAutomaticKnowledge)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(knowledge);
        var tiling = world.SpatialTiling;
        var state = runtime.Expedition;
        state.Validate(tiling);
        if (knowledge.OverworldId != world.Id)
            throw new InvalidOperationException("Cell expedition knowledge belongs to another world.");
        var policy = knowledge.PresentationPolicy ?? MapPresentationPolicy.DmControlled();
        policy.Validate();

        if (applyAutomaticKnowledge)
        {
            knowledge = PresentationKnowledgeProjection.ApplyEnteredCells(
                policy, knowledge,
                runtime.Events.Where(e => e.Kind == CrawlRuntimeEventKind.CellEntered)
                    .Select(e => e.Cell ?? throw new InvalidOperationException(
                        "A cell entry event lacks qualified cell authority.")));
        }

        var triggered = runtime.Events.LastOrDefault(e =>
            e.Kind == CrawlRuntimeEventKind.EncounterTriggered);
        if (triggered is null)
            return new CellWorldProjection(state, knowledge);
        if (triggered.Cell != state.Traversal.CurrentCell || triggered.Hex is not null)
            throw new InvalidOperationException("Encounter must refer to the party's authoritative cell.");

        Location? location = null;
        if (triggered.SubjectId is { } reference)
        {
            location = world.Locations.SingleOrDefault(x => x.Id == reference)
                ?? throw new InvalidOperationException("Encounter location is absent from this world.");
            var snapshot = new CrawlRuntimeLocationSnapshot(
                location.Id, location.Name, location.Category,
                location.DetailMaps.Select(x => new CrawlRuntimeLinkedSceneSnapshot(
                    x.Id, x.Kind, x.ReferenceKey)).ToArray());
            var captured = triggered with { EncounterLocation = snapshot };
            state = state with
            {
                History = state.History.Select(e =>
                    e.Sequence == triggered.Sequence ? captured : e).ToArray()
            };
        }

        if (state.ActiveWatch?.Encounter.Kind != EncounterOutcomeKind.KeyedLocationDiscovery)
            return new CellWorldProjection(state, knowledge);
        if (location is null)
            throw new InvalidOperationException("Keyed discovery requires a real linked location.");
        var cellPolygon = tiling.Resolve(state.Traversal.CurrentCell.Address).Polygon;
        if (!FeatureIntersection.PointInPolygon(location.Position, cellPolygon))
            throw new InvalidOperationException(
                "A keyed encounter may discover only a location in the current authoritative cell.");

        var nextSequence = state.History.Count == 0 ? 1 : state.History[^1].Sequence + 1;
        var occurred = new CrawlRuntimeEvent(
            nextSequence, triggered.WatchNumber,
            CrawlRuntimeEventKind.KeyedLocationEncountered,
            state.ElapsedTravelTime, null,
            $"Encountered keyed location {location.Name}.",
            SubjectId: location.Id, SubjectType: KnowledgeSubjectType.Location)
        { Cell = state.Traversal.CurrentCell };
        var discovered = new CrawlRuntimeEvent(
            nextSequence + 1, triggered.WatchNumber,
            CrawlRuntimeEventKind.LocationDiscovered,
            state.ElapsedTravelTime, null,
            $"Discovered location {location.Name}; unrelated cell contents remain unchanged.",
            SubjectId: location.Id, SubjectType: KnowledgeSubjectType.Location)
        { Cell = state.Traversal.CurrentCell };
        state = state with { History = [.. state.History, occurred, discovered] };
        if (applyAutomaticKnowledge && policy.AutomationMode != PresentationAutomationMode.DmControlled)
        {
            knowledge = KnowledgeDiscovery.Discover(
                knowledge, location.Id, KnowledgeSubjectType.Location,
                "runtime:keyed-location");
        }
        return new CellWorldProjection(state, knowledge);
    }
}
