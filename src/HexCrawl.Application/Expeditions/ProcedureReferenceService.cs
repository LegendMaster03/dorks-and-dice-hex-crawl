using System.Globalization;
using System.Text;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum ProcedureReferenceExecutionSupport
{
    Native,
    Declarative,
    Unsupported
}

public sealed record ProcedureReferenceInputSource(string Key, string Label);
public sealed record ProcedureReferenceNamedValue(string Key, string DisplayName);
public sealed record ProcedureReferenceMapEntry(string Key, string Value);

public sealed record ProcedureReferenceParameter(
    string Key,
    string DisplayName,
    string Type,
    string? Description,
    string RawValue,
    string DisplayValue,
    bool IsUnknown,
    IReadOnlyList<string> ListValues,
    IReadOnlyList<ProcedureReferenceMapEntry> MapEntries,
    string? TechnicalDetail);

public sealed record ProcedureReferenceInput(
    string Key,
    string DisplayName,
    IReadOnlyList<string> ProducerModules,
    IReadOnlyList<ProcedureReferenceInputSource> AllowedSources);

public sealed record ProcedureReferenceDiagnostic(
    ProcedureDependencyIssueKind Kind,
    string Message,
    string? InputKey,
    IReadOnlyList<ProcedureReferenceInputSource> AllowedSources);

public sealed record ProcedureReferenceMechanic(
    string Key,
    string DisplayName,
    string Description,
    int Version,
    string ExecutionHandler,
    ProcedureAutomationLevel AutomationLevel,
    ProcedureReferenceExecutionSupport ExecutionSupport,
    string ExecutionStatus,
    IReadOnlyList<string> CompatibilityTags);

public sealed record ProcedureReferenceModule(
    string ModuleKey,
    string Category,
    string Section,
    string DisplayName,
    string Purpose,
    string ExecutionStage,
    IReadOnlyDictionary<string, string> PresentationMetadata,
    ProcedureReferenceMechanic Mechanic,
    IReadOnlyList<ProcedureReferenceParameter> Parameters,
    IReadOnlyList<ProcedureReferenceInput> RequiredInputs,
    IReadOnlyList<ProcedureReferenceNamedValue> Outputs,
    IReadOnlyList<ProcedureReferenceDiagnostic> Diagnostics,
    bool IsModified,
    int ModificationCount,
    IReadOnlyList<string> ModificationNotes);

public sealed record ProcedureReferenceSection(
    string Name,
    IReadOnlyList<ProcedureReferenceModule> Modules);

public sealed record ProcedureReference(
    Guid ProcedureId,
    int Revision,
    string Key,
    string Name,
    bool IsExecutable,
    int ModificationCount,
    int ModifiedModuleCount,
    ProcedureOriginMetadata? Origin,
    ProcedureDependencyReport Dependencies,
    IReadOnlyList<ProcedureReferenceSection> Sections);

public sealed class ProcedureReferenceService(CampaignProcedureService procedures)
{
    private static readonly IReadOnlyList<string> SectionOrder =
    [
        "Time",
        "Movement",
        "Party Organization",
        "Navigation",
        "Exploration",
        "Encounters",
        "Survival",
        "Journey Processes",
        "Procedure Support"
    ];

    public async Task<ProcedureReference> GetAsync(
        string ownerUserId,
        Guid procedureId,
        int? revision = null,
        CancellationToken cancellationToken = default)
    {
        var stored = revision is { } exactRevision
            ? await procedures.GetAsync(ownerUserId, procedureId, exactRevision, cancellationToken)
            : await procedures.GetLatestAsync(ownerUserId, procedureId, cancellationToken);
        return Generate(stored.Procedure, stored.ProcedureOrigin);
    }

    public static ProcedureReference Generate(
        CampaignProcedure procedure,
        ProcedureOriginMetadata? origin = null)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var dependencies = procedure.EvaluateDependencies();
        var modules = procedure.Modules
            .Select(module => BuildModule(procedure, module, dependencies))
            .ToArray();
        var sections = modules
            .GroupBy(module => module.Section, StringComparer.Ordinal)
            .OrderBy(group => SectionRank(group.Key))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ProcedureReferenceSection(group.Key, group.ToArray()))
            .ToArray();

        return new ProcedureReference(
            procedure.ProcedureId,
            procedure.Revision,
            procedure.Key,
            procedure.Name,
            CanBindToRuntime(procedure),
            procedure.Overrides.Count,
            modules.Count(module => module.IsModified),
            origin,
            dependencies,
            sections);
    }

    public static string SectionForCategory(string? category) =>
        category?.Trim().ToLowerInvariant() switch
        {
            "time" => "Time",
            "movement" => "Movement",
            "party procedure" => "Party Organization",
            "navigation" => "Navigation",
            "exploration" => "Exploration",
            "encounters" => "Encounters",
            "survival/resources" or "environment/effects" => "Survival",
            "journey processes" => "Journey Processes",
            "procedure" => "Procedure Support",
            _ => string.IsNullOrWhiteSpace(category) ? "Procedure Support" : category.Trim()
        };

    private static ProcedureReferenceModule BuildModule(
        CampaignProcedure procedure,
        MaterializedProcedureModule selected,
        ProcedureDependencyReport dependencies)
    {
        var moduleOverrides = procedure.Overrides
            .Where(value => string.Equals(value.ModuleKey, selected.Module.Key, StringComparison.Ordinal))
            .ToArray();
        var inputs = selected.Module.Reads
            .Concat(selected.Mechanic.InputContract)
            .Distinct(StringComparer.Ordinal)
            .Select(inputKey => BuildInput(procedure, selected, inputKey))
            .ToArray();
        var outputs = selected.Module.Produces
            .Concat(selected.Mechanic.OutputContract)
            .Distinct(StringComparer.Ordinal)
            .Select(value => new ProcedureReferenceNamedValue(value, Humanize(value)))
            .ToArray();
        var diagnostics = dependencies.Issues
            .Where(issue => string.Equals(issue.ModuleKey, selected.Module.Key, StringComparison.Ordinal))
            .Select(issue => new ProcedureReferenceDiagnostic(
                issue.Kind,
                issue.Message,
                issue.InputKey,
                InputSources(issue.AllowedInputSources)))
            .ToArray();
        var support = ClassifyExecutionSupport(selected.Mechanic);

        return new ProcedureReferenceModule(
            selected.Module.Key,
            selected.Module.Category,
            SectionForCategory(selected.Module.Category),
            selected.Module.DisplayName,
            selected.Module.Purpose,
            selected.Module.ExecutionStage,
            selected.Module.PresentationMetadata.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            new ProcedureReferenceMechanic(
                selected.Mechanic.Key,
                selected.Mechanic.DisplayName,
                selected.Mechanic.Description,
                selected.Mechanic.Version,
                selected.Mechanic.ExecutionHandler,
                selected.Mechanic.AutomationLevel,
                support,
                ExecutionStatus(selected.Mechanic.AutomationLevel, support),
                selected.Mechanic.CompatibilityTags.ToArray()),
            BuildParameters(selected),
            inputs,
            outputs,
            diagnostics,
            moduleOverrides.Length > 0,
            moduleOverrides.Length,
            moduleOverrides
                .Select(value => value.Note)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray());
    }

    private static IReadOnlyList<ProcedureReferenceParameter> BuildParameters(
        MaterializedProcedureModule selected)
    {
        var definitions = new Dictionary<string, ProcedureParameterDefinition>(StringComparer.Ordinal);
        foreach (var (key, definition) in selected.Module.ConfigurationSchema)
        {
            definitions[key] = definition;
        }
        foreach (var (key, definition) in selected.Mechanic.ParameterSchema)
        {
            definitions[key] = definition;
        }

        return selected.Parameters
            .Select(pair =>
            {
                definitions.TryGetValue(pair.Key, out var definition);
                return FormatParameter(pair.Key, pair.Value, definition);
            })
            .ToArray();
    }

    private static ProcedureReferenceParameter FormatParameter(
        string key,
        string rawValue,
        ProcedureParameterDefinition? definition)
    {
        var type = definition?.Type ?? "stored";
        var displayValue = rawValue;
        IReadOnlyList<string> listValues = [];
        IReadOnlyList<ProcedureReferenceMapEntry> mapEntries = [];
        string? technicalDetail = null;

        if (string.Equals(type, "boolean", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(rawValue, out var boolean))
        {
            displayValue = boolean ? "Yes" : "No";
        }
        else if (string.Equals(type, "key-list", StringComparison.OrdinalIgnoreCase))
        {
            listValues = rawValue
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            displayValue = string.Join(", ", listValues);
        }
        else if (string.Equals(type, "map<string>", StringComparison.OrdinalIgnoreCase))
        {
            mapEntries = rawValue
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value =>
                {
                    var separator = value.IndexOf('=');
                    return separator < 0
                        ? new ProcedureReferenceMapEntry(value, string.Empty)
                        : new ProcedureReferenceMapEntry(
                            value[..separator].Trim(),
                            value[(separator + 1)..].Trim());
                })
                .ToArray();
            displayValue = string.Join(", ", mapEntries.Select(value =>
                string.IsNullOrEmpty(value.Value) ? value.Key : $"{value.Key}: {value.Value}"));
        }
        else if (DefinitionEstablishesTicks(definition)
            && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            try
            {
                displayValue = FormatDuration(new TimeSpan(ticks));
                technicalDetail = $"Stored value: {rawValue} ticks";
            }
            catch (ArgumentOutOfRangeException)
            {
                // Preserve an out-of-range stored value verbatim rather than reinterpreting it.
            }
        }

        return new ProcedureReferenceParameter(
            key,
            Humanize(key),
            type,
            definition?.Description,
            rawValue,
            displayValue,
            definition is null,
            listValues,
            mapEntries,
            technicalDetail);
    }

    private static ProcedureReferenceInput BuildInput(
        CampaignProcedure procedure,
        MaterializedProcedureModule selected,
        string inputKey)
    {
        var producers = procedure.Modules
            .Where(candidate => candidate.Module.Produces
                .Concat(candidate.Mechanic.OutputContract)
                .Contains(inputKey, StringComparer.Ordinal))
            .Select(candidate => candidate.Module.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ProcedureReferenceInput(
            inputKey,
            Humanize(inputKey),
            producers,
            InputSources(selected.Mechanic.AllowedSourcesFor(inputKey)));
    }

    private static IReadOnlyList<ProcedureReferenceInputSource> InputSources(ProcedureInputSource sources)
    {
        var values = new List<ProcedureReferenceInputSource>();
        if ((sources & ProcedureInputSource.SelectedModule) != 0)
        {
            values.Add(new("SelectedModule", "Selected module producer"));
        }
        if ((sources & ProcedureInputSource.Dm) != 0)
        {
            values.Add(new("Dm", "DM / manual input"));
        }
        if ((sources & ProcedureInputSource.OptionalProvider) != 0)
        {
            values.Add(new("OptionalProvider", "Optional provider"));
        }
        if ((sources & ProcedureInputSource.ExternalState) != 0)
        {
            values.Add(new("ExternalState", "External / runtime state"));
        }
        return values;
    }

    private static ProcedureReferenceExecutionSupport ClassifyExecutionSupport(MechanicDefinition mechanic)
    {
        if (GenericProcedureExecutionHandlers.SupportsNativeExecution(mechanic))
        {
            return ProcedureReferenceExecutionSupport.Native;
        }

        if (string.Equals(
                mechanic.ExecutionHandler,
                GenericProcedureExecutionHandlers.DeclarativeContract,
                StringComparison.Ordinal)
            && mechanic.Version == 1)
        {
            return ProcedureReferenceExecutionSupport.Declarative;
        }

        return ProcedureReferenceExecutionSupport.Unsupported;
    }

    private static string ExecutionStatus(
        ProcedureAutomationLevel automationLevel,
        ProcedureReferenceExecutionSupport support) =>
        support switch
        {
            ProcedureReferenceExecutionSupport.Unsupported =>
                "Unsupported handler/version — the stored definition is preserved, but the current runtime can not execute this version.",
            ProcedureReferenceExecutionSupport.Declarative =>
                "Structural / declarative — Hex Crawl records this procedure contract; the later execution engine is not implemented yet.",
            _ when automationLevel == ProcedureAutomationLevel.Automatic =>
                "Automatic — Hex Crawl executes this behavior natively.",
            _ when automationLevel == ProcedureAutomationLevel.Assisted =>
                "Assisted — Hex Crawl represents and assists this procedure, but DM input may still be required.",
            _ =>
                "Manual — this procedure is recorded here, but resolution is performed by the DM."
        };

    private static bool DefinitionEstablishesTicks(ProcedureParameterDefinition? definition) =>
        definition is not null
        && string.Equals(definition.Type, "integer", StringComparison.OrdinalIgnoreCase)
        && definition.Description?.Contains("ticks", StringComparison.OrdinalIgnoreCase) == true;

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.Ticks % TimeSpan.TicksPerDay == 0)
        {
            return Unit(duration.Ticks / TimeSpan.TicksPerDay, "day");
        }
        if (duration.Ticks % TimeSpan.TicksPerHour == 0)
        {
            return Unit(duration.Ticks / TimeSpan.TicksPerHour, "hour");
        }
        if (duration.Ticks % TimeSpan.TicksPerMinute == 0)
        {
            return Unit(duration.Ticks / TimeSpan.TicksPerMinute, "minute");
        }
        if (duration.Ticks % TimeSpan.TicksPerSecond == 0)
        {
            return Unit(duration.Ticks / TimeSpan.TicksPerSecond, "second");
        }
        return duration.ToString("c", CultureInfo.InvariantCulture);
    }

    private static string Unit(long value, string unit) =>
        $"{value.ToString(CultureInfo.InvariantCulture)} {unit}{(value is 1 or -1 ? string.Empty : "s")}";

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
        var builder = new StringBuilder(normalized.Length + 8);
        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (index > 0
                && char.IsUpper(current)
                && char.IsLower(normalized[index - 1]))
            {
                builder.Append(' ');
            }
            builder.Append(current);
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static int SectionRank(string section)
    {
        for (var index = 0; index < SectionOrder.Count; index++)
        {
            if (string.Equals(SectionOrder[index], section, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return int.MaxValue;
    }

    private static bool CanBindToRuntime(CampaignProcedure procedure)
    {
        try
        {
            _ = GenericProcedureRuntime.Bind(procedure);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
