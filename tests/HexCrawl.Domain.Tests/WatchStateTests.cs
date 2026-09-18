using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class WatchStateTests
{
    [Fact]
    public void BoundaryPauseRetainsSelectedPaceActivitiesAndNavigationAid()
    {
        var context = new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles));
        var start = new HexCoordinate(0, 0);
        var expedition = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            OverworldId = Guid.NewGuid(),
            Position = new WorldPoint(0, 0),
            Traversal = HexTraversalState.StartingIn(start, DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        var plan = new WatchTravelPlan(
            new HexDirection(0),
            new TravelModeSelection("cautious", ["rest", "preparation"]),
            new NavigationAidSelection("compass"),
            ContinueAcrossBoundaries: false);

        var result = new CrawlRuntimeEngine().Advance(
            context,
            CrawlProcedureProfile.SimplifiedFixedDistance(),
            expedition,
            plan,
            new WatchAdvanceInputs(TravelDistanceResolver.Fixed(new DistanceMeasure(12, DistanceUnit.Miles))));

        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, result.PauseReason);
        Assert.NotNull(result.Expedition.ActiveWatch);
        Assert.Equal("cautious", result.Expedition.ActiveWatch.Plan.Mode.PaceKey);
        Assert.Equal(["rest", "preparation"], result.Expedition.ActiveWatch.Plan.Mode.Activities);
        Assert.Equal("compass", result.Expedition.ActiveWatch.Plan.NavigationAid.Key);
        Assert.Contains(
            result.Events,
            item => item.Kind == CrawlRuntimeEventKind.WatchStarted
                && item.Message.Contains("rest", StringComparison.Ordinal)
                && item.Message.Contains("preparation", StringComparison.Ordinal));
    }
}
