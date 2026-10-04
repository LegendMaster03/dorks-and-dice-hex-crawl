using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum JourneyPolicySupport
{
    None,
    Supported,
    Unsupported
}

public enum JourneyEventLinkMode
{
    Standalone,
    ProcessLinked,
    Both
}

public sealed record JourneyProcessPolicy(
    JourneyPolicySupport Support,
    string? StageModel,
    IReadOnlyList<string> StageKeys,
    JourneyStageTransitionModel? StageTransitionModel,
    string? ProgressModel,
    JourneyProgressValueKind? ProgressKind,
    string? ProgressUnit,
    bool AllowNegativeProgress,
    double? ProgressFloor,
    double? ProgressCeiling,
    string? CompletionModel,
    bool RoleDriven,
    JourneyRoleAssignmentModel? RoleAssignmentModel,
    JourneyIntervalIntegrationModel? IntervalIntegrationModel,
    bool BlocksRelevantTravelWhileResolutionRequired,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static JourneyProcessPolicy None { get; } = new(
        JourneyPolicySupport.None, null, [], null, null, null, null, false, null, null,
        null, false, null, null, false, null, null, null, null);

    public JourneyProcessExecutionSnapshot ToExecutionSnapshot()
    {
        if (Support != JourneyPolicySupport.Supported
            || StageModel is null
            || StageTransitionModel is null
            || ProgressModel is null
            || ProgressKind is null
            || CompletionModel is null
            || RoleAssignmentModel is null
            || IntervalIntegrationModel is null
            || MechanicKey is null
            || MechanicVersion is null
            || ExecutionHandler is null)
        {
            throw new InvalidOperationException("Only a supported exact-pinned journey-process policy can create an execution snapshot.");
        }

        var snapshot = new JourneyProcessExecutionSnapshot
        {
            StageModel = StageModel,
            StageTransitionModel = StageTransitionModel.Value,
            ProgressModel = ProgressModel,
            ProgressKind = ProgressKind.Value,
            ProgressUnit = ProgressUnit,
            AllowNegativeProgress = AllowNegativeProgress,
            ProgressFloor = ProgressFloor,
            ProgressCeiling = ProgressCeiling,
            CompletionModel = CompletionModel,
            RoleDriven = RoleDriven,
            RoleAssignmentModel = RoleAssignmentModel.Value,
            IntervalIntegrationModel = IntervalIntegrationModel.Value,
            BlocksRelevantTravelWhileResolutionRequired = BlocksRelevantTravelWhileResolutionRequired,
            MechanicKey = MechanicKey,
            MechanicVersion = MechanicVersion.Value,
            ExecutionHandler = ExecutionHandler
        };
        snapshot.Validate();
        return snapshot;
    }
}

public sealed record JourneyEventPolicy(
    JourneyPolicySupport Support,
    string? TriggerModel,
    IReadOnlyList<JourneyEventTriggerKind> TriggerSources,
    JourneyEventLinkMode? LinkMode,
    string? TargetingModel,
    string? TerrainInfluence,
    string? ConsequenceModel,
    bool RequiresResolvedTrigger,
    bool BlocksRelevantTravelWhileResolutionRequired,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static JourneyEventPolicy None { get; } = new(
        JourneyPolicySupport.None, null, [], null, null, null, null, false, false,
        null, null, null, null);

    public bool Supports(JourneyEventTriggerKind trigger) => TriggerSources.Contains(trigger);

    public bool SupportsTarget(JourneyEventTargetKind target, bool allowUnresolved)
    {
        if (allowUnresolved && target == JourneyEventTargetKind.Unresolved) return true;
        return TargetingModel switch
        {
            "travel-role" => target == JourneyEventTargetKind.Role,
            "explicit-target" => target != JourneyEventTargetKind.Unresolved,
            _ => false
        };
    }
}

/// <summary>
/// Focused Phase 12 projections over the exact materialized journey.process and journey.events
/// modules stored on an expedition CampaignProcedure. Preset identity, procedure origin metadata,
/// and current catalog recipes are deliberately not execution inputs.
/// </summary>
public static class JourneyProcedurePolicyResolver
{
    public static JourneyProcessPolicy ResolveProcess(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.JourneyProcessModule);
        if (selected is null) return JourneyProcessPolicy.None;
        if (!SupportedMechanic(selected, GenericProcedureCatalog.MultiStageExpeditionProcessMechanic, out var reason))
        {
            return UnsupportedProcess(selected, reason!);
        }

        if (!Required(selected, "stageModel", out var stageModel)
            || !Required(selected, "stageTransitionModel", out var transitionRaw)
            || !TryTransition(transitionRaw, out var transition)
            || !Required(selected, "progressModel", out var progressModel)
            || !Required(selected, "progressKind", out var progressKindRaw)
            || !TryProgressKind(progressKindRaw, out var progressKind)
            || !Required(selected, "completionModel", out var completionModel)
            || !IsSupportedCompletionModel(completionModel)
            || !TryBoolean(selected, "roleDriven", out var roleDriven)
            || !Required(selected, "roleAssignmentModel", out var roleRaw)
            || !TryRoleAssignment(roleRaw, out var roleAssignment)
            || !Required(selected, "intervalIntegrationModel", out var intervalRaw)
            || !TryIntervalIntegration(intervalRaw, out var intervalIntegration)
            || !TryBoolean(selected, "blocksRelevantTravelWhileResolutionRequired", out var blocksTravel)
            || !TryBoolean(selected, "allowNegativeProgress", out var allowNegative))
        {
            return UnsupportedProcess(selected,
                "The exact pinned journey.process module is missing required Phase 12 execution parameters or contains an unsupported generic value.");
        }

        var progressUnit = Value(selected, "progressUnit");
        if (progressKind == JourneyProgressValueKind.Numeric && string.IsNullOrWhiteSpace(progressUnit))
        {
            return UnsupportedProcess(selected, "Numeric journey progress requires an explicit progressUnit in the pinned procedure.");
        }
        if (progressKind == JourneyProgressValueKind.ExplicitState && progressUnit is not null)
        {
            return UnsupportedProcess(selected, "Explicit-state journey progress can not declare a numeric progressUnit.");
        }

        if (!TryOptionalFinite(selected, "progressFloor", out var floor)
            || !TryOptionalFinite(selected, "progressCeiling", out var ceiling)
            || floor.HasValue && ceiling.HasValue && floor > ceiling
            || !allowNegative && floor is < 0)
        {
            return UnsupportedProcess(selected, "Journey progress bounds in the pinned procedure are invalid.");
        }

        var stageKeys = Keys(selected, "stageKeys");
        return new JourneyProcessPolicy(
            JourneyPolicySupport.Supported,
            stageModel,
            stageKeys,
            transition,
            progressModel,
            progressKind,
            progressUnit,
            allowNegative,
            floor,
            ceiling,
            completionModel,
            roleDriven,
            roleAssignment,
            intervalIntegration,
            blocksTravel,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    public static JourneyEventPolicy ResolveEvents(CampaignProcedure procedure)
    {
        var selected = Selected(procedure, GenericProcedureCatalog.JourneyEventsModule);
        if (selected is null) return JourneyEventPolicy.None;
        var supportedMechanic = string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.JourneyEventPolicyMechanic, StringComparison.Ordinal)
            || string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ProgressTriggeredJourneyEventPolicyMechanic, StringComparison.Ordinal);
        if (!supportedMechanic)
        {
            return UnsupportedEvent(selected, $"Journey event mechanic '{selected.Mechanic.Key}' is not supported by Phase 12.");
        }
        if (!SupportedDeclarative(selected, out var reason)) return UnsupportedEvent(selected, reason!);

        if (!Required(selected, "triggerModel", out var triggerModel)
            || !Required(selected, "triggerSources", out var triggerSourcesRaw)
            || !TryTriggerSources(triggerSourcesRaw, out var triggerSources)
            || !Required(selected, "linkMode", out var linkModeRaw)
            || !TryLinkMode(linkModeRaw, out var linkMode)
            || !Required(selected, "targetingModel", out var targetingModel)
            || !IsSupportedTargetingModel(targetingModel)
            || !Required(selected, "terrainInfluence", out var terrainInfluence)
            || !Required(selected, "consequenceModel", out var consequenceModel)
            || !TryBoolean(selected, "requiresResolvedTrigger", out var requiresResolvedTrigger)
            || !TryBoolean(selected, "blocksRelevantTravelWhileResolutionRequired", out var blocksTravel))
        {
            return UnsupportedEvent(selected,
                "The exact pinned journey.events module is missing required Phase 12 execution parameters or contains an unsupported generic value.");
        }
        if (triggerSources.Count == 0)
        {
            return UnsupportedEvent(selected, "The exact pinned journey.events policy declares no trigger sources.");
        }
        if (string.Equals(selected.Mechanic.Key, GenericProcedureCatalog.ProgressTriggeredJourneyEventPolicyMechanic, StringComparison.Ordinal)
            && !triggerSources.Contains(JourneyEventTriggerKind.ProcessProgress))
        {
            return UnsupportedEvent(selected,
                "The selected progress-triggered journey-event mechanic must explicitly declare process-progress as a trigger source.");
        }

        return new JourneyEventPolicy(
            JourneyPolicySupport.Supported,
            triggerModel,
            triggerSources,
            linkMode,
            targetingModel,
            terrainInfluence,
            consequenceModel,
            requiresResolvedTrigger,
            blocksTravel,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    private static MaterializedProcedureModule? Selected(CampaignProcedure procedure, string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        procedure.Validate();
        return procedure.Modules.SingleOrDefault(value => string.Equals(value.Module.Key, moduleKey, StringComparison.Ordinal));
    }

    private static bool SupportedMechanic(MaterializedProcedureModule selected, string mechanicKey, out string? reason)
    {
        if (!string.Equals(selected.Mechanic.Key, mechanicKey, StringComparison.Ordinal))
        {
            reason = $"Mechanic '{selected.Mechanic.Key}' is not supported by this Phase 12 focused operation.";
            return false;
        }
        return SupportedDeclarative(selected, out reason);
    }

    private static bool SupportedDeclarative(MaterializedProcedureModule selected, out string? reason)
    {
        if (selected.Mechanic.Version != 1)
        {
            reason = $"Journey mechanic version {selected.Mechanic.Version} is not supported by Phase 12.";
            return false;
        }
        if (!string.Equals(selected.Mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal))
        {
            reason = $"Journey mechanic handler '{selected.Mechanic.ExecutionHandler}' is not supported by Phase 12.";
            return false;
        }
        reason = null;
        return true;
    }

    private static JourneyProcessPolicy UnsupportedProcess(MaterializedProcedureModule selected, string reason) => new(
        JourneyPolicySupport.Unsupported,
        Value(selected, "stageModel"),
        Keys(selected, "stageKeys"),
        null,
        Value(selected, "progressModel"),
        null,
        Value(selected, "progressUnit"),
        false,
        null,
        null,
        Value(selected, "completionModel"),
        false,
        null,
        null,
        false,
        selected.Mechanic.Key,
        selected.Mechanic.Version,
        selected.Mechanic.ExecutionHandler,
        reason);

    private static JourneyEventPolicy UnsupportedEvent(MaterializedProcedureModule selected, string reason) => new(
        JourneyPolicySupport.Unsupported,
        Value(selected, "triggerModel"),
        [],
        null,
        Value(selected, "targetingModel"),
        Value(selected, "terrainInfluence"),
        Value(selected, "consequenceModel"),
        false,
        false,
        selected.Mechanic.Key,
        selected.Mechanic.Version,
        selected.Mechanic.ExecutionHandler,
        reason);

    private static string? Value(MaterializedProcedureModule selected, string key) =>
        selected.Parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static bool Required(MaterializedProcedureModule selected, string key, out string value)
    {
        value = Value(selected, key) ?? string.Empty;
        return value.Length > 0;
    }

    private static IReadOnlyList<string> Keys(MaterializedProcedureModule selected, string key) =>
        Value(selected, key)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray() ?? [];

    private static bool TryBoolean(MaterializedProcedureModule selected, string key, out bool value) =>
        bool.TryParse(Value(selected, key), out value);

    private static bool TryOptionalFinite(MaterializedProcedureModule selected, string key, out double? value)
    {
        value = null;
        var raw = Value(selected, key);
        if (raw is null) return true;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static bool TryTransition(string value, out JourneyStageTransitionModel model)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "explicit": model = JourneyStageTransitionModel.Explicit; return true;
            case "sequential": model = JourneyStageTransitionModel.Sequential; return true;
            case "outcome-selected": model = JourneyStageTransitionModel.OutcomeSelected; return true;
            default: model = default; return false;
        }
    }

    private static bool TryProgressKind(string value, out JourneyProgressValueKind kind)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "numeric": kind = JourneyProgressValueKind.Numeric; return true;
            case "explicit-state": kind = JourneyProgressValueKind.ExplicitState; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryRoleAssignment(string value, out JourneyRoleAssignmentModel model)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "current-at-resolution": model = JourneyRoleAssignmentModel.CurrentAtResolution; return true;
            default: model = default; return false;
        }
    }

    private static bool TryIntervalIntegration(string value, out JourneyIntervalIntegrationModel model)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none": model = JourneyIntervalIntegrationModel.None; return true;
            case "completed-watch-resolution-opportunity": model = JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity; return true;
            default: model = default; return false;
        }
    }

    private static bool TryLinkMode(string value, out JourneyEventLinkMode mode)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "standalone": mode = JourneyEventLinkMode.Standalone; return true;
            case "process-linked": mode = JourneyEventLinkMode.ProcessLinked; return true;
            case "both": mode = JourneyEventLinkMode.Both; return true;
            default: mode = default; return false;
        }
    }

    private static bool IsSupportedCompletionModel(string value) =>
        value is "explicit-completion" or "reach-destination" or "final-stage-completion";

    private static bool IsSupportedTargetingModel(string value) =>
        value is "travel-role" or "explicit-target";

    private static bool TryTriggerSources(string value, out IReadOnlyList<JourneyEventTriggerKind> sources)
    {
        var result = new List<JourneyEventTriggerKind>();
        foreach (var key in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parsed = key.ToLowerInvariant() switch
            {
                "explicit" or "manual" => JourneyEventTriggerKind.Explicit,
                "process-progress" => JourneyEventTriggerKind.ProcessProgress,
                "stage-transition" => JourneyEventTriggerKind.StageTransition,
                "watch-completed" => JourneyEventTriggerKind.WatchCompleted,
                "landmark" => JourneyEventTriggerKind.Landmark,
                "external" => JourneyEventTriggerKind.External,
                _ => (JourneyEventTriggerKind?)null
            };
            if (!parsed.HasValue)
            {
                sources = [];
                return false;
            }
            if (!result.Contains(parsed.Value)) result.Add(parsed.Value);
        }
        sources = result;
        return true;
    }
}
