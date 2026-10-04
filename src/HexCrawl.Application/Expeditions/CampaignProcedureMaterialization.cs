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
    ProcedureOriginMetadata? Origin);

public static class CampaignProcedureMaterializer
{
    public static MaterializedCampaignProcedure Materialize(
        CrawlProcedurePresetDefinition preset,
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

        var modules = preset.Recipe.ModuleSelections.Select(selection =>
        {
            var module = JourneyProcedureContractSchema.ExtendModule(
                GenericProcedureCatalog.ResolveModule(selection.ModuleKey));
            var mechanic = JourneyProcedureContractSchema.ExtendMechanic(
                selection.ModuleKey,
                GenericProcedureCatalog.ResolveMechanic(selection.MechanicKey, selection.MechanicVersion));
            return new MaterializedProcedureModule(
                CampaignProcedureSnapshot.Copy(module),
                CampaignProcedureSnapshot.Copy(mechanic),
                CampaignProcedureSnapshot.CopyStrings(
                    JourneyProcedureContractSchema.UpgradePresetParameters(preset, selection)));
        }).ToArray();

        var procedure = new CampaignProcedure
        {
            ProcedureId = procedureId ?? Guid.NewGuid(),
            Revision = 1,
            Key = preset.Recipe.DefaultProcedureKey,
            Name = preset.Recipe.DefaultProcedureName,
            Modules = modules,
            Overrides = []
        };
        procedure.Validate();
        return new MaterializedCampaignProcedure(procedure, preset.Origin);
    }

    public static CampaignProcedure CreateDraft(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(overrides);
        current.Validate();
        return ApplyOverrides(current, overrides, current.Revision);
    }

    public static CampaignProcedure CreateInitialRevision(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        var revision = CreateDraft(current, overrides);
        revision.Validate();
        return revision;
    }

    public static CampaignProcedure CreateRevision(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(overrides);
        current.Validate();
        var revision = ApplyOverrides(current, overrides, checked(current.Revision + 1));
        revision.Validate();
        return revision;
    }

    private static CampaignProcedure ApplyOverrides(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        int revisionNumber)
    {
        var modules = current.Modules
            .Select(CampaignProcedureSnapshot.Copy)
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
                mechanic = CampaignProcedureSnapshot.Copy(
                    JourneyProcedureContractSchema.ExtendMechanic(
                        selected.Module.Key,
                        GenericProcedureCatalog.ResolveMechanic(value.ReplacementMechanicKey, value.ReplacementMechanicVersion)));
            }

            var parameters = CampaignProcedureSnapshot.CopyStrings(selected.Parameters);
            foreach (var parameter in value.Parameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
            modules[value.ModuleKey] = new MaterializedProcedureModule(
                CampaignProcedureSnapshot.Copy(selected.Module),
                CampaignProcedureSnapshot.Copy(mechanic),
                parameters);
        }

        return current with
        {
            Revision = revisionNumber,
            Modules = current.Modules.Select(module => modules[module.Module.Key]).ToArray(),
            Overrides = current.Overrides
                .Select(CampaignProcedureSnapshot.Copy)
                .Concat(overrides.Select(CampaignProcedureSnapshot.Copy))
                .ToArray()
        };
    }
}