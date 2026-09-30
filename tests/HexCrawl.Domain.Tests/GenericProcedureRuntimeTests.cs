using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Domain.Tests;

public sealed class GenericProcedureRuntimeTests
{
    [Fact]
    public void AdvancedContinuousSnapshotBindsExpectedRuntimeMechanics()
    {
        CampaignProcedure procedure = TestProcedureProfiles.AdvancedContinuous();
        var runtime = GenericProcedureRuntime.Bind(procedure);

        Assert.Equal(TimeSpan.FromHours(4), runtime.Time.IntervalDuration);
        Assert.Equal(TravelResolutionMode.ContinuousDistance, runtime.Movement.TravelResolution);
        Assert.Equal(ActualDistanceResolutionMode.VariableResolved, runtime.Movement.ActualDistanceResolution);
        Assert.Equal(EncounterCheckCadence.PerWatch, runtime.Encounters.Cadence);
        Assert.True(runtime.Navigation.UsesNavigationChecks);
        Assert.True(runtime.Navigation.UsesPersistentVeer);
        Assert.True(runtime.Movement.TracksIntraHexProgress);
        Assert.True(runtime.HexProgress.DirectionChangesCostProgress);
        Assert.True(runtime.HexProgress.SupportsDeliberateDoubleBack);

        Assert.Equal(0.5d, runtime.HexProgress.StartingExitProgressFactor, 12);
        Assert.Equal(0.5d, runtime.HexProgress.NearExitProgressFactor, 12);
        Assert.Equal(1d, runtime.HexProgress.FarExitProgressFactor, 12);
        Assert.Equal(0.5d, runtime.HexProgress.BackExitProgressFactor, 12);
        Assert.Equal(1d / 6d, runtime.HexProgress.DirectionChangeProgressCostFactor, 12);

        var helpers = Assert.IsType<ProcedureResolutionHelperProfile>(runtime.ResolutionHelpers);
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
