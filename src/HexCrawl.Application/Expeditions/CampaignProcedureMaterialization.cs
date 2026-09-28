using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureModuleRecipe(
    string ModuleKey,
    string MechanicKey,
    int MechanicVersion,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record GenericProcedurePresetRecipe(
    string DefaultProcedureKey,
    string DefaultProcedureName,
    IReadOnlyList<ProcedureModuleRecipe> ModuleSelections);

public sealed record MaterializedCampaignProcedure(
    CampaignProcedure Procedure,
    CrawlProcedureProfile CompatibilityProfile,
    ProcedureOriginMetadata? Origin);

public static class CampaignProcedureMaterializer
{
    public static MaterializedCampaignProcedure Materialize(
        CrawlProcedurePresetDefinition preset,
        CrawlProcedureProfile? customizedProcedure = null,
        Guid? procedureId = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(preset.PresetKey)
            || string.IsNullOrWhiteSpace(preset.DisplayName)
            || preset.PresetRevision <= 0)
        {
            throw new InvalidOperationException("A crawl procedure preset requires valid identity metadata before materialization.");
        }
        ArgumentNullException.ThrowIfNull(preset.Recipe);

        CampaignProcedure procedure;
        if (customizedProcedure is not null)
        {
            customizedProcedure.Validate();
            procedure = CampaignProcedureCompatibilityProjector.Capture(customizedProcedure, procedureId);
        }
        else
        {
            var modules = preset.Recipe.ModuleSelections.Select(selection =>
            {
                var module = GenericProcedureCatalog.ResolveModule(selection.ModuleKey);
                var mechanic = GenericProcedureCatalog.ResolveMechanic(selection.MechanicKey, selection.MechanicVersion);
                return new MaterializedProcedureModule(
                    module with { },
                    mechanic with { },
                    Copy(selection.Parameters));
            }).ToArray();
            procedure = new CampaignProcedure
            {
                ProcedureId = procedureId ?? Guid.NewGuid(),
                Revision = 1,
                Key = preset.Recipe.DefaultProcedureKey,
                Name = preset.Recipe.DefaultProcedureName,
                Modules = modules,
                Overrides = []
            };
            procedure.Validate();
        }

        var profile = CampaignProcedureCompatibilityProjector.Project(procedure);
        return new MaterializedCampaignProcedure(procedure, profile, preset.Origin);
    }

    public static CampaignProcedure CreateRevision(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(overrides);
        current.Validate();
        var modules = current.Modules
            .Select(module => module with
            {
                Module = module.Module with { },
                Mechanic = module.Mechanic with { },
                Parameters = Copy(module.Parameters)
            })
            .ToDictionary(module => module.Module.Key, StringComparer.Ordinal);

        foreach (var value in overrides)
        {
            value.Validate();
            if (!modules.TryGetValue(value.ModuleKey, out var selected))
            {
                throw new InvalidOperationException($"Campaign override '{value.OverrideId}' targets unknown module '{value.ModuleKey}'.");
            }

            var mechanic = selected.Mechanic;
            if (!string.IsNullOrWhiteSpace(value.ReplacementMechanicKey))
            {
                mechanic = GenericProcedureCatalog.ResolveMechanic(value.ReplacementMechanicKey, value.ReplacementMechanicVersion);
            }

            var parameters = Copy(selected.Parameters);
            foreach (var parameter in value.Parameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
            modules[value.ModuleKey] = new MaterializedProcedureModule(selected.Module with { }, mechanic with { }, parameters);
        }

        var revision = current with
        {
            Revision = checked(current.Revision + 1),
            Modules = current.Modules.Select(module => modules[module.Module.Key]).ToArray(),
            Overrides = current.Overrides.Concat(overrides).ToArray()
        };
        revision.Validate();
        _ = CampaignProcedureCompatibilityProjector.Project(revision);
        return revision;
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> values) =>
        values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
}

public static class CampaignProcedureCompatibilityProjector
{
    public static CampaignProcedure Capture(CrawlProcedureProfile profile, Guid? procedureId = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var modules = new[]
        {
            Selection(GenericProcedureCatalog.TimeIntervalModule, GenericProcedureCatalog.FixedIntervalDurationMechanic,
                Values(("durationTicks", profile.WatchLength.Ticks.ToString(CultureInfo.InvariantCulture)))),
            Selection(GenericProcedureCatalog.MovementResolutionModule, GenericProcedureCatalog.MovementResolutionPolicyMechanic,
                Values(
                    ("travelResolution", profile.TravelResolution.ToString()),
                    ("actualDistanceResolution", profile.ActualDistanceResolution.ToString()),
                    ("tracksIntraHexProgress", Bool(profile.TracksIntraHexProgress)))),
            Selection(GenericProcedureCatalog.HexProgressModule, GenericProcedureCatalog.HexProgressPolicyMechanic,
                Values(
                    ("startingExitProgressFactor", Number(profile.StartingExitProgressFactor)),
                    ("nearExitProgressFactor", Number(profile.NearExitProgressFactor)),
                    ("farExitProgressFactor", Number(profile.FarExitProgressFactor)),
                    ("backExitProgressFactor", Number(profile.BackExitProgressFactor)),
                    ("directionChangesCostProgress", Bool(profile.DirectionChangesCostProgress)),
                    ("directionChangeProgressCostFactor", Number(profile.DirectionChangeProgressCostFactor)),
                    ("supportsDeliberateDoubleBack", Bool(profile.SupportsDeliberateDoubleBack)))),
            Selection(GenericProcedureCatalog.NavigationModule, GenericProcedureCatalog.NavigationCheckPolicyMechanic,
                Values(
                    ("usesNavigationChecks", Bool(profile.UsesNavigationChecks)),
                    ("usesPersistentVeer", Bool(profile.UsesPersistentVeer)))),
            Selection(GenericProcedureCatalog.EncounterCadenceModule, GenericProcedureCatalog.EncounterCheckCadenceMechanic,
                Values(("cadence", profile.EncounterCadence.ToString()))),
            Selection(GenericProcedureCatalog.ResolutionHelpersModule, GenericProcedureCatalog.DeterministicResolutionHelpersMechanic,
                HelperValues(profile.ResolutionHelpers))
        };
        var procedure = new CampaignProcedure
        {
            ProcedureId = procedureId ?? Guid.NewGuid(),
            Revision = 1,
            Key = profile.Key,
            Name = profile.Name,
            Modules = modules,
            Overrides = []
        };
        procedure.Validate();
        return procedure;
    }

    public static GenericProcedurePresetRecipe ToRecipe(CrawlProcedureProfile profile)
    {
        var captured = Capture(profile);
        return new GenericProcedurePresetRecipe(
            captured.Key,
            captured.Name,
            captured.Modules.Select(module => new ProcedureModuleRecipe(
                module.Module.Key,
                module.Mechanic.Key,
                module.Mechanic.Version,
                module.Parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal))).ToArray());
    }

    public static CrawlProcedureProfile Project(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        procedure.Validate();
        var time = Module(procedure, GenericProcedureCatalog.TimeIntervalModule);
        var movement = Module(procedure, GenericProcedureCatalog.MovementResolutionModule);
        var progress = Module(procedure, GenericProcedureCatalog.HexProgressModule);
        var navigation = Module(procedure, GenericProcedureCatalog.NavigationModule);
        var encounter = Module(procedure, GenericProcedureCatalog.EncounterCadenceModule);
        var helpers = Module(procedure, GenericProcedureCatalog.ResolutionHelpersModule);

        RequireHandler(time, "crawl-profile.watch-length");
        RequireHandler(movement, "crawl-profile.movement-resolution");
        RequireHandler(progress, "crawl-profile.hex-progress");
        RequireHandler(navigation, "crawl-profile.navigation");
        RequireHandler(encounter, "crawl-profile.encounter-cadence");
        RequireHandler(helpers, "crawl-profile.resolution-helpers");

        var profile = new CrawlProcedureProfile
        {
            Key = procedure.Key,
            Name = procedure.Name,
            WatchLength = TimeSpan.FromTicks(Long(time, "durationTicks")),
            TravelResolution = EnumValue<TravelResolutionMode>(movement, "travelResolution"),
            ActualDistanceResolution = EnumValue<ActualDistanceResolutionMode>(movement, "actualDistanceResolution"),
            TracksIntraHexProgress = Boolean(movement, "tracksIntraHexProgress"),
            StartingExitProgressFactor = Double(progress, "startingExitProgressFactor"),
            NearExitProgressFactor = Double(progress, "nearExitProgressFactor"),
            FarExitProgressFactor = Double(progress, "farExitProgressFactor"),
            BackExitProgressFactor = Double(progress, "backExitProgressFactor"),
            DirectionChangesCostProgress = Boolean(progress, "directionChangesCostProgress"),
            DirectionChangeProgressCostFactor = Double(progress, "directionChangeProgressCostFactor"),
            SupportsDeliberateDoubleBack = Boolean(progress, "supportsDeliberateDoubleBack"),
            UsesNavigationChecks = Boolean(navigation, "usesNavigationChecks"),
            UsesPersistentVeer = Boolean(navigation, "usesPersistentVeer"),
            EncounterCadence = EnumValue<EncounterCheckCadence>(encounter, "cadence"),
            ResolutionHelpers = ProjectHelpers(helpers.Parameters)
        };
        profile.Validate();
        return profile;
    }

    private static MaterializedProcedureModule Selection(
        string moduleKey,
        string mechanicKey,
        IReadOnlyDictionary<string, string> parameters) =>
        new(
            GenericProcedureCatalog.ResolveModule(moduleKey) with { },
            GenericProcedureCatalog.ResolveMechanic(mechanicKey) with { },
            parameters);

    private static MaterializedProcedureModule Module(CampaignProcedure procedure, string key) =>
        procedure.Modules.SingleOrDefault(value => string.Equals(value.Module.Key, key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Campaign procedure does not contain required compatibility module '{key}'.");

    private static void RequireHandler(MaterializedProcedureModule module, string handler)
    {
        if (!string.Equals(module.Mechanic.ExecutionHandler, handler, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Module '{module.Module.Key}' uses unsupported compatibility handler '{module.Mechanic.ExecutionHandler}'. The materialized data remains preserved but can not be projected by this runtime version.");
        }
    }

    private static ProcedureResolutionHelperProfile? ProjectHelpers(IReadOnlyDictionary<string, string> values)
    {
        var travel = Boolean(values, "travel.enabled")
            ? new TravelResolutionHelperProfile(
                new DiceRollFormula(Int(values, "travel.diceCount"), Int(values, "travel.dieSides"), Int(values, "travel.modifier")),
                Double(values, "travel.distanceFactor"))
            : null;
        var navigation = Boolean(values, "navigation.enabled")
            ? new NavigationResolutionHelperProfile(
                new DiceRollFormula(Int(values, "navigation.diceCount"), Int(values, "navigation.dieSides"), Int(values, "navigation.modifier")))
            : null;
        var encounter = Boolean(values, "encounter.enabled")
            ? new EncounterResolutionHelperProfile(
                new DiceRollFormula(Int(values, "encounter.diceCount"), Int(values, "encounter.dieSides"), Int(values, "encounter.modifier")),
                new DiceRollResultSet(Value(values, "encounter.wanderingResults")),
                new DiceRollResultSet(Value(values, "encounter.keyedLocationResults")),
                Int(values, "encounter.timingSlots"))
            : null;
        return travel is null && navigation is null && encounter is null
            ? null
            : new ProcedureResolutionHelperProfile(travel, navigation, encounter);
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

    private static string Value(MaterializedProcedureModule module, string key) => Value(module.Parameters, key);

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure compatibility projection requires parameter '{key}'.");

    private static bool Boolean(MaterializedProcedureModule module, string key) => Boolean(module.Parameters, key);
    private static bool Boolean(IReadOnlyDictionary<string, string> values, string key) =>
        bool.TryParse(Value(values, key), out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid boolean.");

    private static int Int(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(Value(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid integer.");

    private static long Long(MaterializedProcedureModule module, string key) =>
        long.TryParse(Value(module, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid long integer.");

    private static double Double(MaterializedProcedureModule module, string key) => Double(module.Parameters, key);
    private static double Double(IReadOnlyDictionary<string, string> values, string key) =>
        double.TryParse(Value(values, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid number.");

    private static T EnumValue<T>(MaterializedProcedureModule module, string key) where T : struct, Enum =>
        Enum.TryParse<T>(Value(module, key), true, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a supported {typeof(T).Name} value.");
}
