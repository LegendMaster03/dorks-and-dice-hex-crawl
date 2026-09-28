using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

internal static class CampaignProcedureSnapshot
{
    public static ProcedureModuleDefinition Copy(ProcedureModuleDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            Reads = value.Reads.ToArray(),
            Produces = value.Produces.ToArray(),
            RequiredDependencies = value.RequiredDependencies.ToArray(),
            OptionalDependencies = value.OptionalDependencies.ToArray(),
            CompatibleMechanicTypes = value.CompatibleMechanicTypes.ToArray(),
            ConfigurationSchema = CopySchema(value.ConfigurationSchema),
            PresentationMetadata = CopyStrings(value.PresentationMetadata)
        };
    }

    public static MechanicDefinition Copy(MechanicDefinition value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            InputContract = value.InputContract.ToArray(),
            OutputContract = value.OutputContract.ToArray(),
            ParameterSchema = CopySchema(value.ParameterSchema),
            CompatibilityTags = value.CompatibilityTags.ToArray(),
            InputRequirements = value.InputRequirements?.Select(item => item with { }).ToArray()
        };
    }

    public static MaterializedProcedureModule Copy(MaterializedProcedureModule value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new MaterializedProcedureModule(
            Copy(value.Module),
            Copy(value.Mechanic),
            CopyStrings(value.Parameters));
    }

    public static CampaignProcedureOverride Copy(CampaignProcedureOverride value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with { Parameters = CopyStrings(value.Parameters) };
    }

    public static Dictionary<string, string> CopyStrings(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, ProcedureParameterDefinition> CopySchema(
        IReadOnlyDictionary<string, ProcedureParameterDefinition> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.ToDictionary(
            item => item.Key,
            item => item.Value with { },
            StringComparer.Ordinal);
    }
}
