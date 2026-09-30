using HexCrawl.Application;

namespace HexCrawl.Web.Modules.Procedures;

public sealed record ProcedureReferenceInputSourceContract(string Key, string Label)
{
    public static ProcedureReferenceInputSourceContract From(ProcedureReferenceInputSource source) =>
        new(source.Key, source.Label);
}

public sealed record ProcedureReferenceNamedValueContract(string Key, string DisplayName)
{
    public static ProcedureReferenceNamedValueContract From(ProcedureReferenceNamedValue value) =>
        new(value.Key, value.DisplayName);
}

public sealed record ProcedureReferenceMapEntryContract(string Key, string Value)
{
    public static ProcedureReferenceMapEntryContract From(ProcedureReferenceMapEntry value) =>
        new(value.Key, value.Value);
}

public sealed record ProcedureReferenceParameterContract(
    string Key,
    string DisplayName,
    string Type,
    string? Description,
    string RawValue,
    string DisplayValue,
    bool IsUnknown,
    IReadOnlyList<string> ListValues,
    IReadOnlyList<ProcedureReferenceMapEntryContract> MapEntries,
    string? TechnicalDetail)
{
    public static ProcedureReferenceParameterContract From(ProcedureReferenceParameter parameter) =>
        new(
            parameter.Key,
            parameter.DisplayName,
            parameter.Type,
            parameter.Description,
            parameter.RawValue,
            parameter.DisplayValue,
            parameter.IsUnknown,
            parameter.ListValues.ToArray(),
            parameter.MapEntries.Select(ProcedureReferenceMapEntryContract.From).ToArray(),
            parameter.TechnicalDetail);
}

public sealed record ProcedureReferenceInputContract(
    string Key,
    string DisplayName,
    IReadOnlyList<string> ProducerModules,
    IReadOnlyList<ProcedureReferenceInputSourceContract> AllowedSources)
{
    public static ProcedureReferenceInputContract From(ProcedureReferenceInput input) =>
        new(
            input.Key,
            input.DisplayName,
            input.ProducerModules.ToArray(),
            input.AllowedSources.Select(ProcedureReferenceInputSourceContract.From).ToArray());
}

public sealed record ProcedureReferenceDiagnosticContract(
    string Kind,
    string Message,
    string? InputKey,
    IReadOnlyList<ProcedureReferenceInputSourceContract> AllowedSources)
{
    public static ProcedureReferenceDiagnosticContract From(ProcedureReferenceDiagnostic diagnostic) =>
        new(
            diagnostic.Kind.ToString(),
            diagnostic.Message,
            diagnostic.InputKey,
            diagnostic.AllowedSources.Select(ProcedureReferenceInputSourceContract.From).ToArray());
}

public sealed record ProcedureReferenceMechanicContract(
    string Key,
    string DisplayName,
    string Description,
    int Version,
    string ExecutionHandler,
    string AutomationLevel,
    string ExecutionSupport,
    string ExecutionStatus,
    IReadOnlyList<string> CompatibilityTags)
{
    public static ProcedureReferenceMechanicContract From(ProcedureReferenceMechanic mechanic) =>
        new(
            mechanic.Key,
            mechanic.DisplayName,
            mechanic.Description,
            mechanic.Version,
            mechanic.ExecutionHandler,
            mechanic.AutomationLevel.ToString(),
            mechanic.ExecutionSupport.ToString(),
            mechanic.ExecutionStatus,
            mechanic.CompatibilityTags.ToArray());
}

public sealed record ProcedureReferenceModuleContract(
    string ModuleKey,
    string Category,
    string Section,
    string DisplayName,
    string Purpose,
    string ExecutionStage,
    IReadOnlyDictionary<string, string> PresentationMetadata,
    ProcedureReferenceMechanicContract Mechanic,
    IReadOnlyList<ProcedureReferenceParameterContract> Parameters,
    IReadOnlyList<ProcedureReferenceInputContract> RequiredInputs,
    IReadOnlyList<ProcedureReferenceNamedValueContract> Outputs,
    IReadOnlyList<ProcedureReferenceDiagnosticContract> Diagnostics,
    bool IsModified,
    int ModificationCount,
    IReadOnlyList<string> ModificationNotes)
{
    public static ProcedureReferenceModuleContract From(ProcedureReferenceModule module) =>
        new(
            module.ModuleKey,
            module.Category,
            module.Section,
            module.DisplayName,
            module.Purpose,
            module.ExecutionStage,
            module.PresentationMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            ProcedureReferenceMechanicContract.From(module.Mechanic),
            module.Parameters.Select(ProcedureReferenceParameterContract.From).ToArray(),
            module.RequiredInputs.Select(ProcedureReferenceInputContract.From).ToArray(),
            module.Outputs.Select(ProcedureReferenceNamedValueContract.From).ToArray(),
            module.Diagnostics.Select(ProcedureReferenceDiagnosticContract.From).ToArray(),
            module.IsModified,
            module.ModificationCount,
            module.ModificationNotes.ToArray());
}

public sealed record ProcedureReferenceSectionContract(
    string Name,
    IReadOnlyList<ProcedureReferenceModuleContract> Modules)
{
    public static ProcedureReferenceSectionContract From(ProcedureReferenceSection section) =>
        new(section.Name, section.Modules.Select(ProcedureReferenceModuleContract.From).ToArray());
}

public sealed record ProcedureReferenceContract(
    Guid ProcedureId,
    int Revision,
    string Key,
    string Name,
    bool IsExecutable,
    int ModificationCount,
    int ModifiedModuleCount,
    ProcedureOriginContract? Origin,
    ProcedureDependencyReportContract Dependencies,
    IReadOnlyList<ProcedureReferenceSectionContract> Sections)
{
    public static ProcedureReferenceContract From(ProcedureReference reference) =>
        new(
            reference.ProcedureId,
            reference.Revision,
            reference.Key,
            reference.Name,
            reference.IsExecutable,
            reference.ModificationCount,
            reference.ModifiedModuleCount,
            ProcedureOriginContract.From(reference.Origin, null, null),
            ProcedureDependencyReportContract.From(reference.Dependencies),
            reference.Sections.Select(ProcedureReferenceSectionContract.From).ToArray());
}
