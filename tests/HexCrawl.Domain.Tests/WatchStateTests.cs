using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class WatchStateTests
{
    [Fact]
    public void BoundaryPauseRetainsSelectedPaceActivitiesAndNavigationAid()
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Watch state",
            Grid = grid
        };
        var start = new HexCoordinate(0, 0);
        var expedition = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            OverworldId = world.Id,
            Position = HexGeometry.HexToWorld(grid, start),
            Traversal = HexTraversalState.StartingIn(start, DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.Id
        };
        var plan = new WatchTravelPlan(
            new HexDirection(0),
            new TravelModeSelection("cautious", ["rest", "preparation"]),
            new NavigationAidSelection("compass"),
            ContinueAcrossBoundaries: false);

        var result = new CrawlRuntimeEngine().Advance(
            world,
            CrawlProcedureProfile.SimplifiedFixedDistance(),
            expedition,
            knowledge,
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
