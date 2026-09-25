using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Tests;

public sealed class CrawlProcedureProfileTests
{
    [Fact]
    public void AlexandrianAdvancedBaselineRetainsReviewedSourceMechanics()
    {
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline();

        Assert.Equal(TimeSpan.FromHours(4), profile.WatchLength);
        Assert.Equal(TravelResolutionMode.ContinuousDistance, profile.TravelResolution);
        Assert.Equal(ActualDistanceResolutionMode.VariableResolved, profile.ActualDistanceResolution);
        Assert.Equal(EncounterCheckCadence.PerWatch, profile.EncounterCadence);
        Assert.True(profile.UsesNavigationChecks);
        Assert.True(profile.UsesPersistentVeer);
        Assert.True(profile.TracksIntraHexProgress);
        Assert.True(profile.DirectionChangesCostProgress);
        Assert.True(profile.SupportsDeliberateDoubleBack);

        Assert.Equal(0.5d, profile.StartingExitProgressFactor, 12);
        Assert.Equal(0.5d, profile.NearExitProgressFactor, 12);
        Assert.Equal(1d, profile.FarExitProgressFactor, 12);
        Assert.Equal(0.5d, profile.BackExitProgressFactor, 12);
        Assert.Equal(1d / 6d, profile.DirectionChangeProgressCostFactor, 12);

        var helpers = Assert.IsType<ProcedureResolutionHelperProfile>(profile.ResolutionHelpers);
        var travel = Assert.IsType<TravelResolutionHelperProfile>(helpers.Travel);
        Assert.Equal(new DiceRollFormula(2, 6, 3), travel.Roll);
        Assert.Equal(0.1d, travel.DistanceFactorPerRollPoint, 12);

        var navigation = Assert.IsType<NavigationResolutionHelperProfile>(helpers.Navigation);
        Assert.Equal(new DiceRollFormula(1, 20), navigation.CheckRoll);

        var encounter = Assert.IsType<EncounterResolutionHelperProfile>(helpers.Encounter);
        Assert.Equal(new DiceRollFormula(1, 8), encounter.CheckRoll);
        Assert.Equal([1], encounter.WanderingResults.Values);
        Assert.Equal([8], encounter.KeyedLocationResults.Values);
        Assert.Equal(8, encounter.TimingSlots);
    }
}
