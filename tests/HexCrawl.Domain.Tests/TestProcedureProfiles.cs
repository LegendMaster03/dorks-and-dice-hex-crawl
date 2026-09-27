using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Tests;

internal static class TestProcedureProfiles
{
    public static CrawlProcedureProfile AdvancedContinuous() => new()
    {
        Key = "test-advanced-continuous",
        Name = "Test Advanced Continuous",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.ContinuousDistance,
        ActualDistanceResolution = ActualDistanceResolutionMode.VariableResolved,
        EncounterCadence = EncounterCheckCadence.PerWatch,
        UsesNavigationChecks = true,
        UsesPersistentVeer = true,
        TracksIntraHexProgress = true,
        DirectionChangesCostProgress = true,
        SupportsDeliberateDoubleBack = true,
        StartingExitProgressFactor = 0.5d,
        NearExitProgressFactor = 0.5d,
        FarExitProgressFactor = 1d,
        BackExitProgressFactor = 0.5d,
        DirectionChangeProgressCostFactor = 1d / 6d,
        ResolutionHelpers = new ProcedureResolutionHelperProfile(
            Travel: new TravelResolutionHelperProfile(new DiceRollFormula(2, 6, 3), 0.1d),
            Navigation: new NavigationResolutionHelperProfile(new DiceRollFormula(1, 20)),
            Encounter: new EncounterResolutionHelperProfile(
                new DiceRollFormula(1, 8),
                DiceRollResultSet.From(new[] { 1 }),
                DiceRollResultSet.From(new[] { 8 }),
                8))
    };

    public static CrawlProcedureProfile FixedDistance() => new()
    {
        Key = "test-fixed-distance",
        Name = "Test Fixed Distance",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.ContinuousDistance,
        ActualDistanceResolution = ActualDistanceResolutionMode.Fixed,
        EncounterCadence = EncounterCheckCadence.None,
        UsesNavigationChecks = false,
        UsesPersistentVeer = false,
        TracksIntraHexProgress = true,
        DirectionChangesCostProgress = false,
        SupportsDeliberateDoubleBack = false,
        StartingExitProgressFactor = 0.5d,
        NearExitProgressFactor = 0.5d,
        FarExitProgressFactor = 1d,
        BackExitProgressFactor = 0.5d
    };

    public static CrawlProcedureProfile HexStep() => new()
    {
        Key = "test-hex-step",
        Name = "Test Hex Step",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.HexSteps,
        ActualDistanceResolution = ActualDistanceResolutionMode.Fixed,
        EncounterCadence = EncounterCheckCadence.None,
        UsesNavigationChecks = false,
        UsesPersistentVeer = false,
        TracksIntraHexProgress = false,
        DirectionChangesCostProgress = false,
        SupportsDeliberateDoubleBack = false
    };
}
