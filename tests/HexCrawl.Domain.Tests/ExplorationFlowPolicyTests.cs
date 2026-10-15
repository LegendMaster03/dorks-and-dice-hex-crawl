using HexCrawl.Domain.Runtime;

namespace HexCrawl.Domain.Tests;

public sealed class ExplorationFlowPolicyTests
{
    [Fact]
    public void OrderedTravelAndEncounterStagesDoNotRequireNamedSystem()
    {
        var flow = new ExplorationFlowPolicy(
            ["travel", "encounter", "action", "journal"],
            ExplorationDiscoveryTrigger.OnFirstVisit,
            ExplorationEncounterTrigger.EveryTravel);
        flow.Validate();
        Assert.True(flow.ShouldGenerateLocation(false, null));
        Assert.False(flow.ShouldGenerateLocation(true, null));
        Assert.True(flow.RequiresEncounterAfterTravel());
        Assert.Equal(ExplorationEncounterRequirement.Required, flow.EncounterForAction(false, null));
        Assert.Empty(flow.AvailableActions(false, false));
    }

    [Fact]
    public void LocationActionsDependOnlyOnVisitedAndRestSiteFlags()
    {
        var flow = new ExplorationFlowPolicy(
            ["choose", "resolve"],
            ExplorationDiscoveryTrigger.OnExplore,
            ExplorationEncounterTrigger.OnExploreOrOptionalTravel,
            "explore", "travel", "rest");
        flow.Validate();
        Assert.Equal(["explore"], flow.AvailableActions(false, false));
        Assert.Equal(["travel"], flow.AvailableActions(true, false));
        Assert.Equal(["explore", "rest"], flow.AvailableActions(false, true));
        Assert.Equal(["travel", "rest"], flow.AvailableActions(true, true));
        Assert.True(flow.ShouldGenerateLocation(false, "explore"));
        Assert.False(flow.ShouldGenerateLocation(false, "travel"));
        Assert.False(flow.ShouldGenerateLocation(true, "explore"));
        Assert.False(flow.RequiresEncounterAfterTravel());
        Assert.Equal(ExplorationEncounterRequirement.Required, flow.EncounterForAction(false, "explore"));
        Assert.Equal(ExplorationEncounterRequirement.OptionalOrRiskBased, flow.EncounterForAction(true, "travel"));
        Assert.Equal(ExplorationEncounterRequirement.None, flow.EncounterForAction(true, "rest"));
        Assert.Equal(ExplorationEncounterRequirement.None, flow.EncounterForAction(false, "travel"));
    }

    [Fact]
    public void ActionDrivenStagesRequireAdjacentStagesAndConfiguredActions()
    {
        new ExplorationFlowPolicy(
            ["choose", "resolve", "journal"],
            ExplorationDiscoveryTrigger.OnExplore,
            ExplorationEncounterTrigger.OnExploreOrOptionalTravel,
            "explore", "travel", "rest", "choose", "resolve").Validate();

        Assert.Throws<InvalidOperationException>(() => new ExplorationFlowPolicy(
            ["choose", "resolve"], ExplorationDiscoveryTrigger.OnExplore,
            ExplorationEncounterTrigger.Manual,
            "explore", "travel", "rest", "choose", null).Validate());
        Assert.Throws<InvalidOperationException>(() => new ExplorationFlowPolicy(
            ["choose", "journal", "resolve"], ExplorationDiscoveryTrigger.OnExplore,
            ExplorationEncounterTrigger.Manual,
            "explore", "travel", "rest", "choose", "resolve").Validate());
    }

    [Fact]
    public void InvalidStagesAndUnknownTriggersAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new ExplorationFlowPolicy(
            ["travel", "TRAVEL"], ExplorationDiscoveryTrigger.Manual,
            ExplorationEncounterTrigger.Manual).Validate());
        Assert.Throws<InvalidOperationException>(() => new ExplorationFlowPolicy(
            ["travel"], (ExplorationDiscoveryTrigger)999,
            ExplorationEncounterTrigger.Manual).Validate());
    }
}
