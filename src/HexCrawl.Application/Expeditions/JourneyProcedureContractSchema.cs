using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

/// <summary>
/// Phase 12 extends the journey parameter contract without creating a second procedure model.
/// The values and their schema metadata are copied into the materialized CampaignProcedure, so
/// Composer/reference surfaces and runtime focused resolvers all consume the same pinned snapshot.
/// Preset recipes must declare their complete generic journey parameters before materialization;
/// runtime journey resolution never reads preset identity or current catalog defaults.
/// </summary>
public static class JourneyProcedureContractSchema
{
    public static IReadOnlyDictionary<string, ProcedureParameterDefinition> ProcessParameters { get; } =
        new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal)
        {
            ["stageKeys"] = new("key-list", false, "Explicit ordered stage keys supplied by the generic procedure when its stage structure is fixed."),
            ["stageTransitionModel"] = new("enum", true, "Explicit, sequential, or outcome-selected stage transition behavior."),
            ["progressKind"] = new("enum", true, "Numeric or explicit-state progress representation."),
            ["progressUnit"] = new("string", false, "Explicit unit for numeric progress; no conversion is implied."),
            ["allowNegativeProgress"] = new("boolean", true, "Whether numeric process progress may become negative."),
            ["progressFloor"] = new("number", false, "Optional numeric progress floor."),
            ["progressCeiling"] = new("number", false, "Optional numeric progress ceiling."),
            ["roleAssignmentModel"] = new("enum", true, "When role assignments are sampled; Phase 12 supports current-at-resolution."),
            ["intervalIntegrationModel"] = new("enum", true, "Whether completed authoritative watches create process resolution opportunities."),
            ["blocksRelevantTravelWhileResolutionRequired"] = new("boolean", true, "Whether unresolved process work blocks further applicable travel.")
        };

    public static IReadOnlyDictionary<string, ProcedureParameterDefinition> EventParameters { get; } =
        new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal)
        {
            ["triggerSources"] = new("key-list", true, "Explicit generic trigger sources such as process-progress, stage-transition, watch-completed, landmark, explicit, or external."),
            ["linkMode"] = new("enum", true, "Standalone, process-linked, or both."),
            ["requiresResolvedTrigger"] = new("boolean", true, "Whether a trigger creates an evaluation opportunity rather than fabricating event content."),
            ["blocksRelevantTravelWhileResolutionRequired"] = new("boolean", true, "Whether unresolved event work blocks further applicable travel.")
        };

    public static IReadOnlyDictionary<string, ProcedureParameterDefinition> ForModule(string moduleKey) =>
        string.Equals(moduleKey, GenericProcedureCatalog.JourneyProcessModule, StringComparison.Ordinal)
            ? ProcessParameters
            : string.Equals(moduleKey, GenericProcedureCatalog.JourneyEventsModule, StringComparison.Ordinal)
                ? EventParameters
                : Empty;

    public static ProcedureModuleDefinition ExtendModule(ProcedureModuleDefinition module)
    {
        var extras = ForModule(module.Key);
        if (extras.Count == 0) return module;
        var schema = module.ConfigurationSchema.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, definition) in extras) schema[key] = definition;
        return module with { ConfigurationSchema = schema };
    }

    public static MechanicDefinition ExtendMechanic(string moduleKey, MechanicDefinition mechanic)
    {
        var extras = ForModule(moduleKey);
        if (extras.Count == 0) return mechanic;
        var schema = mechanic.ParameterSchema.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, definition) in extras) schema[key] = definition;
        return mechanic with { ParameterSchema = schema };
    }

    public static IReadOnlyDictionary<string, ProcedureParameterDefinition> Merge(MaterializedProcedureModule selected)
    {
        var result = selected.Module.ConfigurationSchema
            .Concat(selected.Mechanic.ParameterSchema)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        foreach (var (key, definition) in ForModule(selected.Module.Key)) result[key] = definition;
        return result;
    }

    private static IReadOnlyDictionary<string, ProcedureParameterDefinition> Empty { get; } =
        new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal);
}
