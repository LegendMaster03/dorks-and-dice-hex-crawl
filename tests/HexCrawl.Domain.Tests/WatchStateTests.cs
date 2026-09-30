using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class WatchStateTests
{
    [Fact]
    public void BoundaryPauseRetainsSelectedPaceTypedAssignmentsAndNavigationAid()
    {
        var context = new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles));
        var start = new HexCoordinate(0, 0);
        var member = Guid.NewGuid();
        var assignments = new[]
        {
            new ParticipantActivityAssignment(
                Guid.NewGuid(),
                ParticipantActivityAssignmentScope.Participant,
                member,
                "rest",
                null),
            new ParticipantActivityAssignment(
                Guid.NewGuid(),
                ParticipantActivityAssignmentScope.Participant,
                member,
                "preparation",
                "lookout")
        };
        var expedition = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            Position = new WorldPoint(0, 0),
            Traversal = HexTraversalState.StartingIn(start, DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        var plan = new WatchTravelPlan(
            new HexDirection(0),
            new TravelModeSelection("cautious", assignments),
            new NavigationAidSelection("compass"),
            ContinueAcrossBoundaries: false);

        var result = new CrawlRuntimeEngine().Advance(
            context,
            TestProcedureProfiles.FixedDistance(),
            expedition,
            plan,
            new WatchAdvanceInputs(TravelDistanceResolver.Fixed(new DistanceMeasure(12, DistanceUnit.Miles))));

        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, result.PauseReason);
        Assert.NotNull(result.Expedition.ActiveWatch);
        Assert.Equal("cautious", result.Expedition.ActiveWatch.Plan.Mode.PaceKey);
        Assert.Equal(assignments, result.Expedition.ActiveWatch.Plan.Mode.ActivityAssignments);
        Assert.Equal("compass", result.Expedition.ActiveWatch.Plan.NavigationAid.Key);
        Assert.Contains(
            result.Events,
            item => item.Kind == CrawlRuntimeEventKind.WatchStarted
                && item.Message.Contains("rest", StringComparison.Ordinal)
                && item.Message.Contains("preparation", StringComparison.Ordinal)
                && item.Message.Contains("lookout", StringComparison.Ordinal));
    }
}
