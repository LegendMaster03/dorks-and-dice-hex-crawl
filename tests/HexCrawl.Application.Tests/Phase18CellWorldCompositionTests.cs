using HexCrawl.Application;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class Phase18CellWorldCompositionTests
{
    private static readonly ResolutionProvenance Manual = new(ResolutionSource.ManualRoll);

    [Fact]
    public void KeyedLocationUsesActualCellPolygonAndDoesNotCreateAxialKnowledge()
    {
        var (world, current, inside, outside) = SetupWorld();
        var (runtime, knowledge) = Encounter(
            world, current, inside.Id,
            EncounterOutcomeKind.KeyedLocationDiscovery,
            MapPresentationPolicy.ExplorationMap());
        var projected = ExpeditionCellWorldComposition.Apply(
            world, runtime, knowledge, applyAutomaticKnowledge: true);
        var events = projected.State.History;
        Assert.Equal(4, events.Count);
        Assert.Equal(4, events.Select(e => e.Sequence).Distinct().Count());
        Assert.Empty(projected.Knowledge.KnownHexes);
        Assert.Equal(current.Id, Assert.Single(projected.Knowledge.KnownCells!));
        Assert.Equal(KnowledgeState.Discovered, projected.Knowledge.Entries[inside.Id].State);
        Assert.DoesNotContain(outside.Id, projected.Knowledge.Entries.Keys);
        Assert.Equal(current.Id, Assert.Single(events, e =>
            e.Kind == CrawlRuntimeEventKind.KeyedLocationEncountered).Cell);
        Assert.Equal(current.Id, Assert.Single(events, e =>
            e.Kind == CrawlRuntimeEventKind.LocationDiscovered).Cell);
        Assert.Null(Assert.Single(events, e =>
            e.Kind == CrawlRuntimeEventKind.LocationDiscovered).Hex);
        Assert.Equal(inside.Id, Assert.Single(events, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterTriggered).EncounterLocation!.Id);
    }

    [Fact]
    public void ForeignCellLocationFailsInsteadOfDiscoveringOtherCellContents()
    {
        var (world, current, _, outside) = SetupWorld();
        var (runtime, knowledge) = Encounter(
            world, current, outside.Id,
            EncounterOutcomeKind.KeyedLocationDiscovery,
            MapPresentationPolicy.ExplorationMap());
        Assert.Throws<InvalidOperationException>(() =>
            ExpeditionCellWorldComposition.Apply(world, runtime, knowledge, true));
        Assert.Empty(knowledge.Entries);
        Assert.Null(knowledge.KnownCells);
    }

    [Fact]
    public void DmControlledPresentationRecordsEncounterWithoutAutomaticallyRevealing()
    {
        var (world, current, inside, _) = SetupWorld();
        var (runtime, knowledge) = Encounter(
            world, current, inside.Id,
            EncounterOutcomeKind.KeyedLocationDiscovery,
            MapPresentationPolicy.DmControlled());
        var projection = ExpeditionCellWorldComposition.Apply(world, runtime, knowledge, true);
        Assert.Empty(projection.Knowledge.Entries);
        Assert.Null(projection.Knowledge.KnownCells);
        Assert.Contains(projection.State.History,
            e => e.Kind == CrawlRuntimeEventKind.LocationDiscovered
                 && e.Cell == current.Id && e.Hex is null);
    }

    [Fact]
    public void OrdinaryEncounterWithLinkedSceneSnapshotsLocationWithoutDiscovering()
    {
        var (world, current, inside, _) = SetupWorld();
        var (runtime, knowledge) = Encounter(
            world, current, inside.Id,
            EncounterOutcomeKind.WanderingEncounter,
            MapPresentationPolicy.ExplorationMap());
        var result = ExpeditionCellWorldComposition.Apply(world, runtime, knowledge, true);
        Assert.Equal(2, result.State.History.Count);
        Assert.Equal(inside.Id, Assert.Single(result.State.History, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterTriggered).EncounterLocation!.Id);
        Assert.Empty(result.Knowledge.Entries);
        Assert.Single(result.Knowledge.KnownCells!);
    }

    private static (OverworldDefinition World, WorldCellGeometry Current, Location Inside, Location Outside)
        SetupWorld()
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), generated.Topology!, generated.Realization!,
            new WorldPoint(0, 0));
        tiling.Validate();
        var cell = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var inside = new Location(
            Guid.NewGuid(), "Ancient gate", "landmark",
            cell.Center, LocationDiscoverability.Hidden, []);
        var outside = new Location(
            Guid.NewGuid(), "Distant citadel", "settlement",
            new WorldPoint(10000, 10000), LocationDiscoverability.Hidden, []);
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Keyed encounter test",
            Tiling = tiling, Locations = [inside, outside]
        };
        return (world, cell, inside, outside);
    }

    private static (CellWatchAdvanceResult Runtime, PlayerKnowledgeState Knowledge) Encounter(
        OverworldDefinition world, WorldCellGeometry current,
        Guid subjectId, EncounterOutcomeKind kind, MapPresentationPolicy policy)
    {
        var heading = new WorldPoint(1, 0);
        var plan = new CellWatchTravelPlan(
            heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
        var scheduled = new ResolvedEncounter(
            kind, TimeSpan.FromHours(1), subjectId, "Encounter", Manual);
        var occurrenceId = Guid.NewGuid();
        var trigger = new CrawlRuntimeEvent(
            2, 1, CrawlRuntimeEventKind.EncounterTriggered,
            TimeSpan.FromHours(1), null, "A location was encountered.",
            SubjectId: subjectId, EncounterOutcome: kind,
            EncounterOccurrenceId: occurrenceId)
        { Cell = current.Id };
        var entry = new CrawlRuntimeEvent(
            1, 1, CrawlRuntimeEventKind.CellEntered,
            TimeSpan.FromMinutes(20), null, "Entered a new cell.")
        { Cell = current.Id };
        var runtime = new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            IntendedHeading = heading,
            ElapsedTravelTime = TimeSpan.FromHours(1),
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = current.Id,
                Position = current.Center
            },
            ActiveWatch = new CellActiveWatchState(
                1, TimeSpan.FromHours(4), TimeSpan.FromHours(1),
                plan, scheduled, true, RuntimePauseReason.EncounterTriggered),
            PendingEncounter = new PendingEncounterOccurrence(
                occurrenceId, 2, 1, kind, TimeSpan.FromHours(1),
                null, subjectId, "Encounter", Manual)
            { Cell = current.Id },
            History = [entry, trigger]
        };
        runtime.Validate(world.SpatialTiling);
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.Id,
            PresentationPolicy = policy
        };
        return (new CellWatchAdvanceResult(
            runtime, RuntimePauseReason.EncounterTriggered,
            TimeSpan.FromHours(3), [entry, trigger]), knowledge);
    }
}
