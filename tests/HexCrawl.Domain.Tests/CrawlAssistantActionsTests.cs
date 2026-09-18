using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class CrawlAssistantActionsTests
{
    [Fact]
    public void TravelWatchAssistantMutatesOnlyTravelWatchState()
    {
        var state = State();
        var context = new CrawlRuntimeContext(Miles(12));
        var travel = ResolvedTravelAmount.Distance(
            Miles(6),
            Miles(6),
            new ResolutionProvenance(ResolutionSource.ManualRoll, "physical dice"));

        var result = CrawlAssistantActions.RecordTravelWatch(
            context,
            CrawlProcedureProfile.SimplifiedFixedDistance(),
            state,
            new TravelWatchAssistantInput(
                TimeSpan.FromHours(2),
                travel,
                new HexCoordinate(1, 0),
                Miles(2),
                new HexDirection(0),
                null,
                CompleteWatch: true,
                Note: "physical map"));

        Assert.Equal(new HexCoordinate(1, 0), result.CurrentHex);
        Assert.Equal(6, result.DistanceTraveled.Value, 6);
        Assert.Equal(TimeSpan.FromHours(2), result.ElapsedTravelTime);
        Assert.Equal(1, result.CompletedWatches);
        Assert.False(result.Navigation.IsLost);
        Assert.Contains(result.History, item => item.Kind == CrawlRuntimeEventKind.TravelResolved);
        Assert.Contains(result.History, item =>
            item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && item.Message.Contains("travel-assistant=ManualRoll", StringComparison.Ordinal));
    }

    [Fact]
    public void NavigationAssistantMutatesNavigationWithoutAdvancingTravel()
    {
        var state = State();

        var result = CrawlAssistantActions.RecordNavigation(
            state,
            new NavigationAssistantInput(
                IsLost: true,
                VeerSteps: 1,
                IntendedDirection: new HexDirection(0),
                new ResolutionProvenance(ResolutionSource.ExternalSystem, "external check"),
                Note: "physical compass"));

        Assert.True(result.Navigation.IsLost);
        Assert.Equal(1, result.Navigation.VeerSteps);
        Assert.Equal(new HexDirection(0), result.IntendedDirection);
        Assert.Equal(new HexDirection(1), result.ActualDirection);
        Assert.Equal(state.DistanceTraveled, result.DistanceTraveled);
        Assert.Equal(state.ElapsedTravelTime, result.ElapsedTravelTime);
        Assert.Equal(state.CompletedWatches, result.CompletedWatches);
        Assert.Contains(result.History, item => item.Kind == CrawlRuntimeEventKind.ExpeditionBecameLost);
    }

    [Fact]
    public void EncounterAssistantRecordsCadenceWithoutAdvancingTravel()
    {
        var state = State();

        var result = CrawlAssistantActions.RecordEncounterCadence(
            state,
            new EncounterCadenceAssistantInput(
                EncounterOutcomeKind.WanderingEncounter,
                new ResolutionProvenance(ResolutionSource.ManualRoll, "d20"),
                "wandering monster"));

        Assert.Equal(state.DistanceTraveled, result.DistanceTraveled);
        Assert.Equal(state.ElapsedTravelTime, result.ElapsedTravelTime);
        Assert.Equal(state.CurrentHex, result.CurrentHex);
        Assert.Contains(result.History, item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed);
        Assert.Contains(result.History, item => item.Kind == CrawlRuntimeEventKind.EncounterTriggered);
    }

    [Fact]
    public void FocusedAssistantsDoNotMutateAnActiveFullWorkbenchWatch()
    {
        var state = State() with
        {
            ActiveWatch = new ActiveWatchState(
                1,
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(1),
                new WatchTravelPlan(
                    new HexDirection(0),
                    TravelModeSelection.Normal,
                    NavigationAidSelection.None),
                ResolvedEncounter.None,
                true,
                null)
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CrawlAssistantActions.RecordNavigation(
                state,
                new NavigationAssistantInput(
                    false,
                    0,
                    new HexDirection(0),
                    ResolutionProvenance.ProcedureDefault)));

        Assert.Contains("active full-workbench watch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncounterAssistantRejectsWorldBoundKeyedDiscovery()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CrawlAssistantActions.RecordEncounterCadence(
                State(),
                new EncounterCadenceAssistantInput(
                    EncounterOutcomeKind.KeyedLocationDiscovery,
                    ResolutionProvenance.ProcedureDefault)));

        Assert.Contains("world/map composition", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        OverworldId = Guid.NewGuid(),
        Position = new WorldPoint(0, 0),
        PositionPrecision = WorldPositionPrecision.HexAnchor,
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        Navigation = new NavigationRuntimeState(false, 0),
        DistanceTraveled = Miles(0)
    };

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);
}
