namespace HexCrawl.Domain.Procedure;

public enum ProcedureAutomationLevel
{
    Automatic,
    Assisted,
    Manual
}

[Flags]
public enum ProcedureInputSource
{
    None = 0,
    SelectedModule = 1,
    Dm = 2,
    OptionalProvider = 4,
    ExternalState = 8,
    Manual = Dm,
    RuntimeState = ExternalState
}

public sealed record ProcedureInputRequirement(
    string InputKey,
    ProcedureInputSource AllowedSources)
{
    public void Validate(string mechanicKey, IReadOnlyList<string> inputContract)
    {
        if (string.IsNullOrWhiteSpace(InputKey))
        {
            throw new InvalidOperationException($"Mechanic '{mechanicKey}' input requirement key can not be blank.");
        }
        if (!inputContract.Contains(InputKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mechanic '{mechanicKey}' declares an input-source policy for '{InputKey}', but that value is not in its input contract.");
        }
        if (AllowedSources == ProcedureInputSource.None)
        {
            throw new InvalidOperationException(
                $"Mechanic '{mechanicKey}' input '{InputKey}' must allow at least one input source.");
        }
    }
}

public sealed record ProcedureParameterDefinition(
    string Type,
    bool Required = false,
    string? Description = null,
    string? DefaultValue = null)
{
    public void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Procedure parameter keys can not be blank.");
        }
        if (string.IsNullOrWhiteSpace(Type))
        {
            throw new InvalidOperationException($"Procedure parameter '{key}' requires a type.");
        }
    }
}

public sealed record MechanicDefinition(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> InputContract,
    IReadOnlyList<string> OutputContract,
    IReadOnlyDictionary<string, ProcedureParameterDefinition> ParameterSchema,
    string ExecutionHandler,
    IReadOnlyList<string> CompatibilityTags,
    ProcedureAutomationLevel AutomationLevel,
    int Version,
    IReadOnlyList<ProcedureInputRequirement>? InputRequirements = null)
{
    public IReadOnlyDictionary<string, ProcedureInputSource> ExternalInputSources { get; init; } =
        new Dictionary<string, ProcedureInputSource>(StringComparer.Ordinal);

    public void Validate()
    {
        Require(Key, "Mechanic key");
        Require(DisplayName, "Mechanic display name");
        Require(Description, "Mechanic description");
        Require(ExecutionHandler, "Mechanic execution handler");
        if (Version <= 0)
        {
            throw new InvalidOperationException($"Mechanic '{Key}' requires a positive version.");
        }
        if (string.Equals(ExecutionHandler, "procedure.declarative-contract", StringComparison.Ordinal)
            && AutomationLevel == ProcedureAutomationLevel.Automatic)
        {
            throw new InvalidOperationException(
                $"Declarative mechanic '{Key}' can not be Automatic because the declarative handler does not execute behavior.");
        }
        ValidateDistinct(InputContract, $"Mechanic '{Key}' input contract");
        ValidateDistinct(OutputContract, $"Mechanic '{Key}' output contract");
        ValidateDistinct(CompatibilityTags, $"Mechanic '{Key}' compatibility tags");

        var requirements = InputRequirements ?? [];
        if (requirements.Select(value => value.InputKey).Distinct(StringComparer.Ordinal).Count() != requirements.Count)
        {
            throw new InvalidOperationException($"Mechanic '{Key}' input-source policies can not contain duplicate input keys.");
        }
        foreach (var requirement in requirements)
        {
            requirement.Validate(Key, InputContract);
        }

        const ProcedureInputSource knownExternalSources =
            ProcedureInputSource.Dm | ProcedureInputSource.OptionalProvider | ProcedureInputSource.ExternalState;
        foreach (var input in ExternalInputSources)
        {
            if (string.IsNullOrWhiteSpace(input.Key))
            {
                throw new InvalidOperationException($"Mechanic '{Key}' external input source keys can not be blank.");
            }
            if (!InputContract.Contains(input.Key, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Mechanic '{Key}' declares external sources for '{input.Key}', but that value is not in its input contract.");
            }
            if (input.Value == ProcedureInputSource.None || (input.Value & ~knownExternalSources) != 0)
            {
                throw new InvalidOperationException(
                    $"Mechanic '{Key}' declares invalid external input sources for '{input.Key}'.");
            }
        }

        foreach (var parameter in ParameterSchema)
        {
            parameter.Value.Validate(parameter.Key);
        }
    }

    public ProcedureInputSource AllowedSourcesFor(string inputKey)
    {
        var explicitRequirement = (InputRequirements ?? []).SingleOrDefault(value =>
            string.Equals(value.InputKey, inputKey, StringComparison.Ordinal));
        if (explicitRequirement is not null)
        {
            return explicitRequirement.AllowedSources;
        }
        return ExternalInputSources.TryGetValue(inputKey, out var external)
            ? ProcedureInputSource.SelectedModule | external
            : ProcedureInputSource.SelectedModule;
    }

    private static void ValidateDistinct(IReadOnlyList<string> values, string label)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"{label} can not contain blank values.");
        }
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new InvalidOperationException($"{label} can not contain duplicates.");
        }
    }

    internal static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
    }
}

public sealed record ProcedureModuleDefinition(
    string Key,
    string Category,
    string DisplayName,
    string Purpose,
    string ExecutionStage,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Produces,
    IReadOnlyList<string> RequiredDependencies,
    IReadOnlyList<string> OptionalDependencies,
    IReadOnlyList<string> CompatibleMechanicTypes,
    IReadOnlyDictionary<string, ProcedureParameterDefinition> ConfigurationSchema,
    IReadOnlyDictionary<string, string> PresentationMetadata)
{
    public void Validate()
    {
        MechanicDefinition.Require(Key, "Procedure module key");
        MechanicDefinition.Require(Category, $"Procedure module '{Key}' category");
        MechanicDefinition.Require(DisplayName, $"Procedure module '{Key}' display name");
        MechanicDefinition.Require(Purpose, $"Procedure module '{Key}' purpose");
        MechanicDefinition.Require(ExecutionStage, $"Procedure module '{Key}' execution stage");
        ValidateDistinct(Reads, "reads");
        ValidateDistinct(Produces, "produces");
        ValidateDistinct(RequiredDependencies, "required dependencies");
        ValidateDistinct(OptionalDependencies, "optional dependencies");
        ValidateDistinct(CompatibleMechanicTypes, "compatible mechanics");
        foreach (var parameter in ConfigurationSchema)
        {
            parameter.Value.Validate(parameter.Key);
        }
        if (PresentationMetadata.Any(item => string.IsNullOrWhiteSpace(item.Key)))
        {
            throw new InvalidOperationException($"Procedure module '{Key}' presentation metadata can not contain blank keys.");
        }
    }

    private void ValidateDistinct(IReadOnlyList<string> values, string label)
    {
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Procedure module '{Key}' {label} can not contain blank values.");
        }
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new InvalidOperationException($"Procedure module '{Key}' {label} can not contain duplicates.");
        }
    }
}

public sealed record CampaignProcedureOverride(
    string OverrideId,
    string ModuleKey,
    string? ReplacementMechanicKey,
    int? ReplacementMechanicVersion,
    IReadOnlyDictionary<string, string> Parameters,
    string? Note = null)
{
    public void Validate()
    {
        MechanicDefinition.Require(OverrideId, "Campaign procedure override id");
        MechanicDefinition.Require(ModuleKey, $"Campaign procedure override '{OverrideId}' module key");
        if (ReplacementMechanicVersion.HasValue && ReplacementMechanicVersion <= 0)
        {
            throw new InvalidOperationException($"Campaign procedure override '{OverrideId}' replacement mechanic version must be positive.");
        }
        if (ReplacementMechanicVersion.HasValue && string.IsNullOrWhiteSpace(ReplacementMechanicKey))
        {
            throw new InvalidOperationException($"Campaign procedure override '{OverrideId}' can not specify a replacement version without a replacement mechanic key.");
        }
        if (Parameters.Any(item => string.IsNullOrWhiteSpace(item.Key)))
        {
            throw new InvalidOperationException($"Campaign procedure override '{OverrideId}' can not contain blank parameter keys.");
        }
    }
}

public sealed record MaterializedProcedureModule(
    ProcedureModuleDefinition Module,
    MechanicDefinition Mechanic,
    IReadOnlyDictionary<string, string> Parameters)
{
    public void Validate()
    {
        Module.Validate();
        Mechanic.Validate();
        if (Module.CompatibleMechanicTypes.Count > 0
            && !Module.CompatibleMechanicTypes.Contains(Mechanic.Key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Mechanic '{Mechanic.Key}' is not compatible with module '{Module.Key}'.");
        }
        foreach (var required in Mechanic.ParameterSchema.Where(item => item.Value.Required))
        {
            if (!Parameters.TryGetValue(required.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Module '{Module.Key}' mechanic '{Mechanic.Key}' requires parameter '{required.Key}'.");
            }
        }
        foreach (var required in Module.ConfigurationSchema.Where(item => item.Value.Required))
        {
            if (!Parameters.TryGetValue(required.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Module '{Module.Key}' requires configuration parameter '{required.Key}'.");
            }
        }
        if (Parameters.Any(item => string.IsNullOrWhiteSpace(item.Key)))
        {
            throw new InvalidOperationException($"Module '{Module.Key}' can not contain blank parameter keys.");
        }
    }
}

public enum ProcedureDependencyIssueKind
{
    MissingRequiredModule,
    MissingRequiredProducer,
    UnresolvedInput,
    ManualInputRequired,
    OptionalProviderInputRequired,
    ExternalInputRequired,
    ProducedButUnused,
    IncompatibleMechanic
}

public sealed record ProcedureDependencyIssue(
    ProcedureDependencyIssueKind Kind,
    string ModuleKey,
    string Message,
    string? InputKey = null,
    ProcedureInputSource AllowedInputSources = ProcedureInputSource.None);

public sealed record ProcedureDependencyReport(IReadOnlyList<ProcedureDependencyIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Kind is
        ProcedureDependencyIssueKind.MissingRequiredModule
        or ProcedureDependencyIssueKind.MissingRequiredProducer
        or ProcedureDependencyIssueKind.IncompatibleMechanic);
}

public sealed record CampaignProcedure
{
    public required Guid ProcedureId { get; init; }
    public required int Revision { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<MaterializedProcedureModule> Modules { get; init; }
    public IReadOnlyList<CampaignProcedureOverride> Overrides { get; init; } = [];

    public void Validate()
    {
        if (ProcedureId == Guid.Empty)
        {
            throw new InvalidOperationException("Campaign procedure id can not be empty.");
        }
        if (Revision <= 0)
        {
            throw new InvalidOperationException("Campaign procedure revision must be positive.");
        }
        MechanicDefinition.Require(Key, "Campaign procedure key");
        MechanicDefinition.Require(Name, "Campaign procedure name");
        if (Modules.Count == 0)
        {
            throw new InvalidOperationException("Campaign procedure requires at least one module.");
        }
        if (Modules.Select(module => module.Module.Key).Distinct(StringComparer.Ordinal).Count() != Modules.Count)
        {
            throw new InvalidOperationException("Campaign procedure can not contain duplicate module keys.");
        }
        foreach (var module in Modules)
        {
            module.Validate();
        }
        if (Overrides.Select(value => value.OverrideId).Distinct(StringComparer.Ordinal).Count() != Overrides.Count)
        {
            throw new InvalidOperationException("Campaign procedure can not contain duplicate override ids.");
        }
        foreach (var value in Overrides)
        {
            value.Validate();
        }
        var report = EvaluateDependencies();
        if (report.HasErrors)
        {
            throw new InvalidOperationException(string.Join(" ", report.Issues.Where(IsError).Select(issue => issue.Message)));
        }
    }

    public ProcedureDependencyReport EvaluateDependencies()
    {
        var issues = new List<ProcedureDependencyIssue>();
        var moduleKeys = Modules.Select(module => module.Module.Key).ToHashSet(StringComparer.Ordinal);
        var producedValues = Modules
            .SelectMany(selected => selected.Module.Produces.Concat(selected.Mechanic.OutputContract))
            .ToHashSet(StringComparer.Ordinal);
        var consumedValues = Modules
            .SelectMany(selected => selected.Module.Reads.Concat(selected.Mechanic.InputContract))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var selected in Modules)
        {
            foreach (var dependency in selected.Module.RequiredDependencies)
            {
                if (!moduleKeys.Contains(dependency))
                {
                    issues.Add(new ProcedureDependencyIssue(
                        ProcedureDependencyIssueKind.MissingRequiredModule,
                        selected.Module.Key,
                        $"Module '{selected.Module.Key}' requires module '{dependency}'."));
                }
            }
            if (selected.Module.CompatibleMechanicTypes.Count > 0
                && !selected.Module.CompatibleMechanicTypes.Contains(selected.Mechanic.Key, StringComparer.Ordinal))
            {
                issues.Add(new ProcedureDependencyIssue(
                    ProcedureDependencyIssueKind.IncompatibleMechanic,
                    selected.Module.Key,
                    $"Mechanic '{selected.Mechanic.Key}' is not compatible with module '{selected.Module.Key}'."));
            }

            foreach (var input in selected.Module.Reads
                         .Concat(selected.Mechanic.InputContract)
                         .Distinct(StringComparer.Ordinal))
            {
                if (producedValues.Contains(input))
                {
                    continue;
                }

                var allowedSources = selected.Mechanic.AllowedSourcesFor(input);
                var fallbackSources = allowedSources
                    & (ProcedureInputSource.Dm | ProcedureInputSource.OptionalProvider | ProcedureInputSource.ExternalState);
                if (fallbackSources != ProcedureInputSource.None)
                {
                    issues.Add(new ProcedureDependencyIssue(
                        ProcedureDependencyIssueKind.UnresolvedInput,
                        selected.Module.Key,
                        $"Module '{selected.Module.Key}' input '{input}' has no selected producer. Permitted resolution sources: {DescribeInputSources(allowedSources)}.",
                        input,
                        allowedSources));
                    continue;
                }

                issues.Add(new ProcedureDependencyIssue(
                    ProcedureDependencyIssueKind.MissingRequiredProducer,
                    selected.Module.Key,
                    $"Module '{selected.Module.Key}' requires input '{input}', but no selected producer or permitted fallback source is available.",
                    input,
                    allowedSources));
            }
        }

        foreach (var selected in Modules)
        {
            foreach (var produced in selected.Module.Produces
                         .Concat(selected.Mechanic.OutputContract)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!consumedValues.Contains(produced))
                {
                    issues.Add(new ProcedureDependencyIssue(
                        ProcedureDependencyIssueKind.ProducedButUnused,
                        selected.Module.Key,
                        $"Module '{selected.Module.Key}' produces '{produced}', but no selected module declares that output as an input."));
                }
            }
        }
        return new ProcedureDependencyReport(issues);
    }

    private static string DescribeInputSources(ProcedureInputSource sources)
    {
        var labels = new List<string>();
        if ((sources & ProcedureInputSource.SelectedModule) != 0)
        {
            labels.Add("selected-module producer");
        }
        if ((sources & ProcedureInputSource.Dm) != 0)
        {
            labels.Add("DM/manual input");
        }
        if ((sources & ProcedureInputSource.OptionalProvider) != 0)
        {
            labels.Add("optional provider");
        }
        if ((sources & ProcedureInputSource.ExternalState) != 0)
        {
            labels.Add("external/runtime state");
        }
        return labels.Count == 0 ? "none" : string.Join(", ", labels);
    }

    private static bool IsError(ProcedureDependencyIssue issue) => issue.Kind is
        ProcedureDependencyIssueKind.MissingRequiredModule
        or ProcedureDependencyIssueKind.MissingRequiredProducer
        or ProcedureDependencyIssueKind.IncompatibleMechanic;
}
