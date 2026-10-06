using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureModuleRecipe(
    string ModuleKey,
    string MechanicKey,
    int MechanicVersion,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ProcedureModuleSelection(string ModuleKey, bool Included)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModuleKey))
        {
            throw new InvalidOperationException("Procedure module selection key can not be blank.");
        }
    }
}

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
                CampaignProcedureSnapshot.CopyStrings(selection.Parameters));
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
        IReadOnlyList<CampaignProcedureOverride> overrides) =>
        CreateDraft(current, [], overrides);

    public static CampaignProcedure CreateDraft(
        CampaignProcedure current,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        ValidateComposableSource(current);
        return ApplyChanges(current, moduleSelections, overrides, current.Revision);
    }

    public static CampaignProcedure CreateInitialRevision(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides) =>
        CreateInitialRevision(current, [], overrides);

    public static CampaignProcedure CreateInitialRevision(
        CampaignProcedure current,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        var revision = CreateDraft(current, moduleSelections, overrides);
        revision.Validate();
        return revision;
    }

    public static CampaignProcedure CreateRevision(
        CampaignProcedure current,
        IReadOnlyList<CampaignProcedureOverride> overrides) =>
        CreateRevision(current, [], overrides);

    public static CampaignProcedure CreateRevision(
        CampaignProcedure current,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        current.Validate();
        var revision = ApplyChanges(current, moduleSelections, overrides, checked(current.Revision + 1));
        revision.Validate();
        return revision;
    }

    private static void ValidateComposableSource(CampaignProcedure current)
    {
        if (current.Modules.Count > 0)
        {
            current.Validate();
            return;
        }

        // The only intentionally incomplete source is a transient blank authoring draft.
        // Keep identity/revision invariants here; CreateInitialRevision still calls the full
        // domain validator so an empty procedure can never be persisted.
        if (current.ProcedureId == Guid.Empty
            || current.Revision <= 0
            || string.IsNullOrWhiteSpace(current.Key)
            || string.IsNullOrWhiteSpace(current.Name)
            || current.Overrides.Count > 0)
        {
            throw new InvalidOperationException("An incomplete procedure source is not a valid authoring draft.");
        }
    }

    private static CampaignProcedure ApplyChanges(
        CampaignProcedure current,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        int revisionNumber)
    {
        foreach (var selection in moduleSelections)
        {
            selection.Validate();
        }
        if (moduleSelections.Select(value => value.ModuleKey).Distinct(StringComparer.Ordinal).Count() != moduleSelections.Count)
        {
            throw new InvalidOperationException("Procedure module selections can not contain duplicate module keys.");
        }

        var modules = current.Modules
            .Select(CampaignProcedureSnapshot.Copy)
            .ToDictionary(module => module.Module.Key, StringComparer.Ordinal);
        var order = current.Modules.Select(module => module.Module.Key).ToList();
        var removedModuleKeys = moduleSelections
            .Where(selection => !selection.Included)
            .Select(selection => selection.ModuleKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var selection in moduleSelections)
        {
            if (!selection.Included)
            {
                modules.Remove(selection.ModuleKey);
                order.RemoveAll(key => string.Equals(key, selection.ModuleKey, StringComparison.Ordinal));
                continue;
            }

            if (!modules.ContainsKey(selection.ModuleKey))
            {
                modules[selection.ModuleKey] = ProcedureComposerCustomProcedureFactory.CreateDefaultModule(selection.ModuleKey);
                order.Add(selection.ModuleKey);
            }
        }

        foreach (var value in overrides)
        {
            value.Validate();
            if (!modules.TryGetValue(value.ModuleKey, out var selected))
            {
                throw new InvalidOperationException(
                    $"Campaign override '{value.OverrideId}' targets module '{value.ModuleKey}', but that module is not included in the composed procedure.");
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
            Modules = order.Where(modules.ContainsKey).Select(key => modules[key]).ToArray(),
            Overrides = current.Overrides
                .Where(value => !removedModuleKeys.Contains(value.ModuleKey))
                .Select(CampaignProcedureSnapshot.Copy)
                .Concat(overrides.Select(CampaignProcedureSnapshot.Copy))
                .ToArray()
        };
    }
}
