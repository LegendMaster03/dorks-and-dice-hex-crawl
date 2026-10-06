using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureDependencyFixSuggestion(
    IReadOnlyList<string> ModuleKeys);

public sealed record ProcedureComposerDraft(
    CampaignProcedure Procedure,
    ProcedureOriginMetadata? Origin,
    string? Attribution,
    string? Disclaimer,
    ProcedureDependencyReport Dependencies,
    IReadOnlyList<ProcedureDependencyFixSuggestion>? DependencyFixes = null);

public sealed class ProcedureComposerService(CampaignProcedureService procedures)
{
    public Task<ProcedureComposerDraft> CreateDraftAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default) =>
        CreateDraftAsync(ownerUserId, presetKey, procedureId, revision, [], overrides, cancellationToken);

    public async Task<ProcedureComposerDraft> CreateDraftAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default,
        string? name = null)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = await ResolveSourceAsync(owner, presetKey, procedureId, revision, cancellationToken);
        var draft = ApplyName(
            CampaignProcedureMaterializer.CreateDraft(source.Procedure, moduleSelections, overrides),
            name);
        var dependencies = draft.EvaluateDependencies();
        return new ProcedureComposerDraft(
            draft,
            source.Origin,
            source.Attribution,
            source.Disclaimer,
            dependencies,
            SuggestDependencyFixes(draft, dependencies));
    }

    public Task<StoredCampaignProcedureRevision> CreateAsync(
        string ownerUserId,
        string? presetKey,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default) =>
        CreateAsync(ownerUserId, presetKey, [], overrides, campaignId, cancellationToken);

    public async Task<StoredCampaignProcedureRevision> CreateAsync(
        string ownerUserId,
        string? presetKey,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default,
        string? name = null)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = ResolveCreationSource(presetKey);
        var procedure = ApplyName(
            CampaignProcedureMaterializer.CreateInitialRevision(source.Procedure, moduleSelections, overrides),
            name);
        procedure.Validate();
        return await procedures.CreateAsync(
            owner,
            procedure,
            source.Origin,
            campaignId,
            cancellationToken);
    }

    public Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default) =>
        CreateRevisionAsync(ownerUserId, procedureId, expectedRevision, [], overrides, cancellationToken);

    public async Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
        IReadOnlyList<ProcedureModuleSelection> moduleSelections,
        IReadOnlyList<CampaignProcedureOverride> overrides,
        CancellationToken cancellationToken = default,
        string? name = null)
    {
        var owner = RequireOwner(ownerUserId);
        if (procedureId == Guid.Empty)
        {
            throw new ArgumentException("Procedure id can not be empty.", nameof(procedureId));
        }
        if (expectedRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        }
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        if (moduleSelections.Count == 0 && overrides.Count == 0 && name is null)
        {
            throw new InvalidOperationException("Saving a new procedure revision requires at least one explicit change.");
        }

        var current = await procedures.GetLatestAsync(owner, procedureId, cancellationToken);
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }

        var draft = ApplyName(
            CampaignProcedureMaterializer.CreateDraft(current.Procedure, moduleSelections, overrides),
            name);
        draft.Validate();

        // Override records are audit/history metadata. A newly submitted override that resolves to
        // the already-materialized mechanic and parameters is not a behavioral procedure change.
        // Structural selections, mechanic changes, and parameter changes remain visible through
        // Modules and are therefore included in the equivalence check.
        var materializedChange = draft with
        {
            Overrides = current.Procedure.Overrides
                .Select(CampaignProcedureSnapshot.Copy)
                .ToArray()
        };
        if (CampaignProcedureSnapshot.Equivalent(current.Procedure, materializedChange))
        {
            throw new InvalidOperationException("The submitted procedure changes do not alter the current materialized procedure.");
        }

        return await procedures.CreateCanonicalRevisionAsync(
            owner,
            procedureId,
            expectedRevision,
            draft,
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> GetAsync(
        string ownerUserId,
        Guid procedureId,
        int? revision = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        return revision.HasValue
            ? await procedures.GetAsync(owner, procedureId, revision.Value, cancellationToken)
            : await procedures.GetLatestAsync(owner, procedureId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredCampaignProcedureRevision>> ListRevisionsAsync(
        string ownerUserId,
        Guid procedureId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        return await procedures.ListRevisionsAsync(owner, procedureId, cancellationToken);
    }

    private async Task<Source> ResolveSourceAsync(
        string ownerUserId,
        string? presetKey,
        Guid? procedureId,
        int? revision,
        CancellationToken cancellationToken)
    {
        ValidateSource(presetKey, procedureId);

        if (procedureId.HasValue)
        {
            if (revision.HasValue && revision <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }

            var stored = revision.HasValue
                ? await procedures.GetAsync(ownerUserId, procedureId.Value, revision.Value, cancellationToken)
                : await procedures.GetLatestAsync(ownerUserId, procedureId.Value, cancellationToken);
            var catalogMetadata = FindOriginCatalogMetadata(stored.ProcedureOrigin);
            return new Source(
                stored.Procedure,
                stored.ProcedureOrigin,
                catalogMetadata?.Attribution,
                catalogMetadata?.Disclaimer);
        }

        return ResolveCreationSource(presetKey);
    }

    private static Source ResolveCreationSource(string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            return new Source(ProcedureComposerCustomProcedureFactory.Create(), null, null, null);
        }

        var preset = CrawlProcedureCatalog.Resolve(presetKey);
        var materialized = preset.MaterializeGeneric();
        return new Source(
            materialized.Procedure,
            materialized.Origin,
            preset.Attribution,
            preset.Disclaimer);
    }

    private static CrawlProcedurePresetDefinition? FindOriginCatalogMetadata(ProcedureOriginMetadata? origin)
    {
        if (string.IsNullOrWhiteSpace(origin?.PresetKey))
        {
            return null;
        }

        return CrawlProcedureCatalog.Catalog.FirstOrDefault(value =>
            string.Equals(value.PresetKey, origin.PresetKey, StringComparison.OrdinalIgnoreCase)
            && (!origin.PresetRevision.HasValue || value.PresetRevision == origin.PresetRevision));
    }

    private static void ValidateSource(string? presetKey, Guid? procedureId)
    {
        if (!string.IsNullOrWhiteSpace(presetKey) && procedureId.HasValue)
        {
            throw new ArgumentException("A Composer draft can start from either a preset or a saved procedure, not both.");
        }
    }

    private static IReadOnlyList<ProcedureDependencyFixSuggestion> SuggestDependencyFixes(
        CampaignProcedure procedure,
        ProcedureDependencyReport dependencies)
    {
        if (!dependencies.HasErrors)
        {
            return [];
        }

        var defaults = ComposerDefaultModules()
            .ToDictionary(module => module.Module.Key, StringComparer.Ordinal);
        var selected = procedure.Modules
            .ToDictionary(module => module.Module.Key, CampaignProcedureSnapshot.Copy, StringComparer.Ordinal);
        var order = procedure.Modules.Select(module => module.Module.Key).ToList();
        var added = new HashSet<string>(StringComparer.Ordinal);

        bool AddDefault(string moduleKey)
        {
            if (selected.ContainsKey(moduleKey) || !defaults.TryGetValue(moduleKey, out var value))
            {
                return false;
            }

            selected[moduleKey] = CampaignProcedureSnapshot.Copy(value);
            order.Add(moduleKey);
            added.Add(moduleKey);
            return true;
        }

        ProcedureDependencyReport CurrentReport() =>
            (procedure with
            {
                Modules = order.Select(moduleKey => selected[moduleKey]).ToArray()
            }).EvaluateDependencies();

        for (var pass = 0; pass < defaults.Count; pass++)
        {
            var report = CurrentReport();
            var changed = false;

            foreach (var issue in report.Issues.Where(issue =>
                         issue.Kind == ProcedureDependencyIssueKind.MissingRequiredModule))
            {
                if (!selected.TryGetValue(issue.ModuleKey, out var consumer))
                {
                    continue;
                }

                foreach (var required in consumer.Module.RequiredDependencies)
                {
                    changed |= AddDefault(required);
                }
            }

            foreach (var issue in report.Issues.Where(issue =>
                         issue.Kind == ProcedureDependencyIssueKind.MissingRequiredProducer
                         && issue.InputKey is not null))
            {
                var candidates = defaults.Values
                    .Where(candidate => !selected.ContainsKey(candidate.Module.Key))
                    .Where(candidate =>
                        candidate.Module.Produces.Contains(issue.InputKey!, StringComparer.Ordinal)
                        || candidate.Mechanic.OutputContract.Contains(issue.InputKey!, StringComparer.Ordinal))
                    .ToArray();
                if (candidates.Length == 1)
                {
                    changed |= AddDefault(candidates[0].Module.Key);
                }
            }

            if (!changed)
            {
                break;
            }
        }

        if (added.Count == 0 || CurrentReport().HasErrors)
        {
            return [];
        }

        var ordered = defaults.Keys
            .Where(added.Contains)
            .ToArray();
        return [new ProcedureDependencyFixSuggestion(ordered)];
    }

    private static IReadOnlyList<MaterializedProcedureModule> ComposerDefaultModules()
    {
        var keys = GenericProcedureCatalog.Modules
            .Select(module => module.Key)
            .Append(Phase11GenericProcedureCatalog.ExposureModule)
            .Distinct(StringComparer.Ordinal);

        return keys
            .Select(ProcedureComposerCustomProcedureFactory.CreateDefaultModule)
            .ToArray();
    }

    private static CampaignProcedure ApplyName(CampaignProcedure procedure, string? name)
    {
        if (name is null)
        {
            return procedure;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Procedure name can not be blank.", nameof(name));
        }

        return procedure with { Name = name.Trim() };
    }

    private static string RequireOwner(string? ownerUserId) =>
        !string.IsNullOrWhiteSpace(ownerUserId)
            ? ownerUserId.Trim()
            : throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));

    private sealed record Source(
        CampaignProcedure Procedure,
        ProcedureOriginMetadata? Origin,
        string? Attribution,
        string? Disclaimer);
}