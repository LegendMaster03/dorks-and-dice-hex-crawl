using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum Phase11PolicySupport
{
    None,
    Supported,
    Unsupported
}

/// <summary>
/// Phase 11 adds one generic module that did not exist in the Phase 3 catalog. It is intentionally
/// kept behavior-oriented and declarative: environment truth is an input, not effect state, and
/// incomplete exposure semantics remain manual/provider-resolved.
/// </summary>
public static class Phase11GenericProcedureCatalog
{
    public const string ExposureModule = "survival.exposure";
    public const string ExposurePolicyMechanic = "survival-exposure-policy";

    public static ProcedureModuleDefinition ExposureModuleDefinition { get; } = new(
        ExposureModule,
        "Survival/resources",
        "Survival exposure",
        "Interprets selected environmental dimensions as survival exposure without owning environment truth or persistent effects.",
        "environment-evaluation",
        ["environment.context"],
        ["effects.transient"],
        [],
        [GenericProcedureCatalog.PersistentEffectsModule],
        [ExposurePolicyMechanic],
        new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal)
        {
            ["dimensions"] = new("key-list", true, "Environment dimensions relevant to this exposure policy."),
            ["evaluationModel"] = new("enum", true, "Automatic, check, threshold, or resolved/manual evaluation model."),
            ["evaluationInterval"] = new("string", true, "Procedure-defined cadence identity; it does not imply a time conversion."),
            ["targetScope"] = new("enum", true, "Participant, party, mount, vehicle, or expedition target scope."),
            ["consequenceModel"] = new("enum", true, "Generic consequence model produced by a resolved exposure occurrence.")
        },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["phase"] = "11" });

    public static MechanicDefinition ExposureMechanicDefinition { get; } = new(
        ExposurePolicyMechanic,
        "Survival exposure policy",
        "Consumes effective environment context and explicit resolved exposure input without inferring effects from environment facts.",
        ["environment.context"],
        ["effects.transient"],
        ExposureModuleDefinition.ConfigurationSchema,
        GenericProcedureExecutionHandlers.DeclarativeContract,
        ["survival", "environment", "effect", "phase-11"],
        ProcedureAutomationLevel.Assisted,
        1,
        [new ProcedureInputRequirement(
            "environment.context",
            ProcedureInputSource.Dm | ProcedureInputSource.OptionalProvider | ProcedureInputSource.ExternalState)]);
}

public sealed record ResourceConsumptionPolicy(
    Phase11PolicySupport Support,
    IReadOnlyList<string> ResourceKinds,
    ExpeditionResourceInventoryModel? InventoryModel,
    string? ConsumptionModel,
    string? ConsumptionInterval,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ResourceConsumptionPolicy None { get; } = new(
        Phase11PolicySupport.None, [], null, null, null, null, null, null, null);
}

public sealed record ForagingPolicy(
    Phase11PolicySupport Support,
    string? ResolutionModel,
    double? TimeCost,
    string? TimeUnit,
    string? MovementTradeoff,
    bool ActivityBacked,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ForagingPolicy None { get; } = new(
        Phase11PolicySupport.None, null, null, null, null, false, null, null, null, null);
}

public sealed record CampingPolicy(
    Phase11PolicySupport Support,
    string? ResolutionModel,
    double? TimeCost,
    string? TimeUnit,
    string? WatchModel,
    bool ActivityBacked,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static CampingPolicy None { get; } = new(
        Phase11PolicySupport.None, null, null, null, null, false, null, null, null, null);
}

public sealed record ForcedTravelPolicy(
    Phase11PolicySupport Support,
    double? NormalTravelLimit,
    string? LimitUnit,
    string? CheckModel,
    string? FailureConsequence,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ForcedTravelPolicy None { get; } = new(
        Phase11PolicySupport.None, null, null, null, null, null, null, null, null);
}

public sealed record ExposurePolicy(
    Phase11PolicySupport Support,
    IReadOnlyList<string> Dimensions,
    string? EvaluationModel,
    string? EvaluationInterval,
    ExpeditionEffectScope? TargetScope,
    string? ConsequenceModel,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ExposurePolicy None { get; } = new(
        Phase11PolicySupport.None, [], null, null, null, null, null, null, null, null);
}

/// <summary>
/// Focused Phase 11 policy projection. Every resolver reads only the exact materialized module in
/// the stored CampaignProcedure. Preset keys, origin metadata, and current catalog recipes are not
/// inputs to runtime behavior.
/// </summary>
public static class Phase11ProcedurePolicyResolver
{
    public static ResourceConsumptionPolicy ResolveResources(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.ResourceConsumptionModule);
        if (selected is null) return ResourceConsumptionPolicy.None;
        if (!SupportedMechanic(selected, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic, out var reason))
        {
            return new(Phase11PolicySupport.Unsupported, ParseKeys(selected, "resourceKinds"), null,
                Value(selected, "consumptionModel"), Value(selected, "consumptionInterval"),
                selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, reason);
        }
        if (!TryRequired(selected, "resourceKinds", out var rawKinds)
            || !TryRequired(selected, "inventoryModel", out var rawInventory)
            || !TryRequired(selected, "consumptionModel", out var consumptionModel)
            || !TryRequired(selected, "consumptionInterval", out var consumptionInterval))
        {
            return new(Phase11PolicySupport.Unsupported, ParseKeys(selected, "resourceKinds"), null,
                Value(selected, "consumptionModel"), Value(selected, "consumptionInterval"),
                selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler,
                "The pinned survival.resources module is missing required Phase 11 parameters.");
        }
        var kinds = SplitKeys(rawKinds);
        if (kinds.Count == 0 || !TryInventoryModel(rawInventory, out var inventoryModel))
        {
            return new(Phase11PolicySupport.Unsupported, kinds, null, consumptionModel, consumptionInterval,
                selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler,
                kinds.Count == 0 ? "The pinned resource policy declares no resource kinds."
                    : $"Inventory model '{rawInventory}' is not supported by Phase 11.");
        }
        return new(Phase11PolicySupport.Supported, kinds, inventoryModel, consumptionModel, consumptionInterval,
            selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, null);
    }

    public static ForagingPolicy ResolveForaging(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.ForagingModule);
        if (selected is null) return ForagingPolicy.None;
        var activityBacked = string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ActivityForagingPolicyMechanic, StringComparison.Ordinal);
        if (!activityBacked && !string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ForagingPolicyMechanic, StringComparison.Ordinal))
        {
            return UnsupportedForaging(selected, false, $"Foraging mechanic '{selected.Mechanic.Key}' is not supported by Phase 11.");
        }
        if (!SupportedDeclarative(selected, out var reason)) return UnsupportedForaging(selected, activityBacked, reason!);
        if (!TryRequired(selected, "resolutionModel", out var resolutionModel)
            || !TryFiniteNonNegative(selected, "timeCost", out var timeCost)
            || !TryRequired(selected, "timeUnit", out var timeUnit)
            || !TryRequired(selected, "movementTradeoff", out var movementTradeoff))
        {
            return UnsupportedForaging(selected, activityBacked, "The pinned foraging module has incomplete or invalid Phase 11 parameters.");
        }
        return new(Phase11PolicySupport.Supported, resolutionModel, timeCost, timeUnit, movementTradeoff,
            activityBacked, selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, null);
    }

    public static CampingPolicy ResolveCamping(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.CampingModule);
        if (selected is null) return CampingPolicy.None;
        var activityBacked = string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ActivityCampingPolicyMechanic, StringComparison.Ordinal);
        if (!activityBacked && !string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.CampingPolicyMechanic, StringComparison.Ordinal))
        {
            return UnsupportedCamping(selected, false, $"Camping mechanic '{selected.Mechanic.Key}' is not supported by Phase 11.");
        }
        if (!SupportedDeclarative(selected, out var reason)) return UnsupportedCamping(selected, activityBacked, reason!);
        if (!TryRequired(selected, "resolutionModel", out var resolutionModel)
            || !TryFiniteNonNegative(selected, "timeCost", out var timeCost)
            || !TryRequired(selected, "timeUnit", out var timeUnit)
            || !TryRequired(selected, "watchModel", out var watchModel))
        {
            return UnsupportedCamping(selected, activityBacked, "The pinned camping module has incomplete or invalid Phase 11 parameters.");
        }
        return new(Phase11PolicySupport.Supported, resolutionModel, timeCost, timeUnit, watchModel,
            activityBacked, selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, null);
    }

    public static ForcedTravelPolicy ResolveForcedTravel(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.ForcedTravelModule);
        if (selected is null) return ForcedTravelPolicy.None;
        if (!SupportedMechanic(selected, GenericProcedureCatalog.ForcedTravelPolicyMechanic, out var reason))
        {
            return UnsupportedForcedTravel(selected, reason!);
        }
        if (!TryFinitePositive(selected, "normalTravelLimit", out var limit)
            || !TryRequired(selected, "limitUnit", out var unit)
            || !TryRequired(selected, "checkModel", out var checkModel)
            || !TryRequired(selected, "failureConsequence", out var failureConsequence))
        {
            return UnsupportedForcedTravel(selected, "The pinned forced-travel module has incomplete or invalid Phase 11 parameters.");
        }
        return new(Phase11PolicySupport.Supported, limit, unit, checkModel, failureConsequence,
            selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, null);
    }

    public static ExposurePolicy ResolveExposure(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, Phase11GenericProcedureCatalog.ExposureModule);
        if (selected is null) return ExposurePolicy.None;
        if (!SupportedMechanic(selected, Phase11GenericProcedureCatalog.ExposurePolicyMechanic, out var reason))
        {
            return UnsupportedExposure(selected, reason!);
        }
        if (!TryRequired(selected, "dimensions", out var dimensionsRaw)
            || !TryRequired(selected, "evaluationModel", out var evaluationModel)
            || !TryRequired(selected, "evaluationInterval", out var evaluationInterval)
            || !TryRequired(selected, "targetScope", out var targetScopeRaw)
            || !TryRequired(selected, "consequenceModel", out var consequenceModel)
            || !TryScope(targetScopeRaw, out var targetScope))
        {
            return UnsupportedExposure(selected, "The pinned exposure module has incomplete or invalid Phase 11 parameters.");
        }
        var dimensions = SplitKeys(dimensionsRaw);
        if (dimensions.Count == 0)
        {
            return UnsupportedExposure(selected, "The pinned exposure module declares no environment dimensions.");
        }
        return new(Phase11PolicySupport.Supported, dimensions, evaluationModel, evaluationInterval, targetScope,
            consequenceModel, selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, null);
    }

    private static MaterializedProcedureModule? Selected(CampaignProcedure procedure, string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return procedure.Modules.SingleOrDefault(value => string.Equals(value.Module.Key, moduleKey, StringComparison.Ordinal));
    }

    private static bool SupportedMechanic(MaterializedProcedureModule selected, string mechanicKey, out string? reason)
    {
        if (!string.Equals(selected.Mechanic.Key, mechanicKey, StringComparison.Ordinal))
        {
            reason = $"Mechanic '{selected.Mechanic.Key}' is not supported by this Phase 11 focused operation.";
            return false;
        }
        return SupportedDeclarative(selected, out reason);
    }

    private static bool SupportedDeclarative(MaterializedProcedureModule selected, out string? reason)
    {
        if (selected.Mechanic.Version != 1)
        {
            reason = $"Mechanic version {selected.Mechanic.Version} is not supported by Phase 11.";
            return false;
        }
        if (!string.Equals(selected.Mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal))
        {
            reason = $"Mechanic handler '{selected.Mechanic.ExecutionHandler}' is not supported by Phase 11.";
            return false;
        }
        reason = null;
        return true;
    }

    private static IReadOnlyList<string> ParseKeys(MaterializedProcedureModule selected, string key) =>
        selected.Parameters.TryGetValue(key, out var value) ? SplitKeys(value) : [];

    private static IReadOnlyList<string> SplitKeys(string value) => value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static string? Value(MaterializedProcedureModule selected, string key) =>
        selected.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool TryRequired(MaterializedProcedureModule selected, string key, out string value)
    {
        var raw = Value(selected, key);
        value = raw ?? string.Empty;
        return raw is not null;
    }

    private static bool TryFiniteNonNegative(MaterializedProcedureModule selected, string key, out double value) =>
        TryFinite(selected, key, out value) && value >= 0;

    private static bool TryFinitePositive(MaterializedProcedureModule selected, string key, out double value) =>
        TryFinite(selected, key, out value) && value > 0;

    private static bool TryFinite(MaterializedProcedureModule selected, string key, out double value)
    {
        value = default;
        return selected.Parameters.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private static bool TryInventoryModel(string value, out ExpeditionResourceInventoryModel model)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "counted": model = ExpeditionResourceInventoryModel.Counted; return true;
            case "abstract": model = ExpeditionResourceInventoryModel.Abstract; return true;
            case "supply-die": model = ExpeditionResourceInventoryModel.SupplyDie; return true;
            case "external":
            case "manual":
            case "external/manual": model = ExpeditionResourceInventoryModel.ExternalManual; return true;
            default: model = default; return false;
        }
    }

    private static bool TryScope(string value, out ExpeditionEffectScope scope)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "participant": scope = ExpeditionEffectScope.Participant; return true;
            case "party": scope = ExpeditionEffectScope.Party; return true;
            case "mount": scope = ExpeditionEffectScope.Mount; return true;
            case "vehicle": scope = ExpeditionEffectScope.Vehicle; return true;
            case "expedition": scope = ExpeditionEffectScope.Expedition; return true;
            default: scope = default; return false;
        }
    }

    private static ForagingPolicy UnsupportedForaging(MaterializedProcedureModule selected, bool activityBacked, string reason) => new(
        Phase11PolicySupport.Unsupported, Value(selected, "resolutionModel"),
        TryFiniteNonNegative(selected, "timeCost", out var cost) ? cost : null,
        Value(selected, "timeUnit"), Value(selected, "movementTradeoff"), activityBacked,
        selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, reason);

    private static CampingPolicy UnsupportedCamping(MaterializedProcedureModule selected, bool activityBacked, string reason) => new(
        Phase11PolicySupport.Unsupported, Value(selected, "resolutionModel"),
        TryFiniteNonNegative(selected, "timeCost", out var cost) ? cost : null,
        Value(selected, "timeUnit"), Value(selected, "watchModel"), activityBacked,
        selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, reason);

    private static ForcedTravelPolicy UnsupportedForcedTravel(MaterializedProcedureModule selected, string reason) => new(
        Phase11PolicySupport.Unsupported,
        TryFinitePositive(selected, "normalTravelLimit", out var limit) ? limit : null,
        Value(selected, "limitUnit"), Value(selected, "checkModel"), Value(selected, "failureConsequence"),
        selected.Mechanic.Key, selected.Mechanic.Version, selected.Mechanic.ExecutionHandler, reason);

    private static ExposurePolicy UnsupportedExposure(MaterializedProcedureModule selected, string reason) => new(
        Phase11PolicySupport.Unsupported, ParseKeys(selected, "dimensions"), Value(selected, "evaluationModel"),
        Value(selected, "evaluationInterval"),
        selected.Parameters.TryGetValue("targetScope", out var raw) && TryScope(raw, out var scope) ? scope : null,
        Value(selected, "consequenceModel"), selected.Mechanic.Key, selected.Mechanic.Version,
        selected.Mechanic.ExecutionHandler, reason);
}
