using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Modules.Procedures;

public sealed record ProcedureComposerDraftRequest(
    string? PresetKey,
    Guid? ProcedureId,
    int? Revision,
    IReadOnlyList<ProcedureComposerModuleSelectionRequest>? ModuleSelections,
    IReadOnlyList<ProcedureComposerOverrideRequest>? Overrides);

public sealed record ProcedureComposerCreateRequest(
    string? PresetKey,
    Guid? CampaignId,
    IReadOnlyList<ProcedureComposerModuleSelectionRequest>? ModuleSelections,
    IReadOnlyList<ProcedureComposerOverrideRequest>? Overrides);

public sealed record ProcedureComposerRevisionRequest(
    int ExpectedRevision,
    IReadOnlyList<ProcedureComposerModuleSelectionRequest>? ModuleSelections,
    IReadOnlyList<ProcedureComposerOverrideRequest>? Overrides);

public sealed record ProcedureComposerModuleSelectionRequest(string ModuleKey, bool Included)
{
    public ProcedureModuleSelection ToDomain() => new(ModuleKey, Included);
}

public sealed record ProcedureComposerOverrideRequest(
    string? OverrideId,
    string ModuleKey,
    string? ReplacementMechanicKey,
    int? ReplacementMechanicVersion,
    IReadOnlyDictionary<string, string>? Parameters,
    string? Note)
{
    public CampaignProcedureOverride ToDomain() => new(
        string.IsNullOrWhiteSpace(OverrideId)
            ? $"composer-{Guid.NewGuid():N}"
            : OverrideId.Trim(),
        ModuleKey,
        ReplacementMechanicKey,
        ReplacementMechanicVersion,
        Parameters ?? new Dictionary<string, string>(StringComparer.Ordinal),
        Note);
}

public sealed record ProcedureParameterDefinitionContract(
    string Type,
    bool Required,
    string? Description,
    string? DefaultValue)
{
    public static ProcedureParameterDefinitionContract From(ProcedureParameterDefinition value) =>
        new(value.Type, value.Required, value.Description, value.DefaultValue);
}

public sealed record ProcedureInputContract(
    string InputKey,
    IReadOnlyList<string> AllowedSources);

public sealed record ProcedureMechanicComposerContract(
    string Key,
    string DisplayName,
    string Description,
    int Version,
    string ExecutionHandler,
    ProcedureAutomationLevel AutomationLevel,
    string ExecutionSupport,
    IReadOnlyList<ProcedureInputContract> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyDictionary<string, ProcedureParameterDefinitionContract> ParameterSchema,
    IReadOnlyList<string> CompatibilityTags)
{
    public static ProcedureMechanicComposerContract From(MechanicDefinition mechanic) => new(
        mechanic.Key,
        mechanic.DisplayName,
        mechanic.Description,
        mechanic.Version,
        mechanic.ExecutionHandler,
        mechanic.AutomationLevel,
        ClassifyExecutionSupport(mechanic),
        mechanic.InputContract
            .Select(input => new ProcedureInputContract(input, InputSources(mechanic.AllowedSourcesFor(input))))
            .ToArray(),
        mechanic.OutputContract.ToArray(),
        mechanic.ParameterSchema.ToDictionary(
            item => item.Key,
            item => ProcedureParameterDefinitionContract.From(item.Value),
            StringComparer.Ordinal),
        mechanic.CompatibilityTags.ToArray());

    private static string ClassifyExecutionSupport(MechanicDefinition mechanic)
    {
        if (GenericProcedureExecutionHandlers.SupportsNativeExecution(mechanic))
        {
            return "Native";
        }

        if (string.Equals(
                mechanic.ExecutionHandler,
                GenericProcedureExecutionHandlers.DeclarativeContract,
                StringComparison.Ordinal)
            && GenericProcedureCatalog.Mechanics.Any(candidate =>
                string.Equals(candidate.ExecutionHandler, mechanic.ExecutionHandler, StringComparison.Ordinal)
                && candidate.Version == mechanic.Version))
        {
            return "Declarative";
        }

        return "Unsupported";
    }

    internal static IReadOnlyList<string> InputSources(ProcedureInputSource sources)
    {
        var values = new List<string>();
        if ((sources & ProcedureInputSource.SelectedModule) != 0) values.Add("SelectedModule");
        if ((sources & ProcedureInputSource.Dm) != 0) values.Add("Dm");
        if ((sources & ProcedureInputSource.OptionalProvider) != 0) values.Add("OptionalProvider");
        if ((sources & ProcedureInputSource.ExternalState) != 0) values.Add("ExternalState");
        return values;
    }
}

public sealed record ProcedureDependencyIssueContract(
    ProcedureDependencyIssueKind Kind,
    string ModuleKey,
    string Message,
    string? InputKey,
    IReadOnlyList<string> AllowedInputSources)
{
    public static ProcedureDependencyIssueContract From(ProcedureDependencyIssue issue) =>
        new(
            issue.Kind,
            issue.ModuleKey,
            issue.Message,
            issue.InputKey,
            ProcedureMechanicComposerContract.InputSources(issue.AllowedInputSources));
}

public sealed record ProcedureDependencyReportContract(
    bool HasErrors,
    IReadOnlyList<ProcedureDependencyIssueContract> Issues)
{
    public static ProcedureDependencyReportContract From(ProcedureDependencyReport report) =>
        new(report.HasErrors, report.Issues.Select(ProcedureDependencyIssueContract.From).ToArray());
}

public sealed record ProcedureOverrideContract(
    string OverrideId,
    string ModuleKey,
    string? ReplacementMechanicKey,
    int? ReplacementMechanicVersion,
    IReadOnlyDictionary<string, string> Parameters,
    string? Note)
{
    public static ProcedureOverrideContract From(CampaignProcedureOverride value) =>
        new(
            value.OverrideId,
            value.ModuleKey,
            value.ReplacementMechanicKey,
            value.ReplacementMechanicVersion,
            value.Parameters,
            value.Note);
}

public sealed record ProcedureModuleComposerContract(
    string ModuleKey,
    string Category,
    string DisplayName,
    string Purpose,
    string ExecutionStage,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Produces,
    IReadOnlyList<string> RequiredDependencies,
    IReadOnlyList<string> OptionalDependencies,
    IReadOnlyDictionary<string, string> PresentationMetadata,
    ProcedureMechanicComposerContract Mechanic,
    IReadOnlyList<ProcedureMechanicComposerContract> Alternatives,
    IReadOnlyDictionary<string, ProcedureParameterDefinitionContract> ConfigurationSchema,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<ProcedureInputContract> RequiredInputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<ProcedureDependencyIssueContract> DependencyIssues,
    bool IsModified,
    int ModificationCount,
    IReadOnlyList<string> ValidationIssues)
{
    public static ProcedureModuleComposerContract From(
        MaterializedProcedureModule selected,
        CampaignProcedure procedure,
        ProcedureDependencyReport dependencies)
    {
        var moduleOverrides = procedure.Overrides
            .Where(value => string.Equals(value.ModuleKey, selected.Module.Key, StringComparison.Ordinal))
            .ToArray();
        var alternatives = GenericProcedureCatalog.Mechanics
            .Where(mechanic => selected.Module.CompatibleMechanicTypes.Contains(mechanic.Key, StringComparer.Ordinal))
            .Append(selected.Mechanic)
            .GroupBy(mechanic => (mechanic.Key, mechanic.Version))
            .Select(group => group.First())
            .Select(ProcedureMechanicComposerContract.From)
            .ToArray();
        var requiredInputs = selected.Module.Reads
            .Concat(selected.Mechanic.InputContract)
            .Distinct(StringComparer.Ordinal)
            .Select(input => new ProcedureInputContract(
                input,
                ProcedureMechanicComposerContract.InputSources(selected.Mechanic.AllowedSourcesFor(input))))
            .ToArray();
        var outputs = selected.Module.Produces
            .Concat(selected.Mechanic.OutputContract)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var validationIssues = RequiredParameterIssues(selected);

        return new ProcedureModuleComposerContract(
            selected.Module.Key,
            selected.Module.Category,
            selected.Module.DisplayName,
            selected.Module.Purpose,
            selected.Module.ExecutionStage,
            selected.Module.Reads.ToArray(),
            selected.Module.Produces.ToArray(),
            selected.Module.RequiredDependencies.ToArray(),
            selected.Module.OptionalDependencies.ToArray(),
            selected.Module.PresentationMetadata,
            ProcedureMechanicComposerContract.From(selected.Mechanic),
            alternatives,
            selected.Module.ConfigurationSchema.ToDictionary(
                item => item.Key,
                item => ProcedureParameterDefinitionContract.From(item.Value),
                StringComparer.Ordinal),
            selected.Parameters,
            requiredInputs,
            outputs,
            dependencies.Issues
                .Where(issue => string.Equals(issue.ModuleKey, selected.Module.Key, StringComparison.Ordinal))
                .Select(ProcedureDependencyIssueContract.From)
                .ToArray(),
            moduleOverrides.Length > 0,
            moduleOverrides.Length,
            validationIssues);
    }

    private static IReadOnlyList<string> RequiredParameterIssues(MaterializedProcedureModule selected)
    {
        var issues = new List<string>();
        var schema = selected.Module.ConfigurationSchema
            .Concat(selected.Mechanic.ParameterSchema)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        foreach (var parameter in schema.Where(item => item.Value.Required))
        {
            if (!selected.Parameters.TryGetValue(parameter.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                issues.Add($"Parameter '{parameter.Key}' is required.");
            }
        }

        return issues;
    }
}

public sealed record ProcedureOriginContract(
    string? PresetKey,
    string? PresetDisplayName,
    int? PresetRevision,
    string? Attribution,
    string? Disclaimer)
{
    public static ProcedureOriginContract? From(
        ProcedureOriginMetadata? origin,
        string? attribution,
        string? disclaimer) =>
        origin is null
            ? null
            : new(
                origin.PresetKey,
                origin.PresetDisplayName,
                origin.PresetRevision,
                attribution,
                disclaimer);
}

public sealed record ProcedureComposerContract(
    Guid ProcedureId,
    int Revision,
    string Key,
    string Name,
    bool IsExecutable,
    int ModificationCount,
    int ModifiedModuleCount,
    ProcedureOriginContract? Origin,
    IReadOnlyList<ProcedureModuleComposerContract> Modules,
    ProcedureDependencyReportContract Dependencies,
    IReadOnlyList<ProcedureOverrideContract> Overrides)
{
    public static ProcedureComposerContract From(ProcedureComposerDraft draft)
    {
        var executable = false;
        try
        {
            _ = GenericProcedureRuntime.Bind(draft.Procedure);
            executable = true;
        }
        catch (InvalidOperationException)
        {
            // Structural, incomplete, and future procedures remain editable even when not executable.
        }

        return new ProcedureComposerContract(
            draft.Procedure.ProcedureId,
            draft.Procedure.Revision,
            draft.Procedure.Key,
            draft.Procedure.Name,
            executable,
            draft.Procedure.Overrides.Count,
            draft.Procedure.Overrides.Select(value => value.ModuleKey).Distinct(StringComparer.Ordinal).Count(),
            ProcedureOriginContract.From(draft.Origin, draft.Attribution, draft.Disclaimer),
            draft.Procedure.Modules
                .Select(module => ProcedureModuleComposerContract.From(module, draft.Procedure, draft.Dependencies))
                .ToArray(),
            ProcedureDependencyReportContract.From(draft.Dependencies),
            draft.Procedure.Overrides.Select(ProcedureOverrideContract.From).ToArray());
    }
}

public sealed record ProcedureRevisionSummaryContract(
    Guid ProcedureId,
    int Revision,
    string Name,
    int ModificationCount,
    DateTimeOffset CreatedAt)
{
    public static ProcedureRevisionSummaryContract From(StoredCampaignProcedureRevision stored) =>
        new(
            stored.ProcedureId,
            stored.Revision,
            stored.Procedure.Name,
            stored.Procedure.Overrides.Count,
            stored.CreatedAt);
}