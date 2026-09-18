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

    [Fact]
    public void NonSpatialWatchSupportsPartialResumeAndCompletion()
    {
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance();
        var state = new NonSpatialSessionState { Id = Guid.NewGuid() };

        var partial = CrawlAssistantActions.RecordWatch(
            profile,
            state,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(1.5),
                new ResolutionProvenance(ResolutionSource.ProcedureDefault, "table clock"),
                "first segment"));

        Assert.Equal(TimeSpan.FromHours(1.5), partial.ElapsedTime);
        Assert.Equal(0, partial.CompletedWatches);
        Assert.NotNull(partial.ActiveWatch);
        Assert.Equal(1, partial.ActiveWatch.WatchNumber);
        Assert.Equal(profile.WatchLength, partial.ActiveWatch.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(1.5), partial.ActiveWatch.Elapsed);
        Assert.Equal(profile.WatchLength - TimeSpan.FromHours(1.5), partial.ActiveWatch.Remaining);
        Assert.Contains(partial.History, item => item.Kind == CrawlRuntimeEventKind.WatchStarted && item.Hex is null);
        Assert.Contains(partial.History, item => item.Kind == CrawlRuntimeEventKind.WatchTimeAdvanced && item.Hex is null);
        Assert.Contains(partial.History, item =>
            item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && item.Message.Contains("watch-assistant=ProcedureDefault", StringComparison.Ordinal));

        var completed = CrawlAssistantActions.RecordWatch(
            profile,
            partial,
            new NonSpatialWatchAssistantInput(
                partial.ActiveWatch.Remaining,
                new ResolutionProvenance(ResolutionSource.ManualRoll, "manual clock"),
                "resume"));

        Assert.Equal(profile.WatchLength, completed.ElapsedTime);
        Assert.Equal(1, completed.CompletedWatches);
        Assert.Null(completed.ActiveWatch);
        Assert.Contains(completed.History, item =>
            item.Kind == CrawlRuntimeEventKind.WatchCompleted
            && item.WatchNumber == 1
            && item.Hex is null);
    }

    [Fact]
    public void NonSpatialWatchRejectsElapsedTimeBeyondRemainingWatch()
    {
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance();
        var state = new NonSpatialSessionState
        {
            Id = Guid.NewGuid(),
            ActiveWatch = new NonSpatialActiveWatchState(
                1,
                profile.WatchLength,
                profile.WatchLength - TimeSpan.FromMinutes(30))
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            CrawlAssistantActions.RecordWatch(
                profile,
                state,
                new NonSpatialWatchAssistantInput(
                    TimeSpan.FromHours(1),
                    ResolutionProvenance.ProcedureDefault)));

        Assert.Contains("exceeds the remaining", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonSpatialWatchRecordsDmOverrideWithoutInventingSpatialState()
    {
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance();
        var state = new NonSpatialSessionState { Id = Guid.NewGuid() };

        var result = CrawlAssistantActions.RecordWatch(
            profile,
            state,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(1),
                new ResolutionProvenance(ResolutionSource.DmOverride, "session ruling"),
                "advance the clock"));

        Assert.Equal(TimeSpan.FromHours(1), result.ElapsedTime);
        Assert.NotNull(result.ActiveWatch);
        Assert.All(result.History, item => Assert.Null(item.Hex));
        Assert.Contains(result.History, item =>
            item.Kind == CrawlRuntimeEventKind.DmOverrideApplied
            && item.Message.Contains("advance the clock", StringComparison.Ordinal));
        Assert.Contains(result.History, item =>
            item.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && item.Message.Contains("watch-assistant=DmOverride", StringComparison.Ordinal));
    }

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        Position = new WorldPoint(0, 0),
        PositionPrecision = WorldPositionPrecision.HexAnchor,
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        Navigation = new NavigationRuntimeState(false, 0),
        DistanceTraveled = Miles(0)
    };

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);
}
