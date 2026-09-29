using HexCrawl.Application.Persistence;

namespace HexCrawl.Application;

public sealed class CampaignProcedureService(IHexCrawlStore store)
{
    public async Task<StoredCampaignProcedureRevision> CreateFromPresetAsync(
        string ownerUserId,
        string presetKey,
        Guid? campaignId = null,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        var materialized = CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric();
        var stored = new StoredCampaignProcedureRevision(
            materialized.Procedure,
            owner,
            campaignId,
            materialized.Origin,
            DateTimeOffset.UtcNow);
        return await store.CreateCampaignProcedureRevisionAsync(stored, cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> CreateRevisionAsync(
        string ownerUserId,
        Guid procedureId,
        int expectedRevision,
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
        var current = await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
        if (current.Revision != expectedRevision)
        {
            throw new HexCrawlConcurrencyException(
                $"Campaign procedure revision {expectedRevision} is stale; the current revision is {current.Revision}.");
        }
        var next = CampaignProcedureMaterializer.CreateRevision(current.Procedure, overrides);
        return await store.CreateCampaignProcedureRevisionAsync(
            new StoredCampaignProcedureRevision(
                next,
                owner,
                current.CampaignId,
                current.ProcedureOrigin,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<StoredCampaignProcedureRevision> GetAsync(
        string ownerUserId,
        Guid procedureId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        return await store.GetCampaignProcedureRevisionAsync(procedureId, revision, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure revision was not found.");
    }

    public async Task<StoredCampaignProcedureRevision> GetLatestAsync(
        string ownerUserId,
        Guid procedureId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireOwner(ownerUserId);
        return await store.GetLatestCampaignProcedureRevisionAsync(procedureId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Campaign procedure was not found.");
    }

    private static string RequireOwner(string? ownerUserId) =>
        !string.IsNullOrWhiteSpace(ownerUserId)
            ? ownerUserId.Trim()
            : throw new ArgumentException("Owner user id is required.", nameof(ownerUserId));
}
