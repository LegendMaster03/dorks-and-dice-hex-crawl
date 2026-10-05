using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public sealed record ProcedureComposerDraft(
    CampaignProcedure Procedure,
    ProcedureOriginMetadata? Origin,
    string? Attribution,
    string? Disclaimer,
    ProcedureDependencyReport Dependencies);

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
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = await ResolveSourceAsync(owner, presetKey, procedureId, revision, cancellationToken);
        var draft = CampaignProcedureMaterializer.CreateDraft(source.Procedure, moduleSelections, overrides);
        return new ProcedureComposerDraft(
            draft,
            source.Origin,
            source.Attribution,
            source.Disclaimer,
            draft.EvaluateDependencies());
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
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        ArgumentNullException.ThrowIfNull(moduleSelections);
        ArgumentNullException.ThrowIfNull(overrides);
        var source = ResolveCreationSource(presetKey);
        var procedure = CampaignProcedureMaterializer.CreateInitialRevision(source.Procedure, moduleSelections, overrides);
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
        CancellationToken cancellationToken = default)
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
        if (moduleSelections.Count == 0 && overrides.Count == 0)
        {
            throw new InvalidOperationException("Saving a new procedure revision requires at least one explicit change.");
        }

        var current = await procedures.GetLatestAsync(owner, procedureId, cancellationToken);
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }

        var draft = CampaignProcedureMaterializer.CreateDraft(current.Procedure, moduleSelections, overrides);
        draft.Validate();
        if (CampaignProcedureSnapshot.Equivalent(current.Procedure, draft))
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