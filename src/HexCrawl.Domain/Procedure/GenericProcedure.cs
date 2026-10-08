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
    public bool Equals(MaterializedProcedureModule? other) =>
        other is not null
        && ProcedureStructuralEquality.ModuleEquals(Module, other.Module)
        && ProcedureStructuralEquality.MechanicEquals(Mechanic, other.Mechanic)
        && ProcedureStructuralEquality.DictionaryEquals(Parameters, other.Parameters);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProcedureStructuralEquality.ModuleHash(Module));
        hash.Add(ProcedureStructuralEquality.MechanicHash(Mechanic));
        hash.Add(ProcedureStructuralEquality.DictionaryHash(Parameters));
        return hash.ToHashCode();
    }

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

public static class CampaignProcedureSchema
{
    public const string LegacyVersion = "1";
    public const string CurrentVersion = "1.1";
    public const string CurrentHexTilingGjhNotation = "6/m30/r(h1)";

    public static CampaignProcedure Upgrade(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        if (!string.Equals(procedure.SchemaVersion, LegacyVersion, StringComparison.Ordinal))
        {
            return procedure;
        }

        return procedure with
        {
            SchemaVersion = CurrentVersion,
            TilingGjhNotation = string.IsNullOrWhiteSpace(procedure.TilingGjhNotation)
                ? CurrentHexTilingGjhNotation
                : procedure.TilingGjhNotation
        };
    }
}

public sealed record CampaignProcedure
{
    public required Guid ProcedureId { get; init; }
    public required int Revision { get; init; }
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string SchemaVersion { get; init; } = CampaignProcedureSchema.CurrentVersion;
    public string TilingGjhNotation { get; init; } = CampaignProcedureSchema.CurrentHexTilingGjhNotation;
    public required IReadOnlyList<MaterializedProcedureModule> Modules { get; init; }
    public IReadOnlyList<CampaignProcedureOverride> Overrides { get; init; } = [];

    public bool Equals(CampaignProcedure? other) =>
        other is not null
        && ProcedureId == other.ProcedureId
        && Revision == other.Revision
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(SchemaVersion, other.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(TilingGjhNotation, other.TilingGjhNotation, StringComparison.Ordinal)
        && ProcedureStructuralEquality.SequenceEquals(Modules, other.Modules)
        && ProcedureStructuralEquality.OverrideSequenceEquals(Overrides, other.Overrides);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProcedureId);
        hash.Add(Revision);
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(SchemaVersion, StringComparer.Ordinal);
        hash.Add(TilingGjhNotation, StringComparer.Ordinal);
        hash.Add(ProcedureStructuralEquality.SequenceHash(Modules));
        hash.Add(ProcedureStructuralEquality.OverrideSequenceHash(Overrides));
        return hash.ToHashCode();
    }

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
        if (!string.Equals(SchemaVersion, CampaignProcedureSchema.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Campaign procedure schema version '{SchemaVersion}' is not supported. Expected {CampaignProcedureSchema.CurrentVersion}.");
        }
        MechanicDefinition.Require(TilingGjhNotation, "Campaign procedure GomJau-Hogg tiling notation");
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

internal static class ProcedureStructuralEquality
{
    public static bool ModuleEquals(ProcedureModuleDefinition left, ProcedureModuleDefinition right) =>
        string.Equals(left.Key, right.Key, StringComparison.Ordinal)
        && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && string.Equals(left.Purpose, right.Purpose, StringComparison.Ordinal)
        && string.Equals(left.ExecutionStage, right.ExecutionStage, StringComparison.Ordinal)
        && SequenceEquals(left.Reads, right.Reads)
        && SequenceEquals(left.Produces, right.Produces)
        && SequenceEquals(left.RequiredDependencies, right.RequiredDependencies)
        && SequenceEquals(left.OptionalDependencies, right.OptionalDependencies)
        && SequenceEquals(left.CompatibleMechanicTypes, right.CompatibleMechanicTypes)
        && DictionaryEquals(left.ConfigurationSchema, right.ConfigurationSchema)
        && DictionaryEquals(left.PresentationMetadata, right.PresentationMetadata);

    public static bool MechanicEquals(MechanicDefinition left, MechanicDefinition right) =>
        string.Equals(left.Key, right.Key, StringComparison.Ordinal)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && SequenceEquals(left.InputContract, right.InputContract)
        && SequenceEquals(left.OutputContract, right.OutputContract)
        && DictionaryEquals(left.ParameterSchema, right.ParameterSchema)
        && string.Equals(left.ExecutionHandler, right.ExecutionHandler, StringComparison.Ordinal)
        && SequenceEquals(left.CompatibilityTags, right.CompatibilityTags)
        && left.AutomationLevel == right.AutomationLevel
        && left.Version == right.Version
        && SequenceEquals(left.InputRequirements ?? [], right.InputRequirements ?? [])
        && DictionaryEquals(left.ExternalInputSources, right.ExternalInputSources);

    public static bool DictionaryEquals<T>(
        IReadOnlyDictionary<string, T> left,
        IReadOnlyDictionary<string, T> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        var comparer = EqualityComparer<T>.Default;
        foreach (var item in left)
        {
            if (!right.TryGetValue(item.Key, out var value) || !comparer.Equals(item.Value, value))
            {
                return false;
            }
        }
        return true;
    }

    public static bool SequenceEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Count; index++)
        {
            if (!comparer.Equals(left[index], right[index])) return false;
        }
        return true;
    }

    public static bool OverrideSequenceEquals(
        IReadOnlyList<CampaignProcedureOverride> left,
        IReadOnlyList<CampaignProcedureOverride> right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            var x = left[index];
            var y = right[index];
            if (!string.Equals(x.OverrideId, y.OverrideId, StringComparison.Ordinal)
                || !string.Equals(x.ModuleKey, y.ModuleKey, StringComparison.Ordinal)
                || !string.Equals(x.ReplacementMechanicKey, y.ReplacementMechanicKey, StringComparison.Ordinal)
                || x.ReplacementMechanicVersion != y.ReplacementMechanicVersion
                || !DictionaryEquals(x.Parameters, y.Parameters)
                || !string.Equals(x.Note, y.Note, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public static int ModuleHash(ProcedureModuleDefinition value)
    {
        var hash = new HashCode();
        hash.Add(value.Key, StringComparer.Ordinal);
        hash.Add(value.Category, StringComparer.Ordinal);
        hash.Add(value.DisplayName, StringComparer.Ordinal);
        hash.Add(value.Purpose, StringComparer.Ordinal);
        hash.Add(value.ExecutionStage, StringComparer.Ordinal);
        hash.Add(SequenceHash(value.Reads));
        hash.Add(SequenceHash(value.Produces));
        hash.Add(SequenceHash(value.RequiredDependencies));
        hash.Add(SequenceHash(value.OptionalDependencies));
        hash.Add(SequenceHash(value.CompatibleMechanicTypes));
        hash.Add(DictionaryHash(value.ConfigurationSchema));
        hash.Add(DictionaryHash(value.PresentationMetadata));
        return hash.ToHashCode();
    }

    public static int MechanicHash(MechanicDefinition value)
    {
        var hash = new HashCode();
        hash.Add(value.Key, StringComparer.Ordinal);
        hash.Add(value.DisplayName, StringComparer.Ordinal);
        hash.Add(value.Description, StringComparer.Ordinal);
        hash.Add(SequenceHash(value.InputContract));
        hash.Add(SequenceHash(value.OutputContract));
        hash.Add(DictionaryHash(value.ParameterSchema));
        hash.Add(value.ExecutionHandler, StringComparer.Ordinal);
        hash.Add(SequenceHash(value.CompatibilityTags));
        hash.Add(value.AutomationLevel);
        hash.Add(value.Version);
        hash.Add(SequenceHash(value.InputRequirements ?? []));
        hash.Add(DictionaryHash(value.ExternalInputSources));
        return hash.ToHashCode();
    }

    public static int DictionaryHash<T>(IReadOnlyDictionary<string, T> values)
    {
        var hash = new HashCode();
        hash.Add(values.Count);
        foreach (var item in values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            hash.Add(item.Key, StringComparer.Ordinal);
            hash.Add(item.Value);
        }
        return hash.ToHashCode();
    }

    public static int SequenceHash<T>(IReadOnlyList<T> values)
    {
        var hash = new HashCode();
        hash.Add(values.Count);
        foreach (var value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    public static int OverrideSequenceHash(IReadOnlyList<CampaignProcedureOverride> values)
    {
        var hash = new HashCode();
        hash.Add(values.Count);
        foreach (var value in values)
        {
            hash.Add(value.OverrideId, StringComparer.Ordinal);
            hash.Add(value.ModuleKey, StringComparer.Ordinal);
            hash.Add(value.ReplacementMechanicKey, StringComparer.Ordinal);
            hash.Add(value.ReplacementMechanicVersion);
            hash.Add(DictionaryHash(value.Parameters));
            hash.Add(value.Note, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
