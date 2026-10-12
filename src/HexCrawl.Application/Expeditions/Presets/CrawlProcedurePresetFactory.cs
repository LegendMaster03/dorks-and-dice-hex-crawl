using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

internal static class CrawlProcedurePresetFactory
{
    internal static CrawlProcedurePresetDefinition Preset(
        string tilingDsSymbol,
        string presetKey,
        string displayName,
        string description,
        string procedureKey,
        string procedureName,
        IReadOnlyList<ProcedureModuleRecipe> modules,
        string? attribution = null,
        string? disclaimer = null,
        string category = "Familiar procedures") =>
        new(
            presetKey,
            displayName,
            description,
            2,
            new GenericProcedurePresetRecipe(procedureKey, procedureName, modules.ToArray())
            {
                TilingDsSymbol = tilingDsSymbol
            },
            attribution,
            disclaimer,
            category);

    internal static IReadOnlyList<ProcedureModuleRecipe> ExecutableCore(
        TimeSpan interval,
        TravelResolutionMode travelResolution,
        ActualDistanceResolutionMode actualDistanceResolution,
        EncounterCheckCadence encounterCadence,
        bool usesNavigationChecks,
        bool usesPersistentVeer,
        double neighborCenterDistanceFactor,
        bool supportsDeliberateDoubleBack,
        ProcedureResolutionHelperProfile? resolutionHelpers = null) =>
    [
        NativeTime(interval),
        new ProcedureModuleRecipe(
            GenericProcedureCatalog.MovementResolutionModule,
            GenericProcedureCatalog.MovementResolutionPolicyMechanic,
            2,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["travelResolution"] = travelResolution.ToString(),
                ["actualDistanceResolution"] = actualDistanceResolution.ToString()
            }),
        Structural(GenericProcedureCatalog.SpatialTurnCostModule,
            GenericProcedureCatalog.SpatialTurnCostPolicyMechanic,
            ("neighborCenterDistanceFactor", Number(neighborCenterDistanceFactor)),
            ("supportsDeliberateDoubleBack", Bool(supportsDeliberateDoubleBack))),
        Structural(GenericProcedureCatalog.NavigationModule, GenericProcedureCatalog.NavigationCheckPolicyMechanic,
            ("usesNavigationChecks", Bool(usesNavigationChecks)),
            ("usesPersistentVeer", Bool(usesPersistentVeer))),
        NativeEncounterCadence(encounterCadence),
        new ProcedureModuleRecipe(
            GenericProcedureCatalog.ResolutionHelpersModule,
            GenericProcedureCatalog.DeterministicResolutionHelpersMechanic,
            1,
            HelperValues(resolutionHelpers))
    ];

    internal static ProcedureModuleRecipe NativeTime(TimeSpan interval) =>
        Structural(
            GenericProcedureCatalog.TimeIntervalModule,
            GenericProcedureCatalog.FixedIntervalDurationMechanic,
            ("durationTicks", interval.Ticks.ToString(CultureInfo.InvariantCulture)));

    internal static ProcedureModuleRecipe NativeEncounterCadence(EncounterCheckCadence cadence) =>
        Structural(
            GenericProcedureCatalog.EncounterCadenceModule,
            GenericProcedureCatalog.EncounterCheckCadenceMechanic,
            ("cadence", cadence.ToString()));

    internal static ProcedureModuleRecipe Structural(
        string moduleKey,
        string mechanicKey,
        params (string Key, string Value)[] parameters) =>
        new(
            moduleKey,
            mechanicKey,
            1,
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));

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

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
