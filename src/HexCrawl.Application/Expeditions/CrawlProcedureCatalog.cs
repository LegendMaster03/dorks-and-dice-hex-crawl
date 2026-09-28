using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public static class CrawlProcedureCatalog
{
    public static IReadOnlyList<CrawlProcedurePresetDefinition> All { get; } =
    [
        new(
  "alexandrian-advanced",
  "Alexandrian Advanced",
  "Four-hour continuous-distance travel with navigation, persistent veer, per-watch encounters, and procedure helpers.",
  1,
  AlexandrianAdvancedTemplate()),
        new(
  "simple-fixed-distance",
  "Simple Fixed Distance",
  "Four-hour continuous-distance travel using fixed resolved distance without navigation or encounter checks.",
  1,
  SimplifiedFixedDistanceTemplate()),
        new(
  "simple-hex-step",
  "Simple Hex Step",
  "Four-hour travel resolved as whole hex steps without intra-hex progress, navigation, or encounter checks.",
  1,
  SimplifiedHexStepTemplate())
    ];

    public static CrawlProcedurePresetDefinition Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
  ? All[0]
  : All.FirstOrDefault(item => string.Equals(item.PresetKey, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown crawl procedure preset '{key}'.", nameof(key));
    }

    private static CrawlProcedureProfile AlexandrianAdvancedTemplate() => new()
    {
        Key = "alexandrian-advanced",
        Name = "Alexandrian Advanced",
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

    private static CrawlProcedureProfile SimplifiedFixedDistanceTemplate() => new()
    {
        Key = "simple-fixed-distance",
        Name = "Simple Fixed Distance",
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

    private static CrawlProcedureProfile SimplifiedHexStepTemplate() => new()
    {
        Key = "simple-hex-step",
        Name = "Simple Hex Step",
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
