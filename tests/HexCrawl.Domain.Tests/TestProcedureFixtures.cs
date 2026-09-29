using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Domain.Tests;

internal sealed record TestProcedureFixture
{
    public string Key { get; init; } = "test-procedure";
    public string Name { get; init; } = "Test Procedure";
    public TimeSpan WatchLength { get; init; } = TimeSpan.FromHours(4);
    public TravelResolutionMode TravelResolution { get; init; } = TravelResolutionMode.ContinuousDistance;
    public ActualDistanceResolutionMode ActualDistanceResolution { get; init; } = ActualDistanceResolutionMode.Fixed;
    public EncounterCheckCadence EncounterCadence { get; init; } = EncounterCheckCadence.PerWatch;
    public bool UsesNavigationChecks { get; init; }
    public bool UsesPersistentVeer { get; init; }
    public bool TracksIntraHexProgress { get; init; } = true;
    public bool DirectionChangesCostProgress { get; init; }
    public bool SupportsDeliberateDoubleBack { get; init; } = true;
    public double StartingExitProgressFactor { get; init; } = 0.5d;
    public double NearExitProgressFactor { get; init; } = 0.5d;
    public double FarExitProgressFactor { get; init; } = 1d;
    public double BackExitProgressFactor { get; init; } = 0.5d;
    public double DirectionChangeProgressCostFactor { get; init; }
    public ProcedureResolutionHelperProfile? ResolutionHelpers { get; init; }

    public CampaignProcedure Materialize()
    {
        var modules = new[]
        {
            Module(
                "test.time",
                "test.fixed-interval-duration",
                GenericProcedureExecutionHandlers.FixedIntervalDuration,
                Values(("durationTicks", WatchLength.Ticks.ToString(CultureInfo.InvariantCulture)))),
            Module(
                "test.movement",
                "test.movement-resolution-policy",
                GenericProcedureExecutionHandlers.MovementResolutionPolicy,
                Values(
                    ("travelResolution", TravelResolution.ToString()),
                    ("actualDistanceResolution", ActualDistanceResolution.ToString()),
                    ("tracksIntraHexProgress", Bool(TracksIntraHexProgress)))),
            Module(
                "test.hex-progress",
                "test.hex-progress-policy",
                GenericProcedureExecutionHandlers.HexProgressPolicy,
                Values(
                    ("startingExitProgressFactor", Number(StartingExitProgressFactor)),
                    ("nearExitProgressFactor", Number(NearExitProgressFactor)),
                    ("farExitProgressFactor", Number(FarExitProgressFactor)),
                    ("backExitProgressFactor", Number(BackExitProgressFactor)),
                    ("directionChangesCostProgress", Bool(DirectionChangesCostProgress)),
                    ("directionChangeProgressCostFactor", Number(DirectionChangeProgressCostFactor)),
                    ("supportsDeliberateDoubleBack", Bool(SupportsDeliberateDoubleBack)))),
            Module(
                "test.navigation",
                "test.navigation-policy",
                GenericProcedureExecutionHandlers.NavigationCheckPolicy,
                Values(
                    ("usesNavigationChecks", Bool(UsesNavigationChecks)),
                    ("usesPersistentVeer", Bool(UsesPersistentVeer)))),
            Module(
                "test.encounters",
                "test.encounter-cadence",
                GenericProcedureExecutionHandlers.EncounterCheckCadence,
                Values(("cadence", EncounterCadence.ToString()))),
            Module(
                "test.helpers",
                "test.resolution-helpers",
                GenericProcedureExecutionHandlers.DeterministicResolutionHelpers,
                HelperValues(ResolutionHelpers))
        };

        var procedure = new CampaignProcedure
        {
            ProcedureId = Guid.NewGuid(),
            Revision = 1,
            Key = Key,
            Name = Name,
            Modules = modules,
            Overrides = []
        };
        procedure.Validate();
        return procedure;
    }

    public static implicit operator CampaignProcedure(TestProcedureFixture fixture) => fixture.Materialize();
    public static implicit operator GenericProcedureRuntime(TestProcedureFixture fixture) =>
        GenericProcedureRuntime.Bind(fixture.Materialize());

    private static MaterializedProcedureModule Module(
        string moduleKey,
        string mechanicKey,
        string handler,
        IReadOnlyDictionary<string, string> parameters)
    {
        var module = new ProcedureModuleDefinition(
            moduleKey,
            "test",
            moduleKey,
            "Domain runtime test fixture module.",
            "test",
            [],
            [],
            [],
            [],
            [mechanicKey],
            new Dictionary<string, ProcedureParameterDefinition>(),
            new Dictionary<string, string>());
        var mechanic = new MechanicDefinition(
            mechanicKey,
            mechanicKey,
            "Domain runtime test fixture mechanic.",
            [],
            [],
            new Dictionary<string, ProcedureParameterDefinition>(),
            handler,
            [],
            ProcedureAutomationLevel.Assisted,
            1);
        return new MaterializedProcedureModule(module, mechanic, parameters);
    }

    private static IReadOnlyDictionary<string, string> HelperValues(ProcedureResolutionHelperProfile? helpers)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["travel.enabled"] = Bool(helpers?.Travel is not null),
            ["navigation.enabled"] = Bool(helpers?.Navigation is not null),
            ["encounter.enabled"] = Bool(helpers?.Encounter is not null)
        };
        if (helpers?.Travel is { } travel)
        {
            AddRoll(values, "travel", travel.Roll);
            values["travel.distanceFactor"] = Number(travel.DistanceFactorPerRollPoint);
        }
        if (helpers?.Navigation is { } navigation)
        {
            AddRoll(values, "navigation", navigation.CheckRoll);
        }
        if (helpers?.Encounter is { } encounter)
        {
            AddRoll(values, "encounter", encounter.CheckRoll);
            values["encounter.wanderingResults"] = encounter.WanderingResults.Canonical;
            values["encounter.keyedLocationResults"] = encounter.KeyedLocationResults.Canonical;
            values["encounter.timingSlots"] = encounter.TimingSlots.ToString(CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static void AddRoll(IDictionary<string, string> values, string prefix, DiceRollFormula roll)
    {
        values[$"{prefix}.diceCount"] = roll.DiceCount.ToString(CultureInfo.InvariantCulture);
        values[$"{prefix}.dieSides"] = roll.DieSides.ToString(CultureInfo.InvariantCulture);
        values[$"{prefix}.modifier"] = roll.Modifier.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

internal static class TestProcedureProfiles
{
    public static TestProcedureFixture AdvancedContinuous() => new()
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

    public static TestProcedureFixture FixedDistance() => new()
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

    public static TestProcedureFixture HexStep() => new()
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
